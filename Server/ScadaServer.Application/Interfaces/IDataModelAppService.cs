using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 数据模型应用服务：管理数据模型的增删改查。
    /// 数据模型承载其下的模型变量模板与通信协议配置。
    /// </summary>
    public interface IDataModelAppService
    {
        /// <summary>按ID查询单个数据模型（默认已聚合变量）；不存在返回 null。</summary>
        Task<DataModelDto?> GetByIdAsync(int id);

        /// <summary>
        /// 获取数据模型列表。
        /// <paramref name="includeVariables"/> 为 false 时跳过模型变量查询，
        /// 用于列表页只展示模型概览、降低 N+1 查询开销；详情页请使用 <see cref="GetByIdAsync"/>（默认已聚合变量）。
        /// </summary>
        Task<List<DataModelDto>> GetListAsync(bool includeVariables = true);

        /// <summary>新增一个数据模型，返回创建后的 DTO。</summary>
        Task<DataModelDto> CreateAsync(CreateDataModelDto dto);

        /// <summary>更新一个数据模型，返回更新后的 DTO。</summary>
        Task<DataModelDto> UpdateAsync(DataModelDto dto);

        /// <summary>删除一个数据模型。</summary>
        Task DeleteAsync(int id);
    }
}

