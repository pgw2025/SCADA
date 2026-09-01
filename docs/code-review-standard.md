# SCADA 代码审查标准（Code Review Standard）

> 适用范围：`D:\CSharp\SCADA` 前后端双仓库（后端 .NET 8 / ASP.NET Core，前端 Vite + Vue3 + TypeScript）。
> 配套文档：`code-review-process.md`（审查流程与门禁）。
> 基线：以现有 `Server/Readme.md` "必须修复" 章节 + 本仓库静态探查发现的风险点为起点。
> 用法：作为 Pull Request 评审的勾选清单（见 `code-review-process.md` 附录 B）。

---

## 0. 目的

把"代码质量参差不齐"收敛为**可勾选、可度量、可回滚**的审查动作。标准分两类：

- **红线（Blocker / Critical）**：合入前必须清零，否则 CI 门禁阻断。
- **质量问题（Major / Minor / Info）**：按严重度要求修改或登记技术债。

---

## 1. 严重度分级

| 级别 | 含义 | 合入规则 |
|------|------|----------|
| **Blocker（阻断/红线）** | 安全漏洞、数据丢失风险、违反分层契约 | 必须修复，CI 阻断，禁止绕过 |
| **Critical（高危）** | 高概率故障、弱口令、密钥泄露、事务崩溃 | 必须修复，且需第二评审人确认 |
| **Major（严重）** | 一致性/可维护性问题，会累积极性技术债 | 必须修复或登记 TASK 并排期 |
| **Minor（次要）** | 风格/可读性/轻微冗余 | 鼓励修复，可在 PR 中一次性清理 |
| **Info（建议）** | 优化点、可借鉴的更好写法 | 仅记录，不强制 |

---

## 2. 通用原则（跨语言）

1. **不出红线**：任何提交不得引入 Blocker / Critical 级问题（凭据入库、明文密钥、未授权访问、本地/UTC 时间混用、违反事务契约）。
2. **单一真相源**：同类配置/枚举/常量只在一处定义，跨端通过生成或显式映射同步（见 4.6）。
3. **失败可见**：异常不允许静默吞掉；错误必须有日志或向上抛出。
4. **可测试**：核心业务（采集、计算、联动、报警）逻辑应可脱离 PLC/网络单测。
5. **不扩大范围**：PR 只解决其标题所述问题，重构与修复分离提交。

---

## 3. 分层架构合规（后端）

依赖方向必须为：`WebApi → Application → Domain` 且 `WebApi/Application/Infrastructure → Infrastructure → Domain`，**Domain 层不得依赖任何 ORM / 外部框架**。

| 检查项 | 标准 | 反例（红线） |
|--------|------|--------------|
| 层依赖 | 上层可依赖下层，下层不可反向引用 | Infrastructure 引用 WebApi；Domain 引用 EF Core |
| 实体纯度 | `Domain/Entities` 仅含业务属性与行为，不含 `using Microsoft.EntityFrameworkCore` | 实体类直接标注 EF 特性或导航属性注入 DbContext |
| 仓储契约 | 接口在 `Application/Interfaces`，实现在 `Infrastructure/Repositories` | 控制器直接 `new` DbContext |
| DTO 隔离 | DTO 不得直接暴露领域实体内部字段；映射在 Application 层 | 把 `Entity` 当响应直接返回 |

> 现状：分层方向正确，无反向依赖，此项以"守住不退化"为主。

---

## 4. 后端 .NET 8 审查清单

### 4.1 安全（红线区）

