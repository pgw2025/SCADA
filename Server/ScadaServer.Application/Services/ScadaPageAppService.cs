using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 组态页面（ScadaPage）应用服务：负责画布页面的增删改查。
    /// 维护同端（ProjectId + Platform）首页唯一性；删除页面时级联删除其下组件。
    /// </summary>
    public class ScadaPageAppService : IScadaPageAppService
    {
        /// <summary>组态页面仓储，提供持久化能力。</summary>
        private readonly IScadaPageRepository _repository;
        /// <summary>组件仓储，用于删除页面时级联清理组件。</summary>
        private readonly IHmiComponentRepository _componentRepository;
        /// <summary>工作单元，用于删除页面及其组件伴随的原子操作。</summary>
        private readonly IUnitOfWork _uow;

        /// <summary>构造函数：注入页面、组件仓储及工作单元。</summary>
        public ScadaPageAppService(
            IScadaPageRepository repository,
            IHmiComponentRepository componentRepository,
            IUnitOfWork uow)
        {
            _repository = repository;
            _componentRepository = componentRepository;
            _uow = uow;
        }

        /// <summary>按主键获取组态页面，不存在时返回 null。</summary>
        public async Task<ScadaPageDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        /// <summary>获取全部组态页面列表。</summary>
        public async Task<List<ScadaPageDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        /// <summary>按项目（可选）与归属端（可选）过滤组态页面列表。</summary>
        public async Task<List<ScadaPageDto>> GetByProjectAsync(int? projectId, string? platform = null)
        {
            IEnumerable<ScadaPage> list = projectId == null
                ? await _repository.GetListAsync()
                : await _repository.GetListAsync(p => p.ProjectId == projectId.Value);

            if (!string.IsNullOrWhiteSpace(platform))
                list = list.Where(p => string.Equals(p.Platform, platform, StringComparison.OrdinalIgnoreCase));

            return list.Select(MapToDto).ToList();
        }

        /// <summary>新增组态页面：同名端设为首页时清除其下其它首页，返回生成的主键。</summary>
        public async Task<int> CreateAsync(ScadaPageDto dto)
        {
            var platform = NormalizePlatform(dto.Platform);

            // 同端首页唯一：新建即设为首页时，清除同 (ProjectId, Platform) 下其它首页
            if (dto.IsHome)
                await UnsetOtherHomePagesAsync(dto.ProjectId, platform, null);

            var entity = new ScadaPage
            {
                ProjectId = dto.ProjectId,
                Name = dto.Name,
                IsHome = dto.IsHome,
                Platform = platform,
                Width = dto.Width > 0 ? dto.Width : 1100,
                Height = dto.Height > 0 ? dto.Height : 700,
                BackgroundJson = NormalizeBackgroundJson(dto.BackgroundJson),
                AdaptMode = NormalizeAdaptMode(dto.AdaptMode),
                LayersJson = ScadaLayerJson.Normalize(dto.LayersJson)
            };
            await _repository.InsertAsync(entity);
            return entity.Id;
        }

        /// <summary>更新组态页面：处理首页唯一性与字段归一化，成功返回 true，记录不存在时返回 false。</summary>
        public async Task<bool> UpdateAsync(ScadaPageDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null) return false;

            var platform = NormalizePlatform(dto.Platform);

            // 同端首页唯一：若本次设为首页，清除同 (ProjectId, Platform) 下其它首页
            if (dto.IsHome)
                await UnsetOtherHomePagesAsync(dto.ProjectId, platform, dto.Id);

            entity.Name = dto.Name;
            entity.IsHome = dto.IsHome;
            entity.Platform = platform;
            entity.Width = dto.Width > 0 ? dto.Width : entity.Width;
            entity.Height = dto.Height > 0 ? dto.Height : entity.Height;
            entity.BackgroundJson = NormalizeBackgroundJson(dto.BackgroundJson);
            entity.AdaptMode = NormalizeAdaptMode(dto.AdaptMode);
            entity.LayersJson = ScadaLayerJson.Normalize(dto.LayersJson);
            await _repository.UpdateAsync(entity);
            return true;
        }

        /// <summary>删除组态页面：同一事务内级联删除其下组件后再删除页面。</summary>
        public async Task DeleteAsync(int id)
        {
            await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                // 删除页面下所有组件
                await _componentRepository.DeleteRangeAsync(c => c.PageId == id);

                // 删除页面
                var entity = await _repository.GetByIdAsync(id);
                if (entity != null) await _repository.DeleteAsync(entity);

                return true;
            });
        }

        #region 映射

        /// <summary>将组态页面实体映射为 DTO。</summary>
        private static ScadaPageDto MapToDto(ScadaPage entity) => new()
        {
            Id = entity.Id,
            ProjectId = entity.ProjectId,
            Name = entity.Name,
            IsHome = entity.IsHome,
            Platform = entity.Platform,
            Width = entity.Width,
            Height = entity.Height,
            BackgroundJson = entity.BackgroundJson,
            AdaptMode = entity.AdaptMode,
            LayersJson = entity.LayersJson
        };

        /// <summary>归一化归属端：空/非法值一律回退 Desktop。</summary>
        private static string NormalizePlatform(string? platform)
            => string.Equals(platform, "Mobile", StringComparison.OrdinalIgnoreCase) ? "Mobile" : "Desktop";

        /// <summary>
        /// 归一化背景配置 JSON：空白串归一化为 NULL（未配置）。
        /// 内容结构由前端负责序列化/校验，后端仅透传存储。
        /// </summary>
        private static string? NormalizeBackgroundJson(string? json)
            => string.IsNullOrWhiteSpace(json) ? null : json.Trim();

        /// <summary>归一化自适应模式：仅允许 FitScaleUp / Stretch，其余（含空）归一化为 NULL。</summary>
        private static string? NormalizeAdaptMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return null;
            return mode.Trim() switch
            {
                "FitScaleUp" => "FitScaleUp",
                "Stretch" => "Stretch",
                _ => null
            };
        }

        /// <summary>
        /// 保证同一 (ProjectId, Platform) 范围内至多一个首页：将除 excludeId 外的其它首页置否。
        /// </summary>
        private async Task UnsetOtherHomePagesAsync(int projectId, string platform, int? excludeId)
        {
            var siblings = await _repository.GetListAsync(p =>
                p.ProjectId == projectId && p.Platform == platform && p.IsHome);
            foreach (var s in siblings)
            {
                if (excludeId != null && s.Id == excludeId.Value) continue;
                s.IsHome = false;
                await _repository.UpdateAsync(s);
            }
        }

        #endregion
    }
}
