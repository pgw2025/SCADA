using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 系统全局配置应用服务：管理系统级配置（系统标题、轮询间隔、MQTT Broker 地址、数据保留周期等）的增删改查。
    /// </summary>
    public interface ISystemConfigAppService
    {
        /// <summary>按ID查询单个系统配置项；不存在返回 null。</summary>
        Task<SystemConfigDto?> GetByIdAsync(int id);

        /// <summary>查询全部系统配置项。</summary>
        Task<List<SystemConfigDto>> GetListAsync();

        /// <summary>新增一个系统配置项。</summary>
        Task CreateAsync(SystemConfigDto dto);

        /// <summary>更新一个系统配置项。</summary>
        Task UpdateAsync(SystemConfigDto dto);

        /// <summary>删除一个系统配置项；不存在时静默忽略或抛出业务异常（由实现决定）。</summary>
        Task DeleteAsync(int id);
    }
}

