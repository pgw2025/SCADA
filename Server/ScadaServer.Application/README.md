# ScadaServer.Application 项目说明文档

> 本文档基于对 `ScadaServer.Application` 项目源码的逆向梳理生成，面向单人开发者，用于理解架构、自查 Code Review 与排查 Bug。
> 原则：**所有内容均来自实际源码**；遇到程序集外无法确认的细节一律标注【缺少文件，无法确认】，不做臆测。
> 本项目只做“应用/业务”层，不包含数据库访问实现、设备驱动实现、运行时引擎实现，这些都在兄弟项目（Domain / Infrastructure / Runtime / WebApi）里。

---

## 1. 项目概述

- **项目功能**：这是整套 SCADA 系统的**业务应用层（Application Layer）**，是所有业务规则的汇聚点。它向上承接 WebApi（控制器）的调用，向下通过 Domain 层定义好的仓储接口读写 MySQL，通过一批抽象接口与“实时运行时引擎”对接，完成设备管理、变量管理、历史数据、报警、脚本、组态画面项目（HMI）、开放 API 暴露、MQTT、导入导出等一系列 SCADA 业务。
- **业务目标**：把“什么东西可以配置”“配置怎么校验”“数据往哪存/怎么取”“如何和实时采集运行时对接”等**业务语义**集中在本层，让上层（WebApi）只做 HTTP 转发，让下层（仓储/运行时代理）只做具体 IO，从而保持各层职责单一。
- **使用场景**：
  1. WebApi 控制器处理 HTTP 请求后，调用本层的 `XxxAppService` 完成业务。
  2. 采集运行时的数据（历史点、报警、实时快照）需要落库 / 查询时，通过本层定义的 `IHistoryRecorder`、`IAlarmRecorder`、`IInfluxStore` 等接口。
  3. 业务侧（设备增删改、脚本写入、绑定联动）需要操作“正在运行的设备”时，通过 `IRuntimeDeviceManager` 回调运行时代理。

**注意**：本层**不直接接触外部设备（PLC/S7 等）**，不直接落库（无 DbContext），不直接跑 SignalR/MQTT 服务端实例；这些均以接口形式抽象在本层，由其他项目实现并注入进来（见第 7 章）。

---

## 2. 技术栈清单

| 类别 | 内容 |
|------|------|
| 语言/框架 | C#、.NET 8（net8.0）、ASP.NET Core（隐式引用） |
| 分层定位 | 应用服务层（AppService），依赖 Domain，不与 Infrastructure/Runtime 直接引用 |
| ORM | Microsoft.EntityFrameworkCore 8.0.17（仅用于捕获 `DbUpdateException` 等异常类型，不做 Context 操作） |
| MySQL 客户端 | MySqlConnector 2.6.2（识别 MySQL 唯一键冲突错误码 1062） |
| 身份/令牌 | Microsoft.AspNetCore.Identity 2.3.10、System.IdentityModel.Tokens.Jwt 7.1.2 |
| JS 脚本引擎 | Jint 3.1.3（脚本**语法校验/静态解析**用，只解析不执行） |
| 定时表达式 | Cronos 0.13.0（Cron 表达式校验） |
| Excel 读写 | ClosedXML 0.104.2（TIA 变量表 xlsx 解析、xlsx 导出） |
| 配置抽象 | Microsoft.Extensions.Configuration.Abstractions 8.0.0 |
| 协议/通信 | 本层无直接实现；通过抽象接口对接（S7/Modbus/OPC UA/MQTT/Virtual）【驱动实现在 Runtime/Infrastructure，缺少文件】 |
| 时序库 | InfluxDB 2.x（经 `IInfluxStore` 抽象，仅写入/查询/清理/导出语义，无原生实现） |
| 实时通知 | SignalR（经 `IScadaNotificationService` 抽象） |

> **基础脚手架代码 / 第三方原生代码 / 我们自己业务代码 的区分**：
> - **第三方原生代码**：上述 NuGet 包（EF Core、MySqlConnector、Jint、Cronos、ClosedXML、Identity、Jwt、Config.Abstractions）。
> - **我们自己写的业务代码**：本 `ScadaServer.Application` 工程内的全部 `.cs` 文件（AppService / DTO / 接口 / 转换器 / 导入导出 / Options）。本工程没有脚手架模板代码，是纯业务代码。
> - **基础框架脚手架**：不在本工程，属 WebApi/Infrastructure 等兄弟项目【缺少文件，无法确认】。

