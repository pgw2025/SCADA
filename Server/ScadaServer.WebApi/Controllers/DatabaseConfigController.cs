using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 数据库配置控制器（统一走 DatabaseConfigs 表，替代原 databases.json 双轨）。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdmin")]
    public class DatabaseConfigController : ControllerBase
    {
        private readonly IDatabaseConfigAppService _appService;
        private readonly IRuntimeDatabaseService _runtimeService;

        public DatabaseConfigController(IDatabaseConfigAppService appService, IRuntimeDatabaseService runtimeService)
        {
            _appService = appService;
            _runtimeService = runtimeService;
        }

        /// <summary>获取全部数据库配置（含备用清单）</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        /// <summary>按 ID 获取数据库配置</summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var dto = await _appService.GetByIdAsync(id);
            return dto != null ? Ok(dto) : NotFound();
        }

        /// <summary>新增数据库配置</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DatabaseConfigDto dto)
        {
            await _appService.CreateAsync(dto);
            return Ok(dto);
        }

        /// <summary>更新数据库配置（密码/令牌掩码回传 = 不改密）</summary>
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] DatabaseConfigDto dto)
        {
            await _appService.UpdateAsync(dto);
            return Ok();
        }

        /// <summary>删除数据库配置</summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return Ok();
        }

        // ===== 主库（MySQL，自举依赖）配置 =====

        /// <summary>获取当前生效的主库（MySQL）连接配置（密码回显为掩码）</summary>
        [HttpGet("main")]
        public async Task<IActionResult> GetMainConfig() => Ok(await _runtimeService.GetMainConfigAsync());

        /// <summary>保存主库（MySQL）连接配置到 override 文件（重启后生效；密码掩码/空 = 不改密）</summary>
        [HttpPut("main")]
        public async Task<IActionResult> SaveMainConfig([FromBody] MainDatabaseConfigDto dto)
        {
            await _runtimeService.SaveMainConfigAsync(dto);
            return Ok();
        }

        // ===== 连接测试（主库 / 历史库通用） =====

        /// <summary>对指定后端类型执行连接测试（不会改变当前生效配置）</summary>
        [HttpPost("test-connection")]
        public async Task<IActionResult> TestConnection([FromBody] TestConnectionRequest request)
        {
            var result = await _runtimeService.TestConnectionAsync(request);
            return Ok(result);
        }
    }
}
