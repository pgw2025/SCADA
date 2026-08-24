namespace ScadaServer.Domain.Interfaces
{
    /// <summary>
    /// 设备运行时只读视图（即"RuntimeDevice"的领域抽象）。
    /// <para>
    /// 目的：让协议驱动层在连接设备时只依赖本接口（获得设备标识与连接配置），
    /// 而不感知具体运行时类型与 <c>Device</c> / <c>DataModel</c> 等实体，
    /// 从而保证 <b>驱动不允许知道 DataModel / ModelVariable</b>。
    /// </para>
    /// <para>
    /// 本接口定义在 Domain 层，保持依赖方向单向：Domain ← Infrastructure(驱动) ← Runtime(实现)。
    /// </para>
    /// </summary>
    public interface IRuntimeDevice
    {
        /// <summary>设备 ID。</summary>
        int Id { get; }

        /// <summary>设备业务键（Device.Key）。</summary>
        string Key { get; }

        /// <summary>
        /// 设备连接配置（JSON 字符串，来自 DeviceConfig.JsonConfig）。
        /// 驱动在 ConnectAsync 中反序列化为自己的协议配置（如 S7 的 IP/Rack/Slot、OPC UA 的 EndpointUrl）。
        /// </summary>
        string ConfigJson { get; }

        /// <summary>该设备下所有变量运行时（只读视图）。</summary>
        IEnumerable<IRuntimeVariable> Variables { get; }
    }
}
