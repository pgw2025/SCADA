using Microsoft.Extensions.Logging;
using ScadaServer.Domain.Interfaces;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>
    /// 协议驱动工厂实现。根据 <c>Protocol.Key</c> 创建 S7 / OPC UA / 虚拟驱动实例。
    /// </summary>
    /// <remarks>
    /// 驱动注册表：
    /// <list type="bullet">
    /// <item>"S7" → <see cref="S7Driver"/>（不支持订阅）</item>
    /// <item>"OPCUA" → <see cref="OpcUaDriver"/>（支持订阅推送）</item>
    /// <item>"VIRTUAL" → <see cref="VirtualDriver"/>（不支持订阅）</item>
    /// <item>"MODBUSTCP" → 尚未实现（抛 NotSupportedException）</item>
    /// <item>"MQTT" → 尚未实现（抛 NotSupportedException）</item>
    /// </list>
    /// 新增驱动时，只需在数据库中登记一条 Protocol（Key 指向对应的协议键）并在此注册分支，
    /// 运行时与前端无需改动即可派发。
    /// </remarks>
    public class ProtocolDriverFactory : IProtocolDriverFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// 初始化工厂。注入 <see cref="ILoggerFactory"/> 为各驱动创建类别化日志
        /// （如 ILogger&lt;S7Driver&gt;），驱动本身保持按设备独立实例化。
        /// </summary>
        /// <param name="loggerFactory">日志工厂（来自 DI）</param>
        public ProtocolDriverFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        /// <inheritdoc/>
        public IProtocolDriver CreateDriver(string driverKey)
        {
            return driverKey?.Trim().ToUpperInvariant() switch
            {
                "S7DRIVER" or "S7" => new S7Driver(_loggerFactory.CreateLogger<S7Driver>()),
                "OPCUADRIVER" or "OPCUA" => new OpcUaDriver(_loggerFactory.CreateLogger<OpcUaDriver>()),
                "VIRTUALDRIVER" or "VIRTUAL" => new VirtualDriver(),
                "MODBUSTCPDRIVER" or "MODBUSTCP" => throw new NotSupportedException($"驱动 {driverKey} 尚未实现（ModbusTcp 驱动待开发）"),
                "MQTTDRIVER" or "MQTT" => throw new NotSupportedException($"驱动 {driverKey} 尚未实现（MQTT 驱动待开发）"),
                _ => throw new NotSupportedException($"不支持的驱动键: {driverKey}")
            };
        }

        /// <inheritdoc/>
        public bool SupportsSubscription(string driverKey)
        {
            // 与 CreateDriver 的注册表保持同构；未知键返回 false（不抛异常，调用方以此做表单禁用）。
            return driverKey?.Trim().ToUpperInvariant() switch
            {
                "OPCUADRIVER" or "OPCUA" => true,
                _ => false
            };
        }
    }
}
