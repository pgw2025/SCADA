namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>
    /// 设备运行时管理器接口。
    /// <para>
    /// 供运行时层（<c>IRuntimeDeviceManager</c>）向基础设施层触发"设备配置变更 → 运行时刷新"的通知，
    /// 是 MQTT/驱动等基础设施模块在收到配置变化后，反向驱动设备运行时对象重建/刷新的回调契约。
    /// </para>
    /// </summary>
    public interface IDeviceRuntimeManager
    {
        /// <summary>
        /// 刷新指定设备的运行时配置（设备或其变量发生变化时调用）。
        /// 触发 Runtime 层重新加载该设备的运行参数（连接配置、变量列表、映射关系等）。
        /// </summary>
        /// <param name="deviceId">需要刷新的设备 ID</param>
        Task RefreshDevice(int deviceId);

        /// <summary>
        /// 重载全部设备的运行时配置（如批量配置变更 / 系统初始化时调用）。
        /// </summary>
        Task ReloadAll();
    }
}
