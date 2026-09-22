namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 批量启用/停用设备采集的请求体。
    /// 目标定位二选一：优先 <see cref="DeviceIds"/>（显式列表），否则按 <see cref="AreaId"/>（含子树）解析。
    /// </summary>
    public class BatchSetEnabledRequest
    {
        /// <summary>按区域定位：服务端自行解析该区域（含子孙）下所有设备。</summary>
        public int? AreaId { get; set; }

        /// <summary>是否包含子孙区域（仅 <see cref="AreaId"/> 路径生效）。</summary>
        public bool IncludeSubAreas { get; set; } = true;

        /// <summary>按显式设备 ID 列表定位（复用表格跨区域多选）；与 AreaId 同时提供时以此为准。</summary>
        public List<int>? DeviceIds { get; set; }

        /// <summary>true=启用采集，false=停用采集。</summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 启用时预检不通过（地址未配置）的设备是否跳过（记为 Skipped）。
        /// false 时仍尝试执行，失败项记为 Failed。仅对 <see cref="Enabled"/>=true 有意义。
        /// </summary>
        public bool SkipInvalid { get; set; } = true;
    }
}
