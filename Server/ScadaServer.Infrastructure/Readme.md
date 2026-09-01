# ScadaServer.Infrastructure 项目说明

> 本文档由对 `ScadaServer.Infrastructure.csproj` 及该层源码的逆向梳理生成。
> 定位：这是 SCADA 服务端的「**基础设施层**」，负责数据库持久化、通信驱动、外部服务（InfluxDB / MQTT）的底层落地与系统监控。
> ⚠️ 以下分析**仅基于本目录代码**；凡是依赖其他层（WebApi / Runtime / Application）才能确认的内容，均已标注【缺少文件，无法确认】，不臆测。
> ⚠️ 本层是「实现」而非「编排」：运行时的设备采集编排、告警引擎、脚本引擎、定时任务调度器都在 `ScadaServer.Runtime`(同仓库上一目录)，不在本 Readme 范围内，仅在有依赖关系处提及。

***

## 1. 项目概述

- **功能定位**：SCADA（数据采集与监控）服务端的「地基」层。它不自己决定业务规则，而是把上层（Application / Runtime / WebApi）提出的需求落地为「能落地的东西」：

  - 把所有实体通过 **EF Core + MySQL** 持久化（DbContext、迁移、仓储、工作单元）。

  - 通过统一 **`IProtocolDriver`** 接口对接多种设备协议（S7 / OPC UA / 虚拟设备），屏蔽底层 TCP 细节。

  - 把变量历史数据写入 **InfluxDB 2.x** 时序库，并提供查询/清理/导出。

  - 维护 `MqttManager`、系统资源监控等基础设施能力。

- **业务目标（从代码反推）**：为上层提供「一键存取配置、连通设备、存储历史」。整套服务要做到：协议可插拔、配置热重载、设备断线能重连、历史数据与实时数据分开存。

- **使用场景（从代码反推）**：工业/楼宇等 SCADA 场景，接入西门子 PLC（S7）、OPC UA 服务器、虚拟/模拟设备，采集变量值并入库；配置信息存 MySQL，历史时序数据存 InfluxDB；对外提供 MQTT 转发（当前为占位）。

***

## 2. 技术栈清单

**项目本身**

- 语言/框架：C#，.NET 8（`net8.0`），`Microsoft.AspNetCore.App` FrameworkReference，`Microsoft.Extensions.Hosting.Abstractions`。

- 依赖的两个本仓库项目：`ScadaServer.Application`、`ScadaServer.Domain`（见 csproj `ProjectReference`）。

**主数据库（MySQL）**

- `Pomelo.EntityFrameworkCore.MySql` 8.0.3（EF Core 的 MySQL 提供程序）

- `Microsoft.EntityFrameworkCore` 8.0.17（含 Relational、Tools）

- `MySqlConnector` 2.6.2（被 RuntimeDatabaseService 直接用于连接测试）

**时序数据库（InfluxDB）**

- `InfluxDB.Client` 5.1.0

**设备通信协议**

- `S7netplus` 0.20.0 —— 西门子 S7 PLC（`S7Driver`）

- `OPCFoundation.NetStandard.Opc.Ua` 1.5.378.145 —— OPC UA（`OpcUaDriver`）

- `MQTTnet` 5.1.0.1559 —— MQTT 客户端（`MqttManager`）

**系统监控**

- `System.Diagnostics.PerformanceCounter` 10.0.8 —— 仅 Windows 下采集 CPU/内存/磁盘/网络

**外部服务/依赖**（接入的外部系统）

- MySQL 主库（配置在 WebApi 的 `appsettings.dboverride.json` / `SystemDbConfig` 段，本层通过 `IOptions<SystemDbOptions>` 读取）

- InfluxDB 2.x（`DatabaseConfig` 实体管理，`InfluxStore` 热加载）

- MQTT Broker（`MqttVariableConfig` 实体映射）

- S7 PLC / OPC UA 服务器（设备实体 + `DeviceConfig.JsonConfig`）

***

## 3. 目录结构解析

