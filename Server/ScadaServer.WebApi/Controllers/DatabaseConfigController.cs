using Microsoft.AspNetCore.Mvc;
using ScadaServer.Domain.Entities;
using ScadaServer.Infrastructure.Configuration;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DatabaseConfigController : ControllerBase
    {
        private readonly DatabaseConfigManager _configManager;

        public DatabaseConfigController(DatabaseConfigManager configManager)
        {
            _configManager = configManager;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _configManager.GetAllAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var configs = await _configManager.GetAllAsync();
            var config = configs.FirstOrDefault(c => c.Id == id);
            return config != null ? Ok(config) : NotFound();
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DatabaseConfig entity)
        {
            var configs = await _configManager.GetAllAsync();
            entity.Id = configs.Any() ? configs.Max(c => c.Id) + 1 : 1;
            configs.Add(entity);
            await _configManager.SaveAllAsync(configs);
            return Ok(entity);
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] DatabaseConfig entity)
        {
            var configs = await _configManager.GetAllAsync();
            var index = configs.FindIndex(c => c.Id == entity.Id);
            if (index == -1) return NotFound();
            configs[index] = entity;
            await _configManager.SaveAllAsync(configs);
            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var configs = await _configManager.GetAllAsync();
            var config = configs.FirstOrDefault(c => c.Id == id);
            if (config == null) return NotFound();
            configs.Remove(config);
            await _configManager.SaveAllAsync(configs);
            return Ok();
        }
    }
}
