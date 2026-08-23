using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScadaServer.Domain.Enums;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Runtime.Interface;

namespace ScadaServer.WebApi.Services
{
    /// <summary>
    /// 设备状态持久化订阅者（Singleton）。
    /// 订阅 RuntimeManager 的状态变更事件，将最近已知状态落库到 Devices.LastKnownStatus，
    /// 使进程重启后仍有最后状态可读，避免重启瞬间所有设备显示为未定义状态。
    ///
    /// 由于 RuntimeManager 为 Singleton 而 DbContext 为 Scoped，落库时通过 IServiceScopeFactory
    /// 开辟独立 Scope，避免从根容器捕获 Scoped 服务。
    /// </summary>
    public class DeviceStatusPersistenceSubscriber
    {
        private readonly IRuntimeManager _runtimeManager;
        private readonly IServiceScopeFactory _scopeFactory;

        public DeviceStatusPersistenceSubscriber(
            IRuntimeManager runtimeManager,
            IServiceScopeFactory scopeFactory)
        {
            _runtimeManager = runtimeManager;
            _scopeFactory = scopeFactory;

            _runtimeManager.StatusChanged += OnStatusChanged;
        }

        private async void OnStatusChanged(object? sender, DeviceStatusChangedEventArgs e)
        {
            // 仅在状态值变化时触发，频率可控；此处仅做一次轻量列更新。
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

                await db.Devices
                    .Where(d => d.Id == e.DeviceId)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastKnownStatus, e.Status));
            }
            catch (Exception ex)
            {
                // 持久化失败不应影响运行时采集循环与实时推送
                Console.Error.WriteLine($"[Persistence] 设备状态落库失败 (DeviceId={e.DeviceId}): {ex.Message}");
            }
        }
    }
}
