# 消息通知投递记录功能 — 完整代码修改方案

> 状态：**v2 待审批**（未改动任何代码；已并入 2026-09-05 自审修订 R1-R4）  
> 日期：2026-09-05  
> 关联分析：消息通知界面功能实现度分析（2026-09-05 工作日志）  
> 范围：仅补齐「投递记录」Tab 的后端链路 + 前端适配；不涉及渠道配置/策略/模板三个已闭环模块的功能变更。

### 修订记录

| 版本 | 修订 | 内容 |
|---|---|---|
| v1 | 初稿 | 17 文件改动方案 |
| v2 | **R1（P0）** | §5.2/§5.3：Recorder 必须实现 `IHostedService` 并以三件套注册（具体类型 + 接口转发 + AddHostedService），否则消费循环不启动、记录永不落库 |
| v2 | **R2（P0）** | §3.1/§6.2：eventType 映射修正——`Alarm` 细分为 `alarmTriggered` / `alarmRecovered`（依据 `msg.Tokens["eventType"]`），与前端联合类型（notificationApi.ts:71）对齐；原稿映射为 `alarm` 不在前端类型内 |
| v2 | **R3（P0）** | §5.2/§6.1/§6.4：重试消息静默丢弃防护——`Enqueue` 改返回 `bool`、新增 `IsChannelEnabled` 前置校验、`RetryAsync` 入队失败回置 Failed、扇出定向消息渠道队列满时回写失败行 |
| v2 | **R4（P1）** | §5.2：启动清扫僵尸 Retrying 行（服务重启会把在途重试中断，行永久停留过渡态） |

---

## 一、背景与目标

### 1.1 现状缺口（P0 断链）

前端「消息通知中心 → 投递记录」Tab 已有完整 UI（筛选/搜索/统计/详情弹窗/重试按钮），调用 3 个接口（`Client/src/api/notificationApi.ts:92-99`）：

| 前端调用                                           | 后端现状    |
| ---------------------------------------------- | ------- |
| `GET /api/NotificationConfig/logs`             | ❌ 端点不存在 |
| `POST /api/NotificationConfig/logs/clear`      | ❌ 端点不存在 |
| `POST /api/NotificationConfig/logs/{id}/retry` | ❌ 端点不存在 |

后端 `NotificationConfigController` 仅 4 个端点（Get/Save/TestDingTalk/TestEmail）；全库无 `NotificationLog` 实体/表；`ExternalNotificationService` 发送结果只写 ILogger（`HostedServices/ExternalNotificationService.cs:260-266`），不落任何投递记录。另：测试发送（`NotificationConfigService.SendTestAsync`）绕过消息队列直连发送器，也不产生记录。

### 1.2 目标

1. 三渠道（钉钉/邮件/Web Push）每次真实投递的**终态结果落库**，可查询、可清空。
2. 失败记录支持**一键重试**（复用现有队列管线，不绕过限流/重试）。
3. 测试发送结果同样落记录（前端测试成功后会刷新日志列表，`NotificationCenterView.vue:498/515`）。
4. 前端零功能删减，仅扩展类型与筛选选项。

---


## 二、决策点（需用户逐项确认）

