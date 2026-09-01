using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 资产（资产/设备/模型/区域）仓储：提供配置对象的批量加载与详情查询。
    /// </summary>
    public interface IAssetRepository
    {
        /// <summary>查询全部区域。</summary>
        Task<List<Area>> GetAreasAsync();

        /// <summary>查询全部数据模型（含其模型变量）。</summary>
        Task<List<DataModel>> GetModelsWithVariablesAsync();

        /// <summary>按ID查询设备详情（含变量等关联）。</summary>
        Task<Device> GetDeviceDetailAsync(int id);

        /// <summary>查询全部设备。</summary>
        Task<List<Device>> GetDevicesAsync();
    }

    /// <summary>
    /// 组态（HMI）仓储：提供工程/页面/组件的整树加载与组件保存。
    /// </summary>
    public interface IHmiRepository
    {
        /// <summary>按ID查询工程完整对象图（含页面与组件）。</summary>
        Task<ScadaProject> GetProjectFullAsync(int id);

        /// <summary>按ID查询页面（含其组件）。</summary>
        Task<ScadaPage> GetPageWithComponentsAsync(int id);

        /// <summary>保存指定页面的组件集合（整体替换）。</summary>
        Task SavePageComponentsAsync(int pageId, List<HmiComponent> components);
    }

    /// <summary>
    /// 自动化仓储：提供联动规则/数据转换等自动化配置的查询。
    /// </summary>
    public interface IAutomationRepository
    {
        /// <summary>查询全部启用的数据转换规则。</summary>
        Task<List<DataConversion>> GetActiveConversionsAsync();
    }
}

