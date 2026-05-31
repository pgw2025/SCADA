# IOTA-SCADA 工业物联脑与 ASP.NET Core API 后端对接规范指南

本指南详细说明了本前端工业组态管理系统（基于 Vue 3 + TypeScript）如何与您后端的 **ASP.NET Core Web API / SignalR** 服务器对接。文档包含了 C# 实体模型、JSON 通讯契约、RESTful API 终结点规范以及基于 SignalR 的工业实时时序遥测通信设计。

---

## 目录
1. [系统整体对接架构](#1-系统整体对接架构)
2. [C# 数据模型定义 (DTOs)](#2-c-数据模型定义-dtos)
3. [RESTful 核心接口与 JSON 格式](#3-restful-核心接口与-json-格式)
4. [SignalR 实时遥测数据管道 (SCADA 双向控制)](#4-signalr-实时遥测数据管道-scada-双向控制)
5. [ASP.NET Core 核心控制器实现示例 (ScadaController)](#5-aspnet-core-核心控制器实现示例-scadacontroller)
6. [Vue 3 端 Axios / SignalR 对接改造建议](#6-vue-3-端-axios--signalr-对接改造建议)

---

## 1. 系统整体对接架构

IOTA-SCADA 系统采用**混合通讯架构**：
*   **静态与元数据管理 (HTTPS/REST)**：用于区域管理、设备模型、设备配置、组态图形（HMI）画布、联动触发器规则、计划调度任务等的增删改查 (CRUD)。
*   **动态工业遥测 & 双向控制 (SignalR)**：用于实时工业物理点位（变量）的更新推送和下行合闸/设值控制指令。
*   **历史多维时序查询 (HTTPS/REST)**：用于图表时序历史数据包的调取。

```
                  ┌─────────────────────────────────────┐
                  │          Vue 3 组态前端画面         │
                  └──────────────────┬──────────────────┘
                                     │
                   REST APIs (JSON)  │  SignalR Websocket
                   (配置/曲线/日志)  │  (实时遥测 & 下行控制)
                                     ▼
                  ┌─────────────────────────────────────┐
                  │      ASP.NET Core Web API / Hub     │
                  └──────────────────┬──────────────────┘
                                     │
         ┌───────────────────────────┼───────────────────────────┐
         ▼                           ▼                           ▼
┌──────────────────┐       ┌──────────────────┐        ┌──────────────────┐
│  关系型/配置库   │       │ 时序库 (Influx)  │        │   物理驱动总线   │
│ (SQL Server/PG)  │       │ (Historical DB)  │        │ (OPC UA / S7 /..)│
└──────────────────┘       └──────────────────┘        └──────────────────┘
```

数据格式说明：本前端遵循现代 Web 标准，接口传输全部使用 **`application/json`**，字段命名遵从 **camelCase (驼峰命名)**。ASP.NET Core 默认的 `System.Text.Json` 在序列化时会自动进行驼峰处理。

---

## 2. C# 数据模型定义 (DTOs)

为了保证前后端类型 100% 对应，建议在 ASP.NET Core 的 `Models` 或 `DTOs` 命名空间下引入以下类。类型已经过转换以匹配 TypeScript 接口。

### 2.1 设备与变量模型 (DataModel, Device, ModelVariable)

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IotaScada.Models
{
    // C# 对应的设备协议类型
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeviceType
    {
        OPCUA,
        S7,
        MQTT,
        Virtual
    }

    public class ModelVariableDto
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "analog"; // analog 或 digital
        public string Unit { get; set; } = string.Empty;
        public double Min { get; set; }
        public double Max { get; set; }
        public string Address { get; set; } = string.Empty; // Modbus地址/S7 DB偏置等
        public string Description { get; set; } = string.Empty;
    }

    public class DataModelDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DeviceType Type { get; set; }
        public List<ModelVariableDto> Variables { get; set; } = new();
    }

    public class DeviceDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty; // SCADA-PUMP-01 编码
        public string AreaId { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public DeviceType Type { get; set; }
        public string? IpAddress { get; set; }
        public string? Port { get; set; }
        public string? Topic { get; set; }
        public string Status { get; set; } = "offline"; // online 或 offline
        
        // 核心寄存器当前值缓存：值可能是数字、布尔
        public Dictionary<string, object> Variables { get; set; } = new();
        public string LastUpdated { get; set; } = string.Empty;
    }
}
```

### 2.2 组态画布模型 (ScadaScreenProject, ScadaPage, HMIComponent)

组态图画布是将物理点位绑定的核心载体（即 `bindField` 映射设备变量）：

```csharp
namespace IotaScada.Models
{
    public class HMIComponentDto
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // pump, valve, tank, boiler, text 等
        public string Name { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Label { get; set; } = string.Empty;
        public string BindField { get; set; } = string.Empty; // 绑定变量键 (例如 tank_level)
        public int ZIndex { get; set; } = 1;
        public ComponentProps Props { get; set; } = new();
    }

    public class ComponentProps
    {
        public string? ActiveColor { get; set; }
        public string? InactiveColor { get; set; }
        public bool? ShowValue { get; set; }
        public double? MaxValue { get; set; }
        public string? Unit { get; set; }
        public string? FillColor { get; set; }
        public string? StrokeColor { get; set; }
        public double? ThresholdMin { get; set; }
        public double? ThresholdMax { get; set; }
        public int? FontSize { get; set; }
        public string? Align { get; set; } // "left", "center", "right"
        public bool? Bold { get; set; }
        public string? ButtonMode { get; set; } // toggle, momentary, set-value
        public double? ClickValue { get; set; }
        public string? ButtonText { get; set; }
        public string? StateMappings { get; set; }
        public string? TimeFormat { get; set; }
    }

    public class ScadaPageDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<HMIComponentDto> Components { get; set; } = new();
    }

    public class ScadaScreenProjectDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ScadaPageDto> Pages { get; set; } = new();
    }
}
```

### 2.3 报警联动触发器 & 系统计划任务

```csharp
namespace IotaScada.Models
{
    public class VariableTriggerDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string VariableKey { get; set; } = string.Empty;
        public string Condition { get; set; } = "greater"; // less, greater, equal
        public double Threshold { get; set; }
        public string ActionType { get; set; } = "alarm"; // alarm, linkage
        public string AlarmLevel { get; set; } = "warning"; // info, normal, warning
        public string? LinkageVariableKey { get; set; }
        public object? LinkageValue { get; set; }
        public bool Active { get; set; }
    }

    public class ScheduledTaskDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "backup"; // set_value, backup, execute_script, clear_history
        public string CronExpression { get; set; } = string.Empty;
        public TaskParams Params { get; set; } = new();
        public string? LastRun { get; set; }
        public string Status { get; set; } = "idle"; // idle, running, success, failed
        public bool Active { get; set; }
    }

    public class TaskParams
    {
        public string? VariableKey { get; set; }
        public double? NewValue { get; set; }
        public string? ScriptId { get; set; }
        public int? RetentionDays { get; set; }
    }
}
```

---

## 3. RESTful 核心接口与 JSON 格式

以下是前端调用频次最高、作为数据总脑核心的各个功能模块的 API 接口规范与 JSON 承载格式：

### 3.1 获取现场所有设备列表及实时状态变量 (GET /api/scada/devices)
*   **用途**：初始渲染设备管理、仪表盘、点位模型时，加载当前的变量值列表。
*   **请求类型**：`GET`
*   **返回格式 (JSON)**:
```json
[
  {
    "id": "dev-1",
    "name": "1号污水净化备用循环变频站",
    "code": "OPC-WWT-101",
    "areaId": "area-1",
    "modelId": "model-wastewater",
    "type": "OPCUA",
    "ipAddress": "192.168.1.10",
    "port": "4840",
    "status": "online",
    "variables": {
      "tank_level": 68.0,
      "purified_level": 32.25,
      "flow_rate": 18.5,
      "pump_state": true,
      "valve_state": true,
      "alarm_status": false
    },
    "lastUpdated": "2026-05-31 09:12:05"
  },
  {
    "id": "dev-2",
    "name": "中温中压过热蒸汽汽水反应锅炉",
    "code": "S7-BLR-202",
    "areaId": "area-2",
    "modelId": "model-thermal",
    "type": "S7",
    "ipAddress": "192.168.2.14",
    "port": "102",
    "status": "online",
    "variables": {
      "boiler_temp": 72.5,
      "boiler_press": 55.2,
      "pump_state": true,
      "alarm_status": false
    },
    "lastUpdated": "2026-05-31 09:12:05"
  }
]
```

### 3.2 变量下行控制设值 (POST /api/scada/variables/write)
*   **用途**：在组态大屏上，点击按钮、切换开关拨码、或设值输入器时，把指令安全下发给 PLC。
*   **请求类型**：`POST`
*   **请求体格式 (JSON)**:
```json
{
  "variableKey": "valve_state",
  "value": false
}
```
*   **响应体格式 (JSON)**:
```json
{
  "success": true,
  "message": "下行指令注入成功，S7/OPC-UA通讯节点寄存器写入完毕",
  "timestamp": "2026-05-31T03:12:06Z"
}
```

### 3.3 保存组态工程大屏数据 (POST /api/scada/projects)
*   **用途**：在线画完组态画布后，将拖拽落点的泵阀、仪表盘、液位罐以及数据点绑定位置保存到后端。
*   **请求类型**：`POST`
*   **请求体格式 (JSON)**:
```json
{
  "id": "project-purify",
  "name": "循环污水高倍净化系统工程",
  "description": "工业曝气池双水箱重力落差级联调节...",
  "pages": [
    {
      "id": "page-ww-primary",
      "name": "曝气净化段主画面 (Primary Monitor)",
      "components": [
        {
          "id": "ww-tank-1",
          "type": "tank",
          "name": "储水罐",
          "x": 280,
          "y": 120,
          "width": 120,
          "height": 180,
          "label": "一号澄清溢流高位加压罐",
          "bindField": "tank_level",
          "zIndex": 2,
          "props": {
            "fillColor": "#1e293b",
            "strokeColor": "#334155"
          }
        }
      ]
    }
  ]
}
```
*   **响应体格式 (JSON)**:
```json
{
  "success": true,
  "projectId": "project-purify",
  "message": "SCADA画布节点序列化完毕，已入库存储。"
}
```

### 3.4 时序历史曲线数据请求 (GET /api/scada/history)
*   **用途**：多维时序历史曲线模块，为 Recharts 历史折线图调取某时间范围内的变量模拟点数据。
*   **请求类型**：`GET`
*   **参数列表**：
    *   `variableKey` (例如 `boiler_temp`)
    *   `limit` (采样点数量限制，如 `50`, `150`)
*   **返回格式 (JSON)**:
```json
[
  {
    "id": "hist-seed-1",
    "variableKey": "boiler_temp",
    "variableName": "反应炉膛核心温度 (boiler_temp)",
    "value": 72.5,
    "timestamp": "2026-05-31 09:00:00"
  },
  {
    "id": "hist-seed-2",
    "variableKey": "boiler_temp",
    "variableName": "反应炉膛核心温度 (boiler_temp)",
    "value": 73.04,
    "timestamp": "2026-05-31 09:04:00"
  }
]
```

---

## 4. SignalR 实时遥测数据管道 (SCADA 双向控制)

在工业 Web 通信中，频繁 HTTP 轮询由于开销大很难保证小于 **300ms** 的现场刷新敏感度。微软的 **ASP.NET Core SignalR** 是高并发工业实时推送的首选方案。

### 4.1 回路信号说明
1.  **服务端推到前端 (Server-to-Client)**：
    *   方法名：`ReceiveVariableUpdate`
    *   数据：当真实 PLC 变量或驱动发生改变时，后端主动通知所有连接的大屏实例，大屏以微秒级重绘仪表和流向动效。
2.  **前端推到服务端 (Client-to-Server)**：
    *   方法名：`WritePlcVariable`
    *   大屏操作控件直接调用 Hub 的下行写入方案。

### 4.2 C# SignalR 设备集成管道群设 (ScadaHub.cs)

```csharp
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using System;

