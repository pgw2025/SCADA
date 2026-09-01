using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// MQTT 服务器应用服务：管理 MQTT 服务器配置的增删改查、启停与连接测试。
    /// 配置变更后通过 <see cref="IMqttManager"/> 实现运行时热生效。
    /// </summary>
    public interface IMqttServerAppService
    {
        /// <summary>按ID查询单个 MQTT 服务器配置；不存在返回 null。</summary>
        Task<MqttServerDto?> GetByIdAsync(int id);

        /// <summary>查询全部 MQTT 服务器配置。</summary>
        Task<List<MqttServerDto>> GetListAsync();

        /// <summary>新增一个 MQTT 服务器配置。</summary>
        Task CreateAsync(MqttServerDto dto);

        /// <summary>更新一个 MQTT 服务器配置。</summary>
        Task UpdateAsync(MqttServerDto dto);

        /// <summary>删除一个 MQTT 服务器配置。</summary>
        Task DeleteAsync(int id);

        /// <summary>
        /// 启用/停用服务器（停用即断开连接且不再发布），返回更新后的 DTO。
        /// </summary>
        /// <param name="id">服务器 ID</param>
        /// <param name="enabled">是否启用</param>
        Task SetEnabledAsync(int id, bool enabled);

        /// <summary>
        /// 返回所有服务器的实时连接状态（供前端卡片展示）。
        /// </summary>
        Task<List<MqttServerStatusDto>> GetStatusesAsync();

        /// <summary>
        /// 使用给定参数测试连接（不落库），返回成功/失败与错误信息。
        /// </summary>
        /// <param name="dto">连接测试参数</param>
        /// <returns>连接测试结果（成功与否及错误信息）</returns>
        Task<MqttTestConnectionResultDto> TestConnectionAsync(MqttTestConnectionDto dto);
    }
}