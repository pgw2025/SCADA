using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ScadaServer.WebApi.Hubs
{
    /// <summary>
    /// SCADA实时通信Hub，用于向客户端推送实时数据更新
    /// </summary>
    /// <remarks>
    /// 客户端可通过 SignalR 连接到此Hub接收设备变量更新、报警通知等实时消息。
    /// 该 Hub 为纯服务端下行推送通道（无客户端敏感上行操作），但仍要求登录（携带 JWT）：
    /// 实时设备数据属于敏感运行信息，禁止匿名连接窥探。JWT 经 accessTokenFactory 以
    /// access_token 查询参数传递，由 JwtBearerEvents.OnMessageReceived 注入鉴权。
    /// </remarks>
    [Authorize]
    public class ScadaHub : Hub
    {
        /// <summary>
        /// 设备实时数据分组名：变量更新仅推送至订阅该设备的分组，
        /// 避免 Clients.All 全连接广播随客户端数与变量数线性放大带宽。
        /// </summary>
        public static string DeviceGroup(int deviceId) => $"device-{deviceId}";

        /// <summary>
        /// 客户端订阅指定设备的实时变量更新（页面挂载/切换设备时调用，引用计数由前端管理）。
        /// </summary>
        public Task SubscribeDevice(int deviceId) =>
            Groups.AddToGroupAsync(Context.ConnectionId, DeviceGroup(deviceId));

        /// <summary>
        /// 客户端取消订阅指定设备的实时变量更新。
        /// </summary>
        public Task UnsubscribeDevice(int deviceId) =>
            Groups.RemoveFromGroupAsync(Context.ConnectionId, DeviceGroup(deviceId));
    }
}
