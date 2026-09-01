using ScadaServer.Application.Interfaces;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>
    /// MQTT 消息处理服务实现（占位/预留）。
    /// <para>
    /// 当前仅用于满足 <see cref="IMqttService"/> 接口契约，实际的消息发布与订阅
    /// 由 <see cref="MqttManager"/> 承担（它根据 MQTT 变量映射将设备变量变更实时发布到 Broker）。
    /// 本类型在现有版本中没有接入业务调用，方法均返回 <see cref="Task.CompletedTask"/>，
    /// 保留它是为了在后续需要给 MQTT 通道追加一层应用级过滤/格式化逻辑时自动接入而无须改动接口。
    /// </para>
    /// </summary>
    public class MqttHandler : IMqttService
    {
        /// <summary>
        /// 发布一条消息到指定主题。
        /// <para>占位实现：不执行任何网络操作，直接返回完成。</para>
        /// </summary>
        /// <param name="topic">MQTT 主题</param>
        /// <param name="payload">消息负载（字符串）</param>
        public async Task PublishAsync(string topic, string payload)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// 订阅指定主题。
        /// <para>占位实现：不执行任何网络操作，直接返回完成。</para>
        /// </summary>
        /// <param name="topic">MQTT 主题</param>
        public async Task SubscribeAsync(string topic)
        {
            await Task.CompletedTask;
        }
    }
}
