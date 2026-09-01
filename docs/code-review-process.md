# SCADA 代码审查流程（Code Review Process）

> 配套文档：`code-review-standard.md`（审查标准/清单）。
> 目标：把审查从"靠自觉"变成"有门禁、有勾选、有度量"的系统机制。

---

## 1. 角色与职责

| 角色 | 职责 |
|------|------|
| **作者（Author）** | 自审（见第 3 节）→ 写清 PR 描述 → 回应评审意见 → 修复后 re-request |
| **评审人（Reviewer）** | 依据标准逐条勾选 → 标注严重度 → 给出 `Approve` / `Request changes` → 对 Critical 须显式确认已修复 |
| **第二评审人（2nd Reviewer）** | 仅当 PR 含 **Critical** 或 **架构/分层/事务契约**改动时必需；两人独立通过方可合入 |
| **Maintainer** | 维护 `code-review-standard.md`、月度审计、技术债看板 |

> 最小配置：每个 PR 至少 1 名 Reviewer；Critical/架构改动需 2 名。

---

## 2. 审查时机与门禁

```
开发者本地                      CI（自动门禁）                   PR 评审（人工）
───────────                   ─────────────                   ─────────────
写代码 → 自审(§3)              build 通过                       Reviewer 打开 PR
   ↓                           tsc --noEmit（前端）              ↓
git commit（结构化提交）  ──▶   eslint/prettier（接入后）  ──▶   按标准勾选清单(附录B)
   ↓                           （未来）单元测试 + 覆盖率          ↓
git push → 开 PR              （未来）安全扫描                   决定 Approve / Changes
                                                              ↓
                                                          合入 main（需审批）
```

**门禁规则（建议分阶段启用）**：

| 阶段 | 门禁 | 阻断条件 |
|------|------|----------|
| 立即 | 构建通过（后端 `dotnet build` / 前端 `vite build`） | 编译失败 |
| 立即 | 后端无新增 Blocker/Critical（人工核对标准 S1–S7、T1–T2、D1–D2） | 红线未清零 |
| 排期 | 前端 `tsc --noEmit` 0 error（先 warn 后 error） | 见 F2 决策点 |
| 排期 | ESLint 0 error（接入后） | 见 F1 决策点 |
| 排期 | 单元测试通过 + 覆盖率不低于阈值（如 60%） | 新红线修复无回归测试 |

> 所有门禁**不自动 push**；合入后由 Maintainer 按现有约定（`git push` 需显式确认）推远端。

---

## 3. 自审清单（作者提交前必做）

1. [ ] 本 PR 只解决标题所述问题（重构与修复分离）
2. [ ] 无 Blocker/Critical（凭据入库、明文密钥、未授权访问、本地/UTC 混用、直接 `BeginTransaction`）
3. [ ] 新增 SQL 全部参数化（无 `ExecuteSqlRaw` + 拼接）
4. [ ] 时间戳统一 UTC；无 `DateTime.Now` 入库
5. [ ] 异常无空 `catch {}`；业务异常用 `BusinessException`
6. [ ] 接口成功响应走 `ApiResponse<T>.Ok`
7. [ ] 前端改动走 `src/api/*`，无散落 axios；管理路由带 `meta.roles`
8. [ ] 关键逻辑带测试（见标准 §7）
9. [ ] 提交信息符合 Structured Conventional Commits（partial-plan 带完成标记）

---

## 4. 严重度处理规则

| 级别 | 处理动作 | 合入 |
|------|----------|------|
| Blocker | 必须修复；作者修复后 Reviewer 复核 | 否，清零才放行 |
| Critical | 必须修复 + 第二评审人确认 | 否，双确认才放行 |
| Major | 修复，或登记技术债 TASK 并排期（PR 描述注明 TASK 号） | 可，须有 TASK |
| Minor | 鼓励同 PR 清理 | 可 |
| Info | 仅记录 | 可 |

> 红线（S1–S7 / T1–T2 / D1–D2）无论 PR 大小都必须清零，不得以"赶进度"为由绕过。

---

## 5. 工具链与物料

