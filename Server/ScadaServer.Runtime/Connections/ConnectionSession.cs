using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces;
using ScadaServer.Infrastructure.Communication;
using ScadaServer.Runtime.Devices;

namespace ScadaServer.Runtime.Connections
{
    /// <summary>
    /// 连接会话：一个 <see cref="DeviceConnection"/> 对应一条物理连接（进程内单例），
    /// 其下挂载的多台设备共享同一驱动实例。
    /// <para>
    /// 职责：持有唯一驱动实例；设备挂载/卸载（引用计数生命周期，末位离场销毁会话）；
    /// 连接状态扇出到挂载设备（复用设备级 ConnectionStateChanged → RuntimeManager 推送链路）；
    /// 会话级重连归口（闸门 + 探测 + 重建，P3）。
    /// </para>
    /// <para>
    /// 不职责：轮询调度（DeviceScheduler / DeviceWorker）、报警判定、历史落库、变量读写。
    /// 设备运行时不随会话重建而重建（变量值/报警计数/RunState 全部存活）。
    /// </para>
    /// </summary>
    public sealed class ConnectionSession : IRuntimeConnection, IAsyncDisposable
    {
        private DeviceConnection _connection;   // 配置实体（ConfigJson 真相源）；热更新时经 UpdateConnectionConfig 整体替换
        private readonly string _protocolKey;
        private readonly IProtocolDriverFactory _driverFactory;
        private readonly ILogger _logger;

        /// <summary>驱动实例：仅在会话生命周期方法内（Connect/Reconnect/Dispose）替换，读写路径经 volatile 快照。</summary>
        private volatile IProtocolDriver? _driver;

        /// <summary>挂载设备表：key = deviceId。挂载/卸载与会话销毁由 _lifecycleLock 串行化。</summary>
        private readonly ConcurrentDictionary<int, DeviceRuntime> _mounted = new();

        /// <summary>生命周期锁：串行化挂载/卸载/建连/销毁/重建。按项目约定永不 Dispose。</summary>
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

        private volatile bool _disposed;
        private volatile DeviceConnectionState _state = DeviceConnectionState.Initializing;

        // 重连闸门：0=空闲，1=重建在途（Interlocked，多设备并发断线信号收敛为一次重建）。
        private int _reconnecting;

        // 断线信号聚合计数（Interlocked）：用于判定"会话是否持续故障"的辅助观测。
        private int _failureSignals;

        /// <summary>当前驱动实例（无会话/未连接时为 null）。</summary>
        public IProtocolDriver? Driver => _driver;

        /// <summary>会话是否已销毁（终态，仅 DisposeAsync 置位）。</summary>
        public bool IsDisposed => _disposed;

        /// <summary>当前挂载设备数。</summary>
        public int MountCount => _mounted.Count;

        /// <summary>是否无任何挂载设备。</summary>
        public bool IsEmpty => _mounted.IsEmpty;

        /// <summary>挂载设备快照（ConcurrentDictionary 弱一致遍历的稳定副本，供运行期枚举/遍历卸载）。</summary>
        public IReadOnlyCollection<DeviceRuntime> MountedSnapshot => _mounted.Values.ToArray();

        /// <summary>刷新连接配置实体引用（配置热更新用）。仅更新 ConfigJson/IsEnabled 等参数真相源，不改变协议/挂载关系。</summary>
        public void UpdateConnectionConfig(DeviceConnection connection) => _connection = connection;

        /// <summary>最近一次状态翻转时刻（UTC）。</summary>
        public DateTime StateChangedAt { get; private set; } = DateTime.UtcNow;

        public int ConnectionId => _connection.Id;

        public string Key => string.IsNullOrEmpty(_connection.Name) ? $"#{_connection.Id}" : _connection.Name;

        public string ConfigJson => _connection.ConfigJson ?? "{}";

        /// <summary>
        /// 会话连接状态。setter 扇出到全部挂载设备（每台设备触发其 ConnectionStateChanged，
        /// 进而由 RuntimeManager 推送状态并落库）。仅在值变化时推进 StateChangedAt。
        /// </summary>
        public DeviceConnectionState State
        {
            get => _state;
            private set
            {
                if (_state == value) return;
                _state = value;
                StateChangedAt = DateTime.UtcNow;
                foreach (var rt in _mounted.Values)
                    rt.ConnectionState = value;
            }
        }

        /// <summary>初始化会话。</summary>
        /// <param name="connection">连接配置实体（ConfigJson 真相源）。</param>
        /// <param name="protocolKey">协议驱动键（Protocol.Key，用于工厂创建驱动）。</param>
        /// <param name="driverFactory">驱动工厂。</param>
        /// <param name="logger">日志组件。</param>
        public ConnectionSession(
            DeviceConnection connection,
            string protocolKey,
            IProtocolDriverFactory driverFactory,
            ILogger logger)
        {
            _connection = connection;
            _protocolKey = protocolKey;
            _driverFactory = driverFactory;
            _logger = logger;
        }

        /// <summary>
        /// 建连（幂等）：已有驱动实例直接返回已连接。首次建连或上次失败后的重试由调用方结合 State 决定触发。
        /// </summary>
        /// <returns>是否已连接。</returns>
        public async Task<bool> ConnectAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_disposed) return false;
                if (_driver != null) return true;