| # | 规则 | 证据（本仓库现状） | Before → After |
|---|------|--------------------|----------------|
| S1 | **禁止任何凭据/密钥入库**（含 `appsettings*.json`、编译产物 `bin/`） | `WebApi/appsettings.json:7` 明文 `"Password":"Pgw15221236646"`；`:13` JWT `dev-only` 开发密钥；`bin/Debug/.../appsettings.json` 同样明文 | `Before: "Password":"Pgw15221236646"` → `After: "Password":"${SCADA_DB_PASSWORD}"`（环境变量 / Secret 注入，且 `.gitignore` 覆盖 `appsettings*.secret.json`） |
| S2 | **默认管理员必须强口令**，禁止硬编码弱口令 | `Infrastructure/Persistence/DatabaseInitializer.cs:157` 硬编码 `"123456"`，`:163` 仅 `LogWarning` | 改为从环境变量读取，首次登录强制改密；无变量则启动失败（fail-fast） |
| S3 | **敏感实体落库前必须加密**：`MqttServer.Password`、`DatabaseConfig.Password` 等 | `Domain/Entities/MqttServer.cs:40`、`DatabaseConfig.cs:45` 为明文字符串列；`Domain/Readme.md:270` 已自告警 | 存储侧增加 `SecretEncrypt`/`SecretDecrypt`，回显继续走 `SecretMask` |
| S4 | **开放接口必须校验凭证 + 限速**：`ExposedApiMiddleware` 的 `ExposedKey` 须作为密钥校验，而非仅定位变量 | `WebApi/Middlewares/ExposedApiMiddleware.cs:28-93`：`dto.ExposedKey` 只用于定位，无 `[Authorize]`、无速率限制 | 命中后先校验 `ExposedKey` 签名/白名单，叠加 `RateLimiter`，失败返回 401 |
| S5 | **生产环境不得向客户端返回堆栈** | `WebApi/Middlewares/ExceptionMiddleware.cs:72-76`：`if (_env.IsDevelopment())` 返回 `StackTrace` | 仅 Development 返回明细；生产统一返回 `traceId`，明细落日志 |
| S6 | **禁止 `ExecuteSqlRaw` + 字符串拼接**（防 SQL 注入） | 全仓库未命中，原始 SQL 均用 `ExecuteSqlInterpolatedAsync`（如 `AlarmRecordRepository.cs:133`）——保持现状 | 维持参数化；新增 SQL 必须插值或 LINQ |
| S7 | **每个写操作端点必须有 `[Authorize]`**，依赖 `FallbackPolicy` 兜底 | `Authentication.Extensions.cs:114-125` 已配置 `FallbackPolicy=RequireAuthenticatedUser`；`DeviceController.cs:60` 写变量 `[Roles="Operator,Admin"]` | 维持；新增端点显式声明最小权限策略 |

### 4.2 时间与时钟

| # | 规则 | 证据 | Before → After |
|---|------|------|----------------|
| T1 | **统一使用 UTC**：触发判断与截止计算必须用同一基准（推荐 `DateTime.UtcNow`），或注入 `ITimeProvider` 便于测试 | 三处清理服务 `DateTime.Now`（本地）与 `DateTime.UtcNow`（UTC）混用：`SystemLogCleanupHostedService.cs:68 vs :112`、`AlarmRecordCleanupHostedService.cs:64 vs :98`、`ScriptExecutionRecordCleanupHostedService.cs:59 vs :90`；`Runtime/Readme.md:128` 约定"时间戳一律 UTC" | `Before: var now = DateTime.Now; if(now.Hour==3)` → `After: var now = DateTime.UtcNow; if(now.Hour==3)`（注意 UTC 凌晨对应本地时区偏移，统一以 UTC 为准判断） |
| T2 | **持久化时间戳一律 UTC**，展示层再按用户时区转换 | 业务写库点已多数合规（`Device.cs:59/64`、`ExposedApiMiddleware.cs:80` `NormalizeUtc`） | 维持；新增时间字段默认 UTC，禁止 `DateTime.Now` 入库 |

### 4.3 数据访问与事务（EF Core 重试策略）

> 关键约束：`MySqlRetryingExecutionStrategy` 已启用（`Database.Extensions.cs:36-40`）。**直接 `BeginTransaction` 会抛 `InvalidOperationException`**——这是"沉睡的地雷"。

