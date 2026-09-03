# 数据库实体差异分析：`docs/数据库修改想法.md`（设计稿） vs 项目当前实现

> 分析时间：2026-09-03
> 范围：仅对比 `docs/数据库修改想法.md` 中提出的 11+1（Area）张核心表与 `ScadaServer.Domain/Entities` 中实际落地、且已在 `ScadaDbContext` 注册的实体。
> 只做差异分析，不含任何代码/库表改动建议的执行。

---

## 一、两套模型总览与对应关系

| # | 设计文档（数据库修改想法.md） | 当前项目实现（表名） | 对应实体文件 |
|---|---|---|---|
| 1 | Device | `Devices` | Device.cs |
| 2 | Controller | `Controllers` | Controller.cs |
| 3 | DeviceConnection | `DeviceConnections` | DeviceConnection.cs |
| 4 | DataModel | `DataModels` | DataModel.cs |
| 5 | DataPoint | `DataPoints` | DataPoint.cs |
| 6 | DataPointMapping | `DataPointMappings` | DataPointMapping.cs |
| 7 | DeviceDataModel | `DeviceDataModels` | DeviceDataModel.cs |
| 8 | DeviceDataValue（最新值快照） | `VariableRealtime` | VariableRealtime.cs |
| 9 | DataHistory（历史数据） | `VariableHistory` | VariableHistory.cs |
| 10 | AlarmRecord | `AlarmRecords` | AlarmRecord.cs |
| 11 | Area | `Areas` | Area.cs |
| — | （无独立协议表，字符串字段） | `Protocols`（项目新增，见差异 ②） | Protocol.cs |

结论：**设计稿的 12 张表在项目中全部有对应落地**，但表名从文档示例（单数 `Device` / 下划线 `data_point`，SqlSugar 风格）统一为了 EF Core 复数 PascalCase 风格。

---

## 二、贯穿多张表的结构性差异（先读这部分）

1. **Device 不再承载"类型"**：设计稿 `Device.DeviceType varchar`（Conveyor/Mixer…）在项目中消失。"设备是什么型号"由 `Device.ModelId → DataModel`（含 Vendor/ModelName）承担；"怎么通信"由 `DataModel.ProtocolId / Device.ConnectionId → DeviceConnection` 承担。
2. **协议从自由字符串升级为独立 `Protocols` 表**：设计稿中 `Controller.ControllerType varchar`、`DeviceConnection.Protocol varchar` 都是自由字符串；项目改为 `Protocols` 表（Key/Name/DriverKey），`Controller.ProtocolId`、`DeviceConnection.ProtocolId`、`DataModel.ProtocolId` 三处全部 FK 引用，驱动按 `Protocol.DriverKey` 派发。
3. **设备编码 Code → 自动生成的唯一 `Key`**：设计稿 `Device.Code` 为手工业务编码；项目用 `Device.Key`（区域 Code 前缀自动生成、全局唯一，`ix_device_key` 唯一索引）承担，运行时按 Key 快速查找。
4. **组织层级统一收敛到 Area 树**：设计稿自己建议"弱化 Location、引入 AreaId"；项目直接落地——`Device` 无 `Location`、无设备树 `ParentId`、无 `Sort/Status` 列，只有 `AreaId`。设备父子树（如"输送机01 下挂子部件"）在项目中尚无对应结构。
5. **模板/实例职责拆分得更彻底（DataPoint 1:N DataPointMapping）**：设计稿中地址、缩放、周期全部平铺在 `DataPoint` 与 `DataPointMapping` 两张表；项目把"地址/位偏移/轮询/缩放/死区/读写权限"都设计成**设备实例上可空、可覆盖、缺省回退模板**的两层模型，模板字段（DataPoint.Address 等）标记 Obsolete 待移除。
6. **实时/历史值的存储策略更具体**：设计稿快照表 `DeviceDataValue` 用自增 Id + text 值 + 冗余 DataType；项目用 `(DeviceId, VariableKey)` 复合主键 + `double Value + RawValue` 双轨 + 业务键/名称冗余 + 字符串 Quality。写入频率策略由 `DataPoint.StoreMode / StoreIntervalMs` 控制（设计稿仅提"不要每读一次就 UPDATE"）。
7. **报警从"静态记录"演进为"规则引擎事件流"**：设计稿 `AlarmRecord` 只有 Active/Recovered 一个 Status；项目拆分出 `AlarmRules` / `LinkageRules` 两张规则表（设计稿没有），`AlarmRecord` 记录命中的 RuleId，并把"恢复"与"人工确认"拆成两个正交状态（Acked 体系）。
8. **时间戳风格**：部分项目实体（DataPoint、DataPointMapping）**没有** CreatedAt/UpdatedAt，设计稿每张表都带这两个审计列；项目实体统一 `DateTime.UtcNow` UTC 存储。

