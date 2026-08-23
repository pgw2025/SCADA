using System.Text.Json;
using ScadaServer.Domain.Entities;
using Microsoft.AspNetCore.Hosting;

namespace ScadaServer.Infrastructure.Configuration
{
    public class DatabaseConfigManager
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public DatabaseConfigManager(IWebHostEnvironment env)
        {
            _filePath = Path.Combine(env.ContentRootPath, "databases.json");
        }

        public async Task<List<DatabaseConfig>> GetAllAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (!File.Exists(_filePath)) return new List<DatabaseConfig>();
                var json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<List<DatabaseConfig>>(json) ?? new List<DatabaseConfig>();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task SaveAllAsync(List<DatabaseConfig> configs)
        {
            await _semaphore.WaitAsync();
            try
            {
                var json = JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_filePath, json);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
