using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// MQTT 服务器管理器：统一连接、断开、发布与状态管理。
    /// 内部维护多台 MQTT 服务器及其变量映射，供运行时采集层将变量更新推送到外部（区别于单条连接的 <see cref="IMqttService"/>）。
    /// </summary>
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
        /// <param name="deviceId">变量所属设备 ID</param>
        /// <param name="variableKey">变量业务键</param>
        /// <param name="value">变量值</param>
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
        /// <param name="dto">连接测试参数</param>
        /// <returns>连接测试结果（成功与否及错误信息）</returns>
        Task<MqttTestConnectionResultDto> TestConnectionAsync(MqttTestConnectionDto dto);
    }
}