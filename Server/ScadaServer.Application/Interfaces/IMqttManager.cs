namespace ScadaServer.Application.Interfaces
{
    public interface IMqttManager
    {
        /// <summary>
        /// 初始化并连接所有已配置的 MQTT 服务器
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
        /// 重新加载 MQTT 配置和映射
        /// </summary>
        Task ReloadAsync();
    }
}
