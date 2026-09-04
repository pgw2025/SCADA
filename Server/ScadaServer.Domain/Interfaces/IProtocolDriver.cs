using ScadaServer.Domain.Entities;

namespace ScadaServer.Domain.Interfaces
{
    /// <summary>
    /// 协议驱动接口，定义与物理设备通信的标准方法。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 各协议驱动（S7、ModbusTcp、OpcUa等）需实现此接口。
    /// 支持连接管理、数据读写、订阅等功能。
    /// </para>
    /// <para>
    /// <b>解耦约束（第九阶段）</b>：驱动不允许知道 <c>DataModel</c> / <c>DataPoint</c>，
    /// 只接收 <see cref="IRuntimeConnection"/>（RuntimeConnection，连接维度）与
    /// <see cref="IRuntimeVariable"/>（RuntimeVariable，变量维度）。
    /// 地址、位偏移、轮询间隔、缩放等"设备实现"信息一律由运行时从 DataPointMapping 解析后经接口暴露；
    /// 驱动自身不得触碰模型模板实体。
    /// </para>
    /// </remarks>
    public interface IProtocolDriver : IAsyncDisposable
    {
        /// <summary>
        /// 建立到设备所在连接配置的物理连接。
        /// </summary>
        /// <param name="connection">连接运行时（含连接配置 ConfigJson）。驱动不感知设备。</param>
        /// <returns>连接是否成功</returns>
        Task<bool> ConnectAsync(IRuntimeConnection connection);

        /// <summary>
        /// 读取单个变量值
        /// </summary>
        /// <param name="variable">变量运行时（地址来自 DataPointMapping）</param>
        /// <returns>变量值；无可用值 / 设备未连接时返回 null（调用方视作本次读取无效）</returns>
        Task<object?> ReadAsync(IRuntimeVariable variable);

        /// <summary>
        /// 批量读取多个变量值
        /// </summary>
        /// <param name="variables">变量运行时列表</param>
        /// <returns>变量键值对字典</returns>
        Task<IDictionary<string, object>> ReadBatchAsync(IEnumerable<IRuntimeVariable> variables);

        /// <summary>
        /// 探测物理连接是否仍存活。
        /// <para>
        /// 语义：<b>仅用于会话级重连判定</b>，不作为读写前置条件（避免每轮多一次往返）。
        /// 各驱动实现为其"连接状态"的轻量化只读视图：S7 返回 PLC 是否仍连接；
        /// OPC UA 返回会话是否可用；虚拟设备恒 true。不得在本方法内做任何网络 IO。
        /// </para>
        /// </summary>
        /// <returns>连接存活返回 true；未连接/已失效/驱动已释放返回 false。</returns>
        Task<bool> IsAliveAsync();

        /// <summary>
        /// 写入单个变量值到物理设备。
        /// </summary>
        /// <param name="variable">变量运行时（地址来自 DataPointMapping，数据类型来自 DataType）</param>
        /// <param name="value">待写入的原始值（由驱动按 DataType 转换为设备对应类型）</param>
        /// <remarks>
        /// 成功返回；失败（设备未连接、地址非法、类型不匹配、通信错误等）抛异常，由上层捕获并向调用方返回失败原因。
        /// 驱动需按 <see cref="IRuntimeVariable.DataType"/> 将传入值转换为设备期望的具体类型。
        /// </remarks>
        Task WriteAsync(IRuntimeVariable variable, object value);

        /// <summary>
        /// 订阅变量值变化
        /// </summary>
        /// <param name="variables">要订阅的变量运行时列表</param>
        /// <param name="onValueChanged">值变化回调函数</param>
        Task SubscribeAsync(IEnumerable<IRuntimeVariable> variables, Action<string, object> onValueChanged);

        /// <summary>
        /// 取消订阅变量
        /// </summary>
        /// <param name="variables">要取消订阅的变量运行时列表</param>
        Task UnsubscribeAsync(IEnumerable<IRuntimeVariable> variables);

        /// <summary>
        /// 断开与设备的连接
        /// </summary>
        Task DisconnectAsync();
    }
}
