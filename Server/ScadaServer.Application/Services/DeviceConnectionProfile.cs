using System.Text.Json;
using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 设备连接配置的解析辅助（阶段 3 引入）：从驱动配置 JSON 原文中提取管理/检索用的冗余列
    /// （Host/Port/超时），供 <see cref="DeviceConnection"/> 双写与历史数据回填共用同一套算法。
    /// <para>
    /// 依据 P3-B：<see cref="DeviceConnection.ConfigJson"/> 保存驱动完整配置原文，
    /// Host/Port 仅为提取的冗余列、不参与运行时连接；因此本解析失败只影响管理展示列，绝不影响驱动连接。
    /// </para>
    /// </summary>
    public static class DeviceConnectionProfile
    {
        /// <summary>
        /// 配置反序列化选项：属性名大小写不敏感，兼容 camelCase / PascalCase 两种存储格式。
        /// </summary>
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// 连接摘要（冗余列）解析结果。解析失败或协议无网络端点（Virtual）时 Host/Port 为空。
        /// </summary>
        public sealed record ConnectionSummary(string? Host, int? Port, int TimeoutMs);

        /// <summary>空摘要：Host/Port 为 null，超时用默认 5000。</summary>
        public static readonly ConnectionSummary EmptySummary = new(null, null, 5000);

        /// <summary>
        /// 从协议配置 JSON 原文解析连接摘要（Host/Port/TimeoutMs）。
        /// 按协议驱动键路由解析：S7 → IpAddress/Port(默认102)；OPC UA → 端点 URL 主机/端口(默认4840)；
        /// 其它协议（Virtual 等）或 JSON 非法 → 返回空摘要（管理列留空，不影响驱动运行）。
        /// </summary>
        public static ConnectionSummary ParseConnectionSummary(string? driverKey, string configJson)
        {
            var kind = driverKey?.Trim().ToUpperInvariant();
            try
            {
                switch (kind)
                {
                    case "S7" or "S7DRIVER":
                    {
                        var c = JsonSerializer.Deserialize<S7Config>(configJson, JsonOpts);
                        if (c == null) return EmptySummary;
                        return new ConnectionSummary(
                            string.IsNullOrWhiteSpace(c.IpAddress) ? null : c.IpAddress,
                            c.Port > 0 ? c.Port : 102,
                            c.IoTimeoutMs ?? 5000);
                    }
                    case "OPCUA" or "OPCUADRIVER":
                    {
                        var c = JsonSerializer.Deserialize<OpcUaConfig>(configJson, JsonOpts);
                        if (c == null || string.IsNullOrWhiteSpace(c.EndpointUrl)) return EmptySummary;
                        var (host, port) = ExtractUriHostPort(c.EndpointUrl, 4840);
                        return new ConnectionSummary(host, port, 5000);
                    }
                    default:
                        // Virtual 及其它协议：无网络端点，Host/Port 保持 NULL。
                        return EmptySummary;
                }
            }
            catch (JsonException)
            {
                // 配置非法：返回空摘要；驱动连接仍以 ConfigJson 原文反序列化，不受影响。
                return EmptySummary;
            }
        }

        /// <summary>从 URL/端点串提取主机与端口；未指定端口时使用 <paramref name="defaultPort"/>。</summary>
        private static (string? Host, int? Port) ExtractUriHostPort(string endpoint, int defaultPort)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return (null, null);
            var host = string.IsNullOrWhiteSpace(uri.Host) ? null : uri.Host;
            var port = uri.Port > 0 ? uri.Port : defaultPort;
            return (host, port);
        }

        /// <summary>字符串截断到指定长度（UTF-16 字符数），超长时保留前 N 字符，防入库超列宽。</summary>
        public static string? Truncate(string? value, int maxLength)
            => string.IsNullOrEmpty(value)
                ? value
                : (value.Length <= maxLength ? value : value[..maxLength]);
    }
}