namespace IotaScada.Hubs
{
    public class ScadaHub : Hub
    {
        // 客户端连接时的日志与心跳跟踪
        public override async Task OnConnectedAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "ActiveScadaDisplays");
            await base.OnConnectedAsync();
        }

        // 当用户在大屏画布点击按钮、开关、改变数值时，直接通过 WebSocket 长连接将指令写入 PLC 
        public async Task WritePlcVariable(string variableKey, object value)
        {
            try
            {
                // TODO: 在此处调用底层的 OPC-UA 客户端连接器 / 西门子 S7-NET-PLUS 驱动等，下发点位值
                // 例如: PlcDriver.Write(variableKey, value);

                // 指令写入完毕后，广播通知所有关联车间中控台刷新其控件动画
                await Clients.Group("ActiveScadaDisplays").SendAsync("ReceiveVariableUpdate", variableKey, value);
            }
            catch (Exception ex)
            {
                // 若 PLC 通信超时或故障，通知下发用户
                await Clients.Caller.SendAsync("ReceiveSystemAlarm", $"点位指令 [{variableKey}] 写入发生严重硬阻断: {ex.Message}");
            }
        }
    }
}
```

---

## 5. ASP.NET Core 核心控制器实现示例 (ScadaController)

这是一个标准的 REST 接口 C# 实现模板，覆盖了前后端接口跨域 (CORS) 与 REST 核心逻辑。

```csharp
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System;
using IotaScada.Models;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using IotaScada.Hubs;

