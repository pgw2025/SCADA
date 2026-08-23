using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class AreaAppService : IAreaAppService
    {
        private readonly IAreaRepository _repository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly IUnitOfWork _uow;

        public AreaAppService(IAreaRepository repository, IDeviceRepository deviceRepository, IUnitOfWork uow) 
        { 
            _repository = repository; 
            _deviceRepository = deviceRepository;
            _uow = uow;
        }

        public async Task<AreaDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new AreaDto { Id = entity.Id, Name = entity.Name, Description = entity.Description };
        }

        public async Task<List<AreaDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new AreaDto { Id = entity.Id, Name = entity.Name, Description = entity.Description }).ToList();
        }

        public async Task<AreaDto> CreateAsync(AreaDto dto)
        {
            // 业务校验：名称不能重复
            var existing = await _repository.GetListAsync(a => a.Name == dto.Name);
            if (existing.Any())
            {
                throw new BusinessException($"区域名称 '{dto.Name}' 已存在");
            }

            var entity = new Area { Name = dto.Name, Description = dto.Description };
            await _repository.InsertAsync(entity);

            // 返回包含生成 ID 的 DTO
            dto.Id = entity.Id;
            return dto;
        }

        public async Task<AreaDto> UpdateAsync(AreaDto dto)
        {
            // 1. 检查记录是否存在
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {dto.Id} 的区域不存在");
            }

            // 2. 业务校验：名称不能与其他区域重复
            var existing = await _repository.GetListAsync(a => a.Name == dto.Name && a.Id != dto.Id);
            if (existing.Any())
            {
                throw new BusinessException($"区域名称 '{dto.Name}' 已存在");
            }

            // 3. 更新字段
            entity.Name = dto.Name;
            entity.Description = dto.Description;
            await _repository.UpdateAsync(entity);

            // 4. 返回最新的 DTO
            return new AreaDto 
            { 
                Id = entity.Id, 
                Name = entity.Name, 
                Description = entity.Description 
            };
        }

        public async Task DeleteAsync(int id)
        {
            // 1. 检查区域是否存在
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            // 2. 安全检查：如果该区域下还有设备，禁止删除
            // 获取该区域下的设备数量
            var devices = await _deviceRepository.GetListAsync(d => d.AreaId == id);
            if (devices.Any())
            {
                throw new BusinessException($"无法删除区域 '{entity.Name}'，因为该区域下尚有 {devices.Count} 台设备。请先移除或删除相关设备。");
            }

            // 3. 执行删除
            await _repository.DeleteAsync(entity);
        }
    }
}

