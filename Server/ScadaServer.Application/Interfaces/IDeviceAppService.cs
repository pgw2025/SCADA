using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 设备应用服务：管理设备的增删改查及向设备运行时变量下发写入指令。设备删除的级联清理见 <see cref="IDeviceDeletionService"/>。
    /// </summary>
    public interface IDeviceAppService
    {
        /// <summary>按ID查询单个设备；不存在返回 null。</summary>
        Task<DeviceDto?> GetByIdAsync(int id);

        /// <summary>
        /// 获取设备列表。
        /// <paramref name="includeVariables"/> 为 false 时跳过各设备变量明细的加载
        /// （每台设备可省 2 次 DB 查询），适用于仅需要设备状态/概要的轻量轮询场景。
        /// 注意：前端共享 devices store 的多个视图依赖变量的 key 集合（拓扑/变量下拉/映射），
        /// 全局设备轮询应保持 true。
        /// </summary>
        /// <param name="includeVariables">是否聚合各设备变量明细</param>
        Task<List<DeviceDto>> GetListAsync(bool includeVariables = true);

        /// <summary>新增一个设备，返回创建后的 DTO。</summary>
        Task<DeviceDto> CreateAsync(CreateDeviceDto dto);

        /// <summary>更新一个设备，返回更新后的 DTO。</summary>
        Task<DeviceDto> UpdateAsync(DeviceDto dto);

        /// <summary>删除一个设备。</summary>
        Task DeleteAsync(int id);

        /// <summary>
        /// 向设备运行时变量写入值（下发强制/控制命令）。
        /// 运行时校验（变量存在/启用/非只读/设备在线等）不通过或物理写入失败时抛 <see cref="ScadaServer.Domain.Exceptions.BusinessException"/>，
        /// 由全局异常处理返回 { success=false, message } 供前端展示。
        /// </summary>
        Task WriteVariableAsync(int deviceId, string variableKey, object value);
    }
}