| #      | 决策点                 | 选项                                                                                                                                          | 建议                                                         |
| ------ | ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------- |
| **D1** | 存储介质                | **A**：新表 `NotificationLogs`（EF Core 实体 + 迁移）；**B**：单例内存环形缓冲（重启丢失，无法支撑跨重启重试）；**C**：复用 SystemLogs 表（Category=Notification，字段语义不匹配）            | **A**。可持久、可重试、与 AlarmRecord/PushSubscription 同模式           |
| **D2** | 记录粒度                | **A**：每消息×每渠道一条记录，只落终态 Success/Failed，另设 `Retrying` 过渡态（重试已受理、尚未完成）；**B**：每次重试尝试都单独落行（行数膨胀，前端详情语义混乱）                                        | **A**。与前端 `status: 'Success'\|'Failed'\|'Retrying'` 类型完全对齐 |
| **D3** | 被限流合并/队列满载丢弃的消息是否落行 | **A**：不落行（限流合并说明已捎带进下一条消息正文；风暴时不写爆表）；**B**：落 Failed 行（error=「限流合并 N 条」/「队列满载丢弃」）                                                            | **A** 起步 + 在限流合并捎带消息的正文中已可追溯；B 可后续加                        |
| **D4** | 重试投递路径              | **A**：`ExternalMessage` 增加可选 `TargetChannel` 字段，重试消息仍走主队列→扇出→限流→重试管线，但扇出时只投递到指定渠道；**B**：重试直接 new Scope 调 Sender.SendAsync（绕过限流，报警风暴时可能打爆渠道） | **A**。复用全部既有保护，改动集中在扇出循环一处                                 |
| **D5** | 记录保留策略              | **A**：不自动清理，仅用户手动「清空记录」；**B**：挂入 SystemLogCleanupHostedService 按保留期分批清理                                                                     | **A**。通知量级远小于系统日志；表加 `(TimestampUtc)` 索引即可，后续要自动清理再加       |
| **D6** | `GET /logs` 返回形态    | **A**：最新 N 条（默认 500，`Id` 倒序，返回数组，兼容现有前端 `ref<NotificationLogItem[]>`）；**B**：分页对象                                                            | **A**。前端当前实现就是整数组渲染，改动最小                                   |
| **D7** | 是否顺带修正保存 toast 文案矛盾 | **A**：顺带把「系统已即时应用」改为「已保存，重启后端服务后生效」（1 行，`NotificationCenterView.vue:480`）；**B**：本次不动                                                        | **A**。属同一界面上轮分析已确认的 P1 问题，仅改一处文案                           |

以下方案按 **D1=A / D2=A / D3=A / D4=A / D5=A / D6=A / D7=A** 撰写；若某项改选，对应小节会标注替代写法。

---

## 三、数据库设计（D1=A）


### 3.1 新实体 `NotificationLog`

位置：`Server/ScadaServer.Domain/Entities/NotificationLog.cs`（与 AlarmRecord/PushSubscription 同层）

```csharp
/// <summary>外部消息投递记录（钉钉/邮件/Web Push 三渠道真实投递终态）。</summary>
public class NotificationLog
{
    public long Id { get; set; }

    /// <summary>投递完成时间（UTC，项目时间约定）。</summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>渠道标识：dingTalk | email | webPush（与前端 NotificationLogItem.channel 对齐）。</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>事件类型：alarmTriggered | alarmRecovered | deviceStatus | systemAlarm | systemError | scriptExecution | test（与前端 NotificationLogItem.eventType 联合类型一一对应）。</summary>
    public string EventType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>收件方摘要（邮箱列表 / 钉钉群机器人 / Web Push 订阅摘要）。</summary>
    public string Recipient { get; set; } = string.Empty;

    /// <summary>Success | Failed | Retrying（Retrying 仅在重试受理后、管线完成前的过渡态）。</summary>
    public string Status { get; set; } = string.Empty;

    public long LatencyMs { get; set; }

    publ
ic string? Error { get; set; }

    /// <summary>正文预览（MarkdownText 截断 500 字符）。</summary>
    public string? PayloadPreview { get; set; }

    /// <summary>完整 ExternalMessage 序列化 JSON，重试时还原重发。</summary>
    public string? PayloadJson { get; set; }
}
```

### 3.2 `ScadaDbContext` 注册（`Infrastructure/Persistence/ScadaDbContext.cs`）

```csharp
public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
// OnModelCreating:
modelBuilder.Entity<NotificationLog>().ToTable("NotificationLogs");
modelBuilder.Entity<NotificationLog>()
    .HasIndex(l => l.TimestampUtc);
modelBuilder.Entity<NotificationLog>()
    .Property(l => l.Channel).HasMaxLength(16);
modelBuilder.Entity<NotificationLog>()
    .Property(l => l.EventType).HasMaxLength(24);
modelBuilder.Entity<NotificationLog>()
    .Property(l => l.Status).HasMaxLength(16);
modelBuilder.Entity<NotificationLog>()
    .Property(l => l.Title).HasMaxLength(255);
modelBuilder.Entity<NotificationLog>()
    .Property(l => l.Recipient).HasMaxLength(255);
```

