using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Communication;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Runtime.Devices;
using ScadaServer.Runtime.Bindings;
using ScadaServer.Runtime.Events;
using ScadaServer.Runtime.Alarms;
using ScadaServer.Runtime.Interface;

namespace ScadaServer.Runtime
{
    /// <summary>
    /// SCADA运行时管理器，负责管理所有设备运行时的生命周期
    /// </summary>
    public class RuntimeManager : IRuntimeManager, IRuntimeDeviceManager
    {
        /// <summary>
        /// 设备运行时字典，键为设备ID
        /// </summary>
        public ConcurrentDictionary<int, Devices.DeviceRuntime> DeviceRuntimes { get; } = new();

        private readonly ILogger<RuntimeManager> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IProtocolDriverFactory _driverFactory;
        private readonly DeviceRegistry _deviceRegistry;
        private readonly IScadaNotificationService _notificationService;
        private readonly IHistoryRecorder _historyRecorder;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IVariableChangeBus _changeBus;
        private readonly IVariableBindingEngine _bindingEngine;
        private readonly IAlarmRuleEngine _alarmRuleEngine;
        private readonly IAlarmRecorder _alarmRecorder;
        private readonly IRealtimeSnapshotService _realtimeSnapshot;
        private readonly IVariableWriteAuditRecorder _variableWriteAudit;
        private DeviceScheduler? _scheduler;

        /// <summary>
        /// 各设备上一次对外推送/持久化的状态，用于去重，避免重复触发 StatusChanged。
        /// </summary>
        private readonly ConcurrentDictionary<int, DeviceStatus> _lastPushedStatus = new();

        /// <summary>
        /// 设备运行时状态变更事件（仅在对外状态值变化时触发）。
        /// </summary>
        public event EventHandler<DeviceStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// 初始化运行时管理器
        /// </summary>
        public RuntimeManager(
            ILogger<RuntimeManager> logger,
            ILoggerFactory loggerFactory,
            IProtocolDriverFactory driverFactory,
            DeviceRegistry deviceRegistry,
            IScadaNotificationService notificationService,
            IHistoryRecorder historyRecorder,
            IServiceScopeFactory scopeFactory,
            IVariableChangeBus changeBus,
            IVariableBindingEngine bindingEngine,
            IAlarmRuleEngine alarmRuleEngine,
            IAlarmRecorder alarmRecorder,
            IRealtimeSnapshotService realtimeSnapshot,
            IVariableWriteAuditRecorder variableWriteAudit)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _driverFactory = driverFactory;
            _deviceRegistry = deviceRegistry;
            _notificationService = notificationService;
            _historyRecorder = historyRecorder;
            _scopeFactory = scopeFactory;
            _changeBus = changeBus;
            _bindingEngine = bindingEngine;
            _alarmRuleEngine = alarmRuleEngine;
            _alarmRecorder = alarmRecorder;
            _realtimeSnapshot = realtimeSnapshot;
            _variableWriteAudit = variableWriteAudit;
        }

        /// <inheritdoc/>
        public void RegisterDevice(Devices.DeviceRuntime runtime)
        {
            DeviceRuntimes[runtime.Device.Id] = runtime;
            runtime.ConnectionStateChanged += OnDeviceConnectionStateChanged;

            // 初始化首轮状态，确保订阅者能拿到初始态（如初始化失败导致的 Offline）。
            var initial = MapConnectionStateToStatus(runtime);
            _lastPushedStatus[runtime.Device.Id] = initial;
        }

