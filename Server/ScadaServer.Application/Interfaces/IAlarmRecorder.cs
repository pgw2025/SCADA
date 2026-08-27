using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 报警记录器接口。
    /// <para>
    /// 供运行时报警检测层调用，将报警事件（触发/恢复）异步入队，由后台服务（AlarmRecorder）
    /// 批量落库，避免在采集循环内同步写数据库阻塞采集。与 <see cref="IHistoryRecorder"/> 模式一致。
    /// </para>
    /// </summary>
    public interface IAlarmRecorder
    {
        /// <summary>
        /// 记录一个报警事件（触发或恢复），非阻塞入队；队列满时丢弃并告警计数。
        /// </summary>
        void Record(AlarmEvent evt);

        /// <summary>
        /// 标记不再有新事件（关闭通道，触发后台排空剩余数据）。
        /// </summary>
        void Complete();
    }
}