---

## 三、逐表字段差异明细

### 3.1 Device（`Devices`）

| 设计稿字段 | 当前项目 | 差异类型 | 用途/说明 |
|---|---|---|---|
| Id bigint | Id int | 类型 | 项目全库业务主键用 int（AlarmRecord/VariableHistory 等大表用 long） |
| Code（设备编码） | **Key** | 更名+语义 | 自动生成、全局唯一（区域 Code 前缀），供运行时快速查找；设计稿的手工业务编码未采用 |
| DeviceType varchar | —（无） | 删除 | "型号"归 DataModel（Vendor/ModelName），"协议"归 Protocol/连接，避免 Device 再存冗余类型串 |
| Description | —（无） | 删除 | 设备描述由所引模型与区域承载，未单独落列 |
| ControllerId | ControllerId int?（可空 FK） | 保留但可空 | 阶段 3 过渡列，未回填/手工场景可为 NULL |
| ParentId（父设备） | —（无） | 删除 | 设备树未实现；组织层级统一走 Area 树（设计稿第九节也主张区分两种 ParentId） |
| Location | —（无，改 AreaId） | 删除 | 按设计稿自己的建议删除，位置归属改由 AreaId 表达 |
| Enabled | IsEnabled | 更名 | 项目统一 Is* 命名 |
| Status int | LastKnownStatus（DeviceStatus?）+ 运行时 DeviceRuntime | 更名+策略 | 不落静态状态列；运行时状态在内存，仅将"最后一次已知状态"持久化供重启恢复展示 |
| Sort | —（无） | 删除 | 设备排序未实现（区域树内已有 Sort） |
| —（设计稿没有） | **Name** | 项目独有 | 设计稿示例有 Name 但字段表漏列；设备名称 |
| —（设计稿没有） | **ModelId**（FK DataModels） | 项目独有 | 主数据模型，回答"这台设备是什么型号、跑哪套变量模板" |
| —（设计稿没有） | **ConnectionId**（FK DeviceConnections） | 项目独有 | 设备默认连接；阶段 6 起为连接配置唯一真相源入口 |
| —（设计稿没有） | **PollingInterval** | 项目独有 | 设备级采集周期（ms），作为变量级周期的顶层默认 |
| —（设计稿没有） | **JsonConfig / Version** | 项目独有（历史列） | 原 DeviceConfigs.JsonConfig 的继承列；阶段 6 起不再写入，仅只读兜底，待评估删除；Version 为配置版本/乐观锁 |
| —（设计稿没有） | **LastCommunicationTime** | 项目独有 | 最后一次通信时间，仅记录不参与状态判定 |

### 3.2 Controller（`Controllers`）

| 设计稿字段 | 当前项目 | 差异类型 | 用途/说明 |
|---|---|---|---|
| ControllerType varchar（PLC/OPCUA） | **ProtocolId**（FK Protocols） | 更名+结构化 | "协议即控制器类型"：类型不再靠自由字符串，而是引用 Protocols 表（S7/OPCUA 等），与驱动派发统一 |
| Manufacturer / Model / Description | 同名保留 | 一致 | 厂商/型号/描述 |
| Code / Name | 同名保留 | 一致 | 编码（`ix_controllers_code` 唯一）/名称 |
| Enabled | IsEnabled | 更名 | 统一命名；禁用后不可再被连接引用 |

### 3.3 DeviceConnection（`DeviceConnections`）

| 设计稿字段 | 当前项目 | 差异类型 | 用途/说明 |
|---|---|---|---|
| Protocol varchar | **ProtocolId**（FK Protocols） | 结构化 | 同 3.2，协议由 Protocols 表定义 |
| Host / Port（公共字段） | Host（可空）/ Port（int?） | 角色弱化 | 项目明确为**冗余检索列**，仅管理/展示；运行时连接唯一真相源是 ConfigJson |
| ConfigJson text | ConfigJson longtext | 保留 | 驱动完整配置原文（rack/slot、端点等），运行时逐字节等价反序列化 |
| Timeout int | **TimeoutMs**（=5000） | 更名+单位化 | 明确毫秒；无显式值时默认 5000 |
| ReconnectInterval int | **ReconnectIntervalMs**（=5000） | 更名+单位化 | 同上 |
| Enabled | IsEnabled | 更名 | — |

