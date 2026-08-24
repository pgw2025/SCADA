namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 设备类型枚举
    /// </summary>
    public enum DeviceType
    {
        /// <summary>
        /// 西门子 S7 协议
        /// </summary>
        S7 = 1,

        /// <summary>
        /// Modbus TCP 协议
        /// </summary>
        ModbusTcp = 2,

        /// <summary>
        /// OPC UA 协议
        /// </summary>
        OpcUa = 3,

        /// <summary>
        /// MQTT 协议
        /// </summary>
        Mqtt = 4,

        /// <summary>
        /// 虚拟设备（用于测试）
        /// </summary>
        Virtual = 5,

        /// <summary>
        /// BACnet 协议
        /// </summary>
        BACnet = 6,

        /// <summary>
        /// DNP3 协议
        /// </summary>
        DNP3 = 7
    }

    /// <summary>
    /// DeviceType 扩展方法。
    /// </summary>
    public static class DeviceTypeExtensions
    {
        /// <summary>
        /// 当前已实现（拥有可用驱动）的设备类型集合。
        /// 与 ProtocolDriverFactory.CreateDriver 的已实现分支保持一致：
        /// 仅 S7 / OpcUa / Virtual 具备可用驱动，其余类型在运行时初始化阶段会抛出 NotSupportedException。
        /// </summary>
        private static readonly HashSet<DeviceType> ImplementedTypes = new()
        {
            DeviceType.S7,
            DeviceType.OpcUa,
            DeviceType.Virtual
        };

        /// <summary>
        /// 判断该设备类型是否已实现可用驱动。
        /// </summary>
        public static bool IsDriverImplemented(this DeviceType type) => ImplementedTypes.Contains(type);
    }

    /// <summary>
    /// 按 <c>Protocol.DriverKey</c>（字符串）判断对应驱动是否已实现。
    /// 与 <see cref="DeviceTypeExtensions.IsDriverImplemented"/> 及
    /// <c>ProtocolDriverFactory.CreateDriver(string)</c> 的已实现分支保持一致：
    /// 仅 S7 / OPC UA / Virtual 具备可用驱动，其余驱动键在运行时创建阶段会抛出 NotSupportedException。
    /// 协议实体全面接管后，此判断是创建设备前的统一前置校验入口。
    /// </summary>
    public static class ProtocolDriverSupport
    {
        /// <summary>
        /// 已实现可用驱动的协议驱动键集合（大小写不敏感，与驱动工厂匹配规则一致）。
        /// 同时容纳"纯驱动键"（S7）与"驱动类名"（S7Driver）两类写法。
        /// </summary>
        private static readonly HashSet<string> ImplementedDriverKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "S7", "S7DRIVER",
            "OPCUA", "OPCUADRIVER",
            "VIRTUAL", "VIRTUALDRIVER"
        };

        /// <summary>
        /// 判断指定驱动键是否已实现可用驱动。
        /// </summary>
        public static bool IsDriverImplemented(string? driverKey)
            => !string.IsNullOrWhiteSpace(driverKey) && ImplementedDriverKeys.Contains(driverKey.Trim());
    }
}
