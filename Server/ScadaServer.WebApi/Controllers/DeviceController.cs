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
        /// <returns>设备列表</returns>
        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _deviceAppService.GetListAsync());

        /// <summary>
        /// 根据ID获取设备
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <returns>设备信息</returns>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id) => Ok(await _deviceAppService.GetByIdAsync(id));

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

        /// <summary>
        /// 更新设备配置（带事务）
        /// </summary>
        /// <remarks>
        /// 业务编排接口：更新配置并记录日志（含事务）
        /// </remarks>
        /// <param name="id">设备ID</param>
        /// <param name="newAddress">新地址</param>
        /// <returns>更新结果</returns>
        [HttpPost("{id}/update-config")]
        public async Task<IActionResult> UpdateConfig(int id, [FromBody] string newAddress)
        {
            await _deviceAppService.UpdateDeviceConfigTxAsync(id, newAddress);
            return Ok(new { Message = "Configuration updated with transaction." });
        }

        /// <summary>
        /// 下发控制指令
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <param name="command">指令内容</param>
        /// <returns>下发结果</returns>
        [HttpPost("{id}/control")]
        public async Task<IActionResult> SendCommand(int id, [FromBody] string command)
        {
            // Logic for sending command via protocol drivers
            return Ok(new { DeviceId = id, Status = "Command Sent", Payload = command });
        }
    }
}

