# C# ASP.NET Core API 与 SCADA 物联网智能中控网关对接指南

本指南为工业系统架构人员及后端开发人员设计，详细说明如何使用 **ASP.NET Core (Web API)** 与本 Vue.js 智能 SCADA 工业边缘平台进行数据交互。包括对 RESTful API 网关的对接、WEBHOOK 接收端、以及企业云端 **MQTT** 的代理集成。

---

## 目录

1. [物模型遥测数据 JSON 规约](#1-物模型遥测数据-json-规约)
2. [C# 实体定义 (POCO / DTO)](#2-c-实体定义-poco--dto)
3. [ASP.NET Core Web API 接收推送控制器](#3-aspnet-core-web-api-接收推送控制器)
4. [通过 MQTTnet 集成 MQTT 遥测流](#4-通过-mqttnet-集成-mqtt-遥测流)
5. [调用 SCADA 边缘 RESTful API 获取数据](#5-调用-scada-边缘-restful-api-获取数据)
6. [接口安全机制与密钥认证](#6-接口安全机制与密钥认证)

---

## 1. 物模型遥测数据 JSON 规约

设备在进行本地自动转换或通过 API 数据网关（RESTful）及 MQTT 转发至云端时，数据模型均遵循高可扩展性、强类型声明的 JSON 数据交换结构。

### 1.1 网关单个变量遥测输出 (RESTful API 主动暴露格式)
当云端或其它系统主动请求本网关暴露的 HTTP GET Endpoint 时，网关返回如下结构：

```json
{
  "system": "IOTA-SCADA M2M GATEWAY",
  "version": "V6.0 企业级",
  "api_name": "API_Name",
  "endpoint": "/api/path",
  "device": {
    "id": "dev-01",
    "code": "PLC-01",
    "name": "空压机1号机",
    "status": "online"
  },
  "payload": {
    "variable_key": "temperature",
    "current_value": 78.45,
    "timestamp": "2026-05-31 04:10:00",
    "data_quality": "GOOD (0x0)"
  }
}
```

### 1.2 批量变量状态主动上报 (Webhook 推送 / MQTT Payload 格式)
网关在检测到数值越界、或是变量数值刷新时，将以下列 JSON 结构主动发送 POST 请求或 MQTT PUSH 传输到 ASP.NET Core 服务器：

```json
{
  "gatewayId": "scada_edge_9842",
  "deviceId": "dev-01",
  "deviceName": "空压机1号机",
  "timestamp": "2026-05-31T04:10:00Z",
  "variables": [
    {
      "key": "temperature",
      "value": 78.45,
      "updatedAt": "2026-05-31 12:10:00",
      "unit": "℃",
      "status": "normal"
    },
    {
      "key": "pressure",
      "value": 2.14,
      "updatedAt": "2026-05-31 12:09:58",
      "unit": "MPa",
      "status": "warning"
    }
  ]
}
```

---

## 2. C# 实体定义 (POCO / DTO)

在 ASP.NET Core 应用中，推荐使用 `System.Text.Json.Serialization` 执行强类型属性反序列化，确保完美的命名规约兼容（Camel/Snake Case 兼容）。

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ScadaIntegration.Dto
{
    // --- 适配 1.1 主动查询返回的结构 ---
    public class ScadaGatewayApiResult
    {
        [JsonPropertyName("system")]
        public string System { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("api_name")]
        public string ApiName { get; set; }

        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; }

        [JsonPropertyName("device")]
        public DeviceInfo Device { get; set; }

        [JsonPropertyName("payload")]
        public PayloadInfo Payload { get; set; }
    }

    public class DeviceInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }
    }

    public class PayloadInfo
    {
        [JsonPropertyName("variable_key")]
        public string VariableKey { get; set; }

        [JsonPropertyName("current_value")]
        public object CurrentValue { get; set; } // 数值可能为 double, int, bool 或 string

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; }

        [JsonPropertyName("data_quality")]
        public string DataQuality { get; set; }
    }

    // --- 适配 1.2 主动Webhook推送/MQTT上报的批量结构 ---
    public class ScadaTelemetryPostDto
    {
        [JsonPropertyName("gatewayId")]
        public string GatewayId { get; set; }

        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; }

        [JsonPropertyName("deviceName")]
        public string DeviceName { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("variables")]
        public List<TelemetryVariableDto> Variables { get; set; }
    }

    public class TelemetryVariableDto
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("value")]
        public object Value { get; set; }

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }
    }
}
```

---

## 3. ASP.NET Core Web API 接收推送控制器

下面的示例程序编写了标准的接收端 **Web API Controller**，负责接收来自 SCADA 主动转发的值越界报警或数据变化。

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ScadaIntegration.Dto;
using System;
using System.Threading.Tasks;

namespace ScadaIntegration.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TelemetryReceiverController : ControllerBase
    {
        private readonly ILogger<TelemetryReceiverController> _logger;

        public TelemetryReceiverController(ILogger<TelemetryReceiverController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 接收设备变量批量遥测推送 WEBHOOK
        /// </summary>
        /// <param name="telemetry">反序列化的批量遥测输入模型</param>
        [HttpPost("webhook")]
        public async Task<IActionResult> ReceiveWebhook([FromBody] ScadaTelemetryPostDto telemetry)
        {
            if (telemetry == null)
            {
                return BadRequest(new { code = 400, message = "无效的请求体Payload" });
            }

            try
            {
                _logger.LogInformation($"[网关上报] 收到来自网关 [{telemetry.GatewayId}] 下 [{telemetry.DeviceName}] 的遥测推送");

                foreach (var variable in telemetry.Variables)
                {
                    _logger.LogInformation($"  - 物理测点: {variable.Key} = {variable.Value} {variable.Unit} (越界状态: {variable.Status})");
                    
                    // TODO: 在这里执行您的业务逻辑，例如写入 C# EF Core (MSSQL, TimescaleDB, Postgre) 开启时序库持久化
                }

                // 返回 200 给 SCADA 边缘，确认响应
                return Ok(new { code = 200, received = true, serverTime = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理 SCADA Webhook 时网卡发生故障或数据写冲突");
                return StatusCode(500, new { code = 500, message = "内部服务写入异常" });
            }
        }
    }
}
```