namespace IotaScada.Controllers
{
    [ApiController]
    [Route("api/scada")]
    public class ScadaController : ControllerBase
    {
        private readonly IHubContext<ScadaHub> _hubContext;

        // 通过 ASP.NET Core 默认 DI 依赖注入服务
        public ScadaController(IHubContext<ScadaHub> hubContext)
        {
            _hubContext = hubContext;
        }

        // 1. 获取所有设备及通道遥测列表
        [HttpGet("devices")]
        public ActionResult<IEnumerable<DeviceDto>> GetDevices()
        {
            // 此处通常从关系型数据库(SQL Server)或实时写缓存 Redis 读取数据
            var devices = new List<DeviceDto>
            {
                new DeviceDto
                {
                    Id = "dev-1",
                    Name = "1号污水净化阀泵变频站",
                    Code = "OPC-WWT-101",
                    Status = "online",
                    Variables = new Dictionary<string, object>
                    {
                        { "tank_level", 65.4 },
                        { "pump_state", true }
                    },
                    LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                }
            };
            return Ok(devices);
        }

        // 2. 模拟/注入下行 PLC 控制指令
        [HttpPost("variables/write")]
        public async Task<IActionResult> WriteVariable([FromBody] VariableWritePayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.VariableKey))
            {
                return BadRequest(new { Success = false, Message = "指令负荷对象解析空。校验失败。" });
            }

