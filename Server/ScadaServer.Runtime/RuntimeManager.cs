using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
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
using ScadaServer.Runtime.DataConversion;
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
        /// 设备驱动写入的兜底超时（毫秒，配置 Devices:WriteTimeoutMs，默认 5000，收敛 500~60000）。
        /// <para>
        /// S7 等驱动的写路径已在驱动层经 CancellationToken 封顶（见 S7Driver），此处为
        /// 跨驱动（含 OPC UA / 虚拟设备及未来驱动）的统一兜底：防止无超时驱动的网络 IO
        /// 无界挂起沿调用链放大（绑定引擎异步等待、脚本写桥同步等待、HTTP 请求同步等待）。
        /// 超时后底层写入可能迟到落地（孤儿任务），结果以写入审计日志为准。
        /// </para>
        /// </summary>
        private readonly int _deviceWriteTimeoutMs;

        /// <summary>
        /// 各设备上一次对外推送/持久化的状态，用于去重，避免重复触发 StatusChanged。
        /// </summary>
        private readonly ConcurrentDictionary<int, DeviceStatus> _lastPushedStatus = new();

        /// <summary>
        /// 设备自动重连的进程级统计：key = 设备ID。
        /// 生命周期为进程级：跨 runtime 重建（重连/重载会整体替换 DeviceRuntime 对象）累计——
        /// 挂在 DeviceRuntime 实例上的计数会在每次重连时随旧对象销毁而丢失（方案 P1）。
        /// 语义：进程启动以来自动重连发起次数（含初始连接失败后的占位重连）。
        /// </summary>
        private readonly ConcurrentDictionary<int, DeviceReconnectStats> _reconnectStats = new();

        /// <summary>
        /// 重连在途时被移除的占位运行时（P4 阶段修复：重连 connect 期间移除后不在册）。
        /// 保留旧占位引用，使 TryGetRuntimeSnapshot 在重连窗口仍能返回该设备的断线诊断快照
        /// （配合 D5-a 回退：不因"重连期间终止"而丢失观测面）。连接完成后随即清除。
        /// </summary>
        private readonly ConcurrentDictionary<int, Devices.DeviceRuntime> _reconnecting = new();

        /// <summary>单设备重连统计（进程级聚合）。</summary>
        private sealed class DeviceReconnectStats
        {
            public int Count { get; set; }
            public DateTime? LastReconnectAt { get; set; }
        }

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
            IVariableWriteAuditRecorder variableWriteAudit,
            IConfiguration? configuration)
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

            _deviceWriteTimeoutMs = ParseConfigTimeout(configuration?["Devices:WriteTimeoutMs"], 5000);
        }

        /// <summary>配置超时值解析：null/非法取缺省，越界收敛到 [500, 60000] 毫秒。</summary>
        private static int ParseConfigTimeout(string? value, int fallbackMs)
        {
            return int.TryParse(value, out var ms) ? Math.Clamp(ms, 500, 60000) : fallbackMs;
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
            // Device → Controller / Connection(→Protocol) / DataModel → DataPointMapping(→DataPoint)
            // 阶段 6：连接参数与驱动协议均只取 Device.Connection（DataModel 仍加载用于主模型/绑定日志与空值守卫）。
            // 运行时仅消费新模型；严禁直接访问 DataPoint.Address，地址由 DataPointMapping 提供。
            var devices = await db.Devices
                .Include(d => d.Controller)
                .Include(d => d.Connection).ThenInclude(c => c!.Protocol)
                .Include(d => d.Model)
                .Include(d => d.DataPointMappings).ThenInclude(dv => dv.DataPoint)
                // 阶段 5：绑定行仅用于启动日志统计（N 台绑定 / 主模型），运行时变量解析仍以 Device.Model（主模型）为唯一生效集合。
                .Include(d => d.DeviceDataModels)
                .Where(d => d.IsEnabled)
                .ToListAsync();

            _logger.LogInformation("找到 {Count} 个已启用设备。", devices.Count);

            // 阶段 5 运维确认行：展示各设备绑定模型总数（≥1 含主模型）。
            // 附加（非主）模型不参与采集——多模型变量合并为后续版本特性，本阶段运行时只认主模型。
            foreach (var device in devices)
            {
                var bound = device.DeviceDataModels?.Count ?? 0;
                var primary = device.DeviceDataModels?.FirstOrDefault(b => b.IsPrimary);
                _logger.LogInformation(
                    "设备 {Key}（ID {DeviceId}）绑定 {Bound} 个模型，主模型 {Primary}（ID {PrimaryId}）",
                    device.Key, device.Id, bound,
                    primary != null && device.Model != null ? device.Model.Name : (device.Model?.Name ?? "(缺失)"),
                    device.ModelId);
            }

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
        /// 移除旧运行时后重新走完整注册流程（加载设备图 → 连接新驱动 → 注册）；
        /// 若连接仍失败，注册流程会再次注册占位运行时，形成退避重试循环。
        /// <para>
        /// 适用两类入口：初始连接失败的占位运行时（无 Worker/驱动，各清理步骤自然跳过）；
        /// 以及<b>运行中因断线转入重连</b>的运行时（Worker 判定连续失败已置
        /// NeedsReconnect 并退出）——此时必须先停 Worker 收尾、Dispose 旧驱动，
        /// 避免旧 Plc 连接泄漏与重建期间的双重采集。
        /// </para>
        /// </summary>
        /// <param name="deviceId">设备 ID</param>
        public async Task ReconnectDeviceAsync(int deviceId)
        {
            // 重入门闸：仅当当前在册运行时确为待重连状态时才执行，避免并发重连。
            // 后续 TryRemove 作为原子闸门：并发调用中只有一个能成功移除并继续。
            if (!DeviceRuntimes.TryGetValue(deviceId, out var current) || !current.NeedsReconnect)
            {
                return;
            }

            if (!DeviceRuntimes.TryRemove(deviceId, out var runtime))
            {
                return;
            }

            // 保留被移除的占位运行时引用：重连 connect 期间该设备不在 DeviceRuntimes，
            // 快照端点据此回退返回占位（断线诊断态），避免重连窗口 404（P4 回归修复）。
            _reconnecting[deviceId] = runtime;

            try
            {
                // 重连计数（进程级）：仅统计实际发起的重连（门闸通过 = 本次重连成立）。
                // 初始连接失败不计入——它由 ConnectionStateChangedAt + LastError + DeviceStatus=Fault
                // 共同表达；避免「初始失败→占位→重连失败→再占位」链式重复计数。
                var stats = _reconnectStats.GetOrAdd(deviceId, _ => new DeviceReconnectStats());
                stats.Count++;
                stats.LastReconnectAt = DateTime.UtcNow;

                _lastPushedStatus.TryRemove(deviceId, out _);

                // 停止残留 Worker 并释放旧驱动（占位运行时无 Worker/驱动，自然跳过）。
                await StopWorkerAndDisposeDriverAsync(runtime);

                // 重新走完整注册流程（内部先幂等移除，再加载、连接、注册）。
                await RegisterDeviceAsync(deviceId);
            }
            finally
            {
                // 连接（成功/失败）均会经 RegisterDeviceAsync 重新注册新运行时到 DeviceRuntimes，
                // 此处清除重连占位，避免在册恢复后仍读取到旧占位数据。
                _reconnecting.TryRemove(deviceId, out _);
            }
        }

        /// <inheritdoc/>
        public async Task RemoveDeviceAsync(int deviceId)
        {
            if (!DeviceRuntimes.TryRemove(deviceId, out var runtime))
            {
                return;
            }

            _lastPushedStatus.TryRemove(deviceId, out _);

            // 1) 停止 Worker 并释放驱动（先收尾再释放，串行化驱动访问）。
            await StopWorkerAndDisposeDriverAsync(runtime);

            // 2) 推送 Offline，使界面即时反映设备已移除/被禁用。
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
        /// 停止指定运行时的采集 Worker 并释放其驱动（RemoveDeviceAsync 与 ReconnectDeviceAsync 共用）。
        /// <para>
        /// 顺序保证：先取消 Worker 并等待收尾（3s 超时容忍），再 Dispose 驱动——
        /// 确保 Worker 不再访问驱动后才释放底层连接（如 S7 的 Plc），
        /// 避免使用已关闭连接与 Plc 泄漏。占位运行时（无 Worker / 无驱动）各步骤自然跳过。
        /// </para>
        /// <para>
        /// 在 DispatchSync 锁内取消并读取 WorkerTask：与调度器派发临界区串行化，
        /// 确保读到的句柄必然属于"最后一个派发的 Worker"——调用方已先从 DeviceRuntimes
        /// 移除该运行时，此后调度器派发校验（在册 + 同引用）必然失败，不会再有新 Worker。
        /// </para>
        /// </summary>
        private async Task StopWorkerAndDisposeDriverAsync(Devices.DeviceRuntime runtime)
        {
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
        }

        /// <summary>
        /// 按设备 ID 加载其完整运行期依赖对象图
        /// （Device → Controller / Connection(→Protocol) / DataModel(→Protocol) → DataPointMapping(→DataPoint)）。
        /// 每次调用处于独立 Scope 内解析 DbContext，避免 Singleton 持有 Scoped DbContext。
        /// </summary>
        private async Task<Device?> LoadDeviceGraphByIdAsync(int deviceId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
            return await db.Devices
                .Include(d => d.Controller)
                .Include(d => d.Connection).ThenInclude(c => c!.Protocol)
                .Include(d => d.Model)
                .Include(d => d.DataPointMappings).ThenInclude(dv => dv.DataPoint)
                .FirstOrDefaultAsync(d => d.Id == deviceId);
        }

        /// <summary>
        /// 查询设备当前未恢复（活跃）报警条数（方案 P2：runtime 重建后与 AlarmRecords 对齐）。
        /// 查询失败返回 0 并告警——不阻塞设备注册主流程（与报警规则加载失败同容错等级）。
        /// </summary>
        private async Task<int> CountActiveAlarmsAsync(ScadaDbContext db, int deviceId)
        {
            try
            {
                return await db.AlarmRecords
                    .CountAsync(a => a.DeviceId == deviceId && a.RecoveredAt == null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "查询设备 {DeviceId} 活跃报警数失败，报警计数从 0 开始。", deviceId);
                return 0;
            }
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

                // 协议真相源（阶段 6 起）：设备默认连接所绑协议。Connection 缺失（不应出现的异常场景）
                // 视为无有效协议并跳过该设备——已删除对 DataModel.Protocol 的运行时回退（6.2）。
                var effectiveProtocol = device.Connection?.Protocol;
                var driverKey = effectiveProtocol?.Key;
                if (string.IsNullOrWhiteSpace(driverKey))
                {
                    _logger.LogWarning("设备 {Key} 未绑定有效协议（Key 为空），已跳过。", device.Key);
                    return false;
                }
                var protocolLabel = driverKey;

                // 先构建运行时对象（Driver 待连接成功后赋值），再以 IRuntimeDevice 只读视图连接驱动。
                // 第九阶段起：驱动只接收 RuntimeDevice / RuntimeVariable，不再感知 Device / DataModel / DataPoint。
                var runtime = new Devices.DeviceRuntime(device)
                {
                    Device = device,
                    Model = model,
                    Protocol = effectiveProtocol,
                    Area = device.Area
                };

                // 驱动创建与连接异常同样视为连接失败（进入占位重连路径），避免初始化异常被外层
                // catch 吞掉后设备永不重试。
                bool connected;
                IProtocolDriver? driver = null;
                try
                {
                    driver = _driverFactory.CreateDriver(driverKey);
                    connected = await driver.ConnectAsync(runtime);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "设备 {Key} ({Protocol}) 连接时发生异常。", device.Key, protocolLabel);
                    connected = false;
                }

                // 连接成功则由运行时持有驱动；任何失败路径（返回 false 或抛异常）都必须释放驱动，
                // 避免异常路径遗留驱动内部资源（如半开的 OPC UA 会话）。
                if (connected)
                {
                    runtime.Driver = driver!;
                }
                else if (driver != null)
                {
                    await driver.DisposeAsync();
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

                var now = DateTime.UtcNow;
                foreach (var dv in device.DataPointMappings ?? Enumerable.Empty<DataPointMapping>())
                {
                    if (dv.DataPoint == null)
                    {
                        _logger.LogWarning(
                            "设备 {Key} 的设备变量 #{DvId} 缺少关联模型变量，已跳过。", device.Key, dv.Id);
                        continue;
                    }

                    // 构建 RuntimeVariable：变量定义来自 DataPoint，设备配置来自 DataPointMapping。
                    runtime.Variables[dv.Id] = new VariableRuntime
                    {
                        Definition = dv.DataPoint,
                        Instance = dv,
                        NextPollTime = now // 首轮立即采集
                    };
                }

                // 状态初始化（方案 P2/P3）：连接成功后、注册前，从 DB 回填报警计数与 RunState。
                // 设备级报警计数与 AlarmRecords 对齐：runtime 重建（重连/重载/重启）后
                // 立即一致，消除「Worker 重建丢状态 → HasAlarm 卡 false/true」的幽灵报警窗口。
                // RunState 从 Devices 持久化列恢复：重启/重载不丢失维护/停机标记。
                // 每次注册/重接连通即一次轻量 COUNT 查询，频率极低，接受额外 scope 开销。
                using (var initScope = _scopeFactory.CreateScope())
                {
                    var initDb = initScope.ServiceProvider.GetRequiredService<ScadaDbContext>();
                    runtime.InitializeAlarmCount(await CountActiveAlarmsAsync(initDb, device.Id));
                    if (device.RunState.HasValue)
                    {
                        runtime.RestoreRunState(device.RunState.Value, device.RunStateChangedAt);
                    }
                }

                RegisterDevice(runtime);

                _deviceRegistry.UpdateDevice(device,
                    (device.DataPointMappings ?? Enumerable.Empty<DataPointMapping>())
                        .Select(dv => dv.DataPoint)
                        .Where(mv => mv != null)
                        .Cast<DataPoint>()
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
        public bool TryGetRuntimeSnapshot(int deviceId, out DeviceRuntimeSnapshotDto? snapshot)
        {
            if (!DeviceRuntimes.TryGetValue(deviceId, out var runtime))
            {
                // P4 回归修复：重连 connect 期间设备被移出在册表以原子化重连、并避免旧 Worker 残留，
                // 该短暂窗口内回退到被保留的占位运行时，保证断线诊断快照始终可查（D5-a 回退）。
                // 未进入重连（禁用/从未注册）的设备仍返回 false → 404，语义不变。
                if (!_reconnecting.TryGetValue(deviceId, out runtime))
                {
                    snapshot = null;
                    return false;
                }
            }

            _reconnectStats.TryGetValue(deviceId, out var stats);

            // 无锁读取多个内存字段：字段间一致性不保证（误差窗口 ≤ 一个采集轮次）。
            // 刻意不加 DeviceRuntime.Lock——那会与采集循环互相阻塞，见方案 P6。
            snapshot = new DeviceRuntimeSnapshotDto
            {
                DeviceId = runtime.Device.Id,
                DeviceKey = runtime.Device.Key,
                DeviceName = runtime.Device.Name,
                IsEnabled = runtime.Device.IsEnabled,

                Status = MapConnectionStateToStatus(runtime),   // 复用既有映射，保证与列表接口同源同值
                ConnectionState = runtime.ConnectionState,
                ConnectionStateChangedAt = runtime.ConnectionStateChangedAt,

                RunState = runtime.RunState,
                RunStateChangedAt = runtime.StateChangedAt,
                HasAlarm = runtime.HasAlarm,

                LastError = runtime.LastError,
                LastCommunicationTime = runtime.LastCommunicationTime,
                ConsecutiveFailureCount = runtime.ConsecutiveFailureCount,
                ReconnectCount = stats?.Count ?? 0,             // 进程级（P1/D9-a）
                LastReconnectAt = stats?.LastReconnectAt,
                AverageResponseTimeMs = runtime.AverageResponseTime,
                SuccessCount = runtime.SuccessCount,
                FailureCount = runtime.FailureCount,
                PollRoundCount = runtime.PollRoundCount,

                VariableCount = runtime.Variables.Count,
                EnabledVariableCount = runtime.Variables.Values.Count(v => v.IsEnabled)
            };
            return true;
        }

        /// <inheritdoc/>
        public void SetDeviceRunState(int deviceId, DeviceRunState runState)
        {
            if (DeviceRuntimes.TryGetValue(deviceId, out var runtime))
                runtime.SetRunState(runState);
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

            // 写入方向：工程值 → 驱动原始值。当前版本恒等透传（未启用反算公式，行为与改造前一致）；
            // 扩展点已收敛在 VariableScaling.ToRaw，将来支持反算表达式无需改动本方法。
            var rawValue = VariableScaling.ToRaw(vr, value);

            // 驱动写入在设备锁外执行：网络 IO 可能耗时秒级，持锁会阻塞同设备采集循环
            // 的全部内存态更新。写驱动期间设备不持有 Lock，采集可正常进行。
            // WaitAsync 兜底超时：驱动层（如 S7Driver）已各自封顶，此处统一防御无超时驱动的
            // 无界挂起（脚本写桥同步等待 / HTTP 请求等待 / 绑定引擎等待都会被其拖死）。
            try
            {
                await runtime.Driver.WriteAsync(vr, rawValue)
                    .WaitAsync(TimeSpan.FromMilliseconds(_deviceWriteTimeoutMs));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning(
                    "设备 {DeviceId} 变量 [{VarKey}] 写入超时（>{TimeoutMs}ms），已放弃等待：底层写入可能仍在进行（孤儿任务），最终结果以写入审计日志为准。",
                    deviceId, variableKey, _deviceWriteTimeoutMs);
                return await FailAsync($"写入超时（>{_deviceWriteTimeoutMs}ms），底层写入仍在进行，结果以审计日志为准");
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
