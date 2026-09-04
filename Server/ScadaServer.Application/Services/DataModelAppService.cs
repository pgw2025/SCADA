using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Domain.Exceptions;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 数据模型应用服务实现：负责变量模型（DataModel）的增删改查。
    /// 模型可定义变量模板，不再绑定协议（协议由设备所附连接决定）；
    /// 删除模型前会校验是否被设备引用，已引用则禁止删除。
    /// </summary>
    public class DataModelAppService : IDataModelAppService
    {
        /// <summary>数据模型仓储，提供持久化能力。</summary>
        private readonly IDataModelRepository _repository;
        /// <summary>模型变量仓储，用于加载/删除模型下的变量模板。</summary>
        private readonly IDataPointRepository _variableRepository;
        /// <summary>设备仓储，用于删除模型前校验引用。</summary>
        private readonly IDeviceRepository _deviceRepository;
        /// <summary>工作单元，用于删除模型及其变量伴随的原子操作。</summary>
        private readonly IUnitOfWork _uow;

        /// <summary>构造函数：注入模型、变量、设备仓储及工作单元。</summary>
        public DataModelAppService(
            IDataModelRepository repository,
            IDataPointRepository variableRepository,
            IDeviceRepository deviceRepository,
            IUnitOfWork uow)
        {
            _repository = repository;
            _variableRepository = variableRepository;
            _deviceRepository = deviceRepository;
            _uow = uow;
        }

        /// <summary>按主键获取数据模型（含变量模板），不存在时返回 null。</summary>
        public async Task<DataModelDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return await MapToDtoAsync(entity, includeVariables: true);
        }

        /// <summary>获取全部数据模型列表；默认同时加载各模型的变量模板。</summary>
        public async Task<List<DataModelDto>> GetListAsync(bool includeVariables = true)
        {
            var list = await _repository.GetListAsync();

            // N+1 优化：一次性取出全部模型变量并按 ModelId 分组，循环内仅内存组装。
            Dictionary<int, List<DataPointDto>>? mvByModel = null;
            if (includeVariables)
            {
                var allVariables = await _variableRepository.GetListAsync();
                mvByModel = allVariables
                    .GroupBy(mv => mv.ModelId)
                    .ToDictionary(g => g.Key, g => g.Select(DataPointMapper.ToDto).ToList());
            }

            return list.Select(entity => ToDto(entity, mvByModel)).ToList();
        }

        /// <summary>
        /// 实体 → DTO。变量列表若已通过 <paramref name="mvByModel"/> 一次加载则复用，否则按需查询回填
        /// （<see cref="DataModel.Variables"/> 为 [NotMapped]，EF 不会加载）。
        /// </summary>
        private async Task<DataModelDto> MapToDtoAsync(DataModel entity, bool includeVariables)
        {
            Dictionary<int, List<DataPointDto>>? mvByModel = null;
            if (includeVariables)
            {
                var variables = await _variableRepository.GetListAsync(mv => mv.ModelId == entity.Id);
                mvByModel = new Dictionary<int, List<DataPointDto>>
                {
                    [entity.Id] = variables.Select(DataPointMapper.ToDto).ToList()
                };
            }
            return ToDto(entity, mvByModel);
        }

        /// <summary>实体 → DTO（同步组装）。优先复用 <paramref name="mvByModel"/> 一次加载的变量，未命中时输出空列表。</summary>
        private static DataModelDto ToDto(DataModel entity, Dictionary<int, List<DataPointDto>>? mvByModel)
        {
            var dto = new DataModelDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Code = entity.Code,
                Version = entity.Version,
                IsPublished = entity.IsPublished,
                Description = entity.Description,
                Vendor = entity.Vendor,
                VendorModel = entity.VendorModel,
                Variables = new List<DataPointDto>()
            };

            if (mvByModel != null && mvByModel.TryGetValue(entity.Id, out var variables))
            {
                dto.Variables = variables;
            }

            return dto;
        }

        /// <summary>新增数据模型：校验名称/编码唯一性并绑定协议，返回含变量模板的最新 DTO。</summary>
        public async Task<DataModelDto> CreateAsync(CreateDataModelDto dto)
        {
            // 0. 规范化：修剪空格
            dto.Name = dto.Name.Trim();
            var code = dto.Code?.Trim() ?? string.Empty;

            // 0.5 编码必填（阶段 4 权威业务键；[Required] 兜底后此处防御空白串）
            if (code.Length == 0)
            {
                throw new BusinessException("模型编码不能为空");
            }

            // 1. 业务校验：名称唯一性
            var existing = await _repository.GetListAsync(m => m.Name == dto.Name);
            if (existing.Any())
            {
                throw new BusinessException($"数据模型名称 '{dto.Name}' 已存在");
            }

            // 1.25 业务校验：编码唯一性（对应库级唯一索引 ix_datamodels_code）
            var codeExists = await _repository.GetListAsync(m => m.Code == code);
            if (codeExists.Any())
            {
                throw new BusinessException($"数据模型编码 '{code}' 已存在");
            }

            var entity = new DataModel
            {
                Name = dto.Name,
                Code = code,
                Version = NormalizeVersion(dto.Version),
                IsPublished = dto.IsPublished,
                Description = dto.Description?.Trim(),
                Vendor = dto.Vendor?.Trim(),
                VendorModel = dto.VendorModel?.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _repository.InsertAsync(entity);

            return await MapToDtoAsync(entity, includeVariables: true);
        }

        /// <summary>更新数据模型（全量替换语义）：校验存在性、名称/编码唯一性，返回最新 DTO。</summary>
        public async Task<DataModelDto> UpdateAsync(DataModelDto dto)
        {
            // 0. 规范化：修剪空格
            dto.Name = dto.Name.Trim();
            var code = dto.Code?.Trim() ?? string.Empty;

            // 更新专用加载（跟踪查询）：仅加载数据模型本体。
            var entity = await _repository.GetByIdForUpdateAsync(dto.Id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {dto.Id} 的数据模型不存在");
            }

            // 0.5 编码必填（PUT 全量替换语义：Code 为业务键，不允许清空）
            if (code.Length == 0)
            {
                throw new BusinessException("模型编码不能为空");
            }

            // 1. 业务校验：名称不能与其他模型重复
            var existing = await _repository.GetListAsync(m => m.Name == dto.Name && m.Id != dto.Id);
            if (existing.Any())
            {
                throw new BusinessException($"数据模型名称 '{dto.Name}' 已存在");
            }

            // 1.25 业务校验：编码不能与其他模型重复（排除自身）
            var codeExists = await _repository.GetListAsync(m => m.Code == code && m.Id != dto.Id);
            if (codeExists.Any())
            {
                throw new BusinessException($"数据模型编码 '{code}' 已存在");
            }

            entity.Name = dto.Name;
            entity.Code = code;
            entity.Version = NormalizeVersion(dto.Version);
            entity.IsPublished = dto.IsPublished;
            entity.Description = dto.Description?.Trim();
            entity.Vendor = dto.Vendor?.Trim();
            entity.VendorModel = dto.VendorModel?.Trim();
            entity.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(entity);

            return (await GetByIdAsync(dto.Id))!;
        }

        /// <summary>归一化版本号：空白 → "1.0"（与实体默认一致），并修剪两端空格。</summary>
        private static string NormalizeVersion(string? version) =>
            string.IsNullOrWhiteSpace(version) ? "1.0" : version.Trim();

        /// <summary>删除数据模型：先校验是否被设备引用，未引用时在同一事务内删除其变量模板与模型本身。</summary>
        public async Task DeleteAsync(int id)
        {
            // 更新专用加载（跟踪查询、不含导航）：删除仅需本体，避免 AsNoTracking+Include
            // 游离图的 Remove 触发导航对象图附加（与 ControllerAppService.DeleteAsync 先例一致）。
            var entity = await _repository.GetByIdForUpdateAsync(id);
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