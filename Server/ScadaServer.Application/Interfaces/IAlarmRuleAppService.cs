using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 报警规则应用服务：管理报警规则的增删改查。
    /// 报警规则用于定义变量越限或条件触发时产生报警的事件，由运行时报警检测层消费。
    /// </summary>
    public interface IAlarmRuleAppService
    {
        /// <summary>按ID查询单个报警规则；不存在返回 null。</summary>
        Task<AlarmRuleDto?> GetByIdAsync(int id);

        /// <summary>查询全部报警规则。</summary>
        Task<List<AlarmRuleDto>> GetListAsync();

        /// <summary>新增一个报警规则。</summary>
        Task CreateAsync(AlarmRuleDto dto);

        /// <summary>更新一个报警规则。</summary>
        Task UpdateAsync(AlarmRuleDto dto);

        /// <summary>删除一个报警规则。</summary>
        Task DeleteAsync(int id);
    }
}