### 3.4 DataModel（`DataModels`）

| 设计稿字段 | 当前项目 | 差异类型 | 用途/说明 |
|---|---|---|---|
| Code | Code（可空，唯一索引） | 保留（后补） | 业务键阶段 4 新增；存量回填取 Name 去重，NULL 允许多条 |
| Enabled | —（无） | 删除 | 模型停用语义由 IsPublished 承担（未发布不可被新建设备引用） |
| Version / IsPublished | 同名保留 | 一致 | 版本号（默认 1.0）/是否发布 |
| —（设计稿没有） | **Vendor / ModelName / VendorModel** | 项目独有 | 描述"这是哪家什么型号的设备"（Siemens S7-1500），支撑前端按厂商/型号筛选；VendorModel 可由前两者拼接也可手填 |
| —（设计稿没有） | **ProtocolId**（FK Protocols 必填） | 项目独有 | 默认协议：新建设备时预选，驱动派发真相源（P3-E） |
| Description | 保留 | 一致 | — |

### 3.5 DataPoint（`DataPoints`，模型变量=模板）

| 设计稿字段 | 当前项目 | 差异类型 | 用途/说明 |
|---|---|---|---|
| DataModelId | **ModelId** | 更名 | 归属模型 FK（Restrict），`ix_modelvariable_model_key` 唯一索引 |
| Code | **Key**（varchar50） | 更名 | 模型内唯一变量键（设计稿叫 Code，项目语义"键"） |
| DataType varchar（Bool/Float/Int32） | **DataType**（DataTypeEnum 强类型） | 类型升级 | 15 种 PLC 类型（INT/REAL/BOOL/DINT/BYTE/BIT/FLOAT/DOUBLE/STRING/UINT16/UINT32/INT64/UINT64/WORD/CHAR），驱动按枚举解释 |
| AccessMode varchar | AccessMode string（"Read"） | 一致（字符串枚举） | Read/Write/ReadWrite 权限权威列，阶段 6 起唯一真值源 |
| Required | **IsRequired** | 更名 | 必填 |
| Enabled | IsEnabled | 更名 | — |
| CreatedAt / UpdatedAt | —（无） | 删除 | DataPoint 无审计时间戳（与文档不同） |
| —（设计稿没有） | **Type**（计算属性） | 项目独有 | Digital/Analog 由 DataType 推导（BOOL/BIT→Digital），**不落库**，避免冗余矛盾 |
| —（设计稿没有） | **Min / Max**（double?） | 项目独有 | 量程上下限：驱动取值合法性校验、UI 显示、无规则时上下限兜底报警的基础 |
| —（设计稿没有） | **StoreMode / StoreIntervalMs / IsStored** | 项目独有 | 历史存储策略（None/Change/Cycle/Compressed/Aggregated）+ 存储周期（默认 5 分钟，Change 模式下作超时兜底防断档）；IsStored 为只读派生 |
| —（设计稿没有） | **UpdateMode** | 项目独有 | 轮询 / 订阅两种取值方式（订阅面向 OPC UA/推送类协议） |
| —（设计稿没有） | **ScaleExpression** | 项目独有 | 工程换算表达式（原始值→工程值，"x*0.1" 等一元公式，Jint 编译缓存委托），替代旧 Slope/Offset 线性模型 |
| —（设计稿没有） | **DeadBand** | 项目独有 | 变化死区，用于变化检测去抖（配合 Change 存储） |
| —（设计稿没有） | **ExtensionData** | 项目独有 | 模板级扩展 JSON（前端自定义属性透传） |

### 3.6 DataPointMapping（`DataPointMappings`，设备变量=实例）

