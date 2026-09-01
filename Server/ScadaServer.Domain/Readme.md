# ScadaServer.Domain 项目分析文档

> 本文档由代码逆向梳理生成，只描述代码实际内容；无法确定之处已用【缺少文件，无法确认】标注，请勿将其当作真实功能。后续可继续补充新文件，本文档将按编号进行版本更新。

***

## 0. 文档版本记录

| 版本   | 日期         | 说明                                |
| ---- | ---------- | --------------------------------- |
| v1.0 | 2026-09-01 | 首次梳理，基于 ScadaServer.Domain 现有全部源码 |

***

## 1. 项目概述

ScadaServer.Domain 是整个 SCADA 系统后端服务中**最底层、最纯粹的“领域模型层”**，属于纯 C# 类库（Class Library），**不包含任何可运行逻辑**，只承担三件事：

1. **数据模型定义**：用 EF Core 的 `[Table]` / `[Key]` / `[MaxLength]` 等特性描述数据库表结构（对标 MySQL 表）。
2. **枚举 / 常量 / 异常**：定义全系统共享的枚举、业务常量、异常类型。
3. **接口契约**：定义数据访问（Repository）与协议驱动（IProtocolDriver）的抽象接口，供上层实现。

**业务目标**：为上层业务、基础设施、运行态等模块提供一本“唯一的数据字典与领域契约”，保证全系统对设备、变量、报警、脚本等概念的描述统一，避免各层各自定义一套模型导致不一致。

**使用场景**：作为类库被其它项目（Runtime、Infrastructure、WebApi 等）引用；本项目自身不可独立运行。

> 注意：当前目录下只有 Domain 这一个项目被分析。它只看到“数据长什么样（实体）”，不看到“数据怎么流转（服务逻辑）”。交互逻辑、启动、配置等均在其它项目。【缺少文件，无法确认】各项目间引用关系与上层实现细节。

***

## 2. 技术栈清单

| 类别         | 内容                                                                                                 |
| ---------- | -------------------------------------------------------------------------------------------------- |
| 目标框架       | .NET 8.0（`net8.0`），`Nullable enable` + `ImplicitUsings enable`                                     |
| 语言         | C#                                                                                                 |
| 数据持久化      | EF Core 实体（仅通过 DataAnnotations 标注，未引用 EF Core 包）；同时存在 `IRepository<T>` 抽象，未限定具体 ORM                |
| 外部 NuGet 包 | **无**。`ScadaServer.Domain.csproj` 未声明任何 `<PackageReference>`，也未引用任何 `<ProjectReference>`           |
| 用到的内置命名空间  | `System.ComponentModel.DataAnnotations`（表/键/字段长度约束）、`System.ComponentModel.DataAnnotations.Schema` |
| 关联概念       | 通信协议 S7 / OPC UA / Virtual 等在 `Enums/DeviceType.cs`、`Entities/Protocol.cs` 中以枚举/实体形式体现；本体不实现任何实际通信 |

> 关键点：这是一个**零第三方依赖**的“纯模型 + 契约”项目，只有框架自带的 BCL 注解参与。这一设计保证了它可作为被所有人依赖的最底层模型库。

***

## 3. 目录结构解析

根目录下共 4 个源码子目录 + 1 个项目文件，外加 `bin/`、`obj/`（编译产物，忽略）。

```
ScadaServer.Domain/
├── ScadaServer.Domain.csproj   # 项目文件（net8.0，无依赖）
├── Constants/                  # 业务常量（当前仅有 SystemRoles）
├── Entities/                   # 核心业务：数据库实体（29 个类）
├── Enums/                      # 核心业务：系统枚举/常量集合（13 个文件）
├── Exceptions/                 # 领域异常
└── Interfaces/
    ├── IProtocolDriver.cs      # 协议驱动契约
    ├── IRuntimeDevice.cs       # 设备运行时只读视图
    ├── IRuntimeVariable.cs     # 变量运行时只读视图
    └── Repositories/           # 数据访问层契约（IRepository + 各实体仓储）
```