| # | 规则 | 证据 | Before → After |
|---|------|------|----------------|
| D1 | **禁止直接 `BeginTransaction` / `BeginTransactionAsync`**，一律走 `ExecuteInTransactionAsync`（已被 `CreateExecutionStrategy` 包裹） | `Infrastructure/Persistence/EfUnitOfWork.cs:25-28` `BeginTran()`、`:43-47` `BeginTransactionAsync()`；`:50-71` 的 `ExecuteInTransactionAsync` 为正确范式；当前两危险 API 无调用方 | `Before: using var tx = _db.Database.BeginTransaction();` → `After: await _uow.ExecuteInTransactionAsync(async () => { /* 业务 */ });` |
| D2 | **事务内不得出现外部 I/O/网络等待**（重试策略会重试整个委托，长事务重试成本高） | — | 业务委托保持短事务；外部调用移出事务边界 |
| D3 | **建议从 `IUnitOfWork` 移除 `BeginTran`/`BeginTransactionAsync`**，仅保留 `ExecuteInTransactionAsync` + `SaveChangesAsync` | `Application/Interfaces/IUnitOfWork.cs:14,30` | 删除危险方法签名，从源头杜绝误用（需 PR 评审 + 编译验证） |

### 4.4 异常处理

| # | 规则 | 证据 | 处理 |
|---|------|------|------|
| E1 | **禁止空 `catch {}` 吞异常**；至少 `LogDebug` | `Infrastructure/Services/SystemMonitorService.cs:129,145` 空 catch（性能计数器可选降级，但应留痕） | Major：补 `_logger.LogDebug(ex, "...")` |
| E2 | **不允许忽略 `Task` / `ValueTask` 返回值**导致静默失败 | 全局异常兜底良好（`Program.cs:257-298`） | Major：显式 `await` 或 `Task.Run(...).Forget()` 显式标注 |
| E3 | **业务异常用 `BusinessException` + `StatusCode`**，由中间件统一转 HTTP | `ExceptionMiddleware.cs:81` 已统一 `ApiResponse.Fail` | 维持；禁止直接 `throw new Exception` 传递业务语义 |

### 4.5 Nullable 与空引用

- 各 csproj 均 `<Nullable>enable</Nullable>`，纪律整体良好。
- **规则**：禁止无理由 `null!`；确需延迟赋值的，用 `= string.Empty` 或构造函数初始化（参考 `Device.cs`、`MqttServer.cs` 现有做法）。
- 现状：全仓库 `null!` 约 12 处零星出现（`DataModel.cs:1`、`Sensor.cs:1` 等），属 Minor，随 PR 清理。

### 4.6 命名与类型一致性

| # | 规则 | 证据 | 处理 |
|---|------|------|------|
| N1 | **DeviceType 前后端单一真相源**：枚举字符串字面量前后端必须一致，新增类型须同步登记转换器 | 后端 `Domain/Enums/DeviceType.cs:11-41`（7 种）；前端 `Client/src/types.ts:341`（已扩到 7 种，名称 `'OPCUA'/'MQTT'`）；桥接靠 `Application/Converters/DeviceTypeJsonConverter.cs:17-26`，`:79` 退化为 `ToString()` | Major：新增类型时先在 `DeviceType.cs` 与 `types.ts` + 转换器三处登记；建议生成式代码或共享 JSON Schema |
| N2 | **删除冗余双真相源**：`VariableType` 由 `DataTypeEnum` 派生，不应独立维护 | `Domain/Enums/VariableEnums.cs:6` vs `:22`；`ModelVariable.cs:35-38` `[NotMapped]` 派生 | Minor：保持派生，禁止在 DB 新增 `VariableType` 列 |
| N3 | **`StoreMode` 统一为枚举**，DTO 不再透传退化的 `IsStored` | `Domain/Enums/StoreModeEnum.cs:7`（已重构为 int 列）；`ModelVariableDto.cs:67`、`ModelVariableMapper.cs:23` 仍透传 `IsStored` | Minor：前端只认 `StoreMode`，移除 `IsStored` 透传 |

### 4.7 接口契约统一

| # | 规则 | 证据 | 处理 |
|---|------|------|------|
| C1 | **成功响应也统一走 `ApiResponse<T>.Ok(data)`**，禁止控制器返回临时匿名对象 | 错误路径统一（`ApiResponse.cs:7/21/26/35`）；成功路径不统一：`DeviceController.cs:39,47,79,93` 直接 `Ok(result)`，`:65,107` 返回 `new { success=true,... }`；前端 `errorHandler.ts` 同时兼容两套字段 | Major：控制器统一返回 `ApiResponse<T>`；前端收敛为单解析路径 |
| C2 | **状态码语义清晰**：业务失败经 `BusinessException.StatusCode` 映射为 4xx，不滥用 200 | `AuthController.cs:49` `Unauthorized` 合规 | 维持 |

