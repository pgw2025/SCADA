namespace ScadaServer.Application.Options
{
    /// <summary>
    /// 组态图片图库存储选项（appsettings.json "HmiImage" 节）。
    /// </summary>
    public class HmiImageOptions
    {
        public const string SectionName = "HmiImage";

        /// <summary>存储目录（相对 ContentRoot 或绝对路径），首次上传自动创建</summary>
        public string StoragePath { get; set; } = "uploads/hmi-images";

        /// <summary>单文件大小上限（MB）</summary>
        public int MaxFileSizeMB { get; set; } = 10;

        /// <summary>允许的图片扩展名白名单（小写）</summary>
        public List<string> AllowedExtensions { get; set; } = new() { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg" };
    }
}