### 3.3 EF 迁移

```
新增迁移：AddNotificationLogs（1 张表 + 1 索引，无风险操作，不删不改任何现有表）
执行目录：必须在 Server/ScadaServer.WebApi 下运行 dotnet ef migrations add / database update
（设计时工厂从该目录 appsettings.json 读 SystemDbConfig 密码）
落库后备份：mysqldump scada_full_<时间戳>.sql（惯例，虽为纯新增也执行）
```

---

## 四、消息模型扩展（D4=A）

`Server/ScadaServer.Application/DTOs/ExternalMessage.cs` 增加两个可选字段（对钉钉/邮件/WebPush 现有读取零影响）：

```csharp
// ---- 投递记录/重试专用可选字段（向后兼容：null = 正常事件消息）----

/// <summary>定向渠道（Sender.Name）：重试消息只投递到该渠道；null = 扇出到全部启用渠道。</summary>
public string? TargetChannel { get; set; }

/// <summary>来源投递记录 Id：重试路径携带，管线完成后更新原行而非新增行。</summary>
public long? SourceLogId { get; set; }
```

---

## 五、记录与重试服务

### 5.1 接口（`Server/ScadaServer.Application/Interfaces/INotificationLogStore.cs`）

```csharp
/// <summary>投递记录写入器（管线单例侧用，非阻塞、绝不抛出影响发送主流程）。</summary>
public interface INotificationLogRecorder
{
    /// <summary>记录/更新一次投递终态。sourceLogId 非空时更新原行（重试），否则新增行。</summary>
    void Record(NotificationLogEntry entry);
}

/// <summary>投递记录查询/清空/重试（控制器侧用）。</summary>
public interface INotificationLogService
{
    Task<IReadOnlyList<NotificationLogDto>> GetRecentAsync(int limit = 500);
    Task ClearAsync();
    Task<NotificationLogDto> RetryAsync(long id);
}

/// <summary>写入条目（应用层 DTO，避免 Domain 依赖）。</summary>
public record NotificationLogEntry(
    string Channel, string EventType, string Title, string Recipient,
    string Status, long LatencyMs, string? Error,
    string? PayloadPreview, string? PayloadJson, long? SourceLogId);
```

> **关键约束**：`Record` 实现必须 fire-and-forget + 全量 try/catch，记录失败只写 ILogger，**绝不阻塞/抛出到发送循环**（采集路径安全红线）。


### 5.2 实现（`Server/ScadaServer.Infrastructure/Services/NotificationLogStore.cs`）

- `NotificationLogRecorder`：单例，**实现 `INotificationLogRecorder` + `IHostedService`（R1/R4）**：
  - `Record(entry)`：仅 `Channel.Writer.TryWrite` 入内部有界 Channel（容量 1024，DropWrite，失败只记 ILogger 计数），立即返回——绝不抛出到发送循环；
  - `StartAsync`（R4 启动清扫）：先执行
    `UPDATE NotificationLogs SET Status='Failed', Error='服务重启，重试中断' WHERE Status='Retrying'`
    （`ExecuteUpdateAsync`，清扫上次运行中断的在途重试行），再启动后台消费循环；
  - 消费循环：批取出条目后开一个 `IServiceScopeFactory` Scope（每批一个，非每条）取 `ScadaDbContext` 统一落库：`SourceLogId` 非空 → `UPDATE` 原行（Status/LatencyMs/Error/TimestampUtc，容忍 0 行命中），否则 `INSERT`；全量 try/catch，DB 故障仅记日志；
  - `StopAsync`：完成写入端 + 排空消费循环（超时 5s 兜底取消）。
