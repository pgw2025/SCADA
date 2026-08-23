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
}