**哪些是核心业务，哪些是工具/配置/静态资源：**

- **核心业务**：`Entities/`（数据模型）、`Enums/`（业务枚举）、`Interfaces/`（领域契约）—— 这三块是领域知识的载体。

- **工具/辅助**：`Constants/SystemRoles.cs`（角色常量）、`Exceptions/`（异常类型）—— 偏基础设施但仍属领域共享定义。

- **静态资源/配置**：目录内不存在任何资源文件或配置文件；`bin/`、`obj/` 为编译产物。

**Entities 明细（29 个）：**

| 实体                      | 表名                     | 用途归类                       |
| ----------------------- | ---------------------- | -------------------------- |
| `EntityBase`            | -（抽象）                  | 实体基类，提供 `int Id` 主键        |
| `Device`                | Devices                | 物理设备实例，主业务对象之一             |
| `DeviceVariable`        | DeviceVariables        | 变量在具体设备上的“实例化”（地址/轮询/缩放覆盖） |
| `DeviceConfig`          | DeviceConfigs          | 设备的协议配置（JSON 文本，1:1）       |
| `DeviceRuntime`         | -（不落库）                 | 设备运行时状态，仅内存维护              |
| `Area`                  | Areas                  | 区域分组                       |
| `DataModel`             | DataModels             | 设备型号模型，绑定 `Protocol`       |
| `ModelVariable`         | ModelVariables         | 变量模板（“变量是什么”）              |
| `Protocol`              | Protocols              | 通信协议定义（S7/OPC UA/Virtual…） |
| `Sensor`                | Sensors                | 传感器                        |
| `DataConversion`        | DataConversions        | 变量间数据转发                    |
| `AlarmRule`             | AlarmRules             | 报警规则                       |
| `AlarmRecord`           | AlarmRecords           | 报警流水（long 主键）              |
| `LinkageRule`           | LinkageRules           | 联动规则                       |
| `VariableHistory`       | VariableHistory        | 历史时序点（long 主键）             |
| `VariableRealtime`      | VariableRealtime       | 实时快照（复合主键）                 |
| `ExposedInterface`      | ExposedInterfaces      | 对外暴露接口                     |
| `MqttServer`            | MqttServers            | MQTT 服务器配置                 |
| `MqttVariableConfig`    | MqttVariableConfigs    | MQTT 变量发布配置                |
| `ScadaProject`          | ScadaProjects          | SCADA 工程                   |
| `ScadaPage`             | ScadaPages             | 工程页面                       |
| `HmiComponent`          | HmiComponents          | 页面内的可拖拽组件                  |
| `ScheduledTask`         | ScheduledTasks         | 定时任务                       |
| `SystemScript`          | SystemScripts          | 系统脚本（Jint 执行）              |
| `ScriptExecutionRecord` | ScriptExecutionRecords | 脚本执行记录（long 主键）            |
| `SystemUser`            | SystemUsers            | 系统用户                       |
| `SystemConfig`          | SystemConfig           | 系统配置                       |
| `SystemLog`             | SystemLogs             | 统一日志（运行/操作/安全）             |
| `ConfigLog`             | ConfigLog              | 设备配置变更日志                   |
| `DatabaseConfig`        | DatabaseConfigs        | 数据库连接配置（MySQL/InfluxDB 等）  |
| `DbVersion`             | DbVersion              | 数据库迁移版本                    |

**Interfaces/Repositories 明细（仓储契约）：** 见第 4 节。

***

## 4. 整体架构 & 分层说明

Domain 是经典分层架构中的**最底层**。整个后端大致呈如下依赖方向（基于注释与接口推断，具体实现文件不在本项目内）：

```text
WebApi（控制器/启动）          ← 最外层，负责 HTTP 入口
  ↓ 调用
Service / Runtime / Application（业务逻辑、采集调度、驱动工厂）
  ↓ 依赖契约
Infrastructure（驱动 S7/OPC UA/Virtual、EF Core 仓储实现）  ← 实现 Domain 接口
  ↓
Domain（本层：实体 + 枚举 + 接口）  ← 最底层，被所有人依赖，不依赖任何人
```