---

## 3. 目录结构解析

```
ScadaServer.Application/
├── ScadaServer.Application.csproj        # 工程文件：引用 Domain + 上述 NuGet 包
├── Converters/                           # JSON 序列化转换器（前后端枚举/类型兼容）
│   ├── DataTypeEnumJsonConverter.cs      #   数据类型枚举别名映射（如 Int16→INT）
│   ├── DeviceTypeJsonConverter.cs        #   设备类型枚举别名映射（如 OPCUA→OpcUa）
│   └── ObjectClrTypeJsonConverter.cs     #   object 值反序列化为原始 CLR 类型（bool/long/double/string）
├── DTOs/                                 # 数据传输对象（应用层进出模型）
│   ├── ApiResponse.cs                    #   统一响应结构 O{Success,Message,Data,Errors}
│   ├── AlarmEvent.cs                     #   运行时报警事件（触发/恢复），Push 与落库共用
│   ├── ScadaTransferDto.cs               #   组态工程/画面导入导出迁移包模型
│   ├── ModelVariableMapper.cs            #   模型变量映射辅助
│   └── ... 其余各业务 DTO（Device/Area/Sensor/Protocol/...）见接口同名归类
├── ImportExport/                         # 模型变量批量导入导出（核心工具逻辑）
│   ├── IVariableImportParser.cs          #   解析器接口（无状态、可单例、部分成功不中断）
│   ├── VariableImportParser.cs           #   按扩展名分发 xlsx / csv
│   ├── TiaXlsxParser.cs                  #   TIA Portal 变量表 xlsx 解析 + TIA 类型映射
│   ├── CsvParser.cs                      #   标准 CSV 导入解析器
│   └── VariableExportService.cs          #   xlsx/csv 导出（列与导入模板往返一致）
├── Interfaces/                           # 本层对外/对内依赖的抽象接口（关键为“防反向依赖”）
│   ├── IUnitOfWork.cs                    #   工作单元/事务抽象（含重试策略内事务）
│   ├── IBusinessRepositories.cs          #   几个聚合查询仓储接口（IAssetRepository/IHmiRepository/IAutomationRepository）
│   ├── IRuntimeDeviceManager.cs          #   ★ 运行时设备增删改热加载 + 变量写入（防反向依赖的关键）
│   ├── IRuntimeStatusProvider.cs         #   设备实时状态提供者（WebApi 适配实现）
│   ├── IRealtimeSnapshotService.cs       #   实时快照（MySQL VariableRealtime 批量 Upsert）
│   ├── IHistoryRecorder.cs / IAlarmRecorder.cs  # 历史点/报警异步入队落库
│   ├── IInfluxStore.cs                   #   InfluxDB 历史库访问抽象（热切换）
│   ├── IMqttManager.cs                   #   MQTT 服务器管理（启动/发布/重连/状态/测试）
│   ├── IScadaNotificationService.cs      #   SignalR 通知抽象（变量/设备状态/报警/脚本）
│   ├── IVariableWriteAuditRecorder.cs    #   非 HTTP 路径变量写入审计（脚本/绑定）
│   ├── IExposedApiRegistry.cs            #   开放 API 网关快速匹配配置缓存
│   ├── IRuntimeDatabaseService.cs        #   主库配置读写 + 连接测试
│   ├── IHistoryMigrationService.cs       #   MySQL 存量历史 → InfluxDB 迁移
│   ├── IScriptValidationService.cs       #   脚本静态校验
│   ├── IMqttService.cs / IMqttServerAppService.cs / IMqttVariableConfigAppService.cs
│   ├── IScadaProjectAppService.cs / IScadaPageAppService.cs / IHmiComponentAppService.cs / IHmiImageAppService.cs
│   ├── IDeviceAppService / IDeviceVariableAppService / IDeviceDeletionService / IModelVariableAppService / IDataModelAppService / IProtocolAppService
│   ├── IAlarmRuleAppService / IAlarmRecordAppService / IDataConversionAppService / ILinkageRuleAppService / IScheduledTaskAppService / ISystemScriptAppService
│   ├── IAreaAppService / ISensorAppService / ISystemConfigAppService / ISystemLogAppService / IConfigLogAppService / ISystemUserAppService
│   └── IDatabaseConfigAppService / IExposedInterfaceAppService / IHistoryAppService
├── Options/                              # 强类型配置节
│   ├── SystemDbOptions.cs                #   "SystemDbConfig" 节，生成 MySQL 连接串
│   ├── SystemLogOptions.cs              #   "SystemLog" 节，日志写库最低级别/截断/黑名单/保留期
│   └── HmiImageOptions.cs               #   "HmiImage" 节，图片库存储路径/大小/扩展名白名单
└── Services/                             # 各业务应用服务实现（与 Interfaces 一一对应）
    ├── DeviceAppService.cs               #   ★ 设备创建/更新/删除/查询 + 与运行时热加载联动（最复杂）
    ├── DeviceDeletionService.cs          #   设备删除的依赖检查与级联清理分层
    ├── DeviceVariableAppService.cs       #   设备变量实例配置
    ├── ModelVariableAppService.cs        #   变量模板（含批量导入冲突检测）
    ├── DataModelAppService.cs            #   数据模型
    ├── ProtocolAppService.cs             #   协议管理
    ├── AreaAppService.cs / SensorAppService.cs / SystemConfigAppService.cs
    ├── ScadaProjectAppService.cs         #   ★ 组态工程 CRUD + 导出/导入（迁移包）
    ├── ScadaPageAppService.cs / HmiComponentAppService.cs / HmiImageAppService.cs
    ├── AlarmRuleAppService.cs / AlarmRecordAppService.cs
    ├── DataConversionAppService.cs / LinkageRuleAppService.cs / ScheduledTaskAppService.cs
    ├── SystemScriptAppService.cs         #   脚本 CRUD + 熔断复位
    ├── ScriptValidationService.cs        #   脚本静态校验（Jint 只解析不执行）
    ├── ScriptVariableCleanupHelper.cs    #   删除变量时联动停用/清理脚本
    ├── HistoryAppService.cs              #   ★ 历史查询/批量/CSV 导出（Influx 优先，MySQL 回退）
    ├── DatabaseConfigAppService.cs       #   数据库配置（DatabaseConfigs 表）掩码回显/同 Type 生效唯一
    ├── RuntimeDatabaseService / ExposedApiRegistry / ExposedInterfaceAppService
    ├── MqttServerAppService.cs / MqttVariableConfigAppService.cs
    ├── SystemLogAppService.cs / ConfigLogAppService.cs / SystemUserAppService.cs
    └── ...
```

