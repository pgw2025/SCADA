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
        /// <summary>协议仓储，提供持久化能力。</summary>
        private readonly IProtocolRepository _repository;
        /// <summary>数据模型仓储，用于删除协议前校验是否有模型引用。</summary>
        private readonly IDataModelRepository _dataModelRepository;

        /// <summary>构造函数：注入协议与数据模型仓储。</summary>
        public ProtocolAppService(IProtocolRepository repository, IDataModelRepository dataModelRepository)
        {
            _repository = repository;
            _dataModelRepository = dataModelRepository;
        }

        /// <summary>按主键获取协议，不存在时返回 null。</summary>
        public async Task<ProtocolDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            return entity == null ? null : MapToDto(entity);
        }

        /// <summary>按协议键查询协议（不区分大小写，取匹配的第一条），不存在时返回 null。</summary>
        public async Task<ProtocolDto?> GetByKeyAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var entity = await _repository.GetListAsync(p => p.Key == key.Trim());
            return entity.FirstOrDefault() is { } match ? MapToDto(match) : null;
        }

        /// <summary>获取全部协议列表。</summary>
        public async Task<List<ProtocolDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        /// <summary>新增协议：校验键唯一性后写入，返回最新 DTO。</summary>
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _repository.InsertAsync(entity);
            return MapToDto(entity);
        }

        /// <summary>更新协议：校验存在性及键唯一性（排除自身）后写入，返回最新 DTO。</summary>
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
            entity.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(entity);

            return MapToDto(entity);
        }

        /// <summary>删除协议：先校验是否有数据模型引用，有则禁止删除；记录不存在时静默忽略。</summary>
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

        /// <summary>将协议实体映射为 DTO。</summary>
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