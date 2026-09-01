using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 系统配置应用服务实现：负责系统级配置（标题、轮询间隔、MQTT 地址、历史保留天数）的增删改查。
    /// </summary>
    public class SystemConfigAppService : ISystemConfigAppService
    {
        /// <summary>系统配置仓储，提供持久化能力。</summary>
        private readonly ISystemConfigRepository _repository;

        /// <summary>构造函数：注入系统配置仓储。</summary>
        public SystemConfigAppService(ISystemConfigRepository repository) { _repository = repository; }

        /// <summary>按主键获取系统配置，不存在时返回 null。</summary>
        public async Task<SystemConfigDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new SystemConfigDto
            {
                Id = entity.Id,
                SystemTitle = entity.SystemTitle,
                PollIntervalMs = entity.PollIntervalMs,
                MqttBrokerHost = entity.MqttBrokerHost,
                RetentionPeriodDays = entity.RetentionPeriodDays
            };
        }

        /// <summary>获取全部系统配置列表。</summary>
        public async Task<List<SystemConfigDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new SystemConfigDto
            {
                Id = entity.Id,
                SystemTitle = entity.SystemTitle,
                PollIntervalMs = entity.PollIntervalMs,
                MqttBrokerHost = entity.MqttBrokerHost,
                RetentionPeriodDays = entity.RetentionPeriodDays
            }).ToList();
        }

        /// <summary>新增系统配置。</summary>
        public async Task CreateAsync(SystemConfigDto dto)
        {
            var entity = new SystemConfig
            {
                SystemTitle = dto.SystemTitle,
                PollIntervalMs = dto.PollIntervalMs,
                MqttBrokerHost = dto.MqttBrokerHost,
                RetentionPeriodDays = dto.RetentionPeriodDays
            };
            await _repository.InsertAsync(entity);
        }

        /// <summary>更新系统配置；记录不存在时静默忽略。</summary>
        public async Task UpdateAsync(SystemConfigDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.SystemTitle = dto.SystemTitle;
                entity.PollIntervalMs = dto.PollIntervalMs;
                entity.MqttBrokerHost = dto.MqttBrokerHost;
                entity.RetentionPeriodDays = dto.RetentionPeriodDays;
                await _repository.UpdateAsync(entity);
            }
        }

        /// <summary>删除系统配置；记录不存在时静默忽略。</summary>
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