**归类**：
- **核心业务**：`Services/`（尤其 DeviceAppService、ScadaProjectAppService、HistoryAppService）、`ImportExport/`、`Interfaces/` 的运行时桥梁接口。
- **工具/支撑**：`Converters/`（序列化兼容）、`Options/`（配置绑定）。
- **纯数据模型/静态资源**：`DTOs/` 属于“契约/模型”，非运行逻辑。

---

## 4. 整体架构 & 分层说明

本工程的调用方向（自顶向下）：

```
WebApi 控制器 (HTTP/SignalR 网关)
        │  调用
        ▼
ScadaServer.Application  (本层：业务规则 + 编排)
   ├─ XxxAppService ──► DOMAIN 仓储接口（IxxxRepository，由 Infrastructure 实现）──► MySQL
   ├─ 事务：IUnitOfWork.ExecuteInTransactionAsync（含 EF 重试策略兼容）
   └─ 运行时桥：IRuntimeDeviceManager / IRuntimeStatusProvider / IRealtimeSnapshotService /
                 IHistoryRecorder / IAlarmRecorder / IMqttManager / IScadaNotificationService /
                 IInfluxStore / IVariableWriteAuditRecorder
                          │（接口定义在本层，实现在 WebApi/Runtime/Infrastructure）
                          ▼
                 ScadaServer.Runtime / Infrastructure / WebApi (实时引擎 / 驱动 / 实现)
```

