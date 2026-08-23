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

        public async Task<ScadaProjectDto> GetByIdAsync(int id)
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

        public async Task CreateAsync(ScadaProjectDto dto)
        {
            var entity = new ScadaProject { Name = dto.Name, Description = dto.Description, CreatedAt = DateTime.Now };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(ScadaProjectDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Name = dto.Name;
                entity.Description = dto.Description;
                await _repository.UpdateAsync(entity);
            }
        }

        public async Task DeleteAsync(int id)
        {
            await using var transaction = await _uow.BeginTransactionAsync();
            try
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

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}

