using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Domain.Exceptions;

namespace ScadaServer.Application.Services
{
    public class DataModelAppService : IDataModelAppService
    {
        private readonly IDataModelRepository _repository;
        private readonly IModelVariableRepository _variableRepository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly IProtocolRepository _protocolRepository;
        private readonly IUnitOfWork _uow;

        public DataModelAppService(
            IDataModelRepository repository,
            IModelVariableRepository variableRepository,
            IDeviceRepository deviceRepository,
            IProtocolRepository protocolRepository,
            IUnitOfWork uow)
        {
            _repository = repository;
            _variableRepository = variableRepository;
            _deviceRepository = deviceRepository;
            _protocolRepository = protocolRepository;
            _uow = uow;
        }

        public async Task<DataModelDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return await MapToDtoAsync(entity, includeVariables: true);
        }

        public async Task<List<DataModelDto>> GetListAsync(bool includeVariables = true)
        {
            var list = await _repository.GetListAsync();
            var dtos = new List<DataModelDto>();
            foreach (var entity in list)
            {
                dtos.Add(await MapToDtoAsync(entity, includeVariables));
            }
            return dtos;
        }

        /// <summary>
        /// 实体 → DTO。由于 <see cref="DataModel.Variables"/> 为 [NotMapped]，EF 不会加载，
        /// 这里按需查询 <see cref="ModelVariable"/> 后回填，保证接口返回的模型变量列表正确。
        /// </summary>
        private async Task<DataModelDto> MapToDtoAsync(DataModel entity, bool includeVariables)
        {
            // 协议字段来自 Include 加载的 Protocol 导航属性
            var dtos = new DataModelDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                VendorModel = entity.VendorModel,
                ProtocolId = entity.ProtocolId,
                ProtocolKey = entity.Protocol?.Key,
                ProtocolName = entity.Protocol?.Name,
                Variables = new List<ModelVariableDto>()
            };

            if (includeVariables)
            {
                var variables = await _variableRepository.GetListAsync(mv => mv.ModelId == entity.Id);
                dtos.Variables = variables.Select(ToModelVariableDto).ToList();
            }

            return dtos;
        }

        private static ModelVariableDto ToModelVariableDto(ModelVariable v) => new()
        {
            Id = v.Id,
            ModelId = v.ModelId,
            Key = v.Key,
            Name = v.Name,
            Type = v.Type,
            DataType = v.DataType,
            Unit = v.Unit,
            Min = v.Min,
            Max = v.Max,
            Description = v.Description,
            IsStored = v.IsStored,
            StoreMode = v.StoreMode,
            UpdateMode = v.UpdateMode,
            ExtensionData = v.ExtensionData
        };

        /// <summary>
        /// 协议绑定校验：协议必须存在且已启用（模型必须绑定协议，作为驱动派发真相源）。
        /// </summary>
        private async Task<int> ResolveProtocolIdAsync(int protocolId)
        {
            var protocol = await _protocolRepository.GetByIdAsync(protocolId);
            if (protocol == null)
            {
                throw new BusinessException($"ID 为 {protocolId} 的协议不存在");
            }
            if (!protocol.IsEnabled)
            {
                throw new BusinessException($"协议 '{protocol.Name}' 已被停用，无法关联到数据模型");
            }
            return protocolId;
        }

        public async Task<DataModelDto> CreateAsync(CreateDataModelDto dto)
        {
            // 0. 规范化：修剪空格
            dto.Name = dto.Name?.Trim();

            // 1. 业务校验：名称唯一性
            var existing = await _repository.GetListAsync(m => m.Name == dto.Name);
            if (existing.Any())
            {
                throw new BusinessException($"数据模型名称 '{dto.Name}' 已存在");
            }

            // 1.5 协议绑定校验
            var protocolId = await ResolveProtocolIdAsync(dto.ProtocolId);

            var entity = new DataModel
            {
                Name = dto.Name,
                Description = dto.Description?.Trim(),
                VendorModel = dto.VendorModel?.Trim(),
                ProtocolId = protocolId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            await _repository.InsertAsync(entity);

            return await MapToDtoAsync(entity, includeVariables: true);
        }

        public async Task<DataModelDto> UpdateAsync(DataModelDto dto)
        {
            // 0. 规范化：修剪空格
            dto.Name = dto.Name?.Trim();

            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {dto.Id} 的数据模型不存在");
            }

            // 1. 业务校验：名称不能与其他模型重复
            var existing = await _repository.GetListAsync(m => m.Name == dto.Name && m.Id != dto.Id);
            if (existing.Any())
            {
                throw new BusinessException($"数据模型名称 '{dto.Name}' 已存在");
            }

            // 1.5 协议绑定校验（PUT 为全量替换语义，未传 ProtocolId 即解绑）
            entity.ProtocolId = await ResolveProtocolIdAsync(dto.ProtocolId);
            entity.Name = dto.Name;
            entity.Description = dto.Description?.Trim();
            entity.VendorModel = dto.VendorModel?.Trim();
            entity.UpdatedAt = DateTime.Now;
            await _repository.UpdateAsync(entity);

            return await MapToDtoAsync(entity, includeVariables: true);
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null)
            {
                return;
            }

            // 1. 安全检查：如果已有设备引用此模型，禁止删除
            var hasDevices = await _deviceRepository.AnyAsync(d => d.ModelId == id);
            if (hasDevices)
            {
                throw new BusinessException($"无法删除模型 '{entity.Name}'，因为已有设备正在使用此模型。请先删除相关设备。");
            }

            // 2. 在重试策略内执行事务，避免 MySqlRetryingExecutionStrategy 冲突
            await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                // 删除模型本身
                await _repository.DeleteAsync(entity);

                return true;
            });
        }
    }
}