---

## 4. 通过 MQTTnet 集成 MQTT 遥测流

如果用户配置了 **MQTT 服务器管理**，本网关会将所有选定的物理变量通过 MQTT 发布（默认发布主题格式为 `prefix/variableKey`）。在 **ASP.NET Core 7.0/8.0** 中，最推荐使用 **MQTTnet** 库实现后台托管长连接监听机制。

### 依赖安装 (NuGet):
```bash
dotnet add package MQTTnet
```

### 建立后台托管长连接服务 (BackgroundService):

```csharp
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using ScadaIntegration.Dto;

namespace ScadaIntegration.Services
{
    public class MqttTelemetryListenerService : BackgroundService
    {
        private readonly ILogger<MqttTelemetryListenerService> _logger;
        private readonly IMqttClient _mqttClient;
        private readonly MqttClientOptions _mqttOptions;

        public MqttTelemetryListenerService(ILogger<MqttTelemetryListenerService> logger)
        {
            _logger = logger;
            
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            // 对应前台设置的 MQTT 转发参数配置
            _mqttOptions = new MqttClientOptionsBuilder()
                .WithTcpServer("broker.emqx.io", 1883) // 这里和 SCADA 侧设置配置需要一致
                .WithClientId("aspnet_core_scada_cloud_listener")
                // .WithCredentials("api_user", "secure_secret") // 若有密码则开启
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .WithCleanSession()
                .Build();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                string topic = e.ApplicationMessage.Topic;
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                
                _logger.LogInformation($"[MQTT 遥测流] 收到消息 - 主题 {topic}");

                try
                {
                    // 解析 SCADA 边缘平台发布出的数据 (多维遥测对象)
                    var variableValue = JsonSerializer.Deserialize<JsonElement>(payload);
                    _logger.LogWarning($"[物理变量刷新] 在主题 [{topic}] 下读取到实时最新模拟值: {variableValue}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MQTT 消息 Payload 解释 JSON 时遇到致命格式阻碍");
                }

                return Task.CompletedTask;
            };

            // 长连接连接及掉线自动重连管理
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_mqttClient.IsConnected)
                    {
                        _logger.LogInformation("建立连接: 正在与云 MQTT 桥接代理建立物联通路...");
                        await _mqttClient.ConnectAsync(_mqttOptions, stoppingToken);

                        // 订阅 SCADA 配置的高频上报通道前缀通配符 e.g. factory/telemetry/#
                        await _mqttClient.SubscribeAsync("factory/scada/telemetry/#");
                        _logger.LogInformation("订阅就绪: 订阅通配符 factory/scada/telemetry/# 主题成功！");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "连接 MQTT 主机失败，10s 后将尝试自动线路重拨...");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_mqttClient.IsConnected)
            {
                await _mqttClient.DisconnectAsync();
            }
            base.StopAsync(cancellationToken);
        }
    }
}
```

