using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class ExposedInterfaceAppService : IExposedInterfaceAppService
    {
        private readonly IExposedInterfaceRepository _repository;
        public ExposedInterfaceAppService(IExposedInterfaceRepository repository) { _repository = repository; }

        public async Task<ExposedInterfaceDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new ExposedInterfaceDto
            {
                Id = entity.Id,
                Name = entity.Name,
                RouteUrl = entity.RouteUrl,
                RequestMethod = entity.RequestMethod,
                DeviceId = entity.DeviceId,
                ExposedKey = entity.ExposedKey,
                Active = entity.Active
            };
        }

        public async Task<List<ExposedInterfaceDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new ExposedInterfaceDto
            {
                Id = entity.Id,
                Name = entity.Name,
                RouteUrl = entity.RouteUrl,
                RequestMethod = entity.RequestMethod,
                DeviceId = entity.DeviceId,
                ExposedKey = entity.ExposedKey,
                Active = entity.Active
            }).ToList();
        }

        public async Task CreateAsync(ExposedInterfaceDto dto)
        {
            var entity = new ExposedInterface
            {
                Name = dto.Name,
                RouteUrl = dto.RouteUrl,
                RequestMethod = dto.RequestMethod,
                DeviceId = dto.DeviceId,
                ExposedKey = dto.ExposedKey,
                Active = dto.Active
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(ExposedInterfaceDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Name = dto.Name;
                entity.RouteUrl = dto.RouteUrl;
                entity.RequestMethod = dto.RequestMethod;
                entity.DeviceId = dto.DeviceId;
                entity.ExposedKey = dto.ExposedKey;
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

