using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 配置变更日志应用服务实现：记录设备配置变更的操作日志（操作人、变更描述、时间）。
    /// 提供日志的增删改查（CRUD）。
    /// </summary>
    public class ConfigLogAppService : IConfigLogAppService
    {
        /// <summary>配置日志仓储，提供持久化能力。</summary>
        private readonly IConfigLogRepository _repository;

        /// <summary>构造函数：注入配置日志仓储。</summary>
        public ConfigLogAppService(IConfigLogRepository repository) { _repository = repository; }

        /// <summary>按主键获取配置日志，不存在时返回 null。</summary>
        public async Task<ConfigLogDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new ConfigLogDto
            {
                Id = entity.Id,
                DeviceId = entity.DeviceId,
                Operator = entity.Operator,
                ChangeDesc = entity.ChangeDesc,
                CreateTime = entity.CreateTime
            };
        }

        /// <summary>获取全部配置日志列表。</summary>
        public async Task<List<ConfigLogDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new ConfigLogDto
            {
                Id = entity.Id,
                DeviceId = entity.DeviceId,
                Operator = entity.Operator,
                ChangeDesc = entity.ChangeDesc,
                CreateTime = entity.CreateTime
            }).ToList();
        }

        /// <summary>新增配置日志。</summary>
        public async Task CreateAsync(ConfigLogDto dto)
        {
            var entity = new ConfigLog
            {
                DeviceId = dto.DeviceId,
                Operator = dto.Operator,
                ChangeDesc = dto.ChangeDesc,
                CreateTime = dto.CreateTime
            };
            await _repository.InsertAsync(entity);
        }

        /// <summary>更新配置日志；记录不存在时静默忽略。</summary>
        public async Task UpdateAsync(ConfigLogDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.DeviceId = dto.DeviceId;
                entity.Operator = dto.Operator;
                entity.ChangeDesc = dto.ChangeDesc;
                entity.CreateTime = dto.CreateTime;
                await _repository.UpdateAsync(entity);
            }
        }

        /// <summary>删除配置日志；记录不存在时静默忽略。</summary>
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

