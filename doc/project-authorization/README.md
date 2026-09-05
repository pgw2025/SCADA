# 组态工程授权（Project Authorization）方案文档

## 1. 背景与目标

当前系统的授权体系只有**角色级**（`RequireAdmin`：Admin / Operator / Viewer），没有**资源级**授权。组态工程（`ScadaProject`）对任何已登录用户完全可见：

- `GET /api/ScadaProject`（工程列表）无条件返回全部工程；
- `GET /api/ScadaProject/{id}/full`（工程整树）、`GET /api/ScadaProject/{id}/export` 等只要求登录，不校验工程归属；
- 前端「组态运行」卡片页（`ProjectPickerView.vue`）直接展示后端返回的全量列表。

本方案为组态工程增加**用户级授权**，实现：

- **只有被授权的用户**，其「组态运行」工程列表中才会出现该工程，才能打开进入组态画面；
- 未授权用户列表中看不到该工程，直接访问工程 URL（列表/详情/整树/导出等）一律返回 **404**（不泄露工程存在性）；
- **Admin 角色恒可见全部工程**，无需授权记录，并独占授权管理入口；
- 授权管理由管理员在「组态运行」卡片页完成（工程维度勾选用户，全量覆盖式保存）。

## 2. 文档索引

| 文档 | 内容 |
|---|---|
| [01-现状分析与方案审查.md](01-现状分析与方案审查.md) | 现状代码事实、v1 方案审查发现的 3 个问题（旁路绕过 / 前端缓存绕过 / 导出语义未定）及修订决策 |
| [02-详细设计.md](02-详细设计.md) | 数据模型、仓储与服务接口契约、API 契约、安全矩阵、前端交互设计、授权生效时效说明 |
| [03-执行计划.md](03-执行计划.md) | **8 个阶段、21 个步骤**的完整执行计划：每步任务、涉及文件、具体改动、验收标准 |
| [04-测试与验收.md](04-测试与验收.md) | 集成验证清单（按角色 × 场景）、回归验收清单、风险与缓解措施 |

## 3. 阶段总览

| 阶段 | 名称 | 步骤数 | 关键交付物 | 依赖 |
|---|---|---|---|---|
| 一 | 数据层：授权实体与仓储 | 4 | `ScadaProjectAuthorization` 实体、EF 配置与迁移、授权仓储 | 无 |
| 二 | 当前用户服务 | 2 | `ICurrentUser` 接口与 WebApi 实现、DI 注册 | 无 |
| 三 | 后端授权核心 | 4 | `GetListAsync` 按用户过滤、`GetById`/`GetTree` 授权校验、授权读写方法 | 一、二 |
| 四 | 授权管理 API 与导出权限 | 2 | `GET/PUT /{id}/authorizations` 端点、工程导出收紧为 Admin | 一、三 |
| 五 | 旁路封堵 | 2 | ScadaPage / HmiComponent 读取与画面导出端点收紧为 Admin | 无（可与三并行） |
| 六 | 前端状态与缓存清理 | 2 | `resetScadaStore()` 登出/登录接线、播放器非 Admin 强制回源 | 三 |
| 七 | 前端授权管理 UI | 3 | 授权 API 封装、工程卡片授权弹窗、空态文案区分 | 四 |
| 八 | 验证与收尾 | 2 | 构建/迁移验证、集成验证与回归测试 | 全部 |

> 阶段一、二相互独立可并行；阶段五（旁路封堵）不依赖新代码，仅加特性标注，可最先落地或与任意阶段并行；阶段三是集成核心；六、七依赖后端完成；八收尾。

## 4. 核心设计决策（速览）

