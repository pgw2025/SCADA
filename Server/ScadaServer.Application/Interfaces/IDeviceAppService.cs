using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IDeviceAppService
    {
        Task<DeviceDto?> GetByIdAsync(int id);

        /// <summary>
        /// 获取设备列表。
        /// <paramref name="includeVariables"/> 为 false 时跳过各设备变量明细的加载
        /// （每台设备可省 2 次 DB 查询），适用于仅需要设备状态/概要的轻量轮询场景。
        /// 注意：前端共享 devices store 的多个视图依赖变量的 key 集合（拓扑/变量下拉/映射），
        /// 全局设备轮询应保持 true。
        /// </summary>
        Task<List<DeviceDto>> GetListAsync(bool includeVariables = true);
        Task<DeviceDto> CreateAsync(CreateDeviceDto dto);
        Task<DeviceDto> UpdateAsync(DeviceDto dto);
        Task DeleteAsync(int id);
    }
}

