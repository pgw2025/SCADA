using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 控制器应用服务：管理控制器/PLC 资产台账（阶段 2）。
    /// 当前仅资产登记（CRUD/列表），不产生任何采集行为；运行连接配置在后续阶段接入。
    /// </summary>
    public interface IControllerAppService
    {
        /// <summary>按ID查询单个控制器；不存在返回 null。</summary>
        Task<ControllerDto?> GetByIdAsync(int id);

        /// <summary>查询全部控制器。</summary>
        Task<List<ControllerDto>> GetListAsync();

        /// <summary>按协议/关键字过滤 + 分页查询控制器。</summary>
        Task<ControllerPagedResultDto> QueryAsync(ControllerQueryDto query);

        /// <summary>下拉数据源（Id+Code+Name+Protocol）。</summary>
        Task<List<ControllerOptionDto>> GetOptionsAsync();

        /// <summary>新增一个控制器，返回创建后的 DTO（含自增ID）。</summary>
        Task<ControllerDto> CreateAsync(CreateControllerDto dto);

        /// <summary>按ID更新指定控制器，返回更新后的 DTO。</summary>
        Task<ControllerDto> UpdateAsync(int id, CreateControllerDto dto);

        /// <summary>删除指定控制器。</summary>
        Task DeleteAsync(int id);
    }
}
