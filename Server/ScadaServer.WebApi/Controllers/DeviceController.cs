using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 设备控制器，处理设备的CRUD操作
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class DeviceController : ControllerBase
    {
        private readonly IDeviceAppService _deviceAppService;

        /// <summary>
        /// 初始化设备控制器
        /// </summary>
        /// <param name="deviceAppService">设备应用服务</param>
        public DeviceController(IDeviceAppService deviceAppService)
        {
            _deviceAppService = deviceAppService;
        }

        /// <summary>
        /// 获取所有设备
        /// </summary>
        /// <param name="includeVariables">
        /// 是否携带各设备的变量明细（默认 true）。
        /// 轻量轮询场景（仅需状态/概要）可传 false 跳过每台设备 2 次变量相关 DB 查询。
        /// </param>
        /// <returns>设备列表</returns>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool includeVariables = true)
            => Ok(await _deviceAppService.GetListAsync(includeVariables));

        /// <summary>
        /// 根据ID获取设备
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <returns>设备信息</returns>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id) => Ok(await _deviceAppService.GetByIdAsync(id));

        /// <summary>
        /// 向设备运行时变量写入值（下发强制/控制命令）。
        /// 端点受全局 FallbackPolicy 默认认证保护；物理写入结果经 SignalR 广播，各客户端刷新后可见新值。
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <param name="variableKey">变量业务键（ModelVariable.Key）</param>
        /// <param name="dto">写入请求，Value 为待写入原始值</param>
        /// <returns>统一成功响应；校验/写入失败抛 BusinessException 由全局异常处理返回</returns>
        [HttpPost("{id}/variables/{variableKey}/write")]
        public async Task<IActionResult> WriteVariable(int id, string variableKey, [FromBody] WriteVariableRequestDto dto)
        {
            await _deviceAppService.WriteVariableAsync(id, variableKey, dto.Value!);
            return Ok(new { success = true, message = "写入成功" });
        }

        /// <summary>
        /// 创建设备
        /// </summary>
        /// <param name="dto">创建设备DTO</param>
        /// <returns>创建结果</returns>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDeviceDto dto)
        {
            var result = await _deviceAppService.CreateAsync(dto);
            return Ok(result);
        }

        /// <summary>
        /// 更新设备
        /// </summary>
        /// <param name="dto">设备DTO</param>
        /// <returns>更新结果</returns>
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] DeviceDto dto)
        {
            var result = await _deviceAppService.UpdateAsync(dto);
            return Ok(result);
        }

        /// <summary>
        /// 删除设备
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <returns>删除结果</returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await _deviceAppService.DeleteAsync(id);
            return Ok(new { success = true, message = "设备删除成功" });
        }
    }
}

