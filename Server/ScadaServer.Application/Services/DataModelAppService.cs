using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.Services
{
    public class DataModelAppService : IDataModelAppService
    {
        private readonly IDataModelRepository _repository;
        private readonly IModelVariableRepository _variableRepository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly IUnitOfWork _uow;

        public DataModelAppService(
            IDataModelRepository repository,
            IModelVariableRepository variableRepository,
            IDeviceRepository deviceRepository,
            IUnitOfWork uow)
        {
            _repository = repository;
            _variableRepository = variableRepository;
            _deviceRepository = deviceRepository;
            _uow = uow;
        }

        public async Task<DataModelDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        public async Task<List<DataModelDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        private DataModelDto MapToDto(DataModel entity)
        {
            return new DataModelDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                Type = entity.Type,
                Variables = entity.Variables?.Select(v => new ModelVariableDto
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
                    Address = v.Address,
                    Description = v.Description,
                    IsStored = v.IsStored,
                    StoreMode = v.StoreMode,
                    UpdateMode = v.UpdateMode,
                    PollingIntervalMs = v.PollingIntervalMs,
                    ExtensionData = v.ExtensionData
                }).ToList() ?? new List<ModelVariableDto>()
            };
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

            var entity = new DataModel
            {
                Name = dto.Name,
                Description = dto.Description?.Trim(),
                Type = dto.Type,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            await _repository.InsertAsync(entity);

            return MapToDto(entity);
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

            // 1. 协议类型锁定保护
            if (entity.Type != dto.Type)
            {
                // 如果模型类型发生变化，检查是否有设备正在使用此模型
                var hasDevices = await _deviceRepository.AnyAsync(d => d.ModelId == dto.Id);
                if (hasDevices)
                {
                    throw new BusinessException($"无法修改模型类型。已有设备关联此模型，请先删除相关设备或解除绑定。");
                }
            }

            // 2. 业务校验：名称不能与其他模型重复
            var existing = await _repository.GetListAsync(m => m.Name == dto.Name && m.Id != dto.Id);
            if (existing.Any())
            {
                throw new BusinessException($"数据模型名称 '{dto.Name}' 已存在");
            }

            entity.Name = dto.Name;
            entity.Description = dto.Description?.Trim();
            entity.Type = dto.Type;
            entity.UpdatedAt = DateTime.Now;
            await _repository.UpdateAsync(entity);

            return MapToDto(entity);
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

            // 2. 利用 IAsyncDisposable 的自动回滚机制
            await using var transaction = await _uow.BeginTransactionAsync();

            // 删除模型本身
            await _repository.DeleteAsync(entity);

            // 显式提交
            await transaction.CommitAsync();
        }
    }
}
