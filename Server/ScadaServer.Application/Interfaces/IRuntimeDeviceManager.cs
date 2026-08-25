using System.Threading.Tasks;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 运行期设备管理接口：支持在 SCADA 运行时不重启的情况下，动态注册/注销/重载单台设备。
    /// </summary>
    /// <remarks>
    /// 由设备相关应用服务在事务提交后调用，使界面"新建/启用/禁用/配置变更"的设备无需重启即可进入运行时。
    /// 方法内部对驱动连接等运行期异常做吞并处理并记录日志，不会向调用方抛异常（避免业务写操作失败被误判）。
    /// </remarks>
    public interface IRuntimeDeviceManager
    {
        /// <summary>
        /// 按设备ID从数据库加载完整对象图并注册到运行时（幂等：若已注册会先注销重建）。
        /// 设备未启用或加载失败时静默跳过并记录日志。
        /// </summary>
        Task RegisterDeviceAsync(int deviceId);

        /// <summary>
        /// 从运行时注销并释放设备（停止 Worker、断开驱动连接、推送 Offline）。设备不在运行时则无操作。
        /// </summary>
        Task RemoveDeviceAsync(int deviceId);

        /// <summary>
        /// 注销后重新注册同一设备（用于配置/模型/变量集合变更后热加载）。等价于 Remove + Register。
        /// </summary>
        Task ReloadDeviceAsync(int deviceId);
    }
}