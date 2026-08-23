using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class HmiComponentAppService : IHmiComponentAppService
    {
        private readonly IHmiComponentRepository _repository;
        public HmiComponentAppService(IHmiComponentRepository repository) { _repository = repository; }

        public async Task<HmiComponentDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new HmiComponentDto
            {
                Id = entity.Id,
                PageId = entity.PageId,
                Type = entity.Type,
                Name = entity.Name,
                X = entity.X,
                Y = entity.Y,
                Width = entity.Width,
                Height = entity.Height,
                ZIndex = entity.ZIndex,
                BindField = entity.BindField,
                PropsJson = entity.PropsJson
            };
        }

        public async Task<List<HmiComponentDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new HmiComponentDto
            {
                Id = entity.Id,
                PageId = entity.PageId,
                Type = entity.Type,
                Name = entity.Name,
                X = entity.X,
                Y = entity.Y,
                Width = entity.Width,
                Height = entity.Height,
                ZIndex = entity.ZIndex,
                BindField = entity.BindField,
                PropsJson = entity.PropsJson
            }).ToList();
        }

        public async Task CreateAsync(HmiComponentDto dto)
        {
            var entity = new HmiComponent
            {
                PageId = dto.PageId,
                Type = dto.Type,
                Name = dto.Name,
                X = dto.X,
                Y = dto.Y,
                Width = dto.Width,
                Height = dto.Height,
                ZIndex = dto.ZIndex,
                BindField = dto.BindField,
                PropsJson = dto.PropsJson
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(HmiComponentDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.PageId = dto.PageId;
                entity.Type = dto.Type;
                entity.Name = dto.Name;
                entity.X = dto.X;
                entity.Y = dto.Y;
                entity.Width = dto.Width;
                entity.Height = dto.Height;
                entity.ZIndex = dto.ZIndex;
                entity.BindField = dto.BindField;
                entity.PropsJson = dto.PropsJson;
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