                var driver = _driverFactory.CreateDriver(_protocolKey);
                bool ok;
                try
                {
                    ok = await driver.ConnectAsync(this);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "连接 {ConnKey}(#{ConnId}) 建连异常。", Key, ConnectionId);
                    await SafeDisposeDriverAsync(driver);
                    State = DeviceConnectionState.Error;
                    return false;
                }

                if (ok)
                {
                    _driver = driver;
                    State = DeviceConnectionState.Connected;
                    return true;
                }

                await SafeDisposeDriverAsync(driver);
                State = DeviceConnectionState.Error;
                return false;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>挂载一台设备到本会话（引用计数），保持当前会话状态与设备同步。</summary>
        public async Task MountAsync(DeviceRuntime runtime)
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_disposed) return;
                _mounted[runtime.Device.Id] = runtime;
                runtime.Session = this;
                runtime.ConnectionState = State;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>从会话卸载一台设备并解绑 Session。返回会话是否已无任何挂载设备（由调用方据此销毁会话）。</summary>
        public async Task<bool> UnmountAsync(int deviceId)
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_mounted.TryRemove(deviceId, out var rt))
                {
                    rt.Session = null;
                }
                return _mounted.IsEmpty;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        // ===================== P3 会话级重连归口 =====================

        /// <summary>
        /// 断线信号上抛入口（DeviceWorker 断线判定触发）。聚合 + 闸门去重：
        /// 先探测连接存活——存活则判定设备级故障，不重建会话（避免坏地址设备反复触发健康连接重建）；
        /// 探测死亡则进入重建路径。
        /// </summary>
        /// <param name="sourceDeviceId">触发信号的设备（日志定位用）。</param>
        public async Task SignalConnectionFailureAsync(int sourceDeviceId)
        {
            Interlocked.Increment(ref _failureSignals);
            if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0)
            {
                return; // 已有重建在途：去重（多设备并发触发收敛为一次）。
            }

            try
            {
                var driver = _driver;
                if (driver != null)
                {
                    bool alive;
                    try { alive = await driver.IsAliveAsync(); }
                    catch { alive = false; }

                    if (alive)
                    {
                        Interlocked.Exchange(ref _failureSignals, 0);
                        _logger.LogWarning(
                            "连接 {ConnKey}(#{ConnId}) 收到设备 {DeviceId} 断线信号，但探测连接存活，判定为设备级故障，不重建会话。",
                            Key, ConnectionId, sourceDeviceId);
                        return;
                    }
                }

                await ReconnectCoreAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _reconnecting, 0);
            }
        }

        /// <summary>会话级重连（闸门保护，至少一次成功才返回 true；返回 false 表示重建在途未执行）。</summary>
        public async Task<bool> ReconnectAsync()
        {
            if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0)
            {
                return false;
            }

            try
            {
                return await ReconnectCoreAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _reconnecting, 0);
            }
        }

        /// <summary>
        /// 会话重建（顺序为硬约束，D5）：
        /// 1. 扇出 Error + 取消挂载 Worker 并等待收尾（杜绝"Worker 用着旧驱动时被脚下拆除"）；
        /// 2. Dispose 旧驱动；
        /// 3. 新驱动建连（连接参数从 _connection 取，配置热更新后即新值）；
        /// 4. 换驱动 + 扇出 Connected + 复位各设备计数（设备运行时不重建）。
        /// </summary>
        private async Task<bool> ReconnectCoreAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_disposed) return false;

                var devices = _mounted.Values.ToList();

                // 1) 扇出 Error + 取消挂载 Worker 并等待收尾。
                State = DeviceConnectionState.Error;
                foreach (var rt in devices)
                {
                    rt.NeedsReconnect = true;
                    rt.CancelWorker();
                }
                foreach (var rt in devices)
                {
                    if (rt.WorkerTask != null)
                    {
                        try
                        {
                            await rt.WorkerTask.WaitAsync(TimeSpan.FromSeconds(3));
                        }
                        catch (TimeoutException)
                        {
                            _logger.LogWarning("连接 {ConnKey}(#{ConnId}) 设备 {DeviceKey} Worker 停止超时（3s），可能仍在退出。",
                                Key, ConnectionId, rt.Device.Key);
                        }
                    }
                }

                // 2) Dispose 旧驱动。
                var old = _driver;
                _driver = null;
                await SafeDisposeDriverAsync(old);

                // 3) 新驱动建连。
                var driver = _driverFactory.CreateDriver(_protocolKey);
                bool ok;
                try
                {
                    ok = await driver.ConnectAsync(this);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "连接 {ConnKey}(#{ConnId}) 重连建连异常。", Key, ConnectionId);
                    await SafeDisposeDriverAsync(driver);
                    State = DeviceConnectionState.Error;
                    return false;
                }

                if (!ok)
                {
                    await SafeDisposeDriverAsync(driver);
                    State = DeviceConnectionState.Error;
                    return false;
                }

                // 4) 换驱动 + 扇出 + 复位（设备运行时不重建 → 变量值/报警计数/RunState 存活）。
                _driver = driver;
                Interlocked.Exchange(ref _failureSignals, 0);
                foreach (var rt in devices)
                {
                    rt.ConsecutiveFailureCount = 0;
                    rt.NeedsReconnect = false;
                }
                State = DeviceConnectionState.Connected;
                return true;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>销毁会话：置终态、释放驱动、解绑全部挂载设备。幂等。</summary>
        public async ValueTask DisposeAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_disposed) return;
                _disposed = true;
                State = DeviceConnectionState.Disconnected;
                var driver = _driver;
                _driver = null;
                await SafeDisposeDriverAsync(driver);
                foreach (var rt in _mounted.Values)
                {
                    rt.Session = null;
                }
                _mounted.Clear();
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        private async Task SafeDisposeDriverAsync(IProtocolDriver? driver)
        {
            if (driver == null) return;
            try
            {
                await driver.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "连接 {ConnKey}(#{ConnId}) 驱动释放异常（已忽略）。", Key, ConnectionId);
            }
        }
    }
}