**各层职责（推理归纳，仅基于本项目能看到的部分）：**

- **Domain（本层）**：

  - `Entities`：对应 MySQL 表结构的数据载体。

  - `Enums`/`Constants`：值域的唯一事实来源，避免字符串散落。

  - `Interfaces/Repositories`：定义“数据怎么存取”的抽象，隔离具体数据库实现（MySQL/EF Core）。

  - `Interfaces/IProtocolDriver` + `IRuntimeDevice`/`IRuntimeVariable`：定义“设备怎么通信”的抽象，并把领域实体与驱动解耦。

**禁止跨层逻辑说明：**

- 注释中反复强调一条硬约束：**驱动不允许知道** **`DataModel`** **/** **`ModelVariable`**。驱动只能通过 `IRuntimeDevice` / `IRuntimeVariable`（运行时导出，且值来自 `DeviceVariable` 实例配置）获取地址与数据类型，不得触碰模板实体。这是 Domain 注释里明确写出的分层纪律。

- Domain 的接口注释还说明了该层刻意保持“依赖方向单向”：`Domain ← Infrastructure(驱动) ← Runtime(实现)`，以避免 Infrastructure 与 Runtime 形成循环引用。

***

## 5. 核心业务数据流

> 说明：本层只提供“数据模型”与“契约”，真正的流转逻辑在其它项目。以下按**数据建模视角**描述系统概念上会发生的流向，具体落地以对应实现为准。

**5.1 设备与变量建模流（配置侧）**

```text
Protocol（通信协议定义）
   ↑ 1:N 绑定
DataModel（设备型号，含 ModelVariable 变量模板）
   ↑ 1:N 实例化
Device（物理设备实例，挂在 Area 区域下）
   ├─ 1:1 DeviceConfig（协议连接配置 JSON，如 S7 的 IP/Rack/Slot）
   └─ 1:N DeviceVariable（变量在设备上的实现：地址/位偏移/轮询/缩放/死区/权限覆盖）
```

模型层面“模板→实例”清晰分层：`ModelVariable` 描述变量“是什么”，`DeviceVariable` 描述它在某一台设备上“怎么实现”，未覆写的字段回退到模板值。

**5.2 采集-实时-历史流（运行侧，概念层面）**

```text
设备(物理) ⇄ 驱动(IProtocolDriver) ⇄ 运行时(RuntimeDevice/RuntimeVariable)
   → VariableRealtime（实时快照，MySQL，按周期批量 Upsert，复合主键 DevceId+VariableKey）
   → VariableHistory（历史时序点，按 StoreMode 策略写入）
   → AlarmRecord（报警规则 / MinMax 兜底 命中后落库）
   → MqttServer/MqttVariableConfig（对外发布）
```

其中：

- `VariableHistory` 实体注释声明它是“查询端按 VariableKey+时间倒序取最近 N 条供趋势曲线展示”，对应仓储 `IVariableHistoryRepository.GetLatestAsync`。

- `VariableRealtime` 注释说明使实时值具备持久化能力、重启可恢复，与“实时库使用 MySQL”的目标对齐。

> 注：项目记忆提到“实时业务数据用 MySQL、历史时序数据用 InfluxDB”，但本项目 `Entities` 里历史体是 `VariableHistory`（表名 VariableHistory，MySQL 语义）。InfluxDB 的实际接入、`DatabaseConfig` 与 Influx 的映射在哪一层完成，【缺少文件，无法确认】。

**5.3 报警流**

```text
AlarmRule（规则）  → 运行时告警检测引擎（不在此项目）
   → AlarmRecord（Source=Rule 或 MinMaxLimit；带防抖 DebounceSeconds）
   → 前端查询未确认/未恢复列表 → Ack / Recover 回写
```

`AlarmRecord` 区分触发/恢复/确认三态（TriggeredAt / RecoveredAt / AckedAt+By），由 `IAlarmRecordRepository` 契约支持按复合条件查询、关联恢复（`FindUnrecoveredIdAsync`）、按设备兜底恢复（`RecoverByDeviceAsync`）。

**5.4 脚本 / 定时任务流**

```text
SystemScript（代码 + 触发类型/间隔/Cron + 读写授权域 + 熔断字段）
   → 脚本引擎（Jint 沙箱，不在此项目）
   → ScriptExecutionRecord（每次执行一条审计记录，long 主键）
   ↔ ScheduledTask（四类任务：set_value / backup / execute_script / clear_history，Cron 调度）
```

**5.5 数采对外输出流**

```text
DataConversion（源设备.源变量 → 目标设备.目标变量，变量间转发）
ExposedInterface（RouteUrl + RequestMethod + 设备，对外暴露 HTTP 接口）
MqttServer + MqttVariableConfig（待发布变量 + 主题/别名）
```

***

## 6. 核心入口文件、关键类说明

- 本项目**没有启动入口、没有 Program/Main、没有配置加载**（纯类库，不包含可执行入口）。【缺少文件，无法确认】启动入口位于外部项目。

- **公共契约类**（本项目最重要的可复用定义）：

  - `IRepository<TDomain, TKey>`：通用仓储 CRUD 抽象，含 GetById / GetList / GetPaged / Count / Any / Insert / Update / Delete 及 Range 批量。

  - `IAlarmRuleRepository` … `IDeviceVariableRepository` 等一组具体仓储：多数为空接口（仅继承 `IRepository`），少数扩展了专属方法（日志分页/清理、脚本执行记录分页/清理、历史游标拉取、实时快照查询、报警记录复合查询）。

  - `IProtocolDriver`：协议驱动契约（Connect / Read / ReadBatch / Write / Subscribe / Unsubscribe / Disconnect，IAsyncDisposable）。

  - `IRuntimeDevice`：设备运行时只读视图（Id / Key / ConfigJson / Variables）。

  - `IRuntimeVariable`：变量运行时只读视图（Key / Name / DataType / Unit / Min / Max / Address / BitOffset / PollingIntervalMs / 缩放 / 死区 / 使能 / 只读）。

  - `IAlarmRecordRepository`、`IVariableHistoryRepository`、`IVariableRealtimeRepository`、`ISystemLogRepository`、`IScriptExecutionRecordRepository`：承载查询/清理等定制方法的仓储。

- **异常类型**：

  - `BusinessException`：业务异常，含 `StatusCode`（默认 400）与 `Errors`，用于向上返回 HTTP 语义错误。

  - `DeviceNotFoundException`：设备不存在，按设备 ID 拼装消息。

- **常量与值域**：

  - `SystemRoles`：Admin / Operator / Viewer 三角色，注释强调角色以字符串存库并对齐 JWT 的 role claim，故用 `const string` 而非枚举。

  - `ScheduledTaskTypes`：四类定时任务类型白名单。

  - `StoreModeEnum`、`TriggerConditionEnum`、`AlarmLevelEnum`、`LinkageActionEnum`、`ScriptTriggerType`、`ScriptTriggerSource`、`ScriptExecutionResult`、`VariableType`、`VariableQuality`、`UpdateMode`、`DeviceStatus`、`DeviceConnectionState`：值域唯一事实来源。

  - `DeviceType`：声明了可已实现的驱动集合（含 `DeviceTypeExtensions.IsDriverImplemented` 与按驱动键判断的 `ProtocolDriverSupport.IsDriverImplemented`），注释声明仅 **S7 / OPC UA / Virtual** 有可用驱动，其余会在运行时抛 `NotSupportedException`。

***

## 7. 外部依赖 & 第三方交互

本项目本体**不主动对接任何外部系统**（无网络、无数据库、无设备通信实现），它只是为这些交互提供“数据形态”与“接口契约”。但从实体定义可看出系统整体会与以下外部对象交互（具体实现/超时/重连在其它项目）：

- **PLC / 现场设备**：`Protocol`（S7 / OPC UA / Virtual…）+ `IProtocolDriver`。连接参数存在 `DeviceConfig.JsonConfig`（如 IP/Rack/Slot）。

- **MQTT Broker**：`MqttServer`（BrokerUrl / Port / 用户名密码 / TopicPrefix / ClientId）+ `MqttVariableConfig`（变量级发布配置）。

- **数据库**：`DatabaseConfig`（MySQL / InfluxDB 2.x 相关字段：Type / BackendType / Host / Port / Username / Password / DatabaseName / Token / Org / Bucket / IsActive / 测试状态）。本项目只定义数据形态，不做连接。

- **前端 SCADA 组态**：`ScadaProject` / `ScadaPage` / `HmiComponent`，页面结构以 JSON 字段（BackgroundJson / LayersJson / PropsJson）沉淀，供前端序列化还原。

> 超时、重连、鉴权、驱动工厂派发等行为不在本项目内。【缺少文件，无法确认】。

***

## 8. 潜在问题与优化建议

> 分两类：【必须修复阻断问题】、【可选优化】。均基于本项目源码判断，不涉及外部项目。

### 【必须修复阻断问题】

1. **凭证明文存储高风险**
   `MqttServer.Username`/`Password`（MqttServer.cs）、`DatabaseConfig.Username`/`Password`/`Token`（DatabaseConfig.cs）直接作为普通字符串列。若相接 DB / MQTT 落库均为明文，等于把数据库连接口令和 MQTT 口令以明文写进业务库，一旦库被拖库即泄露敏感凭据。【阻断】建议：至少对 Password/Token 落库前加密（对称加密 + 主密钥），或改为密文/引用外部密钥管理，且明确禁止写入日志与备份。

2. **`DeviceConfig.JsonConfig`** **无任何结构校验**
   连接配置是纯 JSON 文本（longtext），领域层无 schema 或 DTO 约束，驱动需自行反序列化。错误配置要到运行期连设备才暴露。建议在驱动层或配置层提供校验。

3. **文档/代码边界自相矛盾（抗混淆提示）**
   `DeviceVariable` 注释声称 `ModelVariable.Address` / `BitOffset` / `PollingIntervalMs` 已标记 `[Obsolete]` 待移除，但 `ModelVariable` 实体中**实际上已不存在这三个字段**（Address/BitOffset/PollingIntervalMs 已被彻底删除，仅剩 ScaleSlope/ScaleOffset/DeadBand/IsReadOnly）。注释滞后于代码。建议清理这些“迁入”注释，避免误导后续维护者以为还有回退路径。

### 【可选优化】

1. **主键约定不统一**
   大多数实体用 `int Id`（继承 `EntityBase`），但报警/历史/脚本执行记录等数据量大的表独立用 `long Id`。这是有意为之（注释说明 long 支撑更长时间维度），但 DAO/查询需同时适配两种 Key 类型（`IRepository<T,int>` 与 `IRepository<T,long>`）。建议在文档中显式沉淀这一约定，避免新人困惑。

2. **实体继承建议统一**
   `AlarmRecord/VariableHistory/ScriptExecutionRecord/SystemScript/DbVersion` 未继承 `EntityBase`。现有结构可用，但推荐要么都继承、要么都显式声明主键，保持一致性。

3. **时间语义注释不一致**
   `SystemLog.Timestamp` 注释写“服务器本地时间”，而全系统项目记忆约定“时间统一用 UTC”。若不统一，日志时间会成为 8 小时偏移重灾区。至少应让注释与约定一致，并在生产明确时间口径。

4. **多个 JSON 字段缺少结构化模型**
   `ExtensionData`（Dictionary）、`PropsJson`、`LayersJson`、`BackgroundJson`、`ParamsJson`、`ConfigJson` 均为裸 JSON 字符串/字典。可复用于不同类型实体，但不利于栅格校验与版本演进。可在上游抽象一个“JSON 字段 + 名称/版本”的封装，作为可选优化。

5. **`Sensor`** **实体与设备变量概念有重叠**
   `Sensor` 既含 DeviceId / VariableKey 又含最后采集值 LastValue/LastUpdateTime，与 `DeviceVariable`/`VariableRealtime` 职责部分重叠。建议确认其真身场景，避免多人维护时出现多份实时值真相。

6. **`DataConversion`** **/** **`LinkageRule`** **已非空接口但仅串字符键**
   `SourceVariableKey`/`TargetVariableKey`/`LinkageVariableKey` 均为字符串键；结合项目记忆“变量全局唯一由 DeviceKey+VariableKey 决定”，仅存 VariableKey 可能不足以唯一定位跨设备变量。建议确认是否需补充设备键维度。

