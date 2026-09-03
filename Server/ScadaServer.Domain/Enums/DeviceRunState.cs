namespace ScadaServer.Domain.Enums;

/// <summary>
/// 设备运行状态（机器状态，与 PLC 通信状态正交）。
/// <para>
/// 语义边界（勿与 DeviceStatus.Fault 混淆）：
/// 本枚举描述"机器现在在干什么"（运行/停止/检修…）；
/// DeviceStatus 描述"与 PLC 的通信是否正常"。
/// PLC 连接正常（Online）但机器停止（Stopped）是合法组合，反之亦然。
/// </para>
/// </summary>
public enum DeviceRunState
{
    /// <summary>未知（默认，未置位）。</summary>
    Unknown = 0,

    /// <summary>停机。</summary>
    Stopped = 1,

    /// <summary>运行中。</summary>
    Running = 2,

    /// <summary>暂停（临时停线，预期恢复）。</summary>
    Paused = 3,

    /// <summary>机器故障（设备自身故障/急停，非通信故障）。</summary>
    Fault = 4,

    /// <summary>检修/维护中（禁止远程启停与写入操作的建议状态）。</summary>
    Maintenance = 5
}
