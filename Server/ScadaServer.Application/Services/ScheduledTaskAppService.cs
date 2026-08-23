using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class ScheduledTaskAppService : IScheduledTaskAppService
    {
        private readonly IScheduledTaskRepository _repository;
        public ScheduledTaskAppService(IScheduledTaskRepository repository) { _repository = repository; }

        public async Task<ScheduledTaskDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new ScheduledTaskDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Type = entity.Type,
                CronExpression = entity.CronExpression,
                ParamsJson = entity.ParamsJson,
                Active = entity.Active
            };
        }

        public async Task<List<ScheduledTaskDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new ScheduledTaskDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Type = entity.Type,
                CronExpression = entity.CronExpression,
                ParamsJson = entity.ParamsJson,
                Active = entity.Active
            }).ToList();
        }

        public async Task CreateAsync(ScheduledTaskDto dto)
        {
            var entity = new ScheduledTask
            {
                Name = dto.Name,
                Type = dto.Type,
                CronExpression = dto.CronExpression,
                ParamsJson = dto.ParamsJson,
                Active = dto.Active
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(ScheduledTaskDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Name = dto.Name;
                entity.Type = dto.Type;
                entity.CronExpression = dto.CronExpression;
                entity.ParamsJson = dto.ParamsJson;
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

