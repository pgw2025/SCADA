# 连接级单例共享改造（DeviceConnection → 一条物理连接，多设备共享）

> 状态：**方案已定稿，未开始实施**。本文档组是完整的设计与执行计划，实施时按 [02-执行计划.md](02-执行计划.md) 逐步推进。

## 一句话目标

把当前「每个启用设备各建一条物理 PLC 连接」改为「每个 `DeviceConnection`（连接配置）只建一条物理连接，其下挂载的多台设备共享」，在不改动设备级采集/报警/历史/推送语义的前提下完成。

## 现状与差距

- 数据层已天然支持共享：`Device.ConnectionId` → `DeviceConnection` 是多对一外键，多设备可引用同一连接配置（`DeviceConnectionAppService` 注释亦明确「可能与其他设备共享」）。
- 运行时是瓶颈：`RuntimeManager.BuildAndRegisterDeviceAsync` 对**每个设备**执行 `CreateDriver + ConnectAsync`，即使两台设备共用同一连接配置，也会建立两条 TCP/OPC UA 会话。
- 调度、快照、历史、报警、SignalR 推送全部按设备粒度组织——**这些全部保留不动**，只共享驱动实例。

## 修正后的核心思路（相对最初草案）

最初草案的「调度上移、共享 Worker」经代码审查判定为**过度设计**：驱动本来就不轮询（轮询在 `DeviceWorker`），且 Worker 内嵌大量设备级业务状态（报警状态机、死区、防抖）。修正后的方案是：

> **只共享 Driver 实例。每设备 Worker 照常轮询自己的变量，只是调用会话里的同一个驱动。**
> `DeviceWorker` / `DeviceScheduler` 结构不动；重连归口到会话级；批读（`ReadBatchAsync`）作为吞吐补偿与共享绑定交付。

## 文档索引

| 文档 | 内容 |
|---|---|
| [01-方案审查与修正.md](01-方案审查与修正.md) | 最初草案的审查结论（1 处过设计 + 3 个缺口）、逐条证据、12 条关键决策（D1～D12） |
| [02-执行计划.md](02-执行计划.md) | 6 阶段 23 步的执行计划，每步任务与验收标准 |
| [03-详细设计.md](03-详细设计.md) | `IRuntimeConnection` / `ConnectionSession` / 会话级重连 / 生命周期 / 写入门禁 / 批读的详细设计与代码草图 |
| [04-风险与回归验收.md](04-风险与回归验收.md) | 风险表（R1～R10）、回归矩阵（既有 N/R 基线 + 新增 S1～S8 共享场景） |

## 关键结论速览

1. **无数据库迁移**：`DeviceConnection` 已具备全部所需字段（`ConfigJson`/`IsEnabled`/`ReconnectIntervalMs`），本改造纯运行时层。
2. **改动面收敛**：实际驱动只有 3 个（S7 / OpcUa / Virtual；Modbus、MQTT 工厂抛 `NotSupportedException`），且它们对 `IRuntimeDevice` 的使用面只有 `ConfigJson` + `Key/Id`（日志/上下文），签名切换干净。
3. **OPC UA 订阅路径是休眠代码**：`SubscribeAsync/UnsubscribeAsync` 全仓无运行时调用方，本改造不触碰。
4. **批读必须绑定交付**：Worker 现状是逐变量一次网络往返，共享后 N 台设备挤一条连接，不启用 `ReadBatchAsync` 会直接破坏高采集率场景（吞吐悬崖，详见 01 文档问题 D）。
5. **连接级配置首次生效**：`DeviceConnection.IsEnabled` 与 `ReconnectIntervalMs` 目前运行时根本没有消费，会话层是让它们真正生效的正确位置。

## 阶段总览

| 阶段 | 内容 | 依赖 |
|---|---|---|
| P0 | 基线与准备（分支/构建基线/备份/影响面审计） | — |
| P1 | `IRuntimeConnection` 接口层（行为不变的签名切换） | P0 |
| P2 | `ConnectionSession` 会话层（驱动共享 + 引用计数生命周期） | P1 |
| P3 | 会话级重连归口（信号上抛/探测/去重重建） | P2 |
| P4 | 写入门禁与配置热更新 | P3 |
| P5 | 批读性能补偿（可独立先行） | 可与 P2 并行 |
| P6 | 清理与全量回归验收 | P1～P5 |
