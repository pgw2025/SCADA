using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataModelController : ControllerBase
    {
        private readonly IDataModelAppService _appService;

        public DataModelController(IDataModelAppService appService)
        {
            _appService = appService;
        }

        /// <summary>
        /// 获取数据模型列表。
        /// </summary>
        /// <param name="includeVariables">是否同时加载模型变量（默认 true）。列表页概览可传 false 以节省查询开销。</param>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool includeVariables = true) =>
            Ok(await _appService.GetListAsync(includeVariables));

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id) => Ok(await _appService.GetByIdAsync(id));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDataModelDto dto)
        {
            var result = await _appService.CreateAsync(dto);
            return Ok(result);
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] DataModelDto dto)
        {
            var result = await _appService.UpdateAsync(dto);
            return Ok(result);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return Ok(new { success = true, message = "数据模型删除成功" });
        }
    }
}