        private async void OnDeviceConnectionStateChanged(int deviceId, DeviceConnectionState state)
        {
            // async void 事件处理器：整体兜底捕获，任何路径的异常（含 StatusChanged 订阅者抛出）
            // 都不得击穿成为未观察异常导致进程崩溃。
            try
            {
                if (!DeviceRuntimes.TryGetValue(deviceId, out var runtime))
                {
                    return;
                }

                var status = MapConnectionStateToStatus(runtime);

                // 仅在对外状态值变化时触发，抑制抖动（连接态频繁来回切换但对外语义不变）。
                if (_lastPushedStatus.TryGetValue(deviceId, out var previous) && previous == status)
                {
                    return;
                }

                _lastPushedStatus[deviceId] = status;
                StatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs
                {
                    DeviceId = deviceId,
                    Status = status
                });

                // 主动推送设备状态变更：RuntimeManager 直接调用通知服务，
                // 避免通知服务反向注入 IRuntimeManager 形成 Singleton 循环依赖。
                // 推送失败仅告警，不影响采集循环与事件订阅者（如持久化订阅者）。
                await _notificationService.NotifyDeviceStatusAsync(deviceId, status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设备 {DeviceId} 状态变更处理失败。", deviceId);
            }
        }

        /// <inheritdoc/>
        public async Task InitializeAsync()
        {
            _logger.LogInformation("SCADA 运行时初始化：加载已启用设备...");

            // 仓储注册为 Scoped，而 RuntimeManager 为 Singleton。
            // 在此创建独立 Scope 解析 DbContext，以便通过 Include 一次性加载运行期所需的完整对象图。
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ScadaDbContext>();

            // 加载所有启用设备及其完整运行期依赖：
            // Device → DataModel(→Protocol) → DeviceConfig → DeviceVariable(→ModelVariable)
            // 运行时仅消费新模型；严禁直接访问 ModelVariable.Address，地址由 DeviceVariable 提供。
            var devices = await db.Devices
                .Include(d => d.Model).ThenInclude(m => m!.Protocol)
                .Include(d => d.Config)
                .Include(d => d.DeviceVariables).ThenInclude(dv => dv.ModelVariable)
                .Where(d => d.IsEnabled)
                .ToListAsync();

            _logger.LogInformation("找到 {Count} 个已启用设备。", devices.Count);

            foreach (var device in devices)
            {
                await BuildAndRegisterDeviceAsync(device);
            }

            // 同步加载报警规则快照，保证设备采集首轮即可命中规则报警。
            try
            {
                await _alarmRuleEngine.ReloadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "启动时加载报警规则失败（引擎周期刷新将自动重试）。");
            }

