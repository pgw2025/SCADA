namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 组态图片信息（图库列表/上传结果）。无数据库表，元数据从文件系统派生。
    /// </summary>
    public class HmiImageDto
    {
        /// <summary>存储文件名（32位GUID_清洗后原名.扩展名），同时是访问 URL 标识</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>用户上传时的原始文件名（图库显示用）</summary>
        public string OriginalName { get; set; } = string.Empty;

        /// <summary>文件大小（字节）</summary>
        public long SizeBytes { get; set; }

        /// <summary>上传时间（UTC，取文件创建时间）</summary>
        public DateTime UploadedAtUtc { get; set; }

        /// <summary>图片访问相对 URL（/api/HmiImage/file/{fileName}）</summary>
        public string Url { get; set; } = string.Empty;
    }
}
