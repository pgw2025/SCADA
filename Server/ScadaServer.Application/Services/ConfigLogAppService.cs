using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class ConfigLogAppService : IConfigLogAppService
    {
        private readonly IConfigLogRepository _repository;
        public ConfigLogAppService(IConfigLogRepository repository) { _repository = repository; }

        public async Task<ConfigLogDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new ConfigLogDto
            {
                Id = entity.Id,
                DeviceId = entity.DeviceId,
                Operator = entity.Operator,
                ChangeDesc = entity.ChangeDesc,
                CreateTime = entity.CreateTime
            };
        }

        public async Task<List<ConfigLogDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new ConfigLogDto
            {
                Id = entity.Id,
                DeviceId = entity.DeviceId,
                Operator = entity.Operator,
                ChangeDesc = entity.ChangeDesc,
                CreateTime = entity.CreateTime
            }).ToList();
        }

        public async Task CreateAsync(ConfigLogDto dto)
        {
            var entity = new ConfigLog
            {
                DeviceId = dto.DeviceId,
                Operator = dto.Operator,
                ChangeDesc = dto.ChangeDesc,
                CreateTime = dto.CreateTime
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(ConfigLogDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.DeviceId = dto.DeviceId;
                entity.Operator = dto.Operator;
                entity.ChangeDesc = dto.ChangeDesc;
                entity.CreateTime = dto.CreateTime;
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

