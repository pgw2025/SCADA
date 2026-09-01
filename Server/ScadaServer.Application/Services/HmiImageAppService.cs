using System.Text.RegularExpressions;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 组态图片图库服务实现：文件存服务器目录，元数据（原名/大小/时间）从文件系统派生。
    /// 通过 DI 工厂注入 ContentRootPath 解析存储根目录（Application 层不依赖 ASP.NET Core 类型）。
    /// </summary>
    public class HmiImageAppService : IHmiImageAppService
    {
        /// <summary>存储文件名格式：32位hex GUID + '_' + 清洗后原名（含扩展名）</summary>
        private static readonly Regex StoredNamePattern = new(
            @"^[0-9a-f]{32}_", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>支持的图片扩展名 → Content-Type 映射（不区分大小写）。</summary>
        private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
        };

        /// <summary>图片存储配置（大小上限、允许扩展名、存储相对路径）。</summary>
        private readonly HmiImageOptions _options;
        /// <summary>存储根目录的绝对路径（由内容根目录 + 相对路径解析得到）。</summary>
        private readonly string _rootDir;

        /// <summary>构造函数：基于内容根目录与存储相对路径解析出图片存储根目录。</summary>
        public HmiImageAppService(HmiImageOptions options, string contentRootPath)
        {
            _options = options;
            _rootDir = Path.GetFullPath(Path.Combine(contentRootPath, options.StoragePath));
        }

        /// <summary>上传图片：校验大小与格式后写入存储目录，返回元数据信息。</summary>
        public async Task<HmiImageDto> UploadAsync(Stream stream, string originalFileName, long length)
        {
            if (stream == null || length <= 0)
                throw new ArgumentException("未收到有效文件内容");
            if (length > _options.MaxFileSizeMB * 1024L * 1024L)
                throw new ArgumentException($"文件超过 {_options.MaxFileSizeMB}MB 上限");

            var ext = Path.GetExtension(originalFileName ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !_options.AllowedExtensions.Contains(ext))
                throw new ArgumentException($"不支持的图片格式 '{ext}'（允许：{string.Join("/", _options.AllowedExtensions)}）");

            Directory.CreateDirectory(_rootDir);

            // 原名清洗：剔 Windows/浏览器非法字符与空白，防路径穿越；GUID 前缀防重名
            var safeOriginal = Regex.Replace(originalFileName, @"[\\/:*?""<>|\s]+", "_").Trim();
            var originalNoExt = Path.GetFileNameWithoutExtension(safeOriginal);
            if (string.IsNullOrWhiteSpace(originalNoExt)) originalNoExt = "image";
            var storedName = $"{Guid.NewGuid():N}_{originalNoExt}{ext}";

            var fullPath = Path.Combine(_rootDir, storedName);
            await using (var fs = new FileStream(fullPath, FileMode.CreateNew))
            {
                await stream.CopyToAsync(fs);
            }
            return ToDto(new FileInfo(fullPath));
        }

        /// <summary>列出图库中全部图片（按上传时间倒序）。</summary>
        public Task<List<HmiImageDto>> GetListAsync()
        {
            var result = new List<HmiImageDto>();
            if (Directory.Exists(_rootDir))
            {
                result = Directory.EnumerateFiles(_rootDir)
                    .Select(f => new FileInfo(f))
                    .Where(fi => StoredNamePattern.IsMatch(fi.Name))
                    .Select(ToDto)
                    .OrderByDescending(d => d.UploadedAtUtc)
                    .ToList();
            }
            return Task.FromResult(result);
        }

        /// <summary>打开指定图片，返回文件流与 Content-Type；找不到时返回 null。</summary>
        public Task<(Stream, string)?> OpenAsync(string fileName)
        {
            var resolved = ResolveStoredFile(fileName);
            if (resolved == null)
                return Task.FromResult<(Stream, string)?>(null);

            var contentType = ContentTypes.GetValueOrDefault(
                Path.GetExtension(resolved), "application/octet-stream");
            return Task.FromResult<(Stream, string)?>((File.OpenRead(resolved), contentType));
        }

        /// <summary>删除指定图片；成功返回 true，文件不存在或名称非法时返回 false。</summary>
        public Task<bool> DeleteAsync(string fileName)
        {
            var resolved = ResolveStoredFile(fileName);
            if (resolved == null) return Task.FromResult(false);
            File.Delete(resolved);
            return Task.FromResult(true);
        }

        /// <summary>
        /// 校验存储文件名并解析为根目录内的绝对路径：
        /// 格式必须匹配 GUID 前缀模式（拒绝任意用户构造名），且 GetFullPath 后必须落在根目录内（双重防路径穿越）。
        /// </summary>
        private string? ResolveStoredFile(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)
                || fileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
                || !StoredNamePattern.IsMatch(fileName))
                return null;

            var fullPath = Path.GetFullPath(Path.Combine(_rootDir, fileName));
            if (!fullPath.StartsWith(_rootDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return null;
            return File.Exists(fullPath) ? fullPath : null;
        }

        /// <summary>由文件信息派生 DTO：拼接原始文件名与可访问 URL。</summary>
        private static HmiImageDto ToDto(FileInfo fi)
        {
            var stored = fi.Name;
            // 原名 = 32位GUID + 1个'_' 之后的部分（清洗时已保留扩展名）
            var original = stored.Length > 33 ? stored.Substring(33) : stored;
            return new HmiImageDto
            {
                FileName = stored,
                OriginalName = original,
                SizeBytes = fi.Length,
                UploadedAtUtc = fi.CreationTimeUtc,
                Url = $"/api/HmiImage/file/{Uri.EscapeDataString(stored)}",
            };
        }
    }
}
