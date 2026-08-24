using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>
    /// 协议驱动工厂接口。
    /// </summary>
    /// <remarks>
    /// 第九阶段起，工厂以 <see cref="CreateDriver(string)"/> 为主入口：按
    /// <c>Protocol.DriverKey</c>（如 "S7"、"OPCUA"、"VIRTUAL"）创建驱动，
    /// 实现"协议（Protocol）与驱动（Driver）解耦"。协议与驱动的绑定关系
    /// 完全由数据库中的 Protocol.DriverKey 决定，运行时不再感知具体驱动类型。
    /// </remarks>
    public interface IProtocolDriverFactory
    {
        /// <summary>
        /// 根据驱动键（DriverKey）创建驱动实例。
        /// <para>驱动键来自 <c>Protocol.DriverKey</c>，大小写不敏感；匹配既支持驱动类名（如 "S7Driver"）也支持协议键（如 "S7"）。</para>
        /// </summary>
        /// <param name="driverKey">驱动键（如 "S7"、"OPCUA"、"VIRTUAL"）</param>
        /// <returns>协议驱动实例</returns>
        IProtocolDriver CreateDriver(string driverKey);

        /// <summary>
        /// 根据设备类型创建驱动实例。
        /// <para>过渡兼容入口：在 DataModel 尚未关联 Protocol（ProtocolId 为空）时，
        /// 运行时回退到 <c>DataModel.Type</c>（DeviceType）派发驱动。协议实体完全接管后将移除。</para>
        /// </summary>
        /// <param name="deviceType">设备类型（过渡字段）</param>
        /// <returns>协议驱动实例</returns>
        IProtocolDriver CreateDriver(DeviceType deviceType);
    }

    /// <summary>
    /// 协议驱动工厂实现。根据 <c>Protocol.DriverKey</c> 创建 S7 / OPC UA / 虚拟驱动实例。
    /// </summary>
    /// <remarks>
    /// 驱动注册表：
    /// <list type="bullet">
    /// <item>"S7" / "S7Driver" → <see cref="S7Driver"/></item>
    /// <item>"OPCUA" / "OpcUaDriver" → <see cref="OpcUaDriver"/></item>
    /// <item>"VIRTUAL" / "VirtualDriver" → <see cref="VirtualDriver"/></item>
    /// <item>"MODBUSTCP" / "ModbusTcpDriver" → 尚未实现（抛 NotSupportedException）</item>
    /// <item>"MQTT" / "MqttDriver" → 尚未实现（抛 NotSupportedException）</item>
    /// </list>
    /// 新增驱动时，只需在数据库中登记一条 Protocol（DriverKey 指向新驱动类名）并在此注册分支，
    /// 运行时与前端无需改动即可派发。
    /// </remarks>
    public class ProtocolDriverFactory : IProtocolDriverFactory
    {
        /// <inheritdoc/>
        public IProtocolDriver CreateDriver(string driverKey)
        {
            return driverKey?.Trim().ToUpperInvariant() switch
            {
                "S7DRIVER" or "S7" => new S7Driver(),
                "OPCUADRIVER" or "OPCUA" => new OpcUaDriver(),
                "VIRTUALDRIVER" or "VIRTUAL" => new VirtualDriver(),
                "MODBUSTCPDRIVER" or "MODBUSTCP" => throw new NotSupportedException($"驱动 {driverKey} 尚未实现（ModbusTcp 驱动待开发）"),
                "MQTTDRIVER" or "MQTT" => throw new NotSupportedException($"驱动 {driverKey} 尚未实现（MQTT 驱动待开发）"),
                _ => throw new NotSupportedException($"不支持的驱动键: {driverKey}")
            };
        }

        /// <inheritdoc/>
        public IProtocolDriver CreateDriver(DeviceType deviceType)
        {
            // 过渡兼容：由 DeviceType 映射到内部 DriverKey 后复用统一派发逻辑
            return deviceType switch
            {
                DeviceType.S7 => CreateDriver("S7"),
                DeviceType.OpcUa => CreateDriver("OPCUA"),
                DeviceType.Virtual => CreateDriver("VIRTUAL"),
                DeviceType.ModbusTcp => CreateDriver("MODBUSTCP"),
                DeviceType.Mqtt => CreateDriver("MQTT"),
                _ => throw new NotSupportedException($"不支持的设备类型: {deviceType}")
            };
        }
    }
}
