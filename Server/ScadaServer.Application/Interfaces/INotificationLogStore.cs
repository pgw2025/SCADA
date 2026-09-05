using System.Collections.Generic;
using System.Threading.Tasks;
using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>投递记录写入条目（应用层 DTO，避免 Recorder 依赖 Domain）。</summary>
    /// <param name="Channel">渠道标识：dingTalk | email | webPush</param>
    /// <param name="EventType">alarmTriggered | alarmRecovered | deviceStatus | systemAlarm | systemError | scriptExecution | test</param>
    /// <param name="Title">消息标题（渲染后）</param>
    /// <param name="Recipient">收件方摘要</param>
    /// <param name="Status">Success | Failed</param>
    /// <param name="LatencyMs">投递耗时（毫秒，含重试退避总耗时）</param>
    /// <param name="Error">失败原因</param>
    /// <param name="PayloadPreview">正文预览（截断）</param>
    /// <param name="PayloadJson">完整 ExternalMessage JSON（重试还原用；测试发送为 null）</param>
    /// <param name="SourceLogId">非空时更新原行（重试回写），否则新增行</param>
    public record NotificationLogEntry(
        string Channel, string EventType, string Title, string Recipient,
        string Status, long LatencyMs, string? Error,
        string? PayloadPreview, string? PayloadJson, long? SourceLogId);

    /// <summary>
    /// 投递记录写入器（发送管线单例侧用）。
    /// <para>
    /// 实现必须 fire-and-forget：Record 只入内部有界队列立即返回，全量 try/catch，
    /// 记录失败仅写 ILogger——绝不阻塞/抛出到发送循环（采集路径安全红线）。
    /// </para>
    /// </summary>
    public interface INotificationLogRecorder
    {
        /// <summary>记录/更新一次投递终态。entry.SourceLogId 非空时更新原行（重试回写），否则新增行。</summary>
        void Record(NotificationLogEntry entry);

        /// <summary>丢弃仍在内部队列、尚未落库的待写记录（配合 ClearAsync 避免清空后残留）。</summary>
        void DiscardPending();
    }

    /// <summary>投递记录查询/清空/重试（控制器侧用）。</summary>
    public interface INotificationLogService
    {
        /// <summary>查询最新 limit 条记录（Id 倒序，默认 500）。</summary>
        Task<IReadOnlyList<NotificationLogDto>> GetRecentAsync(int limit = 500);

        /// <summary>清空全部投递记录（同时丢弃 Recorder 待写批次）。</summary>
        Task ClearAsync();

        /// <summary>
        /// 重试一条失败记录：还原 PayloadJson 为 ExternalMessage，定向（TargetChannel）入队复用管线，
        /// 行置 Retrying，管线完成后由 Recorder 回写终态。仅 Status=Failed 且 PayloadJson 非空的行可重试。
        /// </summary>
        Task<NotificationLogDto> RetryAsync(long id);
    }
}
