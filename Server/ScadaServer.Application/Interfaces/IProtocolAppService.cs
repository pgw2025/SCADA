using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 通信协议应用服务：管理系统支持的通信协议（协议/驱动解耦）。
    /// 协议是"数据模型如何通信"的真相源，前端创建数据模型、创建设备时的协议选择来自此接口。
    /// </summary>
    public interface IProtocolAppService
    {
        /// <summary>按ID查询单个协议；不存在返回 null。</summary>
        Task<ProtocolDto?> GetByIdAsync(int id);

        /// <summary>按协议键（Key）查询单个协议；不存在返回 null。</summary>
        Task<ProtocolDto?> GetByKeyAsync(string key);

        /// <summary>查询全部协议。</summary>
        Task<List<ProtocolDto>> GetListAsync();

        /// <summary>新增一个协议，返回创建后的 DTO（含自增ID）。</summary>
        Task<ProtocolDto> CreateAsync(CreateProtocolDto dto);

        /// <summary>按ID更新指定协议，返回更新后的 DTO。</summary>
        Task<ProtocolDto> UpdateAsync(int id, ProtocolDto dto);

        /// <summary>删除指定协议。</summary>
        Task DeleteAsync(int id);
    }
}