using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 运行时数据库管理服务。
    /// <para>
    /// 管理【主库（MySQL）】的配置读取与保存，以及任意数据库（主库/历史库）的连接测试。
    /// 主库配置存放于 appsettings + override 文件（非 DatabaseConfigs 表），避免自举循环；
    /// 修改主库配置后需重启服务生效。
    /// </para>
    /// </summary>
    public interface IRuntimeDatabaseService
    {
        /// <summary>读取当前生效的主库（MySQL）配置（密码回显为掩码）。</summary>
        Task<MainDatabaseConfigDto> GetMainConfigAsync();

        /// <summary>
        /// 保存主库（MySQL）配置到 override 文件（密码掩码/空 = 原值不变）。
        /// 修改主库连接需重启服务生效。
        /// </summary>
        Task SaveMainConfigAsync(MainDatabaseConfigDto dto);

        /// <summary>对指定后端类型执行连接测试（MySQL / InfluxDB）。</summary>
        Task<TestConnectionResult> TestConnectionAsync(TestConnectionRequest request);
    }
}