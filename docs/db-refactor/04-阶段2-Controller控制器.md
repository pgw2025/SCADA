# 阶段 2：Controller 控制器（2-3 天，低风险纯新增）

> 目标：新增目标设计的 `Controller`（控制器/PLC 资产）实体与管理界面。本阶段**只加表、加页面，不接线**——不修改任何现有设备/模型/运行时代码，Device 与新表暂无任何外键关系。
> 前置：阶段 0 完成（与阶段 1 无依赖，可并行或调序）。

***

## 设计要点

1. **实体命名**：`Controller`（命名空间 `ScadaServer.Domain.Entities`，与 WebApi 的 Controller 层无编译冲突；代码评审时统一称"控制器实体"）。表名 `Controllers`。
2. **ControllerType 落地**：目标设计的 ControllerType（PLC/OPCUA Server）直接使用 `ProtocolId` FK → `Protocols`（协议即控制器类型，决策 D3 延伸）。
3. **与 DataModel 的职责切割**：DataModel（业务设备模板，如"输送带标准模型"）保持不动；Controller 承接"物理控制硬件资产"（S7-1500、Kepware 服务器）。当前混在 DataModel.Vendor/ModelName 里的控制器型号信息，本阶段不迁移——留待阶段 3 回填时复制到 Controller.Manufacturer/Model。
4. 本阶段结束时允许"空表"：用户可先手工录入控制器台账（编码/名称/厂商/型号/协议/启用）。
5. 主键 int 自增（决策 D4）；时间戳 UTC（决策 D5）。

## 实体字段（目标设计对齐）

| 字段                    | 类型               | 说明                         |
| --------------------- | ---------------- | -------------------------- |
| Id                    | int 自增           | 主键                         |
| Code                  | string(50)       | 控制器编码，唯一索引                 |
| Name                  | string(100)      | 控制器名称                      |
| ProtocolId            | int FK→Protocols | 控制器类型/所用协议（S7、OPCUA...）    |
| Manufacturer          | string(100)?     | 厂商（Siemens/Kepware...）     |
| Model                 | string(100)?     | 型号（S7-1500/KEPServerEX...） |
| Description           | string(500)?     | 描述                         |
| IsEnabled             | bool             | 启用（禁用后不可被新连接引用）            |
| CreatedAt / UpdatedAt | DateTime         | UTC                        |

***

## 步骤 2.1：实体 + DbContext + 迁移

**任务**：

1. 新增 `ScadaServer.Domain/Entities/Controller.cs`（字段如上；导航属性 `Protocol`、`List<DeviceConnection> Connections` 占位）。
2. [ScadaDbContext.cs](../../Server/ScadaServer.Infrastructure/Persistence/ScadaDbContext.cs)：`DbSet<Controller> Controllers`；FK `ProtocolId`→Protocols（Restrict）；唯一索引 `ix_controllers_code`；字段长度配置。
3. `dotnet ef migrations add AddControllerEntity`，评审 SQL（纯 CREATE TABLE + 索引）。

**验收**：库中出现空表 `Controllers`；Code 唯一约束生效。

## 步骤 2.2：仓储 + 应用服务

**任务**：

1. 新增 `ScadaServer.Infrastructure/Repositories/ControllerRepository.cs`（继承 RepositoryBase 模式，参考 [ProtocolRepository.cs](../../Server/ScadaServer.Infrastructure/Repositories/ProtocolRepository.cs)）。
2. 新增 `ScadaServer.Application/Services/ControllerAppService.cs` + DTO（ControllerDto/CreateRequest/UpdateRequest）：

   - CRUD + 列表（支持按 ProtocolId/关键字过滤、分页）；

   - 删除校验：存在引用时拒绝（本阶段仅协议存在性校验，阶段 3 后追加"有连接引用不可删"）；

   - Code 唯一冲突返回业务错误（参考 ModelVariableAppService 的唯一索引冲突处理模式）。

**验收**：服务层单元可用（或经 API 手测）；重复 Code 报错友好。

## 步骤 2.3：API 控制器