- `NotificationLogService`：Scoped。
  - `GetRecentAsync(limit)`：`OrderByDescending(Id).Take(limit)`，`TimestampUtc` 转本地时间 ISO 字符串输出。
  - `ClearAsync()`：`ExecuteDeleteAsync()` 全表清空。
  - `RetryAsync(id)`（R3 强化后顺序）：
    1. 查行，不存在 → `BusinessException("投递记录不存在")`；`Status != "Failed"` → `BusinessException("仅失败记录可重试")`；
    2. `PayloadJson` 为空或反序列化 `ExternalMessage` 失败 → `BusinessException("投递载荷缺失或损坏，无法重试")`（测试行 PayloadJson=null 在此拦截）；
    3. **渠道前置校验（R3）**：`_queue.IsChannelEnabled(行.Channel)` 为 false → `BusinessException("目标渠道当前未启用，请先在渠道配置中启用后重试")`；
    4. 设 `TargetChannel = 行.Channel`、`SourceLogId = 行.Id`；行置 `Status = "Retrying"` 落库（前端立即看到过渡态）；
    5. **入队校验（R3）**：`if (!_queue.Enqueue(msg))` → 行回置 `Failed`（Error=「队列满载，重试未受理，请稍后再试」）→ `BusinessException` 同文案；
    6. 返回 Retrying 态 DTO。
    - **已知边界**：重试消息同样受限流窗口约束——若当前窗口配额耗尽会与普通消息一起被合并捎带，行在 `Retrying` 停留至下一条消息通过（与现有限流语义一致，可接受）。
    - **残余风险（R3 已收敛）**：主队列成功、但扇出时目标渠道队列满载的场景，由 §6.1 的定向回写兜底（见下），不会出现永久 Retrying。

### 5.3 DI 注册（`WebApi/Extensions/Application.Extensions.cs`，在「外部消息通知」区块追加；R1 三件套）

```csharp
// ========== 投递记录（钉钉/邮件/Web Push 投递终态落库 + 查询/重试）==========
// 三件套（与 ExternalNotificationService 注册模式一致，Application.Extensions.cs:110-112）：
// 缺 AddHostedService 则消费循环不启动、Channel 塞满后所有记录静默丢弃
services.AddSingleton<NotificationLogRecorder>();
services.AddSingleton<INotificationLogRecorder>(sp => sp.GetRequiredService<NotificationLogRecorder>());
services.AddHostedService(sp => sp.GetRequiredService<NotificationLogRecorder>());
services.AddScoped<INotificationLogService, NotificationLogService>();
```

---

## 六、发送管线埋点（`WebApi/HostedServices/ExternalNotificationService.cs`）

### 6.1 扇出定向与丢弃回写（`FanoutAsync`，:177-200；R3）

```csharp
foreach (var state in _states)
{
    // D4：TargetChannel 非空时只投递到指定渠道（重试路径）
    if (msg.TargetChannel is not null &&
        !string.Equals(state.Sender.Name, msg.TargetChannel, StringComparison.OrdinalIgnoreCase))
        continue;
    if (!state.Channel.Writer.TryWrite(msg))
    {
        Interlocked.Increment(ref _fanoutDroppedCount);
        // R3：定向（重试）消息被渠道队列丢弃时回写失败行，避免行永久停留 Retrying
        if (msg.SourceLogId is not null)
            _logRecorder.Record(new NotificationLogEntry(
                Channel: MapChannel(state.Sender.Name), EventType: MapEventType(msg),
                Title: msg.Title, Recipient: state.Sender.RecipientSummary,
                Status: "Failed", LatencyMs: 0, Error: "渠道队列满载，重试消息被丢弃",
                PayloadPreview: null, PayloadJson: null, SourceLogId: msg.SourceLogId));
    }
}
```

> 已核实的 Sender.Name 实际值：`DingTalk`（DingTalkRobotClient.cs:39）/ `Email`（EmailSender.cs:32）/ `WebPush`（WebPushSender.cs:57），与行.Channel 值 `dingTalk/email/webPush` 的 OrdinalIgnoreCase 比较可正确匹配。

### 6.4 `IExternalNotificationQueue` 签名变更（R3）

`Application/Interfaces/IExternalMessageSender.cs`：

