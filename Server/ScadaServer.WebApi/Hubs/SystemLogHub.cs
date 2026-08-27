using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ScadaServer.WebApi.Hubs
{
    /// <summary>
    /// 系统日志实时推送 Hub。
    /// <para>
    /// 使用 [Authorize] 而非 [AllowAnonymous]：仅已登录（携带 JWT）客户端可连接并接收日志推送，
    /// 避免通过 SignalR 匿名泄露运行日志；与实时数据 ScadaHub（[AllowAnonymous]）区分开。
    /// </para>
    /// <para>
    /// 服务端仅向客户端推送非敏感运行日志（ReceiveLog 事件），
    /// 不含操作人/IP 等敏感字段的操作/安全日志不通过此通道推送。
    /// </para>
    /// </summary>
    [Authorize]
    public class SystemLogHub : Hub
    {
    }
}
