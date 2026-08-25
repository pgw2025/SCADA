using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Enums;
using System.Text.RegularExpressions;

namespace ScadaServer.Application.Services
{
    public class ModelVariableAppService : IModelVariableAppService
    {
        private readonly IModelVariableRepository _repository;
        private readonly IDataModelRepository _modelRepository;
        private readonly IVariableTriggerRepository _triggerRepository;

        public ModelVariableAppService(
            IModelVariableRepository repository, 
            IDataModelRepository modelRepository,
            IVariableTriggerRepository triggerRepository) 
        { 
            _repository = repository; 
            _modelRepository = modelRepository;
            _triggerRepository = triggerRepository;
        }

        public async Task<ModelVariableDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        public async Task<List<ModelVariableDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<List<ModelVariableDto>> GetByModelIdAsync(int modelId)
        {
            var list = await _repository.GetListAsync(mv => mv.ModelId == modelId);
            return list.Select(MapToDto).ToList();
        }

        public async Task<ModelVariableDto> CreateAsync(ModelVariableDto dto)
        {
            // 0. 规范化
            dto.Key = dto.Key?.Trim();
            dto.Name = dto.Name?.Trim();

            // 1. 存在性检查：模型必须存在
            var model = await _modelRepository.GetByIdAsync(dto.ModelId);
            if (model == null)
            {
                throw new BusinessException($"ID 为 {dto.ModelId} 的数据模型不存在");
            }

            // 2. 深度业务校验（协议真相源在 Device.Type，此处不再按协议校验地址格式）
            ValidateVariableLogic(dto);

            // 3. 业务校验：在同一个模型下 Key 和 Address 必须唯一
            var keyExists = await _repository.AnyAsync(v => v.ModelId == dto.ModelId && v.Key == dto.Key);
            if (keyExists)
            {
                throw new BusinessException($"模型内已存在标识为 '{dto.Key}' 的变量");
            }

            var entity = MapToEntity(dto);
            await _repository.InsertAsync(entity);
            
            dto.Id = entity.Id; 
            return dto;
        }

        public async Task<ModelVariableDto> UpdateAsync(ModelVariableDto dto)
        {
            // 0. 规范化
            dto.Key = dto.Key?.Trim();
            dto.Name = dto.Name?.Trim();

            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {dto.Id} 的变量定义不存在");
            }

            // 1. 获取模型以获知协议类型
            var model = await _modelRepository.GetByIdAsync(dto.ModelId);
            if (model == null)
            {
                throw new BusinessException($"ID 为 {dto.ModelId} 的数据模型不存在");
            }

            // 2. 深度业务校验（协议真相源在 Device.Type，此处不再按协议校验地址格式）
            ValidateVariableLogic(dto);

            // 3. 依赖检查：如果 Key 发生了变化，检查是否有触发器依赖旧 Key
            if (entity.Key != dto.Key)
            {
                var hasTriggers = await _triggerRepository.AnyAsync(t => t.VariableKey == entity.Key);
                if (hasTriggers)
                {
                    throw new BusinessException($"无法修改变量 Key，因为已有报警/联动规则引用了旧标识 '{entity.Key}'。请先清理关联规则。");
                }
            }

            // 4. 业务校验：Key 查重（排除自身）
            var keyExists = await _repository.AnyAsync(v => v.ModelId == dto.ModelId && v.Key == dto.Key && v.Id != dto.Id);
            if (keyExists)
            {
                throw new BusinessException($"模型内已存在标识为 '{dto.Key}' 的变量");
            }

            MapToEntity(dto, entity);
            await _repository.UpdateAsync(entity);
            
            return dto;
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            // 1. 安全检查：是否有报警触发器引用此变量
            var hasTriggers = await _triggerRepository.AnyAsync(t => t.VariableKey == entity.Key);
            if (hasTriggers)
            {
                throw new BusinessException($"无法删除变量 '{entity.Name}'，因为它正被用于报警或联动规则中（关联 Key: {entity.Key}）。");
            }

            await _repository.DeleteAsync(entity);
        }

        private void ValidateVariableLogic(ModelVariableDto dto)
        {
            // A. 类型匹配校验（信号类型由 DataType 推导，此处直接校验 DataType 合法性）
            var signalType = (dto.DataType == DataTypeEnum.BIT || dto.DataType == DataTypeEnum.BOOL)
                ? VariableType.Digital
                : VariableType.Analog;
            if (signalType == VariableType.Digital && dto.DataType != DataTypeEnum.BOOL && dto.DataType != DataTypeEnum.BIT)
            {
                throw new BusinessException("数字量性质的变量，数据类型必须为 BOOL 或 BIT");
            }

            // B. 地址格式校验已下放到前端按协议拦截 + 运行时驱动；此处不再强制非空。
            //    虚拟/计算类变量本就无物理地址,允许空地址;地址唯一性由调用方在地址非空时校验。

            // C. 历史存储检查
            if (dto.StoreMode == StoreModeEnum.None && dto.IsStored)
            {
                throw new BusinessException("已勾选\"存储历史\"但存储模式为 None，请选择 Change/Cycle 等具体模式");
            }
        }

        private static ModelVariableDto MapToDto(ModelVariable entity) => new()
        {
            Id = entity.Id,
            ModelId = entity.ModelId,
            Key = entity.Key,
            Name = entity.Name,
            Type = entity.Type,
            DataType = entity.DataType,
            Unit = entity.Unit,
            Min = entity.Min,
            Max = entity.Max,
            Description = entity.Description,
            IsStored = entity.IsStored,
            StoreMode = entity.StoreMode,
            UpdateMode = entity.UpdateMode,
            ScaleSlope = entity.ScaleSlope,
            ScaleOffset = entity.ScaleOffset,
            DeadBand = entity.DeadBand,
            IsReadOnly = entity.IsReadOnly,
            ExtensionData = entity.ExtensionData
        };

        private static ModelVariable MapToEntity(ModelVariableDto dto, ModelVariable? entity = null)
        {
            entity ??= new ModelVariable();
            entity.ModelId = dto.ModelId;
            entity.Key = dto.Key;
            entity.Name = dto.Name;
            entity.DataType = dto.DataType;
            entity.Unit = dto.Unit;
            entity.Min = dto.Min;
            entity.Max = dto.Max;
            // 注(P1-5)：Address / BitOffset / PollingIntervalMs 已迁移至 DeviceVariable，
            // 模板层不再写回；地址、采集周期等采集细节统一在设备实例层维护。
            entity.Description = dto.Description;
            entity.StoreMode = dto.StoreMode;
            entity.UpdateMode = dto.UpdateMode;
            entity.ScaleSlope = dto.ScaleSlope;
            entity.ScaleOffset = dto.ScaleOffset;
            entity.DeadBand = dto.DeadBand;
            entity.IsReadOnly = dto.IsReadOnly;
            entity.ExtensionData = dto.ExtensionData ?? new Dictionary<string, string>();
            return entity;
        }
    }
}

