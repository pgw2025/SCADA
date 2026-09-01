using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 传感器应用服务：管理传感器的增删改查。
    /// 传感器关联设备与变量键（VariableKey），记录名称、单位及最后采集值/时间。
    /// </summary>
    public interface ISensorAppService
    {
        /// <summary>按ID查询单个传感器；不存在返回 null。</summary>
        Task<SensorDto?> GetByIdAsync(int id);

        /// <summary>查询全部传感器。</summary>
        Task<List<SensorDto>> GetListAsync();

        /// <summary>新增一个传感器。</summary>
        Task CreateAsync(SensorDto dto);

        /// <summary>更新一个传感器。</summary>
        Task UpdateAsync(SensorDto dto);

        /// <summary>删除一个传感器。</summary>
        Task DeleteAsync(int id);
    }
}

