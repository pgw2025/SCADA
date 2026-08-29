using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 暴露接口（开放 API）配置注册表实现。
    /// <para>
    /// 以单例注册，内存缓存所有 <see cref="ExposedInterface.Active"/> 的接口配置，
    /// 键为规范化的 "(METHOD) 完整路由"（如 "GET:/open/tank/level"），供 /open/* 网关
    /// O(1) 匹配。仓储为 Scoped 生命周期，故在 ReloadAsync 内经 IServiceScopeFactory
    /// 现场创建 scope 解析，避免将 Scoped 依赖提升为单例。
    /// </para>
    /// </summary>
    public class ExposedApiRegistry : IExposedApiRegistry
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private ConcurrentDictionary<string, ExposedInterfaceDto> _cache = new(StringComparer.OrdinalIgnoreCase);
        private bool _loaded;

        public ExposedApiRegistry(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <inheritdoc />
        public async Task ReloadAsync()
        {
            await _gate.WaitAsync();
            try
            {
                List<ExposedInterfaceDto> items;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<IExposedInterfaceRepository>();
                    var entities = await repo.GetListAsync(i => i.Active);
                    items = entities.Select(e => new ExposedInterfaceDto
                    {
                        Id = e.Id,
                        Name = e.Name,
                        RouteUrl = e.RouteUrl,
                        RequestMethod = e.RequestMethod,
                        DeviceId = e.DeviceId,
                        ExposedKey = e.ExposedKey,
                        Active = e.Active
                    }).ToList();
                }

                var map = new ConcurrentDictionary<string, ExposedInterfaceDto>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    var key = BuildKey(item.RequestMethod, item.RouteUrl);
                    if (key != null)
                    {
                        map[key] = item;
                    }
                }
                _cache = map;
                _loaded = true;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <inheritdoc />
        public bool TryMatch(string method, string path, out ExposedInterfaceDto? dto)
        {
            // 首次访问自动加载，避免启动顺序依赖（Startup 时未调用 Reload 也能工作）。
            if (!_loaded)
            {
                ReloadAsync().GetAwaiter().GetResult();
            }

            var key = BuildKey(method, path);
            if (key != null && _cache.TryGetValue(key, out var hit))
            {
                dto = hit;
                return true;
            }

            dto = null;
            return false;
        }

        /// <summary>
        /// 构造缓存键："METHOD:/规范路径"。方法统一大写，路径统一小写并去除尾部斜杠。
        /// 非法输入返回 null。
        /// </summary>
        private static string? BuildKey(string? method, string? routeUrl)
        {
            if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(routeUrl))
                return null;

            var m = method.Trim().ToUpperInvariant();
            if (m is not ("GET" or "POST"))
                return null;

            var p = routeUrl.Trim().TrimEnd('/');
            if (p.Length == 0)
                return null;

            // 统一小写比较：让 /open/Tank/Level 与 /open/tank/level 命中同一条配置。
            return m + ":" + p.ToLowerInvariant();
        }
    }
}