using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class SystemScriptAppService : ISystemScriptAppService
    {
        private readonly ISystemScriptRepository _repository;
        public SystemScriptAppService(ISystemScriptRepository repository) { _repository = repository; }

        public async Task<SystemScriptDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new SystemScriptDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Code = entity.Code,
                TriggerType = entity.TriggerType,
                IntervalSeconds = entity.IntervalSeconds,
                Active = entity.Active
            };
        }

        public async Task<List<SystemScriptDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new SystemScriptDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Code = entity.Code,
                TriggerType = entity.TriggerType,
                IntervalSeconds = entity.IntervalSeconds,
                Active = entity.Active
            }).ToList();
        }

        public async Task CreateAsync(SystemScriptDto dto)
        {
            var entity = new SystemScript
            {
                Name = dto.Name,
                Code = dto.Code,
                TriggerType = dto.TriggerType,
                IntervalSeconds = dto.IntervalSeconds,
                Active = dto.Active
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(SystemScriptDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Name = dto.Name;
                entity.Code = dto.Code;
                entity.TriggerType = dto.TriggerType;
                entity.IntervalSeconds = dto.IntervalSeconds;
                entity.Active = dto.Active;
                await _repository.UpdateAsync(entity);
            }
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }
    }
}

