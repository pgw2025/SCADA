using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 设备-数据模型绑定请求基类（阶段 5：/api/devices/{deviceId}/data-models 子资源请求体）。
    /// </summary>
    public class DeviceDataModelRequest
    {
        /// <summary>目标数据模型 ID；必填，范围需大于 0。</summary>
        [Range(1, int.MaxValue, ErrorMessage = "请选择数据模型")]
        public int DataModelId { get; set; }
    }

    /// <summary>
    /// 绑定数据模型请求（阶段 5）。
    /// <para><see cref="DeviceDataModelRequest.DataModelId"/> 指定要绑定的模型；
    /// <see cref="IsPrimary"/> 为 true 时在绑定同时将其设为主模型（事务内降级旧主并同步 Device.ModelId）。</para>
    /// </summary>
    public class BindDeviceDataModelDto : DeviceDataModelRequest
    {
        /// <summary>是否同时设为主模型（默认 false；设为主时事务内降级旧主模型并同步 <c>Device.ModelId</c>）。</summary>
        public bool IsPrimary { get; set; }
    }

    /// <summary>
    /// 设备-数据模型绑定 DTO（设备详情 <c>models</c> 列表项 / 绑定列表项）。
    /// <para>
    /// 含绑定行自身字段（Id/Version 快照/IsPrimary/IsEnabled）与所绑定模型的摘要
    /// （Code/Name，来自 DataModel）；<see cref="VariableCount"/> 仅在绑定列表接口
    /// （GetByDeviceAsync）中计算填充，设备详情列表为 0 以省查询开销。
    /// </para>
    /// </summary>
    public class DeviceModelBindingDto
    {
        /// <summary>绑定行 ID（主键，管理用）。</summary>
        public int Id { get; set; }

        /// <summary>绑定行的设备 ID。</summary>
        public int DeviceId { get; set; }

        /// <summary>所绑定数据模型 ID（与 <c>DataModel.Id</c> 一致）。</summary>
        public int DataModelId { get; set; }

        /// <summary>模型编码（只读，来自 DataModel.Code）。</summary>
        public string? Code { get; set; }

        /// <summary>模型名称（只读，来自 DataModel.Name）。</summary>
        public string? Name { get; set; }

        /// <summary>绑定版本快照（取绑定时刻模型版本）。</summary>
        public string Version { get; set; } = "1.0";

        /// <summary>是否主模型（主模型行与 <c>Device.ModelId</c> 严格一致）。</summary>
        public bool IsPrimary { get; set; }

        /// <summary>绑定是否启用（MVP 预留，运行时暂不按此过滤）。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>该模型的模型变量数（仅供绑定列表展示，详情/设备列表为 0）。</summary>
        public int VariableCount { get; set; }

        /// <summary>创建时间（UTC 存储）。</summary>
        public DateTime CreatedAt { get; set; }
    }
}
