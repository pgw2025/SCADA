using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    /// <summary>
    /// HMI 组态组件应用服务：负责页面画布组件（位置、图层、绑定、属性）的增删改查。
    /// 写前校验页面存在、绑定设备存在，避免产生孤儿组件或悬空绑定。
    /// </summary>
    public class HmiComponentAppService : IHmiComponentAppService
    {
        /// <summary>组件仓储，提供持久化能力。</summary>
        private readonly IHmiComponentRepository _repository;
        /// <summary>组态页面仓储，用于校验组件所属页面存在。</summary>
        private readonly IScadaPageRepository _pageRepository;
        /// <summary>设备仓储，用于校验组件绑定的设备存在。</summary>
        private readonly IDeviceRepository _deviceRepository;

        /// <summary>构造函数：注入组件、页面、设备仓储。</summary>
        public HmiComponentAppService(
            IHmiComponentRepository repository,
            IScadaPageRepository pageRepository,
            IDeviceRepository deviceRepository)
        {
            _repository = repository;
            _pageRepository = pageRepository;
            _deviceRepository = deviceRepository;
        }

        /// <summary>按主键获取组件，不存在时返回 null。</summary>
        public async Task<HmiComponentDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        /// <summary>获取全部组件列表。</summary>
        public async Task<List<HmiComponentDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        /// <summary>新增组件：校验后写入，返回生成的主键。</summary>
        public async Task<int> CreateAsync(HmiComponentDto dto)
        {
            await ValidateAsync(dto);
            var entity = MapToEntity(dto);
            await _repository.InsertAsync(entity);
            return entity.Id;
        }

        /// <summary>更新组件：校验后全量覆盖字段，成功返回 true，记录不存在时返回 false。</summary>
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
            entity.LayerId = NormalizeLayerId(dto.LayerId);
            entity.PropsJson = dto.PropsJson;
            await _repository.UpdateAsync(entity);
            return true;
        }

        /// <summary>删除组件；记录不存在时静默忽略。</summary>
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

        /// <summary>将组件实体映射为 DTO。</summary>
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
            LayerId = entity.LayerId,
            PropsJson = entity.PropsJson
        };

        /// <summary>将组件 DTO 映射为实体（图层 ID 经归一化处理）。</summary>
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
            LayerId = NormalizeLayerId(dto.LayerId),
            PropsJson = dto.PropsJson
        };

        /// <summary>归一化图层 ID：空白归 NULL；截断至 64 字符（与列宽一致）。</summary>
        private static string? NormalizeLayerId(string? layerId)
        {
            if (string.IsNullOrWhiteSpace(layerId)) return null;
            var v = layerId.Trim();
            return v.Length > 64 ? v[..64] : v;
        }

        #endregion
    }
}