| # | 决策 | 理由 |
|---|---|---|
| D1 | 授权模型 = 中间表 `ScadaProjectAuthorizations`（`ProjectId + UserId` 复合主键，双外键 Cascade） | 工程 ↔ 用户是标准多对多；删除工程/用户时授权记录由 FK 级联自动清理，无孤儿数据 |
| D2 | Admin 恒可见全部工程，不做授权记录；授权对象仅为 Operator/Viewer | 管理员本身管理全部工程，为其维护授权记录是噪音；保存授权时后端自动剔除 Admin 用户 |
| D3 | 未授权访问返回 **404** 而非 403 | 不泄露工程存在性（对未授权用户而言，该工程"不存在"）；且无需区分「不存在/无权限」两种语义 |
| D4 | 工程导出/画面导出统一收紧为 **Admin 专属**（`RequireAdmin`），与导入端点对称 | v1 方案中被授权的 Operator 将能导出工程全量 JSON（画面、组件、变量绑定），授权语义应为「可见可打开」而非「可迁移副本」 |
| D5 | 旁路端点（`GET /api/ScadaPage`、`GET /api/HmiComponent`、`GET /api/ScadaPage/{id}/export`）统一加 `RequireAdmin` | 这三组端点是组态**编辑器**数据面（播放器只用 `/ScadaProject/{id}/full` + 图片 + SignalR）；若不封堵，Operator 可枚举页面/组件数据甚至导出任意画面 JSON，绕过工程授权 |
| D6 | 前端 `scadaStore` 增加 `resetScadaStore()`：登出/登录时清空工程缓存并复位模块级初始化标志 | SPA 无整页刷新：admin 登出 → operator 同页签登录时，`_summariesInitialized` 残留导致列表不重新拉取、`scadaProjects` 缓存可被 `findProject` 直接命中，授权被完全绕过 |
| D7 | 播放器 `selectProject` 对非 Admin **强制回源**（跳过本地缓存直接请求后端） | 管理员撤销授权后，用户已打开的标签页内本地缓存仍可打开工程；强制回源让后端 404 兜底 |
| D8 | `ICurrentUser` 接口定义在 Application 层，实现在 WebApi 层（`IHttpContextAccessor` 解析 JWT claims） | 遵循项目分层惯例（Application 层不引用 ASP.NET Core 类型）；列表过滤与打开校验两处消费 |
| D9 | 保存授权为**全量覆盖**（Replace 语义），同一事务内先删后插 | 弹窗勾选即最终态，交互简单无增量 diff 复杂度；复用现有 `_uow.ExecuteInTransactionAsync` 模式 |
| D10 | 范围外（明确不做）：HmiImage 文件流读取（GUID 文件名不可枚举）、SignalR hub 数据面权限（变量实时值订阅）、页面级/组件级授权粒度 | 与本次「工程可见性」是不同维度；如需收紧另行立项，见 [02-详细设计.md](02-详细设计.md) 第 8 节 |

## 5. 涉及范围一览

```
后端（Server/）
├── ScadaServer.Domain
│   ├── Entities/ScadaProjectAuthorization.cs          [新增] 授权实体
│   └── Interfaces/Repositories/IEntityRepositories.cs [修改] +1 授权仓储接口
├── ScadaServer.Application
│   ├── Interfaces/ICurrentUser.cs                    [新增] 当前用户抽象
│   ├── Interfaces/IScadaProjectAppService.cs          [修改] +4 方法签名
│   ├── DTOs/ScadaProjectAuthorizationDto.cs          [新增] 授权 DTO
│   └── Services/ScadaProjectAppService.cs             [修改] 过滤 + 校验 + 授权读写
├── ScadaServer.Infrastructure
│   ├── Persistence/ScadaDbContext.cs                 [修改] DbSet + OnModelCreating
│   ├── Migrations/xxx_AddScadaProjectAuthorization.cs [新增] EF 迁移
│   └── Repositories/ScadaProjectAuthorizationRepository.cs [新增] 授权仓储实现
└── ScadaServer.WebApi
    ├── Services/CurrentUser.cs                       [新增] JWT claims 解析实现
    ├── Extensions/Application.Extensions.cs           [修改] DI 注册 ×2
    ├── Controllers/ScadaProjectController.cs         [修改] +2 授权端点、Export 收紧
    ├── Controllers/ScadaPageController.cs            [修改] GET/Export 收紧
    └── Controllers/HmiComponentController.cs        [修改] GET 收紧

前端（Client/src/）
├── api/scadaApi.ts                                   [修改] +2 授权 API
├── api/authApi.ts                                    [修改] 登出/登录接线 resetScadaStore
├── store/scadaStore.ts                               [修改] +resetScadaStore、selectProject 回源
└── components/ProjectPickerView.vue                 [修改] +授权按钮、授权弹窗、空态文案
```