**各层职责：**
| 层 | 职责 | 示例 |
|----|------|------|
| `IxxxAppService` + `Services` | 业务规则、校验、事务编排、与运行时对接 | 设备创建必须先校验区域/模型/协议驱动 |
| `IUnitOfWork` | 数据库事务边界（避免手写”用户事务 + 重试策略冲突“问题） | 设备创建 = 插 Device + 插 Config + 插 DeviceVariable 一次提交 |
| 仓储接口（Domain） | 数据访问抽象，本层**只调用接口不写 SQL** | `IDeviceRepository` |
| 运行时桥接口 | 把“对运行中设备/实时数据/通知”的操作抽象化，**切断本层对 Runtime 的编译依赖** | `IRuntimeDeviceManager` |

**重要设计（约束/禁止跨层）：**
1. **本层不直接使用 DbContext / SqlConnection**：数据读写一律走仓储接口，事务走 `IUnitOfWork`。【DbContext 实现在 Infrastructure，缺少文件】
2. **本层不直接 new 运行时**：设备增删改后会调用 `IRuntimeDeviceManager`（幂等注册/注销/重载），而非访问 Runtime 程序集。
3. **数据传输用 DTO**：接口进出皆 DTO，不直接暴露 Domain Entity 给控制器。
4. **运行时桥实现方在 WebApi/Runtime**：通过依赖注入注入，`IRuntimeStatusProvider` 注释明确“由 WebApi 层用 RuntimeManager 适配实现，避免 Application 层反向依赖 Runtime 程序集” —— 这是本项目最重要的分层切面，新增“运行时能力”时应遵循同样模式。

---

## 5. 核心业务数据流

### 场景一：新建一台设备（HTTP POST）
```
Controller(Devices) ─► DeviceAppService.CreateAsync(CreateDeviceDto)
  1) 校验 Area / Model 存在
  2) 校验 Model.Protocol.DriverKey 驱动已实现（目前 S7 / OPC UA / Virtual）【ModbusTCP/MQTT 未枚举确认，Dev 未实现时报友好错】
  3) 按协议驱动校验 ConfigJson 是否能反序列化
  4) 设备标识：未填则按 区域编码-序号 (如 BLR-001) 自动生成并保证唯一
  5) IUnitOfWork.ExecuteInTransactionAsync:
       - Insert Device（撞唯一键 1062 → 转 BusinessException）
       - Insert DeviceConfig
       - 按模型变量模板批量生成 DeviceVariable 实例
  6) 事务成功后：若启用，调 IRuntimeDeviceManager.RegisterDeviceAsync(设备ID) → 无需重启即开始采集
```
> 数据存储：MySQL（Device / DeviceConfig / DeviceVariable）。运行时侧由 Runtime 工程负责实际连接与采集【缺少文件】。

### 场景二：采集到的历史数据如何落库 / 查询（双库：实时 MySQL + 历史 Influx）
```
运行时采集循环
   │ 成功读到变量
   ▼
IHistoryRecorder.Record(...)            // 异步入队，避免阻塞采集
   ▼ [后台服务批量落库]（实现方在 Runtime/Infrastructure【缺少文件】）
   ├─ MySQL VariableRealtime（实时快照，IRealtimeSnapshotService.Update → 周期 Upsert）
   └─ InfluxDB variable_history（IInfluxStore.WriteAsync，device_key+variable_key 为 tag）

前端趋势查询
   ▼
IHistoryAppService.GetHistoryAsync / GetHistoryBatchAsync
   → QuerySingleAsync：InfluxDB 优先（IsConfigured 且返回数据）→ 否则回退 MySQL VariableHistory
   → 时间升序返回；支持窗口聚合降采样（aggregateWindow）
```
> 查询身份 = `device_key + variable_key` 二元组，解决变量跨设备重名问题。

### 场景三：报警的产生 → 通知 + 落库
```
运行时报警检测（规则引擎 / Min-Max 兜底 / 系统级）【引擎在 Runtime【缺少文件】】
   ▼ 产生 AlarmEvent（DTOs/AlarmEvent.cs：触发/恢复、级别、来源、阈值…）
   ├─ IScadaNotificationService.NotifyAlarmAsync(evt)  → SignalR “ReceiveAlarm” 前端
   └─ IAlarmRecorder.Record(evt)                       → 异步批量落库 AlarmRecords（由 AlarmRecorder 实现方处理）
```
> 报警规则 CRUD 在 `AlarmRuleAppService`（写入 AlarmRule 表），实时评估逻辑在 Runtime。