```csharp
public interface IExternalNotificationQueue
{
    bool HasEnabledChannels { get; }

    /// <summary>非阻塞入队。返回 false = 无启用渠道短路或主队列满载丢弃（调用方据此决定是否回写失败）。</summary>
    bool Enqueue(ExternalMessage message);

    /// <summary>指定渠道当前是否启用（重试前置校验用）。Sender.Name 大小写不敏感比较。</summary>
    bool IsChannelEnabled(string senderName);
}
```

- `ExternalNotificationService.Enqueue` 实现：`_states.Count == 0` → return false；`TryWrite` 失败 → return false；成功 → true。`IsChannelEnabled`：`_states.Any(s => string.Equals(s.Sender.Name, senderName, StringComparison.OrdinalIgnoreCase))`。
- 现有调用方 `ExternalNotificationDecorator`（6 处 `_queue.Enqueue(...)`）不使用返回值，签名变更不破坏编译；仅 `NotificationLogService.RetryAsync` 消费返回值。


### 6.2 终态记录（`SendWithRetryAsync`，:242-278）

构造函数注入 `INotificationLogRecorder _logRecorder`。每次发送完成（成功或达到 MaxAttempts 最终失败）调用一次：

```csharp
// 成功：
_logRecorder.Record(new NotificationLogEntry(
    Channel: MapChannel(sender.Name), EventType: MapEventType(msg),
    Title: msg.Title, Recipient: sender.RecipientSummary,
    Status: "Success", LatencyMs: stopwatch.ElapsedMilliseconds,
    Error: null, PayloadPreview: Truncate(msg.MarkdownText, 500),
    PayloadJson: SerializeMessage(msg), SourceLogId: msg.SourceLogId));
// 最终失败：同上，Status="Failed"，Error=最后一次异常消息
```

- `MapChannel`：sender.Name → `dingTalk` / `email` / `webPush`（三个 Name 值已核实见 §6.1，映射写死，未知值兜底原样小写）。
- `MapEventType(msg)`（R2 修正——前端联合类型为 `alarmTriggered | alarmRecovered | deviceStatus | systemAlarm | systemError | scriptExecution | test`，notificationApi.ts:71，无 `alarm` 值；View:1699 为裸渲染 `{{ item.eventType }}`，错值会直接展示给用户）：

```csharp
private static string MapEventType(ExternalMessage msg) => msg.Category switch
{
    // R2：Decorator :157 已写入 Tokens["eventType"] = "Triggered"/"Recovered"，据此细分
    ExternalMessageCategory.Alarm =>
        msg.Tokens != null && msg.Tokens.TryGetValue("eventType", out var t) && t == "Recovered"
            ? "alarmRecovered" : "alarmTriggered",
    ExternalMessageCategory.DeviceStatus => "deviceStatus",
    ExternalMessageCategory.SystemAlarm => "systemAlarm",
    ExternalMessageCategory.SystemError => "systemError",
    ExternalMessageCategory.ScriptExecution => "scriptExecution",
    _ => "unknown"
};
```
- 计时：整段 SendWithRetryAsync 含重试退避的总耗时（与前端 latencyMs 展示语义一致）。
- `SerializeMessage`：`JsonSerializer.Serialize` 整个 ExternalMessage（含 Tokens/Severity），供重试还原。
- **重试路径防递归**：`SourceLogId` 非空的消息完成后 `UPDATE` 原行，不新增行——天然无重复。

### 6.3 `IExternalMessageSender` 接口扩展（收件方摘要）

`Application/Interfaces/IExternalMessageSender.cs` 增加：

```csharp
/// <summary>收件方摘要（投递记录展示用）。</summary>
string RecipientSummary { get; }
```

| 实现                    | 返回值                                      |
| --------------------- | ---------------------------------------- |
| `DingTalkRobotClient` | `"钉钉群机器人"`（webhook 含 access_token，不回显明文） |
| `EmailSender`         | `string.Join(", ", _options.To)`         |
| `WebPushSender`       | `"Web Push 订阅设备"`（订阅明细属敏感端点信息，不展开）       |

