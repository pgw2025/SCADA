using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 暴露接口（开放 API）配置注册表：缓存所有启用的暴露接口，供 /open/* 网关按
    /// (请求方法, 路由路径) 快速匹配，并在增删改后热刷新，无需重启服务。
    /// </summary>
    public interface IExposedApiRegistry
    {
        /// <summary>
        /// 全量重载启用的接口配置到内存缓存（增删改后调用）。
        /// </summary>
        Task ReloadAsync();

        /// <summary>
        /// 按请求方法与路径匹配暴露接口配置。
        /// </summary>
        /// <param name="method">HTTP 请求方法（GET/POST…），不区分大小写。</param>
        /// <param name="path">已剥离 /open 前缀之后的子路径，如 /tank/level。</param>
        /// <param name="dto">命中时的接口配置；未命中为 null。</param>
        /// <returns>是否命中。</returns>
        bool TryMatch(string method, string path, out ExposedInterfaceDto? dto);
    }
}