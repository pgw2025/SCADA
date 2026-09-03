using Microsoft.EntityFrameworkCore;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 设备-数据模型绑定应用服务实现（阶段 5：DeviceDataModels 多对多绑定管理）。
    /// <para>
    /// 主模型（IsPrimary=true）行与 <see cref="Device.ModelId"/> 的双写一致性收敛于本服务
    /// （Bind/SetPrimary 的事务单点）与 DeviceAppService.CreateAsync（创建设备双写）：
    /// 其余任何代码路径都不直接改动 Device.ModelId，避免失同步。
    /// </para>
    /// <para>
    /// 保守策略（与方案文档一致）：附加（非主）模型绑定仅供管理界面与未来扩展；
    /// 运行时变量解析仍以主模型为唯一生效集合（RuntimeManager Include 链走 Device.Model，本阶段零改动）。
    /// </para>
    /// </summary>
    public class DeviceDataModelAppService : IDeviceDataModelAppService
    {
        /// <summary>设备-模型绑定仓储（DeviceDataModels 中间表）。</summary>
        private readonly IDeviceDataModelRepository _repository;
        /// <summary>设备仓储：校验设备存在 + 主模型双写（Device.ModelId）。</summary>
        private readonly IDeviceRepository _deviceRepository;
        /// <summary>数据模型仓储：校验模型存在/已发布并读取版本快照。</summary>
        private readonly IDataModelRepository _modelRepository;
        /// <summary>模型变量仓储：统计各模型变量数（绑定列表展示）。</summary>
        private readonly IDataPointRepository _dataPointRepository;
        /// <summary>设备变量仓储：解绑前检查该模型下的设备变量实例引用。</summary>
        private readonly IDataPointMappingRepository _dataPointMappingRepository;
        /// <summary>工作单元：事务（主模型降级 + 提升 + Device.ModelId 双写）。</summary>
        private readonly IUnitOfWork _uow;
        /// <summary>运行时设备管理器：切主后按启用状态热重载（复用既有 ReloadDeviceAsync 链路）。</summary>
        private readonly IRuntimeDeviceManager _runtimeDeviceManager;

        public DeviceDataModelAppService(
            IDeviceDataModelRepository repository,
            IDeviceRepository deviceRepository,
            IDataModelRepository modelRepository,
            IDataPointRepository dataPointRepository,
            IDataPointMappingRepository dataPointMappingRepository,
            IUnitOfWork uow,
            IRuntimeDeviceManager runtimeDeviceManager)
        {
            _repository = repository;
            _deviceRepository = deviceRepository;
            _modelRepository = modelRepository;
            _dataPointRepository = dataPointRepository;
            _dataPointMappingRepository = dataPointMappingRepository;
            _uow = uow;
            _runtimeDeviceManager = runtimeDeviceManager;
        }

        /// <summary>
        /// 查询某设备的全部绑定（含模型摘要与模型变量数）。
        /// </summary>
        public async Task<List<DeviceModelBindingDto>> GetByDeviceAsync(int deviceId)
        {
            var bindings = await _repository.GetByDeviceAsync(deviceId);
            if (bindings.Count == 0)
            {
                return new List<DeviceModelBindingDto>();
            }

            // 模型变量数：单次加载全量后按 ModelId 分组（变量模板表量级小，与设备列表 N+1 优化同思路）。
            var dataPoints = await _dataPointRepository.GetListAsync();
            var countsByModel = dataPoints
                .GroupBy(mv => mv.ModelId)
                .ToDictionary(g => g.Key, g => g.Count());

            return bindings
                .Select(b => MapToDto(b, countsByModel.TryGetValue(b.DataModelId, out var c) ? c : 0))
                .ToList();
        }

        /// <summary>
        /// 绑定模型：校验模型存在/已发布/未重复；绑定行版本取模型当前版本快照。
        /// <paramref name="dto.IsPrimary"/> 为 true（或设备尚无任何绑定，自动提升为主）时，
        /// 在事务内降级旧主模型并同步 Device.ModelId（唯一双写点）。
        /// </summary>
        public async Task<List<DeviceModelBindingDto>> BindAsync(int deviceId, BindDeviceDataModelDto dto)
        {
            var model = await RequireModelAsync(dto.DataModelId);
            var existingBindings = await _repository.GetByDeviceForUpdateAsync(deviceId);
            if (existingBindings.Any(b => b.DataModelId == dto.DataModelId))
            {
                throw new BusinessException($"模型 '{model.Name}' 已绑定到该设备，请勿重复绑定");
            }

            // 设备尚无任何绑定（理论上仅发生在迁移未回填/直插库的异常情形）时，首个绑定强制提升为主模型，
            // 否则设备将处于"无主模型"的悬挂态（Device.ModelId 与 IsPrimary 双写不变量被破坏）。
            var effectivePrimary = dto.IsPrimary || existingBindings.Count == 0;

            var now = DateTime.UtcNow;
            var binding = new DeviceDataModel
            {
                DeviceId = deviceId,
                DataModelId = dto.DataModelId,
                Version = NormalizeVersion(model.Version),
                IsPrimary = effectivePrimary,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            if (!effectivePrimary)
            {
                // 附加（非主）绑定：单行插入即可（不改 Device.ModelId）。
                await _repository.InsertAsync(binding);
                return await GetByDeviceAsync(deviceId);
            }

            // 主绑定：事务内降级旧主 + 插入新主 + 同步 Device.ModelId（唯一双写点）。
            await _uow.ExecuteInTransactionAsync(async _ =>
            {
                var device = await RequireDeviceForUpdateAsync(deviceId);
                var bindings = await _repository.GetByDeviceForUpdateAsync(deviceId);

                foreach (var b in bindings.Where(b => b.IsPrimary))
                {
                    b.IsPrimary = false;
                    b.UpdatedAt = now;
                    await _repository.UpdateAsync(b);
                }

                await _repository.InsertAsync(binding);

                if (device.ModelId != binding.DataModelId)
                {
                    device.ModelId = binding.DataModelId;
                    device.UpdatedAt = now;
                    await _deviceRepository.UpdateAsync(device);
                }

                return true;
            });

            await ReloadRuntimeIfEnabledAsync(deviceId);
            return await GetByDeviceAsync(deviceId);
        }

        /// <summary>
        /// 切换主模型：目标必须为已绑定模型；事务内降级旧主、提升目标并同步 Device.ModelId（唯一双写点），
        /// 随后按启用状态热重载设备运行时（复用既有 ReloadDeviceAsync 链路，行为与改主模型一致）。
        /// </summary>
        public async Task<List<DeviceModelBindingDto>> SetPrimaryAsync(int deviceId, DeviceDataModelRequest request)
        {
            var bindings = await _repository.GetByDeviceForUpdateAsync(deviceId);
            var target = bindings.FirstOrDefault(b => b.DataModelId == request.DataModelId);
            if (target == null)
            {
                throw new BusinessException($"该模型（ID={request.DataModelId}）未绑定到当前设备，请先绑定再设为主模型");
            }

            // 已是主模型：幂等返回，不触发任何写操作。
            if (target.IsPrimary)
            {
                return await GetByDeviceAsync(deviceId);
            }

            var now = DateTime.UtcNow;
            await _uow.ExecuteInTransactionAsync(async _ =>
            {
                var device = await RequireDeviceForUpdateAsync(deviceId);
                var current = await _repository.GetByDeviceForUpdateAsync(deviceId);

                foreach (var b in current.Where(b => b.IsPrimary))
                {
                    b.IsPrimary = false;
                    b.UpdatedAt = now;
                    await _repository.UpdateAsync(b);
                }

                // 重新定位目标行（同一 DbContext 跟踪图内再次查询，返回同一跟踪实例）。
                var trackedTarget = current.First(b => b.DataModelId == request.DataModelId);
                trackedTarget.IsPrimary = true;
                trackedTarget.UpdatedAt = now;
                await _repository.UpdateAsync(trackedTarget);

                if (device.ModelId != trackedTarget.DataModelId)
                {
                    device.ModelId = trackedTarget.DataModelId;
                    device.UpdatedAt = now;
                    await _deviceRepository.UpdateAsync(device);
                }

                return true;
            });

            await ReloadRuntimeIfEnabledAsync(deviceId);
            return await GetByDeviceAsync(deviceId);
        }

        /// <summary>
        /// 解绑模型：主模型不可解绑（须先切主）；该模型下存在被本设备实例化的设备变量时拒绝
        /// （MVP 策略：拒绝并提示先清理，不做级联删除——与方案文档 5.2 一致）。
        /// </summary>
        public async Task<List<DeviceModelBindingDto>> UnbindAsync(int deviceId, int dataModelId)
        {
            var binding = (await _repository.GetByDeviceForUpdateAsync(deviceId))
                .FirstOrDefault(b => b.DataModelId == dataModelId);
            if (binding == null)
            {
                // DELETE 幂等：未绑定视为已成功。
                return await GetByDeviceAsync(deviceId);
            }

            if (binding.IsPrimary)
            {
                throw new BusinessException("主模型不可解绑，请先切换其他模型为主模型");
            }

            // 引用检查：该模型下的模型变量是否被本设备实例化（此前曾为主模型后切走的残留场景）。
            var dataPointIds = (await _dataPointRepository.GetListAsync(mv => mv.ModelId == dataModelId))
                .Select(mv => mv.Id)
                .ToList();
            if (dataPointIds.Count > 0)
            {
                var referenced = await _dataPointMappingRepository.CountAsync(dv =>
                    dv.DeviceId == deviceId && dataPointIds.Contains(dv.DataPointId));
                if (referenced > 0)
                {
                    throw new BusinessException(
                        $"模型下仍有 {referenced} 个设备变量实例引用该模型的变量，请先在设备变量管理中清理后再解绑");
                }
            }

            await _repository.DeleteAsync(binding.Id);
            return await GetByDeviceAsync(deviceId);
        }

        /// <summary>加载设备（跟踪查询，无导航），不存在抛友好异常；供主模型双写场景使用。</summary>
        private async Task<Device> RequireDeviceForUpdateAsync(int deviceId)
        {
            var device = await _deviceRepository.GetByIdForUpdateAsync(deviceId);
            if (device == null)
            {
                throw new BusinessException($"ID 为 {deviceId} 的设备不存在");
            }

            return device;
        }

        /// <summary>加载数据模型（含协议导航），不存在/未发布抛友好异常；版本快照取模型当前 Version。</summary>
        private async Task<DataModel> RequireModelAsync(int dataModelId)
        {
            var model = await _modelRepository.GetByIdAsync(dataModelId);
            if (model == null)
            {
                throw new BusinessException($"ID 为 {dataModelId} 的数据模型不存在");
            }

            if (!model.IsPublished)
            {
                throw new BusinessException($"模型 '{model.Name}' 未发布，不可绑定到设备");
            }

            return model;
        }

        /// <summary>绑定版本快照规范化：空白回退 "1.0"（与 DataModelAppService NormalizeVersion 语义一致）。</summary>
        private static string NormalizeVersion(string? version)
            => string.IsNullOrWhiteSpace(version) ? "1.0" : version.Trim();

        /// <summary>切主后按启用状态热重载设备运行时；未启用/不存在时静默跳过。</summary>
        private async Task ReloadRuntimeIfEnabledAsync(int deviceId)
        {
            var device = await _deviceRepository.GetByIdAsync(deviceId);
            if (device is { IsEnabled: true })
            {
                await _runtimeDeviceManager.ReloadDeviceAsync(deviceId);
            }
        }

        /// <summary>将绑定实体映射为 DTO（DataModel 摘要为空时名称字段留空）。</summary>
        private static DeviceModelBindingDto MapToDto(DeviceDataModel b, int variableCount) => new()
        {
            Id = b.Id,
            DeviceId = b.DeviceId,
            DataModelId = b.DataModelId,
            Code = b.DataModel?.Code,
            Name = b.DataModel?.Name,
            Version = b.Version,
            IsPrimary = b.IsPrimary,
            IsEnabled = b.IsEnabled,
            VariableCount = variableCount,
            CreatedAt = b.CreatedAt
        };
    }
}