- **提交规范**：Structured Conventional Commits；多步计划带 `partial-plan` 完成标记（沿用现有约定）。
- **PR 模板**：见附录 A，强制填写"改动范围 / 自审勾选 / 测试 / 风险"。
- **审查勾选清单**：见附录 B，Reviewer 在 PR 评论中逐项勾选并标注严重度。
- **技术债看板**：Major 级以上未当场修复项登记到 `docs/` 或项目管理工具，月度审计跟踪。
- **静态检查**（排期）：后端可接 `dotnet format` +  Roslyn analyzers；前端接 ESLint + Prettier。

---

## 6. 工程化迁移排期（针对普遍级短板）

当前前端缺 ESLint/Prettier、TS `strict` 未开、残留 React 依赖；这些是"普遍"级债务，不要求一次清零，但须有排期：

| 项目 | 建议排期 | 门禁策略 |
|------|----------|----------|
| 引入 ESLint + Prettier（F1） | 2 周内 | 先 `warn`，稳定后转 `error` 阻断 |
| 开启 TS `strict`（F2） | 4–6 周，分步 | 先 `strictNullChecks`，再全 `strict` |
| 清理 React 残留（F3） | 随 F1 一并 | Minor，一次性 PR |
| 控制器响应统一 `ApiResponse<T>`（C1） | 随业务 PR 渐进 | Major，逐控制器收敛 |
| 凭据治理 S1/S2/S3 | 最高优先 | Blocker，先于一切 |

---

## 7. 度量与持续改进

- **月度审计**（Maintainer）：统计 PR 平均评审轮次、红线拦截数、Major 技术债消化率、测试覆盖率趋势。
- **红线的"误伤/漏网"复盘**：每季度回顾标准是否过严/过松，更新 `code-review-standard.md` 版本号。
- **标准版本化**：文档头部标注版本（如 `v1.0`，日期），重大调整走一次 PR 评审。

---

## 8. 自动化建议（可选项，需负责人确认后启用）

以下可作为"系统机制"的增强，但**默认不创建**，待你确认：

1. **定时代码健康扫描**：周期性（如每周一）自动跑一遍标准中的红线检查（凭据入库、空 catch、本地/UTC 混用、`BeginTransaction` 调用），生成报告。
2. **PR 自动贴勾选清单**：开 PR 时自动评论附录 B 清单模板。
3. **CI 质量门禁**：将 ESLint / `tsc --noEmit` / 单元测试接入 CI，按第 2 节分阶段设为阻断。

> 需要哪一项，告诉我，我再据 `automation_update` / CI 配置细则落地。

---

## 附录 A：PR 模板（`docs/.github/PULL_REQUEST_TEMPLATE.md` 建议内容）

```markdown
## 改动范围
<!-- 一句话说明本 PR 解决什么问题；重构与修复分开提交 -->

## 自审勾选（标准 §3）
- [ ] 无 Blocker/Critical 红线
- [ ] SQL 参数化 / 时间统一 UTC / 异常无空 catch
- [ ] 接口响应统一 / 前端走 api 封装 / 路由守卫到位
- [ ] 关键逻辑带测试

## 测试
<!-- 如何验证：命令 / 步骤 / 预期 -->

## 风险与决策点
<!-- 涉及的决策点编号（标准 §8）或需 Maintainer 拍板事项 -->

## 关联
<!-- TASK / 设计文档链接 -->
```

## 附录 B：Reviewer 勾选清单（评论模板，可直接复制）

```markdown
### 代码审查结果
| 类别 | 条目 | 结果 | 备注 |
|------|------|------|------|
| 安全 S1–S7 | 凭据/密钥/开放接口/堆栈泄露/SQL | ✅/❌ | |
| 时钟 T1–T2 | UTC 统一 | ✅/❌ | |
| 事务 D1–D3 | 无直接 BeginTransaction | ✅/❌ | |
| 异常 E1–E3 | 无空 catch / BusinessException | ✅/❌ | |
| 一致性 N1–N3 | DeviceType/变量类型/StoreMode | ✅/❌ | |
| 契约 C1–C2 | ApiResponse 统一 | ✅/❌ | |
| 前端 F1–F5 | ESLint/strict/封装/守卫 | ✅/❌ | |
| 测试 §7 | 回归测试覆盖 | ✅/❌ | |

**结论**：Approve / Request changes（Critical 需第二评审人确认）
```
