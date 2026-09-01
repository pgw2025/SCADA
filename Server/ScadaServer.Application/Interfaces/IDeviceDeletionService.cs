namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 设备删除服务：集中处理设备删除时的依赖检查与级联清理。
    /// 用于收敛 <see cref="DeviceAppService"/> 的构造函数，将仅删除阶段用到的
    /// 传感器、变量触发器、对外接口等依赖从设备增改查服务中剥离出来。
    /// </summary>
    public interface IDeviceDeletionService
    {
        /// <summary>
        /// 删除设备及其依赖（传感器、变量触发器、对外接口等）并做级联清理。
        /// 若设备存在无法删除的依赖关系，由实现抛业务异常。
        /// </summary>
        /// <param name="deviceId">要删除的设备 ID</param>
        Task DeleteAsync(int deviceId);
    }
}