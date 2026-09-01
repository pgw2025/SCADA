using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 系统脚本应用服务：管理系统脚本的增删改查及熔断状态复位。
    /// 脚本由运行时脚本引擎按触发策略执行。
    /// </summary>
    public interface ISystemScriptAppService
    {
        /// <summary>按ID查询单个脚本；不存在返回 null。</summary>
        Task<SystemScriptDto?> GetByIdAsync(int id);

        /// <summary>查询全部脚本。</summary>
        Task<List<SystemScriptDto>> GetListAsync();

        /// <summary>新增一个脚本。</summary>
        Task CreateAsync(SystemScriptDto dto);

        /// <summary>更新一个脚本。</summary>
        Task UpdateAsync(SystemScriptDto dto);

        /// <summary>删除一个脚本。</summary>
        Task DeleteAsync(int id);

        /// <summary>
        /// 人工复位熔断状态（Tripped=false、FailureCount=0、LastError=null）。
        /// </summary>
        Task ResetTrippedAsync(int id);
    }
}

