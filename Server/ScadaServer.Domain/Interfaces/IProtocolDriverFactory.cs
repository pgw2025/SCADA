namespace ScadaServer.Domain.Interfaces
{
    /// <summary>
    /// 协议驱动工厂接口。
    /// <para>
    /// 工厂以 <see cref="CreateDriver(string)"/> 为主入口：按
    /// <c>Protocol.Key</c>（如 "S7"、"OPCUA"、"VIRTUAL"）创建驱动，
    /// 实现"协议（Protocol）与驱动（Driver）解耦"。协议与驱动的绑定关系
    /// 完全由数据库中的 Protocol.Key 决定，运行时不再感知具体驱动类型。
    /// </para>
    /// <para>
    /// 接口位于 Domain（而非 Infrastructure）：Application 层（设备变量保存校验订阅能力）
    /// 与 Runtime 层（连接会话创建驱动）都需要消费该能力，避免 Application 反向依赖 Infrastructure。
    /// 实现 <c>ProtocolDriverFactory</c> 位于 <c>ScadaServer.Infrastructure.Communication</c>。
    /// </para>
    /// </summary>
    public interface IProtocolDriverFactory
    {
        /// <summary>
        /// 根据协议键（Key）创建驱动实例。
        /// <para>协议键来自 <c>Protocol.Key</c>，大小写不敏感；匹配既支持协议键（如 "S7"）也兼容驱动类名写法。</para>
        /// </summary>
        /// <param name="driverKey">驱动键（如 "S7"、"OPCUA"、"VIRTUAL"）</param>
        /// <returns>协议驱动实例</returns>
        IProtocolDriver CreateDriver(string driverKey);

        /// <summary>
        /// 协议驱动是否支持订阅推送（按协议键查询，不创建驱动实例）。
        /// <para>用于前端表单能力禁用与后端保存校验：未知协议键一律返回 false（不抛异常）。</para>
        /// </summary>
        /// <param name="driverKey">驱动键（如 "S7"、"OPCUA"、"VIRTUAL"）</param>
        /// <returns>支持订阅推送返回 true；否则 false。</returns>
        bool SupportsSubscription(string driverKey);
    }
}
