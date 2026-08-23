using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class DataConversionAppService : IDataConversionAppService
    {
        private readonly IDataConversionRepository _repository;
        public DataConversionAppService(IDataConversionRepository repository) { _repository = repository; }

        public async Task<DataConversionDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new DataConversionDto
            {
                Id = entity.Id,
                Name = entity.Name,
                SourceDeviceId = entity.SourceDeviceId,
                SourceVariableKey = entity.SourceVariableKey,
                TargetDeviceId = entity.TargetDeviceId,
                TargetVariableKey = entity.TargetVariableKey,
                Active = entity.Active
            };
        }

        public async Task<List<DataConversionDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new DataConversionDto
            {
                Id = entity.Id,
                Name = entity.Name,
                SourceDeviceId = entity.SourceDeviceId,
                SourceVariableKey = entity.SourceVariableKey,
                TargetDeviceId = entity.TargetDeviceId,
                TargetVariableKey = entity.TargetVariableKey,
                Active = entity.Active
            }).ToList();
        }

        public async Task CreateAsync(DataConversionDto dto)
        {
            var entity = new DataConversion
            {
                Name = dto.Name,
                SourceDeviceId = dto.SourceDeviceId,
                SourceVariableKey = dto.SourceVariableKey,
                TargetDeviceId = dto.TargetDeviceId,
                TargetVariableKey = dto.TargetVariableKey,
                Active = dto.Active
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(DataConversionDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Name = dto.Name;
                entity.SourceDeviceId = dto.SourceDeviceId;
                entity.SourceVariableKey = dto.SourceVariableKey;
                entity.TargetDeviceId = dto.TargetDeviceId;
                entity.TargetVariableKey = dto.TargetVariableKey;
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

