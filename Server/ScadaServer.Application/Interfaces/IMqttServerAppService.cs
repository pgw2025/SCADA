using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    public interface IMqttServerAppService
    {
        Task<MqttServerDto?> GetByIdAsync(int id);
        Task<List<MqttServerDto>> GetListAsync();
        Task CreateAsync(MqttServerDto dto);
        Task UpdateAsync(MqttServerDto dto);
        Task DeleteAsync(int id);

        /// <summary>
        /// 启用/停用服务器（停用即断开连接且不再发布），返回更新后的 DTO。
        /// </summary>
        Task SetEnabledAsync(int id, bool enabled);

        /// <summary>
        /// 返回所有服务器的实时连接状态（供前端卡片展示）。
        /// </summary>
        Task<List<MqttServerStatusDto>> GetStatusesAsync();

        /// <summary>
        /// 使用给定参数测试连接（不落库），返回成功/失败与错误信息。
        /// </summary>
        Task<MqttTestConnectionResultDto> TestConnectionAsync(MqttServerDto dto);
    }
}