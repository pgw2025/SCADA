namespace ScadaServer.Application.DTOs
{
    public class ScadaPageDto
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string Name { get; set; } = string.Empty;
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
    }
}
