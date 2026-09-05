using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 外部消息投递记录（消息通知中心 → 投递记录 Tab）。
    /// <para>
    /// 记录钉钉 / SMTP 邮件 / Web Push 三渠道每次真实投递的终态（Success / Failed），
    /// 由 <c>ExternalNotificationService</c> 发送管线在每条消息完成（成功或重试耗尽）后异步写入；
    /// 测试发送由 <c>NotificationConfigService.SendTestAsync</c> 写入（EventType=test，PayloadJson 为空、不可重试）。
    /// </para>
    /// <para>
    /// 失败行支持一键重试：重试消息携带 SourceLogId 回写原行（不新增重复行），
    /// 过渡态 Retrying 仅存在于「重试已受理、管线尚未完成」窗口；服务重启后由
    /// NotificationLogRecorder 启动清扫统一置回 Failed。
    /// </para>
    /// </summary>
    [Table("NotificationLogs")]
    public class NotificationLog
    {
        /// <summary>主键（自增）</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>投递完成时间（UTC，项目时间约定）</summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>渠道标识：dingTalk | email | webPush（与前端 NotificationLogItem.channel 对齐）</summary>
        [MaxLength(16)]
        public string Channel { get; set; } = string.Empty;

        /// <summary>
        /// 事件类型：alarmTriggered | alarmRecovered | deviceStatus | systemAlarm | systemError | scriptExecution | test
        /// （与前端 NotificationLogItem.eventType 联合类型一一对应）
        /// </summary>
        [MaxLength(24)]
        public string EventType { get; set; } = string.Empty;

        /// <summary>消息标题（模板渲染后的最终标题）</summary>
        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        /// <summary>收件方摘要（邮箱列表 / 钉钉群机器人 / Web Push 订阅摘要；不回显 webhook 明文）</summary>
        [MaxLength(255)]
        public string Recipient { get; set; } = string.Empty;

        /// <summary>Success | Failed | Retrying（Retrying 仅为重试受理后、管线完成前的过渡态）</summary>
        [MaxLength(16)]
        public string Status { get; set; } = string.Empty;

        /// <summary>投递耗时（毫秒，含重试退避的总耗时；语义为端到端完成时间）</summary>
        public long LatencyMs { get; set; }

        /// <summary>失败原因（最终一次异常消息 / 限流或队列丢弃说明 / 重启中断说明）</summary>
        [MaxLength(1024)]
        public string? Error { get; set; }

        /// <summary>正文预览（MarkdownText 截断 500 字符，列表展示用）</summary>
        [MaxLength(512)]
        public string? PayloadPreview { get; set; }

        /// <summary>完整 ExternalMessage 序列化 JSON，失败重试时还原重发；测试发送为 null（不可重试）</summary>
        public string? PayloadJson { get; set; }
    }
}
