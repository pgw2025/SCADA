namespace ScadaServer.Application.Interfaces
{
    public interface IExternalMessageSender
    {
        string Name { get; }
        bool Enabled { get; }

        /// <summary>收件方摘要（投递记录展示用；不回显 webhook 等敏感明文）。</summary>
        string RecipientSummary { get; }

        Task SendAsync(DTOs.ExternalMessage message, CancellationToken cancellationToken);
    }

    public interface IExternalNotificationQueue
    {
        bool HasEnabledChannels { get; }

        /// <summary>
        /// 非阻塞入队。返回 false = 无启用渠道短路或主队列满载丢弃；
        /// 重试路径（SourceLogId 非空）调用方据此回写失败行，避免行永久停留 Retrying。
        /// </summary>
        bool Enqueue(DTOs.ExternalMessage message);

        /// <summary>指定渠道当前是否启用（重试前置校验用）。senderName 按 Sender.Name 大小写不敏感比较。</summary>
        bool IsChannelEnabled(string senderName);
    }
}
