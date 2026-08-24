namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 设备删除服务：集中处理设备删除时的依赖检查与级联清理。
    /// 用于收敛 <see cref="DeviceAppService"/> 的构造函数，将仅删除阶段用到的
    /// 传感器、变量触发器、对外接口等依赖从设备增改查服务中剥离出来。
    /// </summary>
    public interface IDeviceDeletionService
    {
        Task DeleteAsync(int deviceId);
    }
}