---

## 5. 前端 Vue3 / TypeScript 审查清单

> 现状短板：缺 ESLint/Prettier、TS `strict` 未开、残留 React 依赖。这些属"普遍"级工程化债务，应在流程中设迁移排期（见 `code-review-process.md` 第 6 节）。

| # | 规则 | 证据 | 处理 |
|---|------|------|------|
| F1 | **引入 ESLint + Prettier 并纳入 CI**：`lint` 脚本不得仅为 `tsc --noEmit` | `Client/package.json:11` 仅有 `tsc --noEmit`；无 `.eslintrc*`/`.prettierrc*` | Major（排期）：加 `eslint`/`prettier` devDependency + 配置文件，CI 跑 `lint` |
| F2 | **开启 TS `strict` + `noUncheckedIndexedAccess`** | `Client/tsconfig.json` 全程无 `strict:true`（且 `jsx:"react-jsx"` 与 React 残留呼应） | Major（排期）：分步开启，先修类型错误 |
| F3 | **清理 React 残留依赖**，避免模板混淆 | `Client/package.json:2,24,17,28` 同时依赖 `react ^19` 与 `@vitejs/plugin-react`，`name:"react-example"`，但实际为 Vue 渲染 | Minor：移除未使用 React 依赖与 `jsx` 配置 |
| F4 | **API/错误封装保持单一**：新增接口必须走 `src/api/*` + `http.ts` 拦截器，禁止散落 `axios` 直连 | `Client/src/api/http.ts:21` 单实例 + JWT 注入 + 401 登出；`errorHandler.ts:14-111` 统一错误解析（现状良好） | 维持；新增资源按现有 `deviceApi.ts` 模式拆分 |
| F5 | **路由守卫覆盖**：管理类路由须带 `meta.roles`，前端隐藏 ≠ 后端放松（后端 `RequireAdmin` 为真源） | `Client/src/constants/roles.ts:5-10` 已定义并注明须与后端 `SystemRoles` 一致；守卫在 `router/index.ts` | Major：评审时核对每个管理路由的 `meta.roles` 与后端策略一致 |

---

## 6. 日志与可观测性

- **规则**：关键路径（采集、联动触发、报警产生/恢复、设备上下线）必须有结构化日志，含 `deviceId`/`variableKey`/时间戳（UTC）。
- **禁止**：把敏感值（密码、密钥、完整 token）写入日志。
- **现状**：`ApiResponse` 已掩码 `SecretMask`，维持。

## 7. 测试要求

| 层级 | 最低要求 |
|------|----------|
| Domain | 业务规则（报警阈值判定、类型派生、联动条件）单测，可脱离 IO |
| Application | 服务方法用 `ExecuteInTransactionAsync` 路径单测（mock `IUnitOfWork`） |
| Runtime | 调度/脚本执行单测，注入 `ITimeProvider` 控制时钟 |
| 前端 | 工具函数（解析/格式化/类型守卫）单测；关键组件交互测试 |

> 新增 Blocker/Critical 修复须带回归测试；PR 勾选"测试已覆盖"。

---

## 8. 决策点（供负责人确认）

1. **S1/S2 凭据治理落地方式**：环境变量 / 密钥管理服务 / 配置中心？需确定 `.gitignore` 与 CI Secret 注入方案。
2. **D3 是否立即删除 `IUnitOfWork.BeginTran`**：建议删除，但会改变接口契约，需评估是否已有外部调用。
3. **F1/F2 工程化迁移排期**：一次性开启 `strict` 可能引入大量类型错误，建议分阶段（先 `strictNullChecks`，再全 `strict`）。
4. **N1 前后端枚举单一真相源方案**：生成式代码 vs 共享 JSON Schema vs 手动登记（当前靠转换器兜底）。
5. **CI 门禁强度**：是否将 ESLint/`tsc --noEmit`/构建作为 PR 必需检查（即使前期报错较多，可先 `warn` 后 `error`）。
