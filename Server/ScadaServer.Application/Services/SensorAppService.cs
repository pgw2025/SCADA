using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class SensorAppService : ISensorAppService
    {
        private readonly ISensorRepository _repository;
        public SensorAppService(ISensorRepository repository) { _repository = repository; }

        public async Task<SensorDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new SensorDto
            {
                Id = entity.Id,
                DeviceId = entity.DeviceId,
                VariableKey = entity.VariableKey,
                Name = entity.Name,
                Unit = entity.Unit,
                LastValue = entity.LastValue,
                LastUpdateTime = entity.LastUpdateTime
            };
        }

        public async Task<List<SensorDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new SensorDto
            {
                Id = entity.Id,
                DeviceId = entity.DeviceId,
                VariableKey = entity.VariableKey,
                Name = entity.Name,
                Unit = entity.Unit,
                LastValue = entity.LastValue,
                LastUpdateTime = entity.LastUpdateTime
            }).ToList();
        }

        public async Task CreateAsync(SensorDto dto)
        {
            var entity = new Sensor
            {
                DeviceId = dto.DeviceId,
                VariableKey = dto.VariableKey,
                Name = dto.Name,
                Unit = dto.Unit,
                LastValue = dto.LastValue,
                LastUpdateTime = dto.LastUpdateTime
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(SensorDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.DeviceId = dto.DeviceId;
                entity.VariableKey = dto.VariableKey;
                entity.Name = dto.Name;
                entity.Unit = dto.Unit;
                entity.LastValue = dto.LastValue;
                entity.LastUpdateTime = dto.LastUpdateTime;
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