---

## 5. 调用 SCADA 边缘 RESTful API 获取数据

开发 C# API 还可以通过 `IHttpClientFactory` 建立定时任务，主动周期轮询获取 SCADA 运行状态。

```csharp
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ScadaIntegration.Dto;

namespace ScadaIntegration.Services
{
    public class ScadaGatewayClient
    {
        private readonly HttpClient _httpClient;

        public ScadaGatewayClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// 调用 SCADA 外传网关 API 获取具体点位实时情况
        /// </summary>
        public async Task<ScadaGatewayApiResult> GetLiveVariableAsync(string exposedRoute)
        {
            // 假设 SCADA 边缘物理网关宿主机器在 http://192.168.1.150:3000
            // exposedRoute 例如 "/api/machinetemp"
            string requestUrl = $"http://192.168.1.150:3000{exposedRoute}";
            
            try
            {
                var result = await _httpClient.GetFromJsonAsync<ScadaGatewayApiResult>(requestUrl);
                return result;
            }
            catch (HttpRequestException ex)
            {
                // 日志记录连接被拒或超时
                return null;
            }
        }
    }
}
```

---

## 6. 接口安全机制与密钥认证

为了确保物理测点写入及请求不会被未授权的内网应用劫持，建议在 ASP.NET Core 中实施 **API Key 验证头过滤器**。

### 多路校验中间件示例 (ApiKeyMiddleware.cs)
在 SCADA 侧配置 API 注册外输时，可以在默认 `Request Headers` 或者业务 Token 设定请求识别头：`X-SCADA-Token: Web_Safe_Secret_Key`。

```csharp
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace ScadaIntegration.Middleware
{
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private const string APIKEYNAME = "X-SCADA-Token";
        private const string EXPECTED_APIKEY = "Web_Safe_Secret_Key_Token_6688"; // 本地设置防物理误触鉴权密码

        public ApiKeyMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 对于 Webhook 控制器进行过滤器校验
            if (context.Request.Path.StartsWithSegments("/api/telemetryreceiver"))
            {
                if (!context.Request.Headers.TryGetValue(APIKEYNAME, out var extractedApiKey))
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("SCADA HTTP认证未通过: 请求授权标头 X-SCADA-Token 缺失！");
                    return;
                }

                if (!EXPECTED_APIKEY.Equals(extractedApiKey))
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("SCADA HTTP鉴权失败: 密钥无效，请重新校对内网边缘网关握手令牌。");
                    return;
                }
            }

            await _next(context);
        }
    }
}
```

在 `Program.cs` 挂载中间件：
```csharp
app.UseMiddleware<ApiKeyMiddleware>();
```

---
*编撰依据：IOTA-SCADA M2M 物联网网关注册规约 V6.0*
*本篇文档已保存在智能 SCADA 设备管理器系统物理存档中*
