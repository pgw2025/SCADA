using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 设备删除服务实现：删除设备前检查对外接口引用，并在同一事务内级联清理
    /// 传感器、变量触发器、协议配置后再删除设备本身。
    /// <para>注：设备变量（DeviceVariable）随设备删除由数据库外键级联清理（ON DELETE CASCADE），无需在此显式删除。</para>
    /// </summary>
    public class DeviceDeletionService : IDeviceDeletionService
    {
        private readonly IDeviceRepository _repository;
        private readonly ISensorRepository _sensorRepository;
        private readonly IVariableTriggerRepository _triggerRepository;
        private readonly IExposedInterfaceRepository _interfaceRepository;
        private readonly IRepository<DeviceConfig, int> _configRepository;
        private readonly IUnitOfWork _uow;

        public DeviceDeletionService(
            IDeviceRepository repository,
            ISensorRepository sensorRepository,
            IVariableTriggerRepository triggerRepository,
            IExposedInterfaceRepository interfaceRepository,
            IRepository<DeviceConfig, int> configRepository,
            IUnitOfWork uow)
        {
            _repository = repository;
            _sensorRepository = sensorRepository;
            _triggerRepository = triggerRepository;
            _interfaceRepository = interfaceRepository;
            _configRepository = configRepository;
            _uow = uow;
        }

        public async Task DeleteAsync(int deviceId)
        {
            var entity = await _repository.GetByIdAsync(deviceId);
            if (entity == null) return;

            // 1. 依赖检查：检查是否被对外接口引用
            var interfaces = await _interfaceRepository.GetListAsync(i => i.DeviceId == deviceId);
            if (interfaces.Any())
            {
                throw new BusinessException($"无法删除设备 '{entity.Name}'，因为它已被配置到 {interfaces.Count} 个对外数据接口中。请先解除绑定。");
            }

            await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                // 删除级联数据
                await _sensorRepository.DeleteRangeAsync(s => s.DeviceId == deviceId);
                await _triggerRepository.DeleteRangeAsync(tr => tr.DeviceId == deviceId);

                await _configRepository.DeleteRangeAsync(c => c.DeviceId == deviceId);

                // 删除设备
                await _repository.DeleteAsync(entity);

                return true;
            });
        }
    }
}