```
ScadaServer.Infrastructure/
├── ScadaServer.Infrastructure.csproj   ├─ 工程定义/依赖
├── Communication/                    ★ 设备通信层（核心业务）
│   ├── ProtocolDriverFactory.cs      协议驱动工厂（按 DriverKey 创建驱动、协议解耦真相源）
│   ├── IDeviceRuntimeManager.cs      设备运行时管理接口（RefreshDevice/ReloadAll）
│   ├── DeviceRegistry.cs             设备配置内存缓存（线程安全，ConcurrentDictionary）
│   ├── S7Driver.cs                   西门子 S7 驱动（S7netplus）
│   ├── OpcUaDriver.cs                OPC UA 驱动（含自动重连状态机）
│   ├── VirtualDriver.cs              虚拟/模拟设备驱动
│   ├── MqttManager.cs                MQTT 多服务器管理器（连接/重连/发布）
│   └── MqttHandler.cs                IMqttService 占位实现（空实现）
├── Influx/
│   └── InfluxStore.cs               ★ InfluxDB 时序库访问（热重载+引用计数，核心）
├── Persistence/                      ★ 主库持久化（核心）
│   ├── ScadaDbContext.cs             EF Core 上下文（全部 DbSet + Fluent 配置）
│   ├── DatabaseInitializer.cs        建库迁移 + 种子数据
│   ├── EfUnitOfWork.cs               工作单元（含兼容执行策略的事务封装）
│   └── DesignTimeScadaDbContextFactory.cs  EF 迁移设计时工厂（供 dotnet ef）
├── Repositories/                     ★ 仓储层（核心）
│   ├── RepositoryBase.cs             泛型仓储基类（增删改查/分页）
│   ├── AlarmRecordRepository.cs      报警记录仓储（含批量恢复的 SQL）
│   ├── VariableHistoryRepository.cs  变量历史仓储（按设备+变量+时间）
│   ├── DeviceVariableRepository.cs   设备变量仓储
│   └── …（约 25 个实体仓储，见下）
├── Services/                         基础设施服务（工具/后台）
│   ├── SystemMonitorService.cs       CPU/内存/磁盘/网络监控（BackgroundService）
│   ├── RuntimeDatabaseService.cs     主库配置读写 override + 连接测试
│   └── HistoryMigrationService.cs    MySQL 历史存量 → InfluxDB 迁移
├── Migrations/                        EF Core 迁移脚本（建表/演进历史，脚手架生成）
└── DBEntities/                       空文件夹（csproj 声明，暂空）
```

要点：

- **核心业务** = `Communication/`、`Persistence/`、`Repositories/`、`Influx/`、`Services/`（三个服务）。

- **工具/配置** = `Migrations/`（脚手架）、`DesignTimeScadaDbContextFactory`（供迁移工具）、`DBEntities/`（空占位）。

- **静态资源**：本层无前端/静态资源。前端在仓库根 `Client/`，不属于本项目。

`Repositories` 完整清单（继承 `RepositoryBase<,>` 实现各 `IRepository`，多表组合查询放这里）：`AlarmRecord / AlarmRule / Area / ConfigLog / DataConversion / DataModel / DatabaseConfig / DeviceConfig / Device / DeviceVariable / ExposedInterface / HmiComponent / LinkageRule / ModelVariable / MqttServer / MqttVariableConfig / Protocol / ScadaPage / ScadaProject / ScheduledTask / ScriptExecutionRecord / Sensor / SystemConfig / SystemLog / SystemScript / SystemUser / VariableHistory / VariableRealtime`。

***

## 4. 整体架构 & 分层说明

本层在整个方案中的位置（依赖方向：**上层 → 本层**）：

```
WebApi(Controllers/HostedServices)
   → Application(接口 IXXXAppService / IRepository / IUnitOfWork / IInfluxStore)
   → Domain(实体/枚举/IRepository/IProtocolDriver/IRuntimeDevice/IRuntimeVariable)
   → Infrastructure(本层，实现上述接口：DbContext/仓储/驱动/时序库)
```

**本层内部职责分工**：