### 场景四：向运行中设备写值（脚本/绑定/HTTP）
```
IRuntimeDeviceManager.WriteVariableAsync(deviceId, variableKey, value, writeSource?)
   → 定位 DeviceRuntime → VariableRuntime → 驱动 WriteAsync
   → 成功后更新运行时内存值并经 SignalR 广播
   → 非 HTTP 来源(writeSource!=null) 记录审计（IVariableWriteAuditRecorder，仅入队非阻塞）；
     HTTP 用户写入由 WebApi [AuditLog] 记录（含操作人/IP），避免重复
DeviceAppService.WriteVariableAsync 将其包装：失败 → BusinessException → 前端 {success:false,message}
```

### 场景五：组态工程导出 / 导入
```
ExportAsync(id)：GetTreeAsync 拉工程+画面+组件（组件查询已 SQL 下推过滤）
  → 把 bindDeviceId 批量映射为设备业务键(Device.Key)（导出端设备已删则置 null）
  → 返回 ScadaTransferPackageDto（format=scada-project / scada-page，全部剥离自增 id）

ImportAsync(package)：
  → 校验 format/缺省
  → 事务内：工程重名加“导入”后缀；画面重名去重；同端首页唯一（重复则降级普通）
  → bindDeviceKey 按本系统设备键映射为新 bindDeviceId；匹配不到记 warning，保留原键待重新绑定
```

---

## 6. 核心入口文件、关键类说明

本工程是**类库（无 Program.cs / Main）**，真正的进程入口在 WebApi 工程【缺少文件】。本层的“入口”= 被 DI 注册的服务与定义好的契约。

**关键类：**
| 类 | 作用 & 为什么重要 |
|----|----|
| `Services/DeviceAppService.cs` | 最复杂的业务服务。演示了：校验→事务→运行时热加载全链路，含设备标识自动生成+防撞、协议配置校验路由、N+1 优化（一次拉全量变量再内存组装）、运行时状态三级回退（禁用→Offline / 运行时内存态 / 持久化 LastKnownStatus） |
| `Services/ScadaProjectAppService.cs` | 组态工程 CRUD + 导出/导入迁移包，含事务、重名去重、设备键映射容错 |
| `Services/HistoryAppService.cs` | 历史双库查询（Influx 优先 + MySQL 回退）、批量（上限 8 变量）、CSV 导出（严格限制 limit≤10000） |
| `Services/ExposedApiRegistry.cs` | 开放 API 网关匹配缓存（单例 + 内存 ConcurrentDictionary，Reload 内用 IServiceScopeFactory 现场解析 Scoped 仓储，避免把 Scoped 提升为单例） |
| `Services/DatabaseConfigAppService.cs` | 数据库配置管理：敏感字段掩码回显、掩码不改密、同 Type 仅一条生效 |
| `Services/ScriptValidationService.cs` | 脚本静态校验：Jint 只解析不执行、Cronos 校验 Cron、触发类型专属必填项、scope 授权提示 |
| `Services/ScriptVariableCleanupHelper.cs` | 删除变量时联动停用监听脚本 + 剔除写授权（被设备变量/模型变量服务共用） |
| `ImportExport/TiaXlsxParser.cs` + `CsvParser.cs` + `VariableExportService.cs` | 模型变量批量导入导出，TIA 变量表映射、CSV 模板往返一致、部分成功不中断 |
| `Converters/*` | 解决前后端枚举/类型命名不一致，保证 `System.Text.Json` 反序列化兼容 |
| `IUnitOfWork.ExecuteInTransactionAsync<T>` | **必须用它在事务内执行**（因为 DbContext 配置了 `EnableRetryOnFailure`，手动开启事务会抛 “does not support user-initiated transactions”） |

**配置加载**：本层用 `Options/` 绑定配置节（`SystemDbConfig` / `SystemLog` / `HmiImage`）；实际注册监听在 WebApi【缺少文件】。

---

## 7. 外部依赖 & 第三方交互