            // 1. 实机驱动层写入 S7 / Modbus / MQTT ...
            // 2. 写入完毕后快速触发 SignalR 主动推送大屏
            await _hubContext.Clients.Group("ActiveScadaDisplays")
                                    .SendAsync("ReceiveVariableUpdate", payload.VariableKey, payload.Value);

            return Ok(new
            {
                Success = true,
                Message = $"已成功转发指令。点位键: {payload.VariableKey} -> 目标值: {payload.Value}",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    public class VariableWritePayload
    {
         public string VariableKey { get; set; } = string.Empty;
         public object Value { get; set; } = null!;
    }
}
```

要完美启动上面代码，您的 `Program.cs` 需添加 **SignalR** 支持及 **CORS** 跨域规则：

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. 注册核心控制器与 SignalR 管道
builder.Services.AddControllers();
builder.Services.AddSignalR();

// 2. 配置组态大屏跨域请求支持
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVueScada", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "https://your-preview-domain") // 支持前端端口
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // SignalR Websocket 连接必须启用凭据
    });
});

var app = builder.Build();

app.UseCors("AllowVueScada");
app.UseAuthorization();

// 部署映射路由与中间件
app.MapControllers();
app.MapHub<ScadaHub>("/hubs/scada"); // 映射大屏实时数据物理端点

app.Run();
```

---

## 6. Vue 3 端 Axios / SignalR 对接改造建议

在组态前端，只需要进行以下几步操作，即可切断本地 Mock 机制转而引入您的 ASP.NET Core API：

### 6.1 使用 @microsoft/signalr 进行 WebSocket 数据绑定
在前端工作进程中安装微软官方的 SignalR 连接库：
```bash
npm install @microsoft/signalr
```

修改前端设备值更新处的接口，在 `src/store.ts` 中完成长连接初始化：

```typescript
import { HubConnectionBuilder, HubConnection } from '@microsoft/microsoft-signalr';

let signalRConnection: HubConnection | null = null;

export const initializeRealtimeSignals = (backendUrl: string) => {
  signalRConnection = new HubConnectionBuilder()
    .withUrl(`${backendUrl}/hubs/scada`)
    .withAutomaticReconnect()
    .build();

  // 1. 监听来自 ASP.NET Core 服务端主动下发的点位微秒级更新信号
  signalRConnection.on("ReceiveVariableUpdate", (variableKey: string, newValue: any) => {
    // 调用本地数据驱动更新
    updateLocalVariableInMemory(variableKey, newValue);
  });

  signalRConnection.start()
    .then(() => console.log("IOTA-SCADA 已成功桥接至 .NET Core 工业控制链网关"))
    .catch((err) => console.error("网络拓扑中控握手连接失败:", err));
};

// 2. 将下行写入也改造为 SignalR 或 Axios
export const writePlcVariableToBackend = async (key: string, val: any) => {
  if (signalRConnection && signalRConnection.state === "Connected") {
     await signalRConnection.invoke("WritePlcVariable", key, val);
  } else {
     // fallback to RESTful API
     await axios.post(`${API_BASE_URL}/api/scada/variables/write`, { variableKey: key, value: val });
  }
};
```

---

### 技术提示（后端排错）
*   **JSON 布尔值转换**：在处理二位阀反馈、水泵开关等 `digital` 变量时，前端会传入整型的 `0`/`1` 或 `true`/`false`。在 C# `payload.Value` 统一映射为 `object` 后，可利用 `typeof(bool)` 或整型解析灵活转换。
*   **高频写安全队列**：如果在高并发或几十毫秒轮询频次下向 ASP.NET Core 队列发送写信，建议您的服务端使用 `System.Threading.Channels` 来构建消息缓冲区，避免直接阻塞 PLC 主控总线的并发写通道。
*   **SignalR 自动断线重连**：Vue 端的工业看板可能会常年放置在工厂的监控电视或平板上，重连处理首要重要，使用 `.withAutomaticReconnect()` 能有效恢复因为网线瞬断或高功率电磁干扰导致的长轮询中断。
