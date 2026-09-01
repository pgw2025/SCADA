namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 组态工程页面 DTO（定义画布尺寸、背景、图层及运行端适配方式）。
    /// </summary>
    public class ScadaPageDto
    {
        /// <summary>页面ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>所属组态工程ID</summary>
        public int ProjectId { get; set; }

        /// <summary>页面名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>是否为主页（组态运行默认打开页）</summary>
        public bool IsHome { get; set; }

        /// <summary>
        /// 画面归属端：Desktop / Mobile。默认 Desktop。
        /// </summary>
        public string Platform { get; set; } = "Desktop";

        /// <summary>
        /// 画布宽度（像素）
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 画布高度（像素）
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 画布背景配置 JSON（纯色/渐变/图片）。NULL=未配置（默认白底）。
        /// </summary>
        public string? BackgroundJson { get; set; }

        /// <summary>
        /// 运行端自适应屏幕模式：FitScaleUp（等比缩放-允许放大）/ Stretch（拉伸填满）。
        /// NULL/空=未配置（回退兼容行为：等比缩小不放大）。
        /// </summary>
        public string? AdaptMode { get; set; }

        /// <summary>
        /// 页面图层配置 JSON 数组。NULL=未配置（前端回退默认单图层）。
        /// </summary>
        public string? LayersJson { get; set; }
    }
}
