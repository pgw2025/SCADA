using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 数据转换应用服务：管理数据转换（变量间数据转发）规则的增删改查。
    /// 每条规则定义一个源设备变量 → 目标设备变量的数据转发映射及其启停状态。
    /// </summary>
    public interface IDataConversionAppService
    {
        /// <summary>按ID查询单个数据转换规则；不存在返回 null。</summary>
        Task<DataConversionDto?> GetByIdAsync(int id);

        /// <summary>查询全部数据转换规则。</summary>
        Task<List<DataConversionDto>> GetListAsync();

        /// <summary>新增一个数据转换规则。</summary>
        Task CreateAsync(DataConversionDto dto);

        /// <summary>更新一个数据转换规则。</summary>
        Task UpdateAsync(DataConversionDto dto);

        /// <summary>删除一个数据转换规则。</summary>
        Task DeleteAsync(int id);
    }
}

