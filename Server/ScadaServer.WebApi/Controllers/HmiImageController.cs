using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 组态图片图库接口：图片图元与页面背景共用的上传/列表/读取/删除。
    /// 存储为服务器目录文件（无数据库表），引用 URL 存于 HmiComponent.PropsJson / ScadaPage.BackgroundJson。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HmiImageController : ControllerBase
    {
        private readonly IHmiImageAppService _appService;
        private readonly ILogger<HmiImageController> _logger;

        public HmiImageController(IHmiImageAppService appService, ILogger<HmiImageController> logger)
        {
            _appService = appService;
            _logger = logger;
        }

        /// <summary>
        /// 上传图片（multipart，字段名 file）。返回 DTO 含访问 URL（相对路径，前端经 Vite 代理转发）。
        /// </summary>
        [HttpPost("upload")]
        [Authorize(Policy = "RequireAdmin")]
        [RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "请选择要上传的图片文件" });

            try
            {
                await using var stream = file.OpenReadStream();
                var dto = await _appService.UploadAsync(stream, file.FileName, file.Length);
                return Ok(dto);
            }
            catch (ArgumentException ex)
            {
                // 大小超限 / 扩展名白名单拒绝：参数级业务错误，返回 400 带具体文案
                _logger.LogWarning(ex, "图片上传参数错误：{FileName}", file.FileName);
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>图库列表（登录可见）：按上传时间倒序。</summary>
        [HttpGet("list")]
        public async Task<IActionResult> List() => Ok(await _appService.GetListAsync());

        /// <summary>
        /// 读取图片文件流。必须 [AllowAnonymous]：
        /// 1) 全局 FallbackPolicy 默认要求认证（Authentication.Extensions.cs）；
        /// 2) <img> 标签发起的图片请求不携带 JWT header，要求认证必然 401。
        /// 文件名 GUID 化不可枚举，匿名读取安全性与登录接口同级。
        /// 文件内容不变，允许浏览器缓存一天。
        /// </summary>
        [HttpGet("file/{fileName}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetFile(string fileName)
        {
            var opened = await _appService.OpenAsync(fileName);
            if (opened == null) return NotFound();

            Response.Headers["Cache-Control"] = "public, max-age=86400";
            var (stream, contentType) = opened.Value;
            return File(stream, contentType);
        }

        /// <summary>删除图片（引用该图的组件/背景将裂图，前端删除前会提示）。</summary>
        [HttpDelete("{fileName}")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Delete(string fileName)
        {
            var deleted = await _appService.DeleteAsync(fileName);
            return deleted ? NoContent() : NotFound();
        }
    }
}