**任务**：新增 `ScadaServer.WebApi/Controllers/ControllerManagementController.cs`（路由建议 `/api/controllers`，**避免与现有 DeviceController 等路由冲突**）：

- `GET /api/controllers`（分页+过滤）、`GET /{id}`、`POST`、`PUT /{id}`、`DELETE /{id}`、`GET /api/controllers/options`（下拉数据源：Id+Code+Name+Protocol）。

- 遵循现有 Controller 的返回包装与错误处理约定（参考 [ProtocolController.cs](../../Server/ScadaServer.WebApi/Controllers/ProtocolController.cs)）。

**验收**：Swagger 全端点可用。

## 步骤 2.4：前端控制器管理页

**任务**：

1. 新增 `Client/src/api/controllerApi.ts`（沿用 deviceApi.ts 的 axios 封装风格，**相对路径**）。
2. 新增 `Client/src/components/ControllerManagementView.vue`：列表（编码/名称/协议/厂商/型号/启用）、新建/编辑弹窗、删除确认；协议下拉数据来自现有协议 API。
3. 路由与菜单注册（沿用现有视图注册方式）。

**验收**：页面 CRUD 全流程可用；`npm run build` 通过。

## 步骤 2.5：收尾

**任务**：重跑阶段 0 冒烟清单（本阶段不触运行时，预期全绿）；合并分支，tag `db-refactor-phase2`。

***

## 回滚方案

纯新增：回滚应用即可；表可保留（无任何现有代码引用）或 `Update-Database` 回退。

## 风险与注意

| 风险                                    | 对策                                                                               |
| ------------------------------------- | -------------------------------------------------------------------------------- |
| 命名混淆（Controller 实体 vs API Controller） | 命名空间隔离 + 评审约定；API 路由用 `/api/controllers` 与现有 DeviceController（`/api/devices`）无冲突 |
| 用户误以为录入控制器后系统会自动连接                    | 页面明示"控制器为资产台账，连接配置在后续版本接入"；本阶段不产生任何采集行为                                          |

***

## 完成记录（2026-09-03）

- **2.1** ✅ [Controller.cs](../../Server/ScadaServer.Domain/Entities/Controller.cs) 实体、DbContext DbSet + FK(Restrict) + 唯一索引 `ix_controllers_code`、迁移 `20260903065208_AddControllerEntity.cs`（纯 CREATE TABLE）已应用。

- **2.2** ✅ [ControllerRepository.cs](../../Server/ScadaServer.Infrastructure/Repositories/ControllerRepository.cs)（分页/协议/关键字过滤）、[ControllerAppService.cs](../../Server/ScadaServer.Application/Services/ControllerAppService.cs)（CRUD + Code 唯一 + 协议存在性校验）、[ControllerDto.cs](../../Server/ScadaServer.Application/DTOs/ControllerDto.cs)（Create/Controller/Query/Paged/Option）。

- **2.3** ✅ [ControllerManagementController.cs](../../Server/ScadaServer.WebApi/Controllers/ControllerManagementController.cs) 路由 `/api/controllers`（RequireAdmin + AuditLog）：GET 分页过滤、GET options、GET {id}、POST、PUT {id}、DELETE {id}。

- **2.4** ✅ [controllerApi.ts](../../Client/src/api/controllerApi.ts)（相对路径 + 仿真降级）、[ControllerManagementView.vue](../../Client/src/components/ControllerManagementView.vue)（列表/筛选/分页/新建/编辑/删除确认/启用开关）、路由 `/controller-management` 与桌面/移动端菜单「控制器管理」已注册（App.vue）。

- **2.5** ✅ 后端 `dotnet build` 0 错误；前端 `npm run build` 通过（ControllerManagementView 独立 chunk 生成）；API 冒烟测试全绿：列表/创建/下拉/更新/关键字过滤/协议过滤/重复 Code→400/无效协议→400/删除，测试数据已清理。

**验证摘要（手测）**：登录后 `GET /api/controllers` 空表返回 total=0；创建返回完整 DTO；`/options` 返回下拉；重复编码返回 400 业务错误；删除后表回到空。控制器为纯资产台账，未改动任何设备/模型/运行时采集代码。
