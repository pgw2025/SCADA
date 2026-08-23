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
}