| 设计稿字段 | 当前项目 | 差异类型 | 用途/说明 |
|---|---|---|---|
| ConnectionId（必填） | ConnectionId（int? 可空） | 语义放宽 | 空 = 使用设备默认连接（Device.ConnectionId）；阶段 4 新增，运行时暂不读该列 |
| Address varchar（必填） | Address（string?） | 可空 | 权威**展示串**（由 AddressConfigJson 自动生成）；过渡期可回退模板 Address（已 Obsolete） |
| RawDataType varchar | RawDataType（varchar32?） | 保留（记录性） | 如 REAL/DINT；驱动仍按 DataTypeEnum 解释，本列不参与校验 |
| TransformConfig text | —（拆分） | 拆分 | 设计稿用一个大 JSON 装"转换配置"；项目拆成三个强类型覆盖字段：**ScaleExpressionOverride**（工程换算）、**DeadBandOverride**（死区）、**AccessModeOverride**（读写权限），均为"空=回退模板" |
| ScanInterval int | **PollingIntervalMs**（int?） | 更名+可空 | 设备实例采集周期；空回退模板/设备级 Device.PollingInterval |
| Enabled | IsEnabled | 更名 | 单台设备单变量的独立启停 |
| CreatedAt / UpdatedAt | —（无） | 删除 | 无审计时间戳 |
| —（设计稿没有） | **BitOffset**（int?） | 项目独有 | 位操作偏移（DBX 位寻址），迁入自模板 BitOffset |
| —（设计稿没有） | **AddressConfigJson**（longtext?） | 项目独有 | 地址的**权威机读形态**（AddressConfig JSON）；前端只编辑此字段，后端自动生成 Address 展示串，保证一致性 |
| —（设计稿没有） | **ExtensionData** | 项目独有 | 实例级扩展 JSON |

### 3.7 DeviceDataModel（`DeviceDataModels`）

| 设计稿字段 | 当前项目 | 差异类型 | 用途/说明 |
|---|---|---|---|
| Enabled | IsEnabled | 更名 | — |
| CreatedAt | CreatedAt + **UpdatedAt** | 补充 | 文档只有 CreatedAt；项目补 UpdatedAt |
| DeviceId / DataModelId / Version / IsPrimary | 同构保留 | 语义增强 | 唯一索引 (DeviceId, DataModelId)；IsPrimary 行必须与 Device.ModelId 一致（双写单点收敛）；版本为绑定时刻快照；删设备 Cascade、删模型 Restrict |

### 3.8 最新值快照：设计稿 `DeviceDataValue` → 项目 `VariableRealtime`

| 设计稿字段 | 当前项目 | 差异类型 | 用途/说明 |
|---|---|---|---|
| Id bigint 自增 | —（无，复合主键 (DeviceId, VariableKey)） | 删除 | 天然保证"每设备每变量一行"，批量 Upsert 免去先查后改/防重 |
| DataPointId | **VariableKey**（+冗余 DeviceKey/VariableName） | 更名+冗余 | 按**业务键**而不是模板 Id 寻址：变量改名/模板重组不破坏快照对应关系；冗余键/名使无外键大表可直接按设备查询展示 |
| Value text | **Value double + RawValue string?** | 拆分 | Value 数值化（可排序/计算；数字量存 0/1），RawValue 保留驱动原始形态（字符串、带单位格式）避免丢信息 |
| DataType varchar | —（无） | 删除 | 类型以 DataPoint 模板为单一真相源，快照不冗余，杜绝不一致 |
| Quality int | Quality string? | 类型变化 | 存质量枚举名（Good/CommunicationError…），自描述、前端直接展示，免 int 对照 |
| Timestamp | Timestamp | 保留 | 采样时间（设备采集时间，非落库时间） |
| UpdatedAt | —（无） | 删除 | Upsert 单行语义下 Timestamp 即最新，无需独立更新时间 |

### 3.9 历史数据：设计稿 `DataHistory` → 项目 `VariableHistory`

| 设计稿字段 | 当前项目 | 差异类型 | 用途/说明 |
|---|---|---|---|
| Id bigint | Id **long** | 一致 | 长整型自增，支撑长时间跨度 |
| DataPointId | **VariableKey**（+DeviceKey/VariableName 冗余） | 更名+冗余 | 同上；复合索引 `(VariableKey, Timestamp)` 支撑趋势查询，无外键免级联开销 |
| Value text | **Value double + RawValue string?** | 拆分 | 同 3.8 |
| DataType varchar | —（无） | 删除 | 同 3.8 |
| Quality int | Quality string? | 类型变化 | 同 3.8 |

补充：设计稿未定义"何时写一条历史"；项目由 `DataPoint.StoreMode/StoreIntervalMs` 决定（变化存储/周期存储/压缩/聚合），这是比表结构更深一层的运行时策略差异。

### 3.10 AlarmRecord（`AlarmRecords`）

