# 通知配置「保存即生效」改造方案（企业微信报警不推送根因修复）

> 版本：v1（2026-09-10）· 状态：方案定稿，待实施 · 分支：`feat/wecom-robot-notification`

## 1. 背景与目标

**现象**：变量值超限报警在报警记录中有记录，但企业微信群机器人没有收到推送。

**根因（一句话）**：通知发送链路（渠道发送器 + 外部通知服务 + 装饰器）全部通过 `IOptions<NotificationOptions>` 读取**进程启动时的配置快照**，而配置保存只把新值写进了 `appsettings.dboverride.json` 文件、从未刷新这些快照。因此「保存企微渠道并启用」后，运行中的进程仍认为企微未启用，报警事件被「无可用渠道」短路丢弃。

**目标**：让通知配置在保存后**无需重启服务**即可对发送链路生效——至少覆盖「渠道开关 / webhook」与「推送模板 / Push* 策略开关」。

**前置依赖**：`Program.cs` 中 `appsettings.dboverride.json` 的 `reloadOnChange` 已在上一次修复（commit `f210186`）中改为 `true`，本方案不再重复，但作为前提条件列于 §6。

## 2. 关键决策

在三个候选方案中选定 **方案 2-S（发送器动态化 + 管道全量保留）**：

| 方案 | 思路 | 结论 |
|---|---|---|
| 方案 1 | 保持重启生效，仅补「重启后生效」提示 | 治标，弃用 |
| 方案 2 | 重建 `_states` 数组（volatile 替换）+ 消费循环生命周期管理 | 复杂，有循环泄漏/StopAsync 等不到旧循环的风险，弃用 |
| **方案 2-S** | sender 改 `IOptionsMonitor` 让 `Enabled`/webhook 动态化；`_states` **全量保留**（不再按 `Enabled` 过滤），扇出时动态跳过未启用渠道 | **选定**：零 Channel 重建、零并发风险、改动最小 |

核心洞察：`_states` 目前由 `senders.Where(s => s.Enabled)` 过滤构建——这是「静态拓扑」的根源。既然 sender 用 `IOptionsMonitor` 后 `Enabled` 已动态，就**没必要重建管道集合**，只需永远保留全部 sender，把「是否启用」的判断下放到「扇出投递时」动态完成。

## 3. 改动范围总表（8 个文件，全部后端）

| # | 文件 | 改动类型 | 阶段 |
|---|---|---|---|
| 1 | `Infrastructure/Communication/DingTalkRobotClient.cs` | `IOptions`→`IOptionsMonitor`，`Opt` 动态属性 | 一 |
| 2 | `Infrastructure/Communication/WeComRobotClient.cs` | 同上 | 一 |
| 3 | `Infrastructure/Communication/EmailSender.cs` | 同上（含 `RecipientSummary`） | 一 |
| 4 | `Infrastructure/Communication/WebPushSender.cs` | 同上（含 VAPID/级别过滤） | 一 |
| 5 | `WebApi/HostedServices/ExternalNotificationService.cs` | 管道全量 + 动态标志 + `Push` 动态 | 二 |
| 6 | `WebApi/Services/ExternalNotificationDecorator.cs` | `Policy`/`Templates` 动态属性 | 三 |
| 7 | `WebApi/Extensions/Application.Extensions.cs` | Decorator 注册处 IOptions→IOptionsMonitor | 四 |
| 8 | （前置）`WebApi/Program.cs` | 已在上一次修复完成 `reloadOnChange:true` | 已完成 |

前端 **零改动**。

## 4. 阶段与步骤总览

| 阶段 | 步骤 | 内容 | 作用 |
|---|---|---|---|
| 一：发送器动态化 | 1–4 | 四个 sender 改 `IOptionsMonitor` | 渠道开关/webhook 即时生效 |
| 二：管道动态化 | 5–6 | `ExternalNotificationService` 全量 `_states` + 动态 `Enabled`/`Push` | 无需重建管道即可动态启用/禁用渠道 |
| 三：装饰器动态化 | 7 | `ExternalNotificationDecorator` 动态 `Policy`/`Templates` | 模板/Push* 策略开关即时生效 |
| 四：DI 装配 | 8 | `Application.Extensions.cs` 注册调整 | 让装饰器拿到 `IOptionsMonitor` |
| 五：验证回归 | 9–11 | 编译 + typecheck + 手工回归 | 门禁与验收 |

## 5. 生效边界（即时生效 vs 重启生效）

改造后配置项按下表分层生效：

| 生效方式 | 配置项 | 载体 |
|---|---|---|
| **即时生效** | 各渠道 `Enabled` + webhook/SMTP/VAPID | sender（`IOptionsMonitor`） |
| **即时生效** | `PushAlarm` / `PushDeviceOffline` / `PushDeviceOnline` / `PushSystemAlarm` / `PushSystemError` / `PushScript` / `DeviceStatusDebounceMinutes` | Decorator（`Policy` 动态） |
| **即时生效** | 模板 `Templates`（钉钉/企微/邮件正文） | Decorator（`Templates` 动态） |
| **即时生效** | WebPush `MinSeverity` / `PushRecover` / `MaxConcurrentSends` | WebPushSender（`Opt` 动态） |
| **重启生效** | `QueueCapacity` / `MaxPerMinutePerChannel`（构造 Channel/RateBucket，构造期固定） | ExternalNotificationService 构造快照 |
| **即时生效** | `MaxAttempts` / `RetryBaseDelayMs`（每次发送时读取 `Push`） | ExternalNotificationService（`Push` 动态） |

## 6. 前置依赖（已完成，勿重复）

`Server/ScadaServer.WebApi/Program.cs` 中 `appsettings.dboverride.json` 的加载已带 `reloadOnChange: true`（commit `f210186`）。这是本方案 `IOptionsMonitor` 能感知文件变更的前提。

## 7. 文档索引

| 文档 | 内容 |
|---|---|
| [01-问题与方案审查.md](./01-问题与方案审查.md) | 根因链路、三方案对比、选定理由 |
| [02-详细设计.md](./02-详细设计.md) | 8 文件逐项 before/after 代码与注意点 |
| [03-执行计划.md](./03-执行计划.md) | 5 阶段 11 步，每步任务 + 验证 |
| [04-测试与验收.md](./04-测试与验收.md) | 验证清单、回归、风险与回滚 |