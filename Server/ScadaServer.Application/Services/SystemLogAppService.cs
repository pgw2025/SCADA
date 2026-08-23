using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class SystemLogAppService : ISystemLogAppService
    {
        private readonly ISystemLogRepository _repository;
        public SystemLogAppService(ISystemLogRepository repository) { _repository = repository; }

        public async Task<SystemLogDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new SystemLogDto
            {
                Id = entity.Id,
                Timestamp = entity.Timestamp,
                Level = entity.Level,
                Source = entity.Source,
                Content = entity.Content
            };
        }

        public async Task<List<SystemLogDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new SystemLogDto
            {
                Id = entity.Id,
                Timestamp = entity.Timestamp,
                Level = entity.Level,
                Source = entity.Source,
                Content = entity.Content
            }).ToList();
        }

        public async Task CreateAsync(SystemLogDto dto)
        {
            var entity = new SystemLog
            {
                Timestamp = dto.Timestamp,
                Level = dto.Level,
                Source = dto.Source,
                Content = dto.Content
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(SystemLogDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Timestamp = dto.Timestamp;
                entity.Level = dto.Level;
                entity.Source = dto.Source;
                entity.Content = dto.Content;
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

