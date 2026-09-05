using System.Threading.Tasks;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime.Devices;

namespace ScadaServer.Runtime.Processing
{
    /// <summary>
    /// 变量值处理管线：轮询与订阅两个数据来源共用的统一下游处理。
    /// <para>
    /// 核心管线（决策 D2）：工程换算 → 锁内内存更新 + 回声抑制 + IsChanged →
    /// changeBus 变化事件 → 通知入队（有界通道泵）→ 历史落库判定 → 实时快照 → 报警求值。
    /// 与旧 <c>DeviceWorker</c> 内联逻辑严格同序，单变量处理失败不影响其他变量。
    /// </para>
    /// <para>
    /// 单例注册（全部依赖均为单例）。设备注销/停止时须调用 <see cref="StopDevice"/> 停泵，
    /// 防止已卸载设备的通知通道泄漏。
    /// </para>
    /// </summary>
    public interface IVariableValueProcessor
    {
        /// <summary>
        /// 轮询路径入口：原始值驱动，质量由处理器内部判定（null/读失败 → CommunicationError）。
        /// </summary>
        /// <param name="runtime">目标设备运行时。</param>
        /// <param name="vr">变量运行时。</param>
        /// <param name="rawValue">驱动读取的原始值（未做工程换算；错误标记已由调用方映射为 null）。</param>
        /// <param name="now">采集时刻（UTC）。</param>
        Task ApplyPolledAsync(DeviceRuntime runtime, VariableRuntime vr, object? rawValue, DateTime now);

        /// <summary>
        /// 订阅路径入口：值与质量由驱动回调给出（Bad/Uncertain 质量走质量降级分支——值丢弃、
        /// 保留最近有效值并统一置 CommunicationError，仅跃迁时通知一次）。
        /// </summary>
        /// <param name="runtime">目标设备运行时。</param>
        /// <param name="vr">变量运行时。</param>
        /// <param name="value">驱动回调的工程值。</param>
        /// <param name="quality">驱动回调的质量（Good / Bad / Uncertain）。</param>
        /// <param name="now">回调到达时刻（UTC）。</param>
        Task ApplySubscribedAsync(DeviceRuntime runtime, VariableRuntime vr, object? value, VariableQuality quality, DateTime now);

        /// <summary>
        /// 设备注销/运行时停止时停泵：完成该设备通知通道并等待消费任务退出（超时 3s 后放弃）。
        /// </summary>
        void StopDevice(int deviceId);
    }
}