***

## 9. 新手阅读顺序建议

按照“先看骨架 → 再看设备主线 → 再看报警/脚本 → 再看契约”的顺序，最快建立整体认知：

1. **先看** `Entities/EntityBase.cs` + `ScadaServer.Domain.csproj`：理解“这是纯模型层、零依赖、int 自增主键”的地基。
2. **再看** `Entities/Protocol.cs`、`DataModel.cs`、`ModelVariable.cs`、`Device.cs`、`DeviceVariable.cs`：这是系统的绝对主链路——协议→型号→模板→设备实例→变量实例，先懂这套关系等于懂了半个系统。
3. **然后看** `Enums/` 全部：把值域过一遍（尤其 `StoreModeEnum`、`TriggerConditionEnum`、`AlarmLevelEnum`、`ScriptTriggerType`），因为很多注释和远端逻辑都以这些枚举为锚点。
4. **接着看** `Entities/AlarmRecord.cs` + `AlarmRule.cs` + `LinkageRule.cs`：理解报警/联动的三态语义。
5. **再看** `Entities/SystemScript.cs` + `ScriptExecutionRecord.cs` + `ScheduledTask.cs`：理解脚本/定时任务这套扩展能力的数据支撑。
6. **最后读** `Interfaces/`（`IProtocolDriver`、`IRuntimeDevice`、`IRuntimeVariable`、`Repositories/*`）：这些是连接其它项目的“接口”，读完即知道 Domain 为上层提供哪些能力点，转而就可以去看 Runtime/Infrastructure 实现。