            // 所有设备注册完成后加载变量绑定索引，避免设备未就绪即触发转发写入。
            await _bindingEngine.LoadAsync();
        }

        /// <inheritdoc/>
        public async Task RegisterDeviceAsync(int deviceId)
        {
            // 幂等：若已存在同 ID 运行时则先注销，避免残留旧 Worker / 旧驱动。
            await RemoveDeviceAsync(deviceId);

            var device = await LoadDeviceGraphByIdAsync(deviceId);
            if (device == null)
            {
                _logger.LogWarning("运行期注册设备失败：ID {DeviceId} 不存在。", deviceId);
                return;
            }

            if (!device.IsEnabled)
            {
                _logger.LogInformation("运行期注册设备 {Key} 被跳过：设备未启用。", device.Key);
                return;
            }

            await BuildAndRegisterDeviceAsync(device);
        }

        /// <inheritdoc/>
        public async Task ReloadDeviceAsync(int deviceId)
        {
            await RegisterDeviceAsync(deviceId);
        }

        /// <summary>
        /// 自动重连待重连设备（由 DeviceScheduler 按退避窗口触发）。
        /// 移除占位运行时后重新走完整注册流程（加载设备图 → 连接驱动 → 注册）；
        /// 若连接仍失败，注册流程会再次注册占位运行时，形成退避重试循环。
        /// </summary>
        /// <param name="deviceId">设备 ID</param>
        public async Task ReconnectDeviceAsync(int deviceId)
        {
            // 重入门闸：仅当当前在册运行时确为待重连占位时才执行，避免并发重连。
            // 后续 TryRemove 作为原子闸门：并发调用中只有一个能成功移除并继续。
            if (!DeviceRuntimes.TryGetValue(deviceId, out var current) || !current.NeedsReconnect)
            {
                return;
            }

            if (!DeviceRuntimes.TryRemove(deviceId, out var placeholder))
            {
                return;
            }

            _lastPushedStatus.TryRemove(deviceId, out _);

            // 占位运行时无 Worker 与驱动，仅防御性取消（无副作用）。
            lock (placeholder.DispatchSync)
            {
                placeholder.CancelWorker();
            }

            // 重新走完整注册流程（内部先幂等移除，再加载、连接、注册）。
            await RegisterDeviceAsync(deviceId);
        }

        /// <inheritdoc/>
        public async Task RemoveDeviceAsync(int deviceId)
        {
            if (!DeviceRuntimes.TryRemove(deviceId, out var runtime))
            {
                return;
            }

            _lastPushedStatus.TryRemove(deviceId, out _);

            // 1) 取消当前 Worker 并等待其收尾，避免在驱动断开期间仍被采集访问。
            // 在 DispatchSync 锁内取消并读取 WorkerTask：与调度器派发临界区串行化，
            // 确保读到的句柄必然属于"最后一个派发的 Worker"——设备已从 DeviceRuntimes
            // 移除，此后调度器派发校验（在册 + 同引用）必然失败，不会再有新 Worker。
            Task? workerTask;
            lock (runtime.DispatchSync)
            {
                runtime.CancelWorker();
                workerTask = runtime.WorkerTask;
            }

            if (workerTask != null)
            {
                try
                {
                    await workerTask.WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("设备 {Key} Worker 停止超时（3s），可能仍在退出。", runtime.Device.Key);
                }
            }

            // 2) 断开设备驱动并释放资源。
            if (runtime.Driver != null)
            {
                try
                {
                    await runtime.Driver.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "设备 {Key} 驱动断开连接时发生异常（已忽略）。", runtime.Device.Key);
                }
            }

            // 3) 推送 Offline，使界面即时反映设备已移除/被禁用。
            StatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs
            {
                DeviceId = deviceId,
                Status = DeviceStatus.Offline
            });
            try
            {
                await _notificationService.NotifyDeviceStatusAsync(deviceId, DeviceStatus.Offline);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设备 {DeviceId} 注销状态通知推送失败。", deviceId);
            }
        }

        /// <summary>
        /// 按设备 ID 加载其完整运行期依赖对象图（Device → DataModel(→Protocol) → DeviceConfig → DeviceVariable(→ModelVariable)）。
        /// 每次调用处于独立 Scope 内解析 DbContext，避免 Singleton 持有 Scoped DbContext。
        /// </summary>
        private async Task<Device?> LoadDeviceGraphByIdAsync(int deviceId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
            return await db.Devices
                .Include(d => d.Model).ThenInclude(m => m!.Protocol)
                .Include(d => d.Config)
                .Include(d => d.DeviceVariables).ThenInclude(dv => dv.ModelVariable)
                .FirstOrDefaultAsync(d => d.Id == deviceId);
        }

        /// <summary>
        /// 依据数据库实体构建并注册单个设备运行时（含驱动连接）。成功返回 true，失败/跳过返回 false。
        /// 同时用于启动初始化与运行期注册，避免两份实现漂移。
        /// </summary>
        private async Task<bool> BuildAndRegisterDeviceAsync(Device device)
        {
            try
            {
                var model = device.Model;
                if (model == null)
                {
                    _logger.LogWarning("设备 {Key} 缺少关联数据模型，已跳过。", device.Key);
                    return false;
                }

                // 协议真相源为 Protocol.DriverKey（模型必绑协议后不再回退过渡字段）；缺少则跳过该设备。
                var driverKey = model.Protocol?.DriverKey;
                if (string.IsNullOrWhiteSpace(driverKey))
                {
                    _logger.LogWarning("设备 {Key} 的数据模型未绑定有效协议（DriverKey 为空），已跳过。", device.Key);
                    return false;
                }
                var protocolLabel = driverKey;

                // 先构建运行时对象（Driver 待连接成功后赋值），再以 IRuntimeDevice 只读视图连接驱动。
                // 第九阶段起：驱动只接收 RuntimeDevice / RuntimeVariable，不再感知 Device / DataModel / ModelVariable。
                var runtime = new Devices.DeviceRuntime(device)
                {
                    Device = device,
                    Model = model,
                    Protocol = model.Protocol,
                    Config = device.Config,
                    Area = device.Area
                };

                // 驱动创建与连接异常同样视为连接失败（进入占位重连路径），避免初始化异常被外层
                // catch 吞掉后设备永不重试。
                bool connected;
                try
                {
                    var driver = _driverFactory.CreateDriver(driverKey);
                    connected = await driver.ConnectAsync(runtime);
                    if (connected)
                    {
                        runtime.Driver = driver;
                    }
                    else
                    {
                        await driver.DisposeAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "设备 {Key} ({Protocol}) 连接时发生异常。", device.Key, protocolLabel);
                    connected = false;
                }

                if (!connected)
                {
                    // 注册占位运行时并标记待重连：调度器发现 NeedsReconnect 后按退避窗口
                    // 触发 ReconnectDeviceAsync 自动重试，设备状态对外呈现 Fault。
                    runtime.NeedsReconnect = true;
                    runtime.ConnectionState = DeviceConnectionState.Error;
                    RegisterDevice(runtime);
                    _logger.LogWarning(
                        "设备 {Key} ({Protocol}) 连接失败，已注册为待重连，调度器将按退避窗口自动重试。",
                        device.Key, protocolLabel);
                    return false;
                }

                // 驱动连接即视为已连接：设备无需等待首轮采集即可对外呈现在线，
                // 并为空转（无启用变量）设备直接定格在线状态，避免停留在 Initializing/Offline。
                runtime.ConnectionState = DeviceConnectionState.Connected;

                var now = DateTime.Now;
                foreach (var dv in device.DeviceVariables ?? Enumerable.Empty<DeviceVariable>())
                {
                    if (dv.ModelVariable == null)
                    {
                        _logger.LogWarning(
                            "设备 {Key} 的设备变量 #{DvId} 缺少关联模型变量，已跳过。", device.Key, dv.Id);
                        continue;
                    }

                    // 构建 RuntimeVariable：变量定义来自 ModelVariable，设备配置来自 DeviceVariable。
                    runtime.Variables[dv.Id] = new VariableRuntime
                    {
                        Definition = dv.ModelVariable,
                        Instance = dv,
                        NextPollTime = now // 首轮立即采集
                    };
                }

                RegisterDevice(runtime);

                _deviceRegistry.UpdateDevice(device,
                    (device.DeviceVariables ?? Enumerable.Empty<DeviceVariable>())
                        .Select(dv => dv.ModelVariable)
                        .Where(mv => mv != null)
                        .Cast<ModelVariable>()
                        .ToList());

                _logger.LogInformation(
                    "设备 {Key} ({Protocol}) 初始化完成，共 {VarCount} 个变量。",
                    device.Key, protocolLabel, runtime.Variables.Count);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设备 {Key} 初始化失败。", device.Key);
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken token)
        {
            if (_scheduler != null) return;

            _scheduler = new DeviceScheduler(
                this,
                _loggerFactory.CreateLogger<DeviceScheduler>(),
                _loggerFactory.CreateLogger<DeviceWorker>(),
                _notificationService,
                _historyRecorder,
                _changeBus,
                _alarmRuleEngine,
                _alarmRecorder,
                _realtimeSnapshot);

            await _scheduler.StartAsync(token);

            // 调度启动后再启动变量绑定转发循环，避免设备未运行即触发转发写入。
            await _bindingEngine.StartAsync(token);
        }

        /// <inheritdoc/>
        public async Task StopAsync()
        {
            // 先停止变量绑定转发循环并排空，避免停止过程中残留转发写入目标设备。
            await _bindingEngine.StopAsync(CancellationToken.None);

            if (_scheduler != null)
            {
                var scheduler = _scheduler;
                _scheduler = null;

                await scheduler.StopAsync();
            }

            // 停止轮询后，断开所有设备驱动（如 S7 PLC 连接）并释放其资源，确保优雅关闭。
            // 注意：待重连占位运行时没有驱动（Driver 为 null），需判空跳过。
            foreach (var runtime in DeviceRuntimes.Values)
            {
                try
                {
                    if (runtime.Driver != null)
                    {
                        await runtime.Driver.DisposeAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "设备 {Key} 驱动断开连接时发生异常（已忽略）。", runtime.Device.Key);
                }
            }

            // 清空变量绑定索引，避免停止后残留映射在重启前被误触发。
            _bindingEngine.Clear();
        }

        /// <inheritdoc/>
        public bool TryGetRuntimeStatus(int deviceId, out DeviceStatus status)
        {
            if (DeviceRuntimes.TryGetValue(deviceId, out var runtime))
            {
                status = MapConnectionStateToStatus(runtime);
                return true;
            }

            status = DeviceStatus.Offline;
            return false;
        }

        /// <inheritdoc/>
        public async Task<(bool Success, string? ErrorMessage)> WriteVariableAsync(int deviceId, string variableKey, object value, string? writeSource = null)
        {
            // 审计埋点：非 HTTP 来源（系统脚本/变量绑定）在运行时层记录操作日志；
            // HTTP 用户写入由 WebApi 层 [AuditLog] 过滤器记录（含操作人/IP），writeSource 传 null 跳过避免重复。
            async Task<(bool Success, string? ErrorMessage)> FailAsync(string message)
            {
                await RecordVariableWriteAuditAsync(deviceId, variableKey, value, writeSource, false, message);
                return (false, message);
            }

            if (!DeviceRuntimes.TryGetValue(deviceId, out var runtime))
                return await FailAsync("设备不在运行中");

            var vr = runtime.Variables.Values.FirstOrDefault(v => v.Key == variableKey);
            if (vr == null)
                return await FailAsync($"设备下不存在变量 [{variableKey}]");
            if (!vr.IsEnabled)
                return await FailAsync($"变量 [{variableKey}] 已禁用");
            if (vr.IsReadOnly)
                return await FailAsync($"变量 [{variableKey}] 为只读，禁止写入");
            if (runtime.Driver == null)
                return await FailAsync("设备驱动未就绪");
            if (runtime.ConnectionState != DeviceConnectionState.Connected)
                return await FailAsync("设备未连接，无法写入");

            // 服务端强校验数值上下限：前端写值弹窗的 min/max 仅为 HTML 输入约束（可被绕过），
            // 越限值禁止下发物理设备。布尔量（0/1 语义）不参与数值限幅校验。
            if (value is not bool && TryToNumber(value) is double numericValue)
            {
                if (vr.Min.HasValue && numericValue < vr.Min.Value)
                    return await FailAsync($"写入值 {numericValue} 低于变量 [{variableKey}] 下限 {vr.Min}");
                if (vr.Max.HasValue && numericValue > vr.Max.Value)
                    return await FailAsync($"写入值 {numericValue} 超过变量 [{variableKey}] 上限 {vr.Max}");
            }

            // 驱动写入在设备锁外执行：网络 IO 可能耗时秒级，持锁会阻塞同设备采集循环
            // 的全部内存态更新。写驱动期间设备不持有 Lock，采集可正常进行。
            try
            {
                await runtime.Driver.WriteAsync(vr, value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("设备 {DeviceId} 变量 [{VarKey}] 写入失败: {Msg}", deviceId, variableKey, ex.Message);
                return await FailAsync($"写入失败: {ex.Message}");
            }

            // 写成功后短暂持锁仅同步运行时内存态（纯内存赋值，微秒级），
            // 与采集调度（DeviceWorker 同样持 Lock 更新）串行化，消除竞态。
            await runtime.Lock.WaitAsync();
            try
            {
                vr.PreviousValue = vr.Value;
                vr.Value = value;
                vr.UpdateTime = DateTime.UtcNow;
                vr.IsChanged = false; // 置 false，避免下轮轮询因"值变化"再重复广播同一写入
            }
            finally
            {
                runtime.Lock.Release();
            }

            // 写成功后已在临界区内同步运行时内存值（见上）。此处经 SignalR 广播，使所有客户端刷新后能看到新值。
            try
            {
                await _notificationService.NotifyVariableUpdateAsync(deviceId, variableKey, value, vr.Quality, vr.UpdateTime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设备 {DeviceId} 变量 [{VarKey}] 写入回拨通知失败。", deviceId, variableKey);
            }

            // 发布进程内变量变化事件（非阻塞），供绑定引擎等订阅者消费。
            _changeBus.Publish(new VariableChangeEvent
            {
                DeviceId = deviceId,
                VariableKey = variableKey,
                Value = vr.Value,
                PreviousValue = vr.PreviousValue,
                Quality = vr.Quality,
                UpdateTime = vr.UpdateTime,
                Source = VariableChangeSource.UserWrite
            });

            // 同步更新实时快照：手动写入后不等下轮采集回读，MySQL 实时表即时反映新值
            // （否则最长延迟一个轮询周期）。
            try
            {
                _realtimeSnapshot.Update(
                    deviceId,
                    runtime.Device.Key,
                    variableKey,
                    vr.Name,
                    ToNumericSnapshotValue(value),
                    value?.ToString(),
                    vr.Quality.ToString(),
                    vr.UpdateTime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设备 {DeviceId} 变量 [{VarKey}] 写入后更新实时快照失败。", deviceId, variableKey);
            }

            await RecordVariableWriteAuditAsync(deviceId, variableKey, value, writeSource, true, null);
            return (true, null);
        }

        /// <summary>
        /// 尝试将变量值转为数值（写入限幅校验用）；无法转换返回 null。
        /// </summary>
        private static double? TryToNumber(object value)
        {
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                if (value is bool b) return b ? 1.0 : 0.0;
                return null;
            }
        }

        /// <summary>
        /// 转为实时快照的数值列（布尔按 0/1，无法转换按 0），与 DeviceWorker.TryUpdateRealtime 同语义。
        /// </summary>
        private static double ToNumericSnapshotValue(object value)
        {
            if (value is bool flag) return flag ? 1 : 0;
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 记录变量写入审计日志（仅非 HTTP 来源；审计失败不影响写值主业务）。
        /// </summary>
        private async Task RecordVariableWriteAuditAsync(int deviceId, string variableKey, object value, string? writeSource, bool success, string? errorMessage)
        {
            if (string.IsNullOrEmpty(writeSource))
            {
                return;
            }

            try
            {
                await _variableWriteAudit.RecordAsync(deviceId, variableKey, value, writeSource, success, errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设备 {DeviceId} 变量 [{VarKey}] 写入审计日志记录失败（已忽略）。", deviceId, variableKey);
            }
        }

        /// <summary>
        /// 将设备运行时的连接态与运行态映射为对外设备状态枚举。
        /// 注意：此处仅依据运行时内存态，不依赖数据库持久字段。
        /// </summary>
        private static DeviceStatus MapConnectionStateToStatus(Devices.DeviceRuntime runtime)
        {
            if (!runtime.Device.IsEnabled)
            {
                return DeviceStatus.Offline;
            }

            return runtime.ConnectionState switch
            {
                DeviceConnectionState.Connected => DeviceStatus.Online,
                DeviceConnectionState.Connecting or DeviceConnectionState.Initializing => DeviceStatus.Connecting,
                DeviceConnectionState.Error => DeviceStatus.Fault,
                DeviceConnectionState.Disconnected or DeviceConnectionState.Unknown => DeviceStatus.Offline,
                _ => DeviceStatus.Offline
            };
        }
    }
}
