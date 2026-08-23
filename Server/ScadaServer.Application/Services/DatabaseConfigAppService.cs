using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class DatabaseConfigAppService : IDatabaseConfigAppService
    {
        private readonly IDatabaseConfigRepository _repository;
        public DatabaseConfigAppService(IDatabaseConfigRepository repository) { _repository = repository; }

        public async Task<DatabaseConfigDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new DatabaseConfigDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Type = entity.Type,
                BackendType = entity.BackendType,
                Host = entity.Host,
                Port = entity.Port,
                Username = entity.Username,
                DatabaseName = entity.DatabaseName
            };
        }

        public async Task<List<DatabaseConfigDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new DatabaseConfigDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Type = entity.Type,
                BackendType = entity.BackendType,
                Host = entity.Host,
                Port = entity.Port,
                Username = entity.Username,
                DatabaseName = entity.DatabaseName
            }).ToList();
        }

        public async Task CreateAsync(DatabaseConfigDto dto)
        {
            var entity = new DatabaseConfig
            {
                Name = dto.Name,
                Type = dto.Type,
                BackendType = dto.BackendType,
                Host = dto.Host,
                Port = dto.Port,
                Username = dto.Username,
                DatabaseName = dto.DatabaseName
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(DatabaseConfigDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Name = dto.Name;
                entity.Type = dto.Type;
                entity.BackendType = dto.BackendType;
                entity.Host = dto.Host;
                entity.Port = dto.Port;
                entity.Username = dto.Username;
                entity.DatabaseName = dto.DatabaseName;
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