> 提示：本项目没有可运行逻辑，单独读它只能得到“数据字典”。强烈建议看完本层后，紧接着去读 Infrastructure（仓储实现 + S7/OPC UA/Virtual 驱动）与 Runtime（采集/报警/脚本执行），才能真正理解系统如何动起来。

***

## 10. 后续可扩展方向（可选）

本层作为领域模型层，天然适合以下演进（均不改变现有实体，多为补充性增强）：

1. **主键/DTO 形态统一**：为所有实体补齐统一的 Audit 字段（CreatedAt/UpdatedAt/By）基底。
2. **JSON 字段强类型化**：将 `PropsJson/LayersJson/BackgroundJson/ConfigJson/ParamsJson` 收敛为带版本的强类型配置对象，降低运行期解析出错概率。
3. **枚举值域补强**：为 `IEnumerable<Enum>` 提供统一的“展示名/颜色/是否用于告警”元数据，避免前端硬编码。
4. **领域校验**：在 Domain 增加轻量输入校验（如 StoreIntervalMs 下限、Cron 表达式合法性、CronExpression 时区），把边界检查前移。
5. **凭证字段加密约定**：在领域层明确 Password/Token 字段的落库加密规则，从模型层面杜绝明文。

***

## 附：如何继续更新本文档

如需追加或更新，请按如下方式保持版本连贯：

- 新增文件时，把 `bin/`、`obj/` 等编译产物排除，提供源码即可。

- 更新时注明新增内容对应第几节，并递增“0. 文档版本记录”中的版本号与日期。

- 对本文档“已标注【缺少文件，无法确认】”的条目，若有对应代码/配置可补齐，会一并修订，去掉占位标注。

