using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 设备-数据模型绑定应用服务（阶段 5：DeviceDataModels 多对多绑定管理）。
    /// <para>
    /// 职责：设备绑定列表查询、绑定/解绑/切换主模型。主模型（IsPrimary=true）与
    /// <c>Device.ModelId</c> 的双写一致性收敛于本服务与 DeviceAppService（创建设备双写）的事务单点；
    /// 附加（非主）模型绑定仅供管理界面与未来扩展，运行时仍只认主模型。
    /// </para>
    /// </summary>
    public interface IDeviceDataModelAppService
    {
        /// <summary>查询某设备的全部绑定（含模型摘要 Code/Name/Version 与模型变量数）。</summary>
        Task<List<DeviceModelBindingDto>> GetByDeviceAsync(int deviceId);

        /// <summary>
        /// 绑定一个数据模型到设备。校验：设备存在、模型存在且已发布、未重复绑定。
        /// <paramref name="dto.IsPrimary"/> 为 true 时在事务内把旧主模型降级并同步 Device.ModelId（唯一双写点）；
        /// 为 false 时仅新增附加绑定（设备已有主模型的前提下）。
        /// 返回刷新后的绑定列表。
        /// </summary>
        Task<List<DeviceModelBindingDto>> BindAsync(int deviceId, BindDeviceDataModelDto dto);

        /// <summary>
        /// 切换主模型：目标必须是该设备已绑定的模型；事务内降级旧主、提升目标并同步
        /// <c>Device.ModelId</c>（唯一双写点），随后按启用状态热重载设备运行时。
        /// 返回刷新后的绑定列表。
        /// </summary>
        Task<List<DeviceModelBindingDto>> SetPrimaryAsync(int deviceId, DeviceDataModelRequest request);

        /// <summary>
        /// 解绑一个数据模型。主模型不可解绑（须先切换主模型）；
        /// 若该模型下存在被本设备实例化的设备变量（此前曾为主模型残留），拒绝并提示先清理。
        /// 返回刷新后的绑定列表。
        /// </summary>
        Task<List<DeviceModelBindingDto>> UnbindAsync(int deviceId, int dataModelId);
    }
}
