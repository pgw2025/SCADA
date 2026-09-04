using ScadaServer.Domain.Entities;

namespace ScadaServer.Domain.Interfaces
{
    /// <summary>
    /// 连接运行时只读视图（"RuntimeConnection"）。
    /// 连接级单例改造的领域抽象：协议驱动连接时只依赖本接口，不再感知设备。
    /// 多设备可共享同一物理连接；连接参数真相源为 <see cref="DeviceConnection"/>。
    /// <para>
    /// 本接口定义在 Domain 层（而非 Runtime 层），保持依赖方向单向：
    /// Domain ← Infrastructure(驱动/工厂) ← Runtime(运行时实现本接口)，
    /// 避免 Infrastructure 与 Runtime 形成循环引用（与 <see cref="IRuntimeVariable"/> 同构）。
    /// </para>
    /// </summary>
    public interface IRuntimeConnection
    {
        /// <summary>连接配置 ID（DeviceConnection.Id）。</summary>
        int ConnectionId { get; }

        /// <summary>
        /// 连接名称（DeviceConnection.Name，日志定位用）。
        /// 注：DeviceConnection 实体无 Key 列，接口以 Name 承担业务定位语义。
        /// </summary>
        string Key { get; }

        /// <summary>连接配置 JSON（DeviceConnection.ConfigJson，单一真相源）。</summary>
        string ConfigJson { get; }
    }
}