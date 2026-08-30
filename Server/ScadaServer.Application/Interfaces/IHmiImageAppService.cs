using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 组态图片图库应用服务：服务器目录存储（无数据库表），供图元/页面背景引用。
    /// </summary>
    public interface IHmiImageAppService
    {
        /// <summary>
        /// 保存上传图片。存储名 = 32位GUID_清洗后原名.扩展名（防重名/防路径穿越）。
        /// </summary>
        /// <param name="stream">文件内容流</param>
        /// <param name="originalFileName">用户上传时的原始文件名</param>
        /// <param name="length">字节长度（提前校验大小上限）</param>
        Task<HmiImageDto> UploadAsync(Stream stream, string originalFileName, long length);

        /// <summary>图库列表（按上传时间倒序）。目录不存在返回空列表。</summary>
        Task<List<HmiImageDto>> GetListAsync();

        /// <summary>
        /// 打开图片文件流。不存在或文件名非法返回 null；contentType 由扩展名映射。
        /// 返回的 Stream 由调用方（Controller File()）负责释放。
        /// </summary>
        Task<(Stream stream, string contentType)?> OpenAsync(string fileName);

        /// <summary>删除图片。文件名非法或不存在返回 false。</summary>
        Task<bool> DeleteAsync(string fileName);
    }
}
