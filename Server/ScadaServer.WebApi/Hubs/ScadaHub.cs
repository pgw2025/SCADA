using Microsoft.AspNetCore.SignalR;

namespace ScadaServer.WebApi.Hubs
{
    /// <summary>
    /// SCADA实时通信Hub，用于向客户端推送实时数据更新
    /// </summary>
    /// <remarks>
    /// 客户端可通过 SignalR 连接到此Hub接收设备变量更新、报警通知等实时消息。
    /// </remarks>
    public class ScadaHub : Hub
    {
        // 可以根据需要扩展客户端调用服务端的方法
    }
}