| 子模块             | 职责                         | 禁止做的事                                                                      |
| --------------- | -------------------------- | -------------------------------------------------------------------------- |
| `Persistence`   | 管 MySQL 连接、DbContext、迁移、事务 | 不直接写业务规则                                                                   |
| `Repositories`  | 封装实体增删改查与组合查询              | 不调用其它仓储（分层内组合应通过上层）                                                        |
| `Communication` | 连接/读写/订阅/断线重连物理设备          | 驱动不得感知 `DataModel`/`ModelVariable`（只认 `IRuntimeDevice`/`IRuntimeVariable`） |
| `Influx`        | 历史时序数据写/查/清理/导出            | 不做主库业务                                                                     |
| `Services`      | 系统监控、主库配置管理、历史迁移           | 监控服务只采集展示，不做控制                                                             |

**关键架构约定（从代码注释反推）**

- **协议解耦**：`ProtocolDriverFactory.CreateDriver(DriverKey)` 是唯一入口；`DriverKey` 来自数据库 `Protocol.DriverKey`。驱动只依赖 `Domain` 的 `IProtocolDriver/IRuntimeDevice/IRuntimeVariable`，不碰模型模板实体（见 [IProtocolDriver.cs 注释](file:///d:/CSharp/SCADA/Server/ScadaServer.Domain/Interfaces/IProtocolDriver.cs)）。

- **工作单元**：`IUnitOfWork` 提供事务封装，且 `ExecuteInTransactionAsync` 会把「开事务+业务+提交」包进 `CreateExecutionStrategy()`（因为配置了 `EnableRetryOnFailure`，手动开事务必须放进策略内，见 [EfUnitOfWork.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Persistence/EfUnitOfWork.cs) 的注释）。

- **运行时与持续化分离**：实时类数据（变量实时快照表 `VariableRealtime`）由 Runtime 的 `RealtimeSnapshotService` 批量 Upsert（`ScadaDbContext` 中注释），本层只提供表和仓储，不负责写入时序逻辑。

***

## 5. 核心业务数据流

以下为「能根据本层代码确认」的几条链路；进入本层的入口是上层接口调用。

**5.1 设备采集值写入历史库（InfluxDB）**

```
Runtime 采集（不在本层）拿到变量值
  → 拼 List<VariableHistory>
  → IInfluxStore.WriteAsync(List<VariableHistory>)   [InfluxStore.cs]
      → AcquireHolder() 取 InfluxDBClient 引用（若已配置）
      → BuildPoints() 按行协议组装：
            measurement=variable_history
            tags=device_key + variable_key
            fields=value/raw_value/quality/device_id/variable_name
            时间戳统一 UTC、纳秒精度
      → GetWriteApiAsync().WritePointsAsync(...)  最多重试 3 次、退避递增
  → 存储到 InfluxDB bucket
```

**5.2 历史趋势查询**

```
前端(WebApi HistoryController 在本层之外) 传入 device_key/variable_key/时间段/聚合
  → IInfluxStore.QueryLatestAsync(...)              [InfluxStore.cs]
      → BuildQuery() 组装 FLUX：
            range → measurement+device_key+variable_key 过滤
            → 可选 aggregateWindow（函数白名单 mean/max/min/first/last）
            → pivot 合并 fields → 按时间倒序 → limit → 再正序
      → GetQueryApiSync().QuerySync() → 映射为 List<HistoryRecordDto>
  → 返回给上层
```

**5.3 设备配置 → 运行时注册（供上层启动/刷新设备）**

```
上层(WebApi/Runtime) 新增/编辑设备后
  → DeviceRegistry.UpdateDevice(device, variables)   [DeviceRegistry.cs]
      写入 ConcurrentDictionary<int,(Device,List<ModelVariable>)>
  → IDeviceRuntimeManager.RefreshDevice(deviceId) / ReloadAll()   [IDeviceRuntimeManager.cs]
      （具体刷新动作由 Runtime 的 RuntimeManager 实现，不在本层）
```

**5.4 数据库迁移与种子初始化（启动时）**

```
WebApi 启动 → DatabaseInitializer.InitializeAsync()   [DatabaseInitializer.cs]
  → _db.Database.MigrateAsync()       应用 Migrations 建表
  → SeedDataAsync()
       默认区域 Areas("默认区域")
       默认协议 Protocols(S7/OPCUA/VIRTUAL 启用，MODBUSTCP/MQTT 停用)
       默认管理员 SystemUser(admin/123456，PasswordHasher<SystemUser> 哈希)
       DbVersion 版本记录(1.0.0)
```

**5.5 MySQL 存量历史 → InfluxDB 迁移（手动触发）**

```
上层触发 IHistoryMigrationService.MigrateAsync()   [HistoryMigrationService.cs]
  → 用 IServiceScopeFactory 开 scope 取 IVariableHistoryRepository
  → 每批 2000 行按 Id 递增读出；每 500 点一片写入 InfluxStore
  → 单飞锁保证同时仅一个迁移任务
```

***

## 6. 核心入口文件、关键类说明

**启动/初始化入口**

- [DatabaseInitializer.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Persistence/DatabaseInitializer.cs)：应用迁移+种子数据，含默认协议(区分已实现/待实现)与默认 admin(带弱口令告警日志)。

- [DesignTimeScadaDbContextFactory.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Persistence/DesignTimeScadaDbContextFactory.cs)：仅供 `dotnet ef` 用，从多种相对路径读 `appsettings.json` 的 `SystemDbConfig`，固定 ServerVersion 8.0.36 不触发真实连接。

**公共基础设施关键类**

- [ScadaDbContext.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Persistence/ScadaDbContext.cs)：全部 `DbSet` + `OnModelCreating`。重点配置：

  - `Dictionary<string,string>` ↔ JSON 的 `ValueConverter/ValueComparer`（`ExtensionData` 字段）。

  - 多张另行约定：`ExposedInterfaces(RouteUrl,RequestMethod)` 唯一索引、`ModelVariables(ModelId,Key)` 唯一索引、`MqttVariableConfig(MqttServerId,DeviceId,VariableKey)` 唯一索引、`SystemUsers.Username` 唯一索引、`Devices.Key` 唯一索引；大表(AlarmRecords/VariableHistory/ScriptExecutionRecords) 刻意不建外键保写入性能。

- [RepositoryBase.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Repositories/RepositoryBase.cs)：泛型仓储基类，覆盖增删改查/分页/计数（注：分页固定按 `Id` 排序，见潜在问题 8.5）。

- [EfUnitOfWork.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Persistence/EfUnitOfWork.cs)：事务封装，含 `ExecuteInTransactionAsync` 执行策略内事务；`EfTransactionScope.DisposeAsync` 未提交自动回滚。

- [ProtocolDriverFactory.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/ProtocolDriverFactory.cs)：驱动注册表（S7/OPCUA/VIRTUAL 可用；MODBUSTCP/MQTT 抛 `NotSupportedException`）。

**设备通信核心类**

- [S7Driver.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/S7Driver.cs)：S7 采集/读写。

  - 并发模型：`SemaphoreSlim(1,1)` 串行化 PLC 访问，且约定「**永不 Dispose**」信号量（避免与并发 WaitAsync 竞态，见注释）。

  - 状态机：`Interlocked.Exchange` 原子迁移 `Active→Closed` 单向；所有操作获锁后**锁内复检**状态；PLC 关闭集中在 `ClosePlcUnderLockAsync` 且在锁内。

  - 地址解析 `ParseAddress`（internal，供单测）→ `S7AddressInfo`只含位置+宽度；**值解释类型唯一由** **`IRuntimeVariable.DataType`** **决定**，有 `ValidateTypeMatch` 校验地址宽度与类型匹配。

  - 批量读取按 `Area+DbNumber` 分组→组内按偏移排序→`MaxClusterGapBytes=200` 字节间隙聚簇合并读取，减少往返。

  - 通信失败日志「闸门」：首败 Warning（带完整上下文），持续失败降 Debug，恢复记 Information。

  - 读写都有 `CancellationToken` 超时封顶（默认 `IoTimeoutMs=5000`，可配置 500\~60000）。

- [OpcUaDriver.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/OpcUaDriver.cs)：OPC UA 客户端。

  - 双锁并发模型：`_lifecycleLock`(生命周期) + `_ioStateLock`(IO 计数)；锁序恒为 lifecycle→ioState。

  - 状态机：Disconnected/Connecting/Connected/Reconnecting/Disconnecting/Disposed；KeepAlive 断线自动重连，退避 `{5,10,20,30,60}s`，连续失败 5 次释放会话交还运行时层。

  - IO 引用计数 + `_drainTcs`(RunContinuationsAsynchronously) 保证 Disconnect/Reconnect 前排空在途读写，杜绝「拿到 Session 引用后被他线程 Dispose」。

  - 订阅：按 `PollingIntervalMs` 分组建 `Subscription`，`MonitoredItem` 回调推值。

  - 读取校验 StatusCode，Bad/Uncertain 抛带语义异常（区分节点不存在/权限拒绝/超时等）。

- [VirtualDriver.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/VirtualDriver.cs)：模拟驱动，按 DataType 生成随机/区间中点值；写入值缓存可回读；键带设备维度防串值；有随机数加锁。

- [DeviceRegistry.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/DeviceRegistry.cs)：设备配置内存缓存（ConcurrentDictionary）。

**其它服务类**

- [SystemMonitorService.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Services/SystemMonitorService.cs)：`BackgroundService`，每 2 秒采集系统指标；仅 Windows 用 PerformanceCounter，跨平台跳过；维护静态轮询包计数器 `TotalPollPackets`。

- [RuntimeDatabaseService.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Services/RuntimeDatabaseService.cs)：主库配置读写 `appsettings.dboverride.json`（密码掩码回显）、MySQL/InfluxDB 连接测试。

- [HistoryMigrationService.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Services/HistoryMigrationService.cs)：MySQL→InfluxDB 批量迁移（单飞锁）。

**注入注册位置（不在本层代码内）**

- 本层各服务的 DI 注册在 WebApi 的 [Extensions/Infrastructure.Extensions.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.WebApi/Extensions/Infrastructure.Extensions.cs) 中（`AddInfrastructureServices`）：`DeviceRegistry`、`IProtocolDriverFactory`、`SystemMonitorService`(BackgroundService)、`InfluxStore`/`IInfluxStore`、`IRuntimeDatabaseService`、`IHistoryMigrationService`、`IMqttService->MqttHandler` 等。

- 注意：`MqttManager` 虽在本层，但**未在** **`AddInfrastructureServices`** **中注册**；它似乎由外部（WebApi 其它扩展）注册，具体注册位置【缺少文件，无法确认】。

***

## 7. 外部依赖 & 第三方交互

| 外部对象         | 通信方式                         | 超时/重连                                                                         | 密钥/注意点（来自代码）                                               |
| ------------ | ---------------------------- | ----------------------------------------------------------------------------- | ---------------------------------------------------------- |
| MySQL 主库     | EF Core(Pomelo)              | `EnableRetryOnFailure`（EfUnitOfWork 注释提及按执行策略重试）；连接测试用 `MySqlConnector`       | 配置在 override 文件，变更需重启                                      |
| InfluxDB 2.x | `InfluxDB.Client`            | 写失败重试 3 次退避递增                                                                 | `Rebuild()` 热切换客户端，配置变更即时生效；引用计数保证切客户端时不误释放                |
| S7 PLC       | `S7netplus`，TCP + COTP/S7 握手 | 连接/读写均有超时封顶 5s(可配)；读失败由上层重连；自动分组聚簇读                                           | 地址只为位置+宽度，类型由 DataType 决定                                  |
| OPC UA 服务器   | OPC.NET SDK，`opc.tcp://`     | KeepAlive 5s；自动重连退避 {5,10,20,30,60}s，连败 5 次放弃当前会话                             | `AutoAcceptUntrustedCertificates=true`（见潜在问题 8.6）；支持用户名/密码 |
| MQTT Broker  | `MQTTnet` 5.x，TCP            | 连接超时 5s；`ReloadAsync`/`ReconnectAsync` 维护连接；`MqttReconnectHostedService` 不在本层 | `MqttHandler`(IMqttService 实现) 是空占位                        |

**关于重连的补充**：S7 与 OPC UA 的「真正重连」由本层 driver 内的状态机/退避完成；但驱动生命周期（何时 Connect/Disconnect/重建）由上层 Runtime 的 `DeviceWorker` 驱动（不在本层，详见 `ScadaServer.Runtime`）。本层只保证「单次连接可超时、断开可自主清理，不泄漏 Session/Plc」。

***

## 8. 潜在问题与优化建议

以下按「**必须修复阻断问题**」与「**可选优化**」分类。⚠️ 部分结论基于代码注释/上下文推断，标注【推断】。

### 【必须修复阻断问题】

- **8.1 默认管理员弱口令（开发占位）**：`DatabaseInitializer` 创建 `admin/123456`。虽然代码有 `LogWarning` 提示改密，但若直接上生产就是严重安全洞。建议：首次登录强制改密，或从环境变量/密钥注入初始密码，移除硬编码。

  - 位置：[DatabaseInitializer.cs L136-164](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Persistence/DatabaseInitializer.cs#L136-L164)

- **8.2 OPC UA** **`AutoAcceptUntrustedCertificates=true`**：生产环境会接受任意不受信任的服务器证书（中间人风险）。建议：改为按配置显式决定，或提供受信证书白名单。

  - 位置：[OpcUaDriver.cs L142](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/OpcUaDriver.cs#L142)

- **8.3 MQTT 主题字符串拼接无转义**：`topic = $"{prefix}/{mapping.Alias}"`，未对 `Alias`做过滤，含特殊字符时可能构造非法/意外主题。

  - 位置：[MqttManager.cs L311-316](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/MqttManager.cs#L311-L316)

- **8.4 InfluxDB FLUX 字符串转义不完整**：`Escape/EscapeForPredicate` 只处理 `\` 和 `"`，对 `device_key`/`variable_key`/`bucket` 中可能出现的其它 FLUX 特殊字符（如换行、`}`）未全覆盖。虽然 device/variable key 通常是后端自己生成的键【推断】，但若允许用户自定义后缀则存在注入风险。【推断】

  - 位置：[InfluxStore.cs L831-858](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Influx/InfluxStore.cs#L831-L858)

### 【可选优化】

- **8.5 分页固定按** **`Id`** **排序**：`RepositoryBase.GetPagedListAsync` 用 `EF.Property<int>(e,"Id")`，若某实体 Id 不是 int（如 `VariableHistory`/`AlarmRecord` 是 long），此处会出错或行为异常【推断：泛型 TKey 与 int 混用】。建议改为按 `TKey` 排序或用通用列。

  - 位置：[RepositoryBase.cs L41-54](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Repositories/RepositoryBase.cs#L41-L54)

- **8.6 MqttHandler 是空实现**：`IMqttService.PublishAsync/SubscribeAsync` 直接 `await Task.CompletedTask`，即「对外宣称已实现但实际什么都不做」。若某流程误以为它已发布成功，会静默丢数据。建议至少打一条错误日志，或在未实现时抛出明确异常。

  - 位置：[MqttHandler.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/MqttHandler.cs)

- **8.7** **`SystemMonitorService`** **空 catch 吞异常**：构造/采样阶段大量 `catch { }` 无日志，排障困难。建议降级为 Debug 日志或至少记录首次失败。

  - 位置：[SystemMonitorService.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Services/SystemMonitorService.cs)

- **8.8 大空 catch**：`InfluxStore.DisposeClient` 的 catch 为空（注释说明「不抛异常」）。建议至少 Debug 级记录，便于排查资源释放异常。

  - 位置：[InfluxStore.cs L1066-1077](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Influx/InfluxStore.cs#L1066-L1077)

- **8.9 单元测试覆盖不足**：目前仅 `ScadaServer.Infrastructure.Tests` 下有一个 `S7DriverAddressParsingTests.cs`（通过 `InternalsVisibleTo` 测 `ParseAddress`）。OPC UA 的重连状态机、InfluxStore 的引用计数等复杂并发逻辑无单测覆盖，建议补充。

- **8.10 主库密码写入 override 文件为明文**：`RuntimeDatabaseService.SaveMainConfigAsync` 把明文密码写入 `appsettings.dboverride.json`（有掩码回显，但落盘明文）。建议确认该文件不进版本库、权限收紧，或使用密钥/DPAPI。

  - 位置：[RuntimeDatabaseService.cs L80-95](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Services/RuntimeDatabaseService.cs#L80-L95)

***

## 9. 新手阅读顺序建议

> 目标是「最快读懂本层在干什么、怎么接的」，建议由外到内：

1. **先看工程与接线**（1 分钟建立全局）：

   - [ScadaServer.Infrastructure.csproj](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/ScadaServer.Infrastructure.csproj) —— 知道用了哪些库、依赖哪两层。

   - WebApi 的 [Infrastructure.Extensions.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.WebApi/Extensions/Infrastructure.Extensions.cs) —— 看哪些服务被注册成单例/宿主，知道注入关系（虽然在另一个工程，但它是本层的「接线图」）。
2. **看数据持久化**（业务的地基）：

   - [ScadaDbContext.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Persistence/ScadaDbContext.cs) —— 表结构 + 索引/约束/外键约定。

   - [DatabaseInitializer.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Persistence/DatabaseInitializer.cs) —— 建库、种子。

   - [RepositoryBase.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Repositories/RepositoryBase.cs) —— 仓储基类，看懂增删改查骨架。
3. **看时序历史存储**（本项目最有代表性的一个类）：

   - [InfluxStore.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Influx/InfluxStore.cs) —— 重点关注 `AcquireHolder/ClientHolder` 引用计数（读历史即可，不必读透 FLUX 每行）。
4. **看设备通信**（核心困难点）：

   - 先 [ProtocolDriverFactory.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/ProtocolDriverFactory.cs) 和 [IProtocolDriver.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Domain/Interfaces/IProtocolDriver.cs)（接口在 Domain）—— 理解统一抽象。

   - 再 [S7Driver.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/S7Driver.cs)（理解并发锁+地址解析+批量聚簇）→ [OpcUaDriver.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/OpcUaDriver.cs)（理解并发状态机+自动重连）→ [VirtualDriver.cs](file:///d:/CSharp/SCADA/Server/ScadaServer.Infrastructure/Communication/VirtualDriver.cs)（最简单，可放最前建立信心）。
5. **最后看服务与迁移**：`SystemMonitorService`（监控）、`RuntimeDatabaseService`（主库配置）、`HistoryMigrationService`（历史迁移），以及 `Migrations/` 目录（了解演进历史）。

> 提示：`S7Driver` 和 `OpcUaDriver` 并发模型都很讲究（尤其 OPC UA），如果只是了解业务流程可先略过内部并发细节；需要改代码前务必读透其「并发控制模型」注释。

***

## 10. 后续可扩展方向

- **补齐 Modbus TCP 驱动**：`ProtocolDriverFactory` 已为 `MODBUSTCP` 预留分支（现抛异常），配置层 `DatabaseInitializer` 也已预置停用的 Modbus 协议；实现 `IProtocolDriver` 后启用即可。

- **补全 MQTT 订阅驱动与 MqttHandler**：`MqttManager` 已具备多服务器连接/重连/发布能力，但 `IMqttService` 仍是空占位；可打通「MQTT 订阅 → 变量」双向通道。

- **为配置类实体统一加创建/更新时间**：多处种子手动设置 `CreatedAt/UpdatedAt`（见 `DatabaseInitializer`），可抽成基类/拦截器自动填充。

- **配置热重载统一化**：目前 Influx 配置可热切，主库配置需重启；可参考 InfluxStore 的 `Rebuild` 模式统一管理「易变的连接配置」。

- **增强测试**：针对 OPC UA 重连状态机、InfluxStore 引用计数、S7 聚簇/类型校验补单测（现有基础设施已通过 `InternalsVisibleTo` 支持 internal 测试）。

***

> 本文档将随 `ScadaServer.Infrastructure` 项目持续更新。若后续新增文件或改动本层代码，可在本 Readme 基础上追加对应章节/修订记录，保持版本连贯。

