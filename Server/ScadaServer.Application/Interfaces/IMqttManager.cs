using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    public interface IMqttManager
    {
        /// <summary>
        /// 初始化并连接所有已配置且启用的 MQTT 服务器
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// 断开所有 MQTT 连接
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// 发布变量更新到关联的所有 MQTT 服务器
        /// </summary>
        Task PublishVariableUpdateAsync(int deviceId, string variableKey, object value);

        /// <summary>
        /// 重新加载 MQTT 配置和映射（增删改服务器/映射后调用，实现热生效）
        /// </summary>
        Task ReloadAsync();

        /// <summary>
        /// 重连所有「已启用但当前未连接」的服务器（由后台重连服务周期性调用）
        /// </summary>
        Task ReconnectAsync();

        /// <summary>
        /// 返回所有服务器的实时状态（连接状态/错误/重连次数等），供前端卡片展示。
        /// </summary>
        Task<List<MqttServerStatusDto>> GetStatusesAsync();

        /// <summary>
        /// 使用给定参数测试连接（不落库、不影响现有连接），返回成功/失败与错误信息。
        /// </summary>
        Task<MqttTestConnectionResultDto> TestConnectionAsync(MqttTestConnectionDto dto);
    }
}