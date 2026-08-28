using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class HmiComponentAppService : IHmiComponentAppService
    {
        private readonly IHmiComponentRepository _repository;
        private readonly IScadaPageRepository _pageRepository;
        private readonly IDeviceRepository _deviceRepository;

        public HmiComponentAppService(
            IHmiComponentRepository repository,
            IScadaPageRepository pageRepository,
            IDeviceRepository deviceRepository)
        {
            _repository = repository;
            _pageRepository = pageRepository;
            _deviceRepository = deviceRepository;
        }

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
            await ValidateAsync(dto);
            var entity = MapToEntity(dto);
            await _repository.InsertAsync(entity);
            return entity.Id;
        }

        public async Task<bool> UpdateAsync(HmiComponentDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null) return false;

            await ValidateAsync(dto);

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

        /// <summary>
        /// 业务校验（阶段4 后端加固）：
        ///  - 页面必须存在（防孤儿组件）；
        ///  - 若指定绑定设备，则设备必须存在（防悬空绑定）。
        /// 失败抛 BusinessException（400），由全局异常中间件统一转为错误响应。
        /// </summary>
        private async Task ValidateAsync(HmiComponentDto dto)
        {
            if (!await _pageRepository.AnyAsync(p => p.Id == dto.PageId))
                throw new BusinessException($"组件所属页面不存在（PageId={dto.PageId}）");

            if (dto.BindDeviceId.HasValue
                && !await _deviceRepository.AnyAsync(d => d.Id == dto.BindDeviceId.Value))
                throw new BusinessException($"组件绑定的设备不存在（BindDeviceId={dto.BindDeviceId}）");
        }

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
