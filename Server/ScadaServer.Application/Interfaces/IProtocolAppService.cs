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
        Task<ProtocolDto?> GetByIdAsync(int id);
        Task<ProtocolDto?> GetByKeyAsync(string key);
        Task<List<ProtocolDto>> GetListAsync();
        Task<ProtocolDto> CreateAsync(CreateProtocolDto dto);
        Task<ProtocolDto> UpdateAsync(int id, ProtocolDto dto);
        Task DeleteAsync(int id);
    }
}