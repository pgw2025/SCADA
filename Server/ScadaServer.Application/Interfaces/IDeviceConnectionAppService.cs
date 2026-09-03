using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 设备连接应用服务（阶段 3：连接/控制器管理 API）。
    /// 管理 <c>DeviceConnection</c> 资产：按控制器查询、CRUD。
    /// <para>
    /// 引用语义：连接被设备引用（Device.ConnectionId 指向）后，连接生命周期移交设备管理
    /// （删除设备/改连接参数均走设备接口；阶段 6 起 Connection.ConfigJson 即连接配置唯一真相源，
    /// 不再与 Device.JsonConfig 双写）；故更新/删除被设备引用的连接会被拒绝，
    /// 避免绕过设备接口破坏端点唯一性与共享语义。
    /// </para>
    /// </summary>
    public interface IDeviceConnectionAppService
    {
        /// <summary>按 ID 查询单个连接（含控制器/协议导航）；不存在返回 null。</summary>
        Task<DeviceConnectionDto?> GetByIdAsync(int id);

        /// <summary>查询连接列表；controllerId 非空时仅返回该控制器下的连接。</summary>
        Task<List<DeviceConnectionDto>> GetListAsync(int? controllerId = null);

        /// <summary>新增一个连接，返回创建后的 DTO（含自增 ID）。</summary>
        Task<DeviceConnectionDto> CreateAsync(CreateDeviceConnectionDto dto);

        /// <summary>按 ID 更新指定连接（被设备引用时拒绝），返回更新后的 DTO。</summary>
        Task<DeviceConnectionDto> UpdateAsync(int id, CreateDeviceConnectionDto dto);

        /// <summary>删除指定连接（被设备引用时拒绝），并清理因此产生的无引用独占控制器。</summary>
        Task DeleteAsync(int id);
    }
}
