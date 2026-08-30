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

            // N+1 优化：一次性取出全部模型变量并按 ModelId 分组，循环内仅内存组装。
            Dictionary<int, List<ModelVariableDto>>? mvByModel = null;
            if (includeVariables)
            {
                var allVariables = await _variableRepository.GetListAsync();
                mvByModel = allVariables
                    .GroupBy(mv => mv.ModelId)
                    .ToDictionary(g => g.Key, g => g.Select(ModelVariableMapper.ToDto).ToList());
            }

            return list.Select(entity => ToDto(entity, mvByModel)).ToList();
        }

        /// <summary>
        /// 实体 → DTO。变量列表若已通过 <paramref name="mvByModel"/> 一次加载则复用，否则按需查询回填
        /// （<see cref="DataModel.Variables"/> 为 [NotMapped]，EF 不会加载）。
        /// </summary>
        private async Task<DataModelDto> MapToDtoAsync(DataModel entity, bool includeVariables)
        {
            Dictionary<int, List<ModelVariableDto>>? mvByModel = null;
            if (includeVariables)
            {
                var variables = await _variableRepository.GetListAsync(mv => mv.ModelId == entity.Id);
                mvByModel = new Dictionary<int, List<ModelVariableDto>>
                {
                    [entity.Id] = variables.Select(ModelVariableMapper.ToDto).ToList()
                };
            }
            return ToDto(entity, mvByModel);
        }

        private static DataModelDto ToDto(DataModel entity, Dictionary<int, List<ModelVariableDto>>? mvByModel)
        {
            // 协议字段来自 Include 加载的 Protocol 导航属性
            var dto = new DataModelDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                Vendor = entity.Vendor,
                ModelName = entity.ModelName,
                VendorModel = entity.VendorModel,
                ProtocolId = entity.ProtocolId,
                ProtocolKey = entity.Protocol?.Key,
                ProtocolName = entity.Protocol?.Name,
                Variables = new List<ModelVariableDto>()
            };

            if (mvByModel != null && mvByModel.TryGetValue(entity.Id, out var variables))
            {
                dto.Variables = variables;
            }

            return dto;
        }

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
            dto.Name = dto.Name.Trim();

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
                Vendor = dto.Vendor?.Trim(),
                ModelName = dto.ModelName?.Trim(),
                VendorModel = dto.VendorModel?.Trim(),
                ProtocolId = protocolId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _repository.InsertAsync(entity);

            return await MapToDtoAsync(entity, includeVariables: true);
        }

        public async Task<DataModelDto> UpdateAsync(DataModelDto dto)
        {
            // 0. 规范化：修剪空格
            dto.Name = dto.Name.Trim();

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

            // 1.5 协议绑定校验（PUT 为全量替换语义：ProtocolId 必填，未传/为 0 由 DTO [Range] 拦截或在此抛异常）
            entity.ProtocolId = await ResolveProtocolIdAsync(dto.ProtocolId);
            entity.Name = dto.Name;
            entity.Description = dto.Description?.Trim();
            entity.Vendor = dto.Vendor?.Trim();
            entity.ModelName = dto.ModelName?.Trim();
            entity.VendorModel = dto.VendorModel?.Trim();
            entity.UpdatedAt = DateTime.UtcNow;
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

            // 2. 在重试策略内执行事务，避免 MySqlRetryingExecutionStrategy 冲突。
            //    模型中若已定义变量，先显式删除（模型无设备引用时其变量不会被设备实例化，删除安全）——
            //    数据库 (ModelId → DataModel) 外键为 Restrict，避免遗留孤儿变量。
            await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                await _variableRepository.DeleteRangeAsync(mv => mv.ModelId == id);

                // 删除模型本身
                await _repository.DeleteAsync(entity);

                return true;
            });
        }
    }
}