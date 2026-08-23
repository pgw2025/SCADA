using ScadaServer.Domain.Entities;

namespace ScadaServer.Domain.Interfaces
{
    /// <summary>
    /// 协议驱动接口，定义与物理设备通信的标准方法
    /// </summary>
    /// <remarks>
    /// 各协议驱动（S7、ModbusTcp、OpcUa等）需实现此接口。
    /// 支持连接管理、数据读写、订阅等功能。
    /// </remarks>
    public interface IProtocolDriver : IAsyncDisposable
    {
        /// <summary>
        /// 连接到设备
        /// </summary>
        /// <param name="device">设备实体</param>
        /// <param name="configJson">设备配置（JSON格式）</param>
        /// <returns>连接是否成功</returns>
        Task<bool> ConnectAsync(Device device, string configJson);

        /// <summary>
        /// 读取单个变量值
        /// </summary>
        /// <param name="variable">变量定义</param>
        /// <returns>变量值</returns>
        Task<object> ReadAsync(ModelVariable variable);

        /// <summary>
        /// 批量读取多个变量值
        /// </summary>
        /// <param name="variables">变量列表</param>
        /// <returns>变量键值对字典</returns>
        Task<IDictionary<string, object>> ReadBatchAsync(IEnumerable<ModelVariable> variables);

        /// <summary>
        /// 订阅变量值变化
        /// </summary>
        /// <param name="variables">要订阅的变量列表</param>
        /// <param name="onValueChanged">值变化回调函数</param>
        Task SubscribeAsync(IEnumerable<ModelVariable> variables, Action<string, object> onValueChanged);

        /// <summary>
        /// 取消订阅变量
        /// </summary>
        /// <param name="variables">要取消订阅的变量列表</param>
        Task UnsubscribeAsync(IEnumerable<ModelVariable> variables);

        /// <summary>
        /// 断开与设备的连接
        /// </summary>
        Task DisconnectAsync();
    }
}
