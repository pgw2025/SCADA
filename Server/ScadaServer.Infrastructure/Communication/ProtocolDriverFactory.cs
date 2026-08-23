using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>
    /// 协议驱动工厂接口
    /// </summary>
    public interface IProtocolDriverFactory
    {
        /// <summary>
        /// 根据设备类型创建驱动实例
        /// </summary>
        /// <param name="deviceType">设备类型</param>
        /// <returns>协议驱动实例</returns>
        IProtocolDriver CreateDriver(DeviceType deviceType);

        /// <summary>
        /// 根据驱动名称创建驱动实例
        /// </summary>
        /// <param name="driverName">驱动名称</param>
        /// <returns>协议驱动实例</returns>
        IProtocolDriver CreateDriver(string driverName);
    }

    /// <summary>
    /// 协议驱动工厂实现
    /// </summary>
    public class ProtocolDriverFactory : IProtocolDriverFactory
    {
        /// <inheritdoc/>
        public IProtocolDriver CreateDriver(DeviceType deviceType)
        {
            return deviceType switch
            {
                DeviceType.S7 => new S7Driver(),
                DeviceType.OpcUa => new OpcUaDriver(),
                DeviceType.ModbusTcp => throw new NotSupportedException($"驱动 {deviceType} 尚未实现"),
                DeviceType.Mqtt => throw new NotSupportedException($"驱动 {deviceType} 尚未实现"),
                DeviceType.Virtual => throw new NotSupportedException($"驱动 {deviceType} 尚未实现"),
                _ => throw new NotSupportedException($"不支持的设备类型: {deviceType}")
            };
        }

        /// <inheritdoc/>
        public IProtocolDriver CreateDriver(string driverName)
        {
            return driverName?.ToUpper() switch
            {
                "S7DRIVER" or "S7" => new S7Driver(),
                "OPCUADRIVER" or "OPCUA" => new OpcUaDriver(),
                "MODBUSTCPDRIVER" or "MODBUSTCP" => throw new NotSupportedException($"驱动 {driverName} 尚未实现"),
                "MQTTDRIVER" or "MQTT" => throw new NotSupportedException($"驱动 {driverName} 尚未实现"),
                "VIRTUALDRIVER" or "VIRTUAL" => throw new NotSupportedException($"驱动 {driverName} 尚未实现"),
                _ => throw new NotSupportedException($"不支持的驱动名称: {driverName}")
            };
        }
    }
}
