using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 底层 MQTT 客户端抽象接口：提供主题发布 / 订阅的能力。
    /// 注意：当前实现 <see cref="Infrastructure.Communication.MqttHandler"/> 为空实现桩，
    /// 实际的多服务器 MQTT 交互由 <see cref="IMqttManager"/> 完成。
    /// </summary>
    public interface IMqttService
    {
        /// <summary>向指定主题发布一条消息。</summary>
        /// <param name="topic">目标主题</param>
        /// <param name="payload">消息内容（UTF-8 文本）</param>
        Task PublishAsync(string topic, string payload);

        /// <summary>订阅指定主题。</summary>
        /// <param name="topic">要订阅的主题</param>
        Task SubscribeAsync(string topic);
    }
}

