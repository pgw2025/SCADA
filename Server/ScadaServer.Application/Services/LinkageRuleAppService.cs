using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    public class LinkageRuleAppService : ILinkageRuleAppService
    {
        private readonly ILinkageRuleRepository _repository;

        public LinkageRuleAppService(ILinkageRuleRepository repository)
        {
            _repository = repository;
        }

        public async Task<LinkageRuleDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        public async Task<List<LinkageRuleDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task CreateAsync(LinkageRuleDto dto)
        {
            var entity = MapToEntity(dto);
            await _repository.InsertAsync(entity);
            dto.Id = entity.Id;
        }

        public async Task UpdateAsync(LinkageRuleDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null) return;
            entity.Name = dto.Name;
            entity.DeviceId = dto.DeviceId;
            entity.VariableKey = dto.VariableKey;
            entity.Condition = dto.Condition;
            entity.Threshold = dto.Threshold;
            entity.ActionType = dto.ActionType;
            entity.LinkageVariableKey = dto.LinkageVariableKey;
            entity.LinkageValue = dto.LinkageValue;
            entity.Active = dto.Active;
            await _repository.UpdateAsync(entity);
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }

        private static LinkageRuleDto MapToDto(LinkageRule entity) => new()
        {
            Id = entity.Id,
            Name = entity.Name,
            DeviceId = entity.DeviceId,
            VariableKey = entity.VariableKey,
            Condition = entity.Condition,
            Threshold = entity.Threshold,
            ActionType = entity.ActionType,
            LinkageVariableKey = entity.LinkageVariableKey,
            LinkageValue = entity.LinkageValue,
            Active = entity.Active
        };

        private static LinkageRule MapToEntity(LinkageRuleDto dto) => new()
        {
            Name = dto.Name,
            DeviceId = dto.DeviceId,
            VariableKey = dto.VariableKey,
            Condition = dto.Condition,
            Threshold = dto.Threshold,
            ActionType = dto.ActionType,
            LinkageVariableKey = dto.LinkageVariableKey,
            LinkageValue = dto.LinkageValue,
            Active = dto.Active
        };
    }
}
