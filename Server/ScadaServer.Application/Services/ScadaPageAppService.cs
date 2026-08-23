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

        public async Task<ScadaPageDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new ScadaPageDto { Id = entity.Id, ProjectId = entity.ProjectId, Name = entity.Name, IsHome = entity.IsHome };
        }

        public async Task<List<ScadaPageDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new ScadaPageDto { Id = entity.Id, ProjectId = entity.ProjectId, Name = entity.Name, IsHome = entity.IsHome }).ToList();
        }

        public async Task CreateAsync(ScadaPageDto dto)
        {
            var entity = new ScadaPage { ProjectId = dto.ProjectId, Name = dto.Name, IsHome = dto.IsHome };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(ScadaPageDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Name = dto.Name;
                entity.IsHome = dto.IsHome;
                await _repository.UpdateAsync(entity);
            }
        }

        public async Task DeleteAsync(int id)
        {
            await using var transaction = await _uow.BeginTransactionAsync();
            try
            {
                // 删除页面下所有组件
                await _componentRepository.DeleteRangeAsync(c => c.PageId == id);

                // 删除页面
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

