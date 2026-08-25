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

        public async Task<HmiComponentDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        public async Task<List<HmiComponentDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<int> CreateAsync(HmiComponentDto dto)
        {
            var entity = MapToEntity(dto);
            await _repository.InsertAsync(entity);
            return entity.Id;
        }

        public async Task<bool> UpdateAsync(HmiComponentDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null) return false;

            entity.PageId = dto.PageId;
            entity.Type = dto.Type;
            entity.Name = dto.Name;
            entity.X = dto.X;
            entity.Y = dto.Y;
            entity.Width = dto.Width;
            entity.Height = dto.Height;
            entity.ZIndex = dto.ZIndex;
            entity.BindField = dto.BindField;
            entity.Label = dto.Label;
            entity.BindDeviceId = dto.BindDeviceId;
            entity.BindVariableKey = dto.BindVariableKey;
            entity.PropsJson = dto.PropsJson;
            await _repository.UpdateAsync(entity);
            return true;
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }

        #region 映射

        private static HmiComponentDto MapToDto(HmiComponent entity) => new()
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
            Label = entity.Label,
            BindDeviceId = entity.BindDeviceId,
            BindVariableKey = entity.BindVariableKey,
            PropsJson = entity.PropsJson
        };

        private static HmiComponent MapToEntity(HmiComponentDto dto) => new()
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
            Label = dto.Label,
            BindDeviceId = dto.BindDeviceId,
            BindVariableKey = dto.BindVariableKey,
            PropsJson = dto.PropsJson
        };

        #endregion
    }
}