---

## 七、测试发送落记录（`Infrastructure/Services/NotificationConfigService.cs`）

`SendTestAsync`（:251-266）成功/失败分支各加一行（构造注入 `INotificationLogRecorder`）：

```csharp
_logRecorder.Record(new NotificationLogEntry(
    Channel: channel switch { "钉钉" => "dingTalk", "邮件" => "email", _ => channel },
    EventType: "test", Title: "SCADA 通知测试",
    Recipient: sender.RecipientSummary,
    Status: success ? "Success" : "Failed",
    LatencyMs: sw.ElapsedMilliseconds, Error: failMessage,
    PayloadPreview: "## SCADA 通知测试（测试发送）",
    PayloadJson: null,   // 测试消息不支撑重试（用临时配置，重放无意义）
    SourceLogId: null));
```

> `PayloadJson=null` 的行重试时由 `RetryAsync` 第 2 步拦截（BusinessException），前端 toast 提示，无空引用风险。

---

## 八、控制器端点（`WebApi/Controllers/NotificationConfigController.cs`）

追加 3 个端点（继承类级 `[Authorize(Policy = "RequireAdmin")]`，无需重复标注）：

```csharp
/// <summary>查询最近投递记录（默认 500 条，Id 倒序）。</summary>
[HttpGet("logs")]
public async Task<IActionResult> GetLogs([FromQuery] int limit = 500)
    => Ok(await _logService.GetRecentAsync(Math.Clamp(limit, 1, 2000)));

/// <summary>清空全部投递记录。</summary>
[HttpPost("logs/clear")]
public async Task<IActionResult> ClearLogs() { await _logService.ClearAsync(); return Ok(); }

/// <summary>重试一条失败记录（复用队列管线，定向到原渠道）。</summary>
[HttpPost("logs/{id}/retry")]
public async Task<IActionResult> RetryLog(long id) => Ok(await _logService.RetryAsync(id));
```

构造函数追加注入 `INotificationLogService _logService`。

### DTO（`Application/DTOs/NotificationConfigDtos.cs` 追加）

```csharp
public class NotificationLogDto
{
    public long Id { get; set; }
    public string Timestamp { get; set; } = string.Empty;   // 本地时间 ISO 字符串
    public string Channel { get; set; } = string.Empty;     // dingTalk | email | webPush
    public string EventType { get; set; } = string.Empty;   // alarm | deviceStatus | ... | test
    public string Title { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;      // Success | Failed | Retrying
    public long LatencyMs { get; set; }
    public string? Error { get; set; }
    public string? PayloadPreview { get; set; }
}
```

与前端 `NotificationLogItem` 字段一一对应（`id/timestamp/channel/eventType/title/recipient/status/latencyMs/error/payloadPreview`），JSON camelCase 序列化自动对齐。

---

## 九、前端改动

### 9.1 `Client/src/api/notificationApi.ts`

```ts
// channel 联合类型扩展（:70）
channel: 'dingTalk' | 'email' | 'webPush';
// 其余接口签名不变（fetchNotificationLogs/clearNotificationLogs/retryNotificationLog 路径已正确）
```

### 9.2 `Client/src/components/NotificationCenterView.vue`

1. 渠道筛选下拉（`logFilterChannel`，:69、:1606）增加 `webPush` 选项「Web 推送」。
2. 渠道标签映射（详情弹窗/表格的渠道列）补充 webPush 显示名。
3. **（D7=A）** :480 toast 文案：`'消息通知配置保存成功，系统已即时应用'` → `'消息通知配置已保存，重启后端服务后生效'`。

不改动：日志表格结构、状态筛选、统计条、重试按钮（`handleRetryLog` 用返回的 DTO 原地更新行，与新端点响应形状一致）、详情弹窗。

---


## 十、文件改动清单汇总

