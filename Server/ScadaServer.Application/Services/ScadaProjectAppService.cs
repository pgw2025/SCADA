using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class ScadaProjectAppService : IScadaProjectAppService
    {
        private readonly IScadaProjectRepository _repository;
        private readonly IScadaPageRepository _pageRepository;
        private readonly IHmiComponentRepository _componentRepository;
        private readonly IUnitOfWork _uow;

        public ScadaProjectAppService(
            IScadaProjectRepository repository,
            IScadaPageRepository pageRepository,
            IHmiComponentRepository componentRepository,
            IUnitOfWork uow)
        {
            _repository = repository;
            _pageRepository = pageRepository;
            _componentRepository = componentRepository;
            _uow = uow;
        }

        public async Task<ScadaProjectDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new ScadaProjectDto { Id = entity.Id, Name = entity.Name, Description = entity.Description, CreatedAt = entity.CreatedAt };
        }

        public async Task<List<ScadaProjectDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new ScadaProjectDto { Id = entity.Id, Name = entity.Name, Description = entity.Description, CreatedAt = entity.CreatedAt }).ToList();
        }

        public async Task<int> CreateAsync(ScadaProjectDto dto)
        {
            var entity = new ScadaProject { Name = dto.Name, Description = dto.Description ?? string.Empty, CreatedAt = DateTime.Now };
            await _repository.InsertAsync(entity);
            return entity.Id;
        }

        public async Task<bool> UpdateAsync(ScadaProjectDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null) return false;

            entity.Name = dto.Name;
            entity.Description = dto.Description ?? string.Empty;
            await _repository.UpdateAsync(entity);
            return true;
        }

        public async Task DeleteAsync(int id)
        {
            await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                // 获取工程下所有页面
                var pages = await _pageRepository.GetListAsync();
                var projectPages = pages.Where(p => p.ProjectId == id).ToList();

                foreach (var page in projectPages)
                {
                    // 删除页面下所有组件
                    await _componentRepository.DeleteRangeAsync(c => c.PageId == page.Id);
                }

                // 删除所有页面
                await _pageRepository.DeleteRangeAsync(p => p.ProjectId == id);

                // 删除工程
                var entity = await _repository.GetByIdAsync(id);
                if (entity != null) await _repository.DeleteAsync(entity);

                return true;
            });
        }

        public async Task<ScadaProjectFullDto?> GetTreeAsync(int id)
        {
            var project = await _repository.GetByIdAsync(id);
            if (project == null) return null;

            var pages = await _pageRepository.GetListAsync(p => p.ProjectId == id);
            var pageIds = pages.Select(p => p.Id).ToList();

            // 阶段4 整树查询优化：仅拉取本工程页面下的组件（SQL 下推过滤），
            // 替代原先「全表拉取组件 → 内存逐页过滤」的 O(全量组件) 做法。
            var allComponents = pageIds.Count == 0
                ? new List<HmiComponent>()
                : await _componentRepository.GetListAsync(c => pageIds.Contains(c.PageId));

            var result = new ScadaProjectFullDto
            {
                Project = new ScadaProjectDto
                {
                    Id = project.Id,
                    Name = project.Name,
                    Description = project.Description,
                    CreatedAt = project.CreatedAt
                }
            };

            foreach (var page in pages)
            {
                var pageComponents = allComponents
                    .Where(c => c.PageId == page.Id)
                    .Select(c => new HmiComponentDto
                    {
                        Id = c.Id,
                        PageId = c.PageId,
                        Type = c.Type,
                        Name = c.Name,
                        X = c.X,
                        Y = c.Y,
                        Width = c.Width,
                        Height = c.Height,
                        ZIndex = c.ZIndex,
                        BindField = c.BindField,
                        Label = c.Label,
                        BindDeviceId = c.BindDeviceId,
                        BindVariableKey = c.BindVariableKey,
                        PropsJson = c.PropsJson
                    })
                    .ToList();

                result.Pages.Add(new ScadaPageWithComponentsDto
                {
                    Id = page.Id,
                    ProjectId = page.ProjectId,
                    Name = page.Name,
                    IsHome = page.IsHome,
                    Platform = page.Platform,
                    Width = page.Width,
                    Height = page.Height,
                    Components = pageComponents
                });
            }

            return result;
        }
    }
}
