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

        public async Task<ModelVariableDto> GetByIdAsync(int id)
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

        public async Task<ModelVariableDto> CreateAsync(ModelVariableDto dto)
        {
            // 0. 规范化
            dto.Key = dto.Key?.Trim();
            dto.Name = dto.Name?.Trim();
            dto.Address = dto.Address?.Trim();

            // 1. 存在性检查：模型必须存在
            var model = await _modelRepository.GetByIdAsync(dto.ModelId);
            if (model == null)
            {
                throw new BusinessException($"ID 为 {dto.ModelId} 的数据模型不存在");
            }

            // 2. 深度业务校验
            ValidateVariableLogic(dto, model.Type);

            // 3. 业务校验：在同一个模型下 Key 和 Address 必须唯一
            var keyExists = await _repository.AnyAsync(v => v.ModelId == dto.ModelId && v.Key == dto.Key);
            if (keyExists)
            {
                throw new BusinessException($"模型内已存在标识为 '{dto.Key}' 的变量");
            }

            var addrExists = await _repository.AnyAsync(v => v.ModelId == dto.ModelId && v.Address == dto.Address);
            if (addrExists)
            {
                throw new BusinessException($"模型内已存在地址为 '{dto.Address}' 的变量");
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
            dto.Address = dto.Address?.Trim();

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

            // 2. 深度业务校验
            ValidateVariableLogic(dto, model.Type);

            // 3. 依赖检查：如果 Key 发生了变化，检查是否有触发器依赖旧 Key
            if (entity.Key != dto.Key)
            {
                var hasTriggers = await _triggerRepository.AnyAsync(t => t.VariableKey == entity.Key);
                if (hasTriggers)
                {
                    throw new BusinessException($"无法修改变量 Key，因为已有报警/联动规则引用了旧标识 '{entity.Key}'。请先清理关联规则。");
                }
            }

            // 4. 业务校验：Key 和 Address 查重（排除自身）
            var keyExists = await _repository.AnyAsync(v => v.ModelId == dto.ModelId && v.Key == dto.Key && v.Id != dto.Id);
            if (keyExists)
            {
                throw new BusinessException($"模型内已存在标识为 '{dto.Key}' 的变量");
            }

            var addrExists = await _repository.AnyAsync(v => v.ModelId == dto.ModelId && v.Address == dto.Address && v.Id != dto.Id);
            if (addrExists)
            {
                throw new BusinessException($"模型内已存在地址为 '{dto.Address}' 的变量");
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

        private void ValidateVariableLogic(ModelVariableDto dto, DeviceType protocolType)
        {
            // A. 类型匹配校验
            if (dto.Type == VariableType.Digital && dto.DataType != DataTypeEnum.BOOL && dto.DataType != DataTypeEnum.BIT)
            {
                throw new BusinessException("数字量性质的变量，数据类型必须为 BOOL 或 BIT");
            }

            // B. 地址格式校验
            if (string.IsNullOrWhiteSpace(dto.Address))
            {
                throw new BusinessException("变量地址不能为空");
            }

            if (protocolType == DeviceType.S7)
            {
                // 西门子 S7 地址正则校验：支持 DB块(DB1.DBX0.0) 或 寄存器区(I0.0, Q1.2, M10.5, MW100, MD100等)
                var s7Regex = @"^(?:DB\d+\.DB[XWDB]\d+(\.\d+)?)|([IQM](?:B|W|D)?\d+(\.\d+)?)$";
                if (!Regex.IsMatch(dto.Address, s7Regex, RegexOptions.IgnoreCase))
                {
                    throw new BusinessException($"西门子 S7 地址 '{dto.Address}' 格式不正确。示例：DB1.DBX0.0, I0.0, Q1.2, M10.5, MW100");
                }
            }
            else if (protocolType == DeviceType.OpcUa)
            {
                // OPC UA 节点 ID 通常包含 ns= 或 i=
                if (!dto.Address.Contains("ns=") && !dto.Address.Contains("i="))
                {
                    throw new BusinessException($"OPC UA 节点 ID '{dto.Address}' 格式不正确。通常应包含 'ns=' 或 'i='。");
                }
            }
            else if (protocolType == DeviceType.Virtual)
            {
                // Virtual 类型不校验具体地址格式，允许任意字符串
            }
            else if (protocolType == DeviceType.Mqtt)
            {
                // MQTT 类型，地址为 Topic
            }
            else
            {
                throw new BusinessException($"不支持的协议类型: {protocolType}");
            }

            // C. 历史存储检查
            if (dto.IsStored && string.IsNullOrEmpty(dto.StoreMode))
            {
                throw new BusinessException("开启历史存储时，必须指定存储模式（Change:变化存储 / Cycle:周期存储）");
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
            Address = entity.Address,
            Description = entity.Description,
            IsStored = entity.IsStored,
            StoreMode = entity.StoreMode,
            UpdateMode = entity.UpdateMode,
            PollingIntervalMs = entity.PollingIntervalMs,
            BitOffset = entity.BitOffset,
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
            entity.Type = dto.Type;
            entity.DataType = dto.DataType;
            entity.Unit = dto.Unit;
            entity.Min = dto.Min;
            entity.Max = dto.Max;
            entity.Address = dto.Address;
            entity.Description = dto.Description;
            entity.IsStored = dto.IsStored;
            entity.StoreMode = dto.StoreMode;
            entity.UpdateMode = dto.UpdateMode;
            entity.PollingIntervalMs = dto.PollingIntervalMs;
            entity.BitOffset = dto.BitOffset;
            entity.ScaleSlope = dto.ScaleSlope;
            entity.ScaleOffset = dto.ScaleOffset;
            entity.DeadBand = dto.DeadBand;
            entity.IsReadOnly = dto.IsReadOnly;
            entity.ExtensionData = dto.ExtensionData ?? new Dictionary<string, string>();
            return entity;
        }
    }
}