- 本层直接对接的“第三方”主要是**抽象接口而非具体实现**，实际实现方在 Runtime / Infrastructure / WebApi：
  | 抽象 | 对外能力 | 说明 |
  |------|----------|------|
  | `IRuntimeDeviceManager` | 设备注册/注销/重载/写值 | 驱动连接、采集 Worker、重连逻辑实现在 Runtime【缺少文件】 |
  | `IInfluxStore` | 历史写入/查询/清理/导出、热切换、Ping/测试 | InfluxDB 2.x client 实现在 Infrastructure【缺少文件】 |
  | `IMqttManager` | MQTT 启动/发布/重连/状态/测试 | MQTT client 实现在 Runtime/Infrastructure【缺少文件】 |
  | `IScadaNotificationService` | SignalR 推送变量/设备状态/报警/脚本事件 | SignalR Hub 在 WebApi【缺少文件】 |
  | `IRealtimeSnapshotService` / `IHistoryRecorder` / `IAlarmRecorder` | 实时快照/历史点/报警异步落库 | 后台排队落库实现在 Runtime/Infrastructure【缺少文件】 |
- **通信协议**：S7 / ModbusTCP / OPC UA / MQTT / Virtual —— 本层只定义驱动**种类枚举与配置校验路由**（`DeviceAppService.DriverKind` / `DeviceTypeJsonConverter` / `ProtocolAppService`），实际驱动实现在 Runtime【缺少文件】。
- **超时 / 重连 / 重试**：
  - 写历史（`IInfluxStore.WriteAsync`）内部“有限重试”，失败由调用方决定回退。
  - 事务层由 EF `EnableRetryOnFailure` 承载重试（本层通过 `ExecuteInTransactionAsync` 兼容）。
  - MQTT 重连由 `IMqttManager.ReconnectAsync`（后台服务周期调用）【触发调度在 WebApi/Runtime，缺少文件】。
  - 其它驱动的重连/超时细节不在本层【缺少文件】。

---

## 8. 潜在问题与优化建议

> 以下基于本工程实际读到的代码。很多“运行时细节”（驱动、重连）不在本层，无法确认的已标注。