| #  | 文件                                                     | 改动类型 | 内容                                                 |
| -- | ------------------------------------------------------ | ---- | -------------------------------------------------- |
| 1  | `Domain/Entities/NotificationLog.cs`                   | 新增   | 实体（§3.1）                                           |
| 2  | `Infrastructure/Persistence/ScadaDbContext.cs`         | 修改   | DbSet + ToTable + 索引/长度（§3.2）                      |
| 3  | `Infrastructure/Migrations/...AddNotificationLogs`     | 新增   | 迁移（§3.3，落本地库）                                      |
| 4  | `Application/DTOs/ExternalMessage.cs`                  | 修改   | +TargetChannel / +SourceLogId（§4）                  |
| 5  | `Application/Interfaces/INotificationLogStore.cs`      | 新增   | Recorder/Service 接口 + Entry record（§5.1）           |
| 6  | `Application/Interfaces/IExternalMessageSender.cs`     | 修改   | +RecipientSummary（§6.3）；Enqueue 改返回 bool + 新增 IsChannelEnabled（§6.4，R3） |
| 7  | `Infrastructure/Communication/DingTalkRobotClient.cs`  | 修改   | 实现 RecipientSummary（1 行属性）                         |
| 8  | `Infrastructure/Communication/EmailSender.cs`          | 修改   | 实现 RecipientSummary（1 行属性）                         |
| 9  | `Infrastructure/Communication/WebPushSender.cs`        | 修改   | 实现 RecipientSummary（1 行属性）                         |
| 10 | `Infrastructure/Services/NotificationLogStore.cs`      | 新增   | Recorder（IHostedService：有界 Channel 批量落库 + 启动清扫）+ Service（查询/清空/重试含 R3 校验回置）（§5.2） |
| 11 | `WebApi/Extensions/Application.Extensions.cs`          | 修改   | Recorder 三件套注册 + Service 注册（§5.3，R1）             |
| 12 | `WebApi/HostedServices/ExternalNotificationService.cs` | 修改   | 扇出定向 + 丢弃回写（R3）+ 终态埋点 + MapChannel/MapEventType（R2）+ Enqueue/IsChannelEnabled 实现（§6.1/6.2/6.4） |
| 13 | `Infrastructure/Services/NotificationConfigService.cs` | 修改   | 测试发送落记录（§7）                                        |
| 14 | `WebApi/Controllers/NotificationConfigController.cs`   | 修改   | +3 端点 + 注入（§8）                                     |
| 15 | `Application/DTOs/NotificationConfigDtos.cs`           | 修改   | +NotificationLogDto（§8）                            |
| 16 | `Client/src/api/notificationApi.ts`                    | 修改   | channel 类型 +webPush（§9.1）                          |
| 17 | `Client/src/components/NotificationCenterView.vue`     | 修改   | 筛选选项/渠道标签 +（D7）toast 文案（§9.2）                      |

纯新增 3 个文件，其余为局部修改；不删不改任何现有表/端点行为。

---

## 十一、实施顺序与验证

### 实施顺序（按依赖排列，5 批）

1. **后端数据层**：#1 实体 → #2 DbContext → #3 迁移 + `dotnet ef database update`（WebApi 目录）
2. **后端服务层**：#4 ExternalMessage 扩展 → #5/#6 接口 → #10 Store 实现 → #7/#8/#9 RecipientSummary → #11 注册
3. **后端管线与 API**：#12 管线埋点 → #13 测试落记录 → #14/#15 控制器 + DTO
4. **前端**：#16 → #17
5. **验证**：build + 测试 + 手工清单

### 验证清单

