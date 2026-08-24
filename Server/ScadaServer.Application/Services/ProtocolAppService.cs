using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 通信协议应用服务。
    /// </summary>
    public class ProtocolAppService : IProtocolAppService
    {
        private readonly IProtocolRepository _repository;
        private readonly IDataModelRepository _dataModelRepository;

        public ProtocolAppService(IProtocolRepository repository, IDataModelRepository dataModelRepository)
        {
            _repository = repository;
            _dataModelRepository = dataModelRepository;
        }

        public async Task<ProtocolDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            return entity == null ? null : MapToDto(entity);
        }

        public async Task<ProtocolDto?> GetByKeyAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var entity = await _repository.GetListAsync(p => p.Key == key.Trim());
            return entity.FirstOrDefault() is { } match ? MapToDto(match) : null;
        }

        public async Task<List<ProtocolDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<ProtocolDto> CreateAsync(CreateProtocolDto dto)
        {
            // 0. 规范化（[Required] 已在控制器校验非空，此处防御性兜底为空串）
            var key = dto.Key?.Trim() ?? string.Empty;
            var name = dto.Name?.Trim() ?? string.Empty;
            var driverKey = dto.DriverKey?.Trim() ?? string.Empty;

            // 1. Key 唯一性校验
            if (await _repository.AnyAsync(p => p.Key == key))
            {
                throw new BusinessException($"协议键 '{key}' 已存在");
            }

            var entity = new Protocol
            {
                Key = key,
                Name = name,
                DriverKey = driverKey,
                Description = dto.Description?.Trim(),
                IsEnabled = dto.IsEnabled,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            await _repository.InsertAsync(entity);
            return MapToDto(entity);
        }

        public async Task<ProtocolDto> UpdateAsync(int id, ProtocolDto dto)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {id} 的协议不存在");
            }

            // 0. 规范化（防御性兜底为空串）
            var key = dto.Key?.Trim() ?? string.Empty;
            var name = dto.Name?.Trim() ?? string.Empty;
            var driverKey = dto.DriverKey?.Trim() ?? string.Empty;

            // 1. Key 唯一性校验（排除自身）
            if (await _repository.AnyAsync(p => p.Key == key && p.Id != id))
            {
                throw new BusinessException($"协议键 '{key}' 已存在");
            }

            entity.Key = key;
            entity.Name = name;
            entity.DriverKey = driverKey;
            entity.Description = dto.Description?.Trim();
            entity.IsEnabled = dto.IsEnabled;
            entity.UpdatedAt = DateTime.Now;
            await _repository.UpdateAsync(entity);

            return MapToDto(entity);
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            // 安全：若已有数据模型绑定该协议，禁止删除（数据库为 Restrict，这里给出友好提示）
            var hasModels = await _dataModelRepository.AnyAsync(m => m.ProtocolId == id);
            if (hasModels)
            {
                throw new BusinessException($"无法删除协议 '{entity.Name}'，因为已有数据模型绑定该协议。请先解除绑定。");
            }

            await _repository.DeleteAsync(entity);
        }

        private static ProtocolDto MapToDto(Protocol entity) => new()
        {
            Id = entity.Id,
            Key = entity.Key,
            Name = entity.Name,
            DriverKey = entity.DriverKey,
            Description = entity.Description,
            IsEnabled = entity.IsEnabled,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}