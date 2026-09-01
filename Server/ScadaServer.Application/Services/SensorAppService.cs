using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 传感器应用服务实现：负责传感器（设备变量在人机界面的筛选视图）的增删改查（CRUD）。
    /// </summary>
    public class SensorAppService : ISensorAppService
    {
        /// <summary>传感器仓储，提供持久化能力。</summary>
        private readonly ISensorRepository _repository;

        /// <summary>构造函数：注入传感器仓储。</summary>
        public SensorAppService(ISensorRepository repository) { _repository = repository; }

        /// <summary>按主键获取传感器，不存在时返回 null。</summary>
        public async Task<SensorDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new SensorDto
            {
                Id = entity.Id,
                DeviceId = entity.DeviceId,
                VariableKey = entity.VariableKey,
                Name = entity.Name,
                Unit = entity.Unit,
                LastValue = entity.LastValue,
                LastUpdateTime = entity.LastUpdateTime
            };
        }

        /// <summary>获取全部传感器列表。</summary>
        public async Task<List<SensorDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new SensorDto
            {
                Id = entity.Id,
                DeviceId = entity.DeviceId,
                VariableKey = entity.VariableKey,
                Name = entity.Name,
                Unit = entity.Unit,
                LastValue = entity.LastValue,
                LastUpdateTime = entity.LastUpdateTime
            }).ToList();
        }

        /// <summary>新增传感器。</summary>
        public async Task CreateAsync(SensorDto dto)
        {
            var entity = new Sensor
            {
                DeviceId = dto.DeviceId,
                VariableKey = dto.VariableKey,
                Name = dto.Name,
                Unit = dto.Unit,
                LastValue = dto.LastValue,
                LastUpdateTime = dto.LastUpdateTime
            };
            await _repository.InsertAsync(entity);
        }

        /// <summary>更新传感器；记录不存在时静默忽略。</summary>
        public async Task UpdateAsync(SensorDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.DeviceId = dto.DeviceId;
                entity.VariableKey = dto.VariableKey;
                entity.Name = dto.Name;
                entity.Unit = dto.Unit;
                entity.LastValue = dto.LastValue;
                entity.LastUpdateTime = dto.LastUpdateTime;
                await _repository.UpdateAsync(entity);
            }
        }

        /// <summary>删除传感器；记录不存在时静默忽略。</summary>
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

