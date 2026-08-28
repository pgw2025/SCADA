using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class ScadaPageAppService : IScadaPageAppService
    {
        private readonly IScadaPageRepository _repository;
        private readonly IHmiComponentRepository _componentRepository;
        private readonly IUnitOfWork _uow;

        public ScadaPageAppService(
            IScadaPageRepository repository,
            IHmiComponentRepository componentRepository,
            IUnitOfWork uow)
        {
            _repository = repository;
            _componentRepository = componentRepository;
            _uow = uow;
        }

        public async Task<ScadaPageDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        public async Task<List<ScadaPageDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<List<ScadaPageDto>> GetByProjectAsync(int? projectId, string? platform = null)
        {
            IEnumerable<ScadaPage> list = projectId == null
                ? await _repository.GetListAsync()
                : await _repository.GetListAsync(p => p.ProjectId == projectId.Value);

            if (!string.IsNullOrWhiteSpace(platform))
                list = list.Where(p => string.Equals(p.Platform, platform, StringComparison.OrdinalIgnoreCase));

            return list.Select(MapToDto).ToList();
        }

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
                Height = dto.Height > 0 ? dto.Height : 700
            };
            await _repository.InsertAsync(entity);
            return entity.Id;
        }

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
            await _repository.UpdateAsync(entity);
            return true;
        }

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

        private static ScadaPageDto MapToDto(ScadaPage entity) => new()
        {
            Id = entity.Id,
            ProjectId = entity.ProjectId,
            Name = entity.Name,
            IsHome = entity.IsHome,
            Platform = entity.Platform,
            Width = entity.Width,
            Height = entity.Height
        };

        /// <summary>归一化归属端：空/非法值一律回退 Desktop。</summary>
        private static string NormalizePlatform(string? platform)
            => string.Equals(platform, "Mobile", StringComparison.OrdinalIgnoreCase) ? "Mobile" : "Desktop";

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