| 项       | 方法                                                                                            | 通过标准                                       |
| ------- | --------------------------------------------------------------------------------------------- | ------------------------------------------ |
| 编译      | 后端 `dotnet build` / 前端 `npm run build`                                                        | 0 错 0 警                                    |
| 单元测试    | `dotnet test`（Recorder 更新/插入双路径与启动清扫；RetryAsync 四类 BusinessException + 队列满回置；MapEventType 全枚举含 Alarm 细分；Enqueue bool 语义与 IsChannelEnabled） | 全绿（现有 96 + 新增）                             |
| 迁移落库    | `dotnet ef database update` + `SHOW CREATE TABLE NotificationLogs`                            | 表 + TimestampUtc 索引存在                      |
| 实时投递落记录 | 启用钉钉测试发送 → 打开投递记录 Tab                                                                         | 出现 `test / dingTalk / Success / 耗时` 行      |
| 失败落记录   | 填错误 webhook 测试 → 刷新日志                                                                         | Failed 行含 error 信息                         |
| 报警 eventType（R2） | 触发一次真实报警（虚拟设备阈值）→ 查记录 | 行 eventType 为 `alarmTriggered`（恢复后为 `alarmRecovered`），非 `alarm` |
| 重试闭环    | 手工 UPDATE 一行为 Failed（或配置错误渠道发真实事件）→ 点重试                                                       | 行变 Retrying → 管线完成后变 Success/Failed，不产生重复行 |
| 定向重试    | 重试 email 失败行                                                                                  | 仅邮件渠道收到（钉钉/WebPush 不收）                     |
| 重试拒绝-渠道未启用（R3） | 禁用邮件渠道（enabled=false 且重启）→ 重试 email 失败行 | toast 提示「目标渠道当前未启用」，行保持 Failed |
| 启动清扫（R4） | 手工 INSERT 一行 Status='Retrying' → 重启后端 → 查询 | 该行变 Failed，Error=「服务重启，重试中断」 |
| 清空      | 点清空 → 刷新                                                                                      | 列表为空、计数徽标消失                                |
| 无渠道回归   | 关闭全部渠道                                                                                        | 管线短路行为不变（`_states.Count==0`，Enqueue 返回 false），不写记录不报错 |

### 风险与红线

- **采集路径安全**：记录写入全异步 + 全 try/catch，DB 故障只影响日志完整性，不影响发送/采集（§5.2 约束）。
- **EF 重试策略**：本方案无手动事务，`MySqlRetryingExecutionStrategy` 无冲突。
- **git 沙箱 quirk**：提交后须校验分支 ref（既定套路）；本方案未获批准前不动代码。

---

## 十二、P2 待勾选项（自审发现，默认不纳入，勾选后并入实施）

| # | 问题 | 建议处理 | 成本 |
|---|---|---|---|
| P2-5 | **Clear 与在途写入竞态**：用户点清空时，Recorder 内部 Channel 可能还有待写批次（清空后残留几条）；在途重试 UPDATE 命中 0 行（已由"容忍 0 行"消化） | Recorder 暴露 `DiscardPending()`，`ClearAsync` 先丢弃待写批次再 `ExecuteDeleteAsync` | 小（1 方法 + 1 行调用） |
| P2-6 | **latencyMs 语义**：当前定义为含重试退避的总耗时，最坏 ≈17s，前端「耗时」列会显示大数字 | 保持现状，在 NotificationLogDto 注释 + 本文档注明语义；或改为仅首次尝试耗时 | 零 / 小 |
| P2-7 | **Recorder 落库粒度**：v2 已改为每批一个 Scope（原稿每条一个），此项已基本消化 | 已并入 §5.2，无需再勾选 | — |
| P2-8 | **测试发送 channel 映射脆弱**：§7 用中文字符串 `"钉钉"/"邮件"` switch，依赖 SendTestAsync 参数文案 | 改为 TestDingTalkAsync/TestEmailAsync 调用点直接传 `NotificationChannel` 常量 | 小 |
| P2-9 | **前端重试后无自动刷新**：行停在 Retrying，终态需手动切 Tab 刷新 | `handleRetryLog` 成功后延迟 3-5s 调一次 `loadDeliveryLogs()` | 小（前端 1 处） |
| P2-10 | **列长度未全覆盖**：PayloadPreview 截 500 字符但未设 MaxLength（默认 longtext）；Error 无长度约束 | `PayloadPreviewHasMaxLength(512)`、`ErrorHasMaxLength(1024)`、PayloadJson 保持 longtext | 小（DbContext 2 行） |

> P2-7 已直接并入 v2（属实现正确性，无语义变化）；其余各项勾选编号回复即可（如「纳入 5/8/10」）。
