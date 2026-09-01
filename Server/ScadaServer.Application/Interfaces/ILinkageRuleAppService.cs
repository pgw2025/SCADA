using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 联动规则应用服务：管理变量联动规则的增删改查。
    /// 联动规则用于定义"条件变量满足时触发动作变量写入"的自动化控制逻辑。
    /// </summary>
    public interface ILinkageRuleAppService
    {
        /// <summary>按ID查询单个联动规则；不存在返回 null。</summary>
        Task<LinkageRuleDto?> GetByIdAsync(int id);

        /// <summary>查询全部联动规则。</summary>
        Task<List<LinkageRuleDto>> GetListAsync();

        /// <summary>新增一个联动规则。</summary>
        Task CreateAsync(LinkageRuleDto dto);

        /// <summary>更新一个联动规则。</summary>
        Task UpdateAsync(LinkageRuleDto dto);

        /// <summary>删除一个联动规则。</summary>
        Task DeleteAsync(int id);
    }
}