### 【必须修复阻断问题】
1. **历史 CSV 导出存在公式注入（CSV Injection）风险**（[HistoryAppService.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Application/Services/HistoryAppService.cs#L95-L116)）
   导出拼的是 `"值"`，若变量值/名称以 `=`、`+`、`-`、`@` 开头，Excel 打开会当作公式执行。建议对值/字段做前缀防护（如 `'` 转义）或明确拒绝这类首字符。

2. **导入文件无大小/行数上限 → 内存 DoS**（[TiaXlsxParser.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Application/ImportExport/TiaXlsxParser.cs#L35-L41)、[CsvParser.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Application/ImportExport/CsvParser.cs)）
   - ClosedXML 会把整个工作簿读入内存，超大 xlsx 可直接打爆内存；
   - CSV 无行数上限累积为 `List`，亦可内存暴涨；
   - 建议在 WebApi 入参加 `MaxFileSize` 限制，并在解析循环内设最大行数/单文件字节数上限。

3. **`ExposedApiRegistry.TryMatch` 首次访问同步阻塞**（[ExposedApiRegistry.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Application/Services/ExposedApiRegistry.cs#L75-L78)）
   用 `ReloadAsync().GetAwaiter().GetResult()`（sync-over-async）在热路径上触发数据库查询，可能造成线程池阻塞甚至死锁。建议在启动时主动预热（`IHostedService` 或 `Startup`）避免运行时懒加载，或改成可安全 sync 的方式。

4. **`DatabaseConfigAppService` 将数据库/Influx 密码、Token 明文落库**（[DatabaseConfigAppService.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Application/Services/DatabaseConfigAppService.cs#L160-L169)）
   输出端做了掩码，但存储为明文，风险在于数据库被拖库即泄露所有连接凭据。建议对称加密落库（密钥放外部/环境变量）或接入密钥管理服务。

### 【可选优化】
5. **部分 CRUD “未找到”静默返回，行为不一致**（如 [AlarmRuleAppService.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Application/Services/AlarmRuleAppService.cs#L47-L49) Update 找不到实体直接 return）。设备/脚本等用 `BusinessException`，建议全层统一“更新不存在的资源应抛业务异常”，减少前端“改成功了其实没改”的错觉。

6. **历史导出用 StringBuilder 无界拼接**（[HistoryAppService.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Application/Services/HistoryAppService.cs#L85-L115)）。8 变量 × 10000 行可能产生很大字符串。建议用可流式写出的响应（如返回 Stream / `FileResult`）并继续限制总数。

7. **`ScadaProjectAppService.DeleteAsync` 全表 `GetListAsync` 后在内存按 ProjectId 过滤**（[ScadaProjectAppService.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Application/Services/ScadaProjectAppService.cs#L62-L82)）。工程不多时可接受，量大建议改为按条件 `DeleteRangeAsync(p => p.ProjectId == id)`。

8. **`DeviceAppService.GenerateDeviceCodeAsync` 用 `StartsWith(prefix)` 找兄弟设备**（[DeviceAppService.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Application/Services/DeviceAppService.cs#L233-L248)），可能把前缀更长的键也算进 maxSeq；数量小时无害，可作为健壮性优化（解析时限定长度为 baseCode-3）。

9. **ImportExport 解析器对单元格类型强转字符串**，若 Excel 单元格为“公式/混合类型”可能取空；TIA 文件一般可控，属低风险健壮性问题。

10. **大量 `AppService` 用手写映射**（`MapToDto/MapToEntity`），字段增多易漏。可选引入 AutoMapper，但**属重构项，非必须**。

---

## 9. 新手阅读顺序建议

1. **先读 DTO + 接口**：
   - `DTOs/ApiResponse.cs`（统一响应约定）
   - `Interfaces/IRuntimeDeviceManager.cs`、`IRuntimeStatusProvider.cs`、`IInfluxStore.cs`（理解“Application 层如何桥接运行时/存储”，这是读懂整套架构的钥匙）
   - `Interfaces/IUnitOfWork.cs`（理解事务规则）
2. **再看一个“完整链路”服务**：`Services/DeviceAppService.cs`（校验→驱动路由→事务→运行时热加载，是范式样本）。
3. **再按业务板块走**：
   - 采集/历史：`Services/HistoryAppService.cs` + `Interfaces/IHistoryRecorder.cs` + `IAlarmRecorder.cs`
   - 报警：`Services/AlarmRuleAppService.cs` + `DTOs/AlarmEvent.cs` + `Interfaces/IAlarmRecordAppService.cs`
   - 组态：`Services/ScadaProjectAppService.cs` + `Interfaces/IScadaPageAppService.cs` + `DTOs/ScadaTransferDto.cs`
   - 脚本：`Services/SystemScriptAppService.cs` + `ScriptValidationService.cs` + `ScriptVariableCleanupHelper.cs`
   - 开放 API：`Services/ExposedApiRegistry.cs` + `ExposedInterfaceAppService.cs`
4. **再看导入导出**：`ImportExport/` 四个文件（TIA 解析最有业务价值）。
5. **最后看支撑**：`Converters/`、`Options/`（前后端兼容与配置绑定）。

最快路径：**ApiResponse → IRuntimeDeviceManager → DeviceAppService → HistoryAppService**，即可把握本层主干。

---

## 10. 后续可扩展方向（可选）

- **统一 CRUD 收敛**：对高频“简单 CRUD + 手写映射”提炼通用基座（泛型 BaseAppService + 表达式映射），减少样板代码与不一致（改前先确认团队接受引入抽象成本）。
- **导入导出能力扩展**：支持更多 PLC 厂商变量表（如 Siemens SCL、OPC UA 信息模型导出）、导入行数/体积防护与断点恢复。
- **双库一致性增强**：历史查询 Influx/MySQL 回退目前是“按是否有数据”判定，可在返回中附带“数据来源标记”，便于定位迁移期数据缺口。
- **敏感凭据治理**：统一加密落库 + 支持从环境变量/密钥库读取，配合上面的阻断问题 #4。
- **运行时热管理扩展**：沿 `IRuntimeDeviceManager` / `IRuntimeStatusProvider` 模式继续扩展（如变量级在线启停、配置热更新的订阅通知）。

---

## 维护说明（版本连贯接口）

本文档为 `ScadaServer.Application` 的基线分析。后续如需补充更新，请继续提供：
- 本工程新增/修改的源码文件（Service / DTO / 接口 / 转换器 / 导入导出）；
- 与本工程协同的兄弟项目（Domain / Infrastructure / Runtime / WebApi）的关键文件（用于补全“缺少文件，无法确认”的部分）；
- 外部协议/设备接入细节（用于完善第 7 章交互表）。

我可在当前库内继续迭代更新本 `README.md`，保持分析与实际代码版本连贯。