| 设计稿字段 | 当前项目 | 差异类型 | 用途/说明 |
|---|---|---|---|
| AlarmCode varchar / AlarmName varchar | **VariableKey/VariableName + RuleId/RuleName** | 更名+重构 | 报警不再有独立业务编码，归属 = 变量（哪台设备哪个变量）+ 规则（命中了哪条 AlarmRule）；RuleId/RuleName 可空（上下限兜底无规则） |
| DataPointId | DataPointId（int?） | 保留（后补） | 设计稿就有；项目阶段 6.5 新增，存量按 DeviceId+VariableKey 回填 |
| AlarmLevel int | **Level**（AlarmLevelEnum） | 类型升级 | Low/Medium/High/Critical 强类型 |
| StartTime / EndTime | **TriggeredAt / RecoveredAt** | 更名 | 语义更精确（触发/恢复时刻），RecoveredAt 可空表示未恢复 |
| Status int（Active/Recovered） | **恢复（RecoveredAt）+ 确认（Acked/AckedAt/AckedBy）两个正交维度** | 拆分 | 支持"已恢复但未确认/已确认但未恢复"等真实工业告警组合；设计稿没有确认概念 |
| Value text | **ActualValue + RecoveryValue** | 拆分 | 触发时值 / 恢复时值，统一字符串存储避免历史类型冲突 |
| Message | Message | 保留 | 报警文案 |
| CreatedAt | —（无） | 删除 | 触发时刻即时间轴（TriggeredAt），无需另存创建时间 |
| —（设计稿没有） | **Condition / Threshold** | 项目独有 | 命中的比较条件与阈值（>、≥、<、== 等），规则与兜底报警通用 |
| —（设计稿没有） | **Source**（Rule/MinMaxLimit/System） | 项目独有 | 触发来源，便于区分规则报警与上下限兜底报警 |
| DeviceId | DeviceId（+DeviceKey 冗余） | 保留 | 无外键（大表写入性能），按 DeviceKey 查询展示；索引 (TriggeredAt)、(Acked,RecoveredAt)、(DeviceId) |

### 3.11 Area（`Areas`）

| 设计稿字段 | 当前项目 | 差异类型 | 用途/说明 |
|---|---|---|---|
| AreaType varchar 或枚举 | **AreaType**（AreaTypeEnum） | 一致 | Factory=1/Workshop=2/ProductionLine=3/Area=4/Warehouse=5，与设计稿推荐的枚举值完全一致 |
| Code | Code（varchar50，可空唯一） | 职责增强 | 除区域编码外还作为**设备 Key 自动生成前缀**（留空回退 A{Id}） |
| Enabled | IsEnabled | 更名 | — |
| ParentId | ParentId（自引用 Restrict） | 一致 | NULL=根区域；树形删除约束 |
| Name / Description / Sort / CreatedAt / UpdatedAt | 同构保留 | 一致 | — |
| —（设计稿没有） | Parent / Children 导航 | 项目独有 | 纯导航属性（非列），树遍历用 |

---

## 四、设计稿提出、但项目明确"不落库/不实现"的点

| 设计稿提议 | 项目现状 | 说明 |
|---|---|---|
| DeviceRuntime / RuntimeValue 不入库 | **一致，未建表** | DeviceRuntime.cs 为纯内存实体（DeviceId/CurrentStatus/统计等），无 [Table] 映射，与设计稿"它们属于运行时"一致 |
| Device.ParentId 设备树 | 未实现 | 组织层级由 Area 树承担；若未来需要"设备-子部件"组合关系需另设计（Device 无 ParentId 列） |
| Device.Location 字符串 | 已按设计稿自身建议删除 | 位置 = AreaId |
| 每张表带 CreatedAt/UpdatedAt | 部分实体无 | DataPoint、DataPointMapping、AlarmRecord、VariableRealtime/VariableHistory（采样语义）未带审计时间戳；VariableRealtime 无 UpdatedAt |

---

## 五、项目额外存在、设计稿未覆盖的表（供了解，非本次差异主体）

设计稿只规划了设备采集主链路。项目在 DbContext 中还注册了：`AlarmRules`/`LinkageRules`（报警/联动规则库，AlarmRecord 的规则来源）、`Protocols`（前文 ②）、`Sensor`、`HmiComponents`/`ScadaPages`/`ScadaProject`（HMI 画面）、`SystemUser`/`SystemLog`/`SystemConfig`/`ConfigLog`/`DbVersion`（系统与审计）、`ScheduledTask`/`SystemScript`/`ScriptExecutionRecord`（定时任务与脚本）、`DataConversion`、`ExposedInterface`、`MqttServer`/`MqttVariableConfig`（MQTT 映射）、`DatabaseConfig` 等。
