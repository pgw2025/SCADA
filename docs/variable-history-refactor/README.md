# 变量历史记录功能重构方案（总览）

> 状态：方案设计（未实施）
> 日期：2026-09-12
> 范围：变量历史记录（采集落库 → 查询导出 → 迁移清理备份）全链路
> 原则：**本方案只做设计，不修改代码**；实施时按阶段推进，每阶段独立可发布、可回滚

---

## 1. 背景与目标

当前变量历史记录功能已具备：存储策略判定（StoreMode/周期/死区）、异步批量落库（InfluxDB 优先 + MySQL 回退）、单变量/批量/聚合/CSV 导出查询、MySQL→Influx 一次性迁移、按保留期清理 Influx、备份导出 Influx CSV。

经代码审查发现 **17 个问题**（4 个 P1、5 个 P2、8 个 P3），集中在四类风险：

| 风险类别 | 问题 |
|---|---|
| 配置生效链路断裂 | InfluxDB 配置需手动迁移才生效、重启即失效 |
| 数据可靠性缺失 | 落库失败静默丢弃、MySQL 无保留期清理、迁移中断数据"隐形丢失" |
| 查询语义不一致 | 聚合仅 Influx 生效、导出上限截断、前端 2000 条硬限制 |
| 功能割裂 | 运行时趋势图与历史查询数据源不互通、失败与无数据不可区分 |

**目标**：修复上述问题，使历史记录功能达到"配置即生效、丢失可感知、查询口径一致、运行时与历史页数据互通"的状态。

## 2. 问题索引与阶段映射

| 编号 | 级别 | 问题摘要 | 所属阶段 |
|---|---|---|---|
| P1-1 | 严重 | InfluxDB 配置不自动生效，重启后回退 MySQL | 阶段 1 |
| P1-2 | 严重 | 落库失败静默丢弃，无重试/补偿/指标 | 阶段 2 |
| P1-3 | 严重 | MySQL 回退路径不支持聚合，UI 与结果不一致 | 阶段 3 |
| P1-4 | 严重 | 导出声称 50000 上限，实际被 clamp 到 10000 | 阶段 3 |
| P2-5 | 中等 | MySQL 历史数据无保留期清理，表无限增长 | 阶段 2 |
| P2-6 | 中等 | 迁移无断点续传、无进度；中断后数据部分可见 | 阶段 4 |
| P2-7 | 中等 | 前端"所有数据"最多 2000 条 | 阶段 3 |
| P2-8 | 中等 | 运行时趋势图无历史回填，与历史查询割裂 | 阶段 5 |
| P2-9 | 中等 | 索引缺 DeviceKey，跨设备同名变量查询放大 | 阶段 3 |
| P3-10 | 轻微 | 查询失败静默清空，与"无数据"不可区分 | 阶段 5 |
| P3-11 | 轻微 | "所有数据"依赖显式 2000-01-01 规避 Influx 默认 -30d | 阶段 5 |
| P3-12 | 轻微 | 历史只记录 Good 质量，中断期曲线为缺口 | 阶段 5（可选项） |
| P3-13 | 轻微 | CSV 转义不完整（换行/制表符/NaN） | 阶段 5 |
| P3-14 | 轻微 | 备份 Influx 全量导出无分片，大库有内存/超时风险 | 阶段 5 |
| P3-15 | 轻微 | 批量变量上限前后端不一致（前端 6 / 后端 8） | 阶段 3 |
| P3-16 | 轻微 | NaN/Infinity 值导致 Influx 整批写入失败回退抖动 | 阶段 2 |
| P3-17 | 轻微 | 历史库与主库配置机制割裂（运维心智负担） | 不开发，backlog |

问题详情（含代码位置）：见 [01-现状问题清单.md](01-现状问题清单.md)。

## 3. 阶段划分

| 阶段 | 文档 | 解决问题 | 粗估工作量 |
|---|---|---|---|
| 阶段 1 | [02-阶段1-配置生效链路修复.md](02-阶段1-配置生效链路修复.md) | P1-1 | 2~3 人日 |
| 阶段 2 | [03-阶段2-写入可靠性与数据清理.md](03-阶段2-写入可靠性与数据清理.md) | P1-2、P2-5、P3-16 | 3~4 人日 |
| 阶段 3 | [04-阶段3-查询一致性修复.md](04-阶段3-查询一致性修复.md) | P1-3、P1-4、P2-7、P2-9、P3-15 | 4~5 人日 |
| 阶段 4 | [05-阶段4-迁移断点续传.md](05-阶段4-迁移断点续传.md) | P2-6 | 3~4 人日 |
| 阶段 5 | [06-阶段5-趋势回填与体验优化.md](06-阶段5-趋势回填与体验优化.md) | P2-8、P3-10~14 | 4~5 人日 |
| 收尾 | [07-测试与验收清单.md](07-测试与验收清单.md) | 全局回归 | 2~3 人日 |

合计约 18~24 人日（不含长周期观察，如保留期清理效果观察）。

### 阶段依赖关系

```
阶段1 配置生效 ──┬──> 阶段3 查询一致性（验证聚合需 Influx 正常生效）
                └──> 阶段4 迁移断点续传（迁移前 Rebuild 逻辑被阶段1吸收）
阶段2 写入可靠性（独立，可与阶段1并行）
阶段5 趋势回填（弱依赖阶段3：建议 limit 参数化后再做回填）
```

**推荐实施顺序**：阶段 1 → 阶段 2 → 阶段 3 → 阶段 4 → 阶段 5 → 收尾回归。
其中阶段 1 与阶段 2 无代码耦合，人手充足时可并行。

## 4. 里程碑

- **M1（阶段 1 完成）**：配置/激活/重启后 InfluxDB 即刻生效，前端可见当前生效后端 → 消除"配置不生效"这一最易踩的坑
- **M2（阶段 2 完成）**：写入失败可重试、可补偿、可观测；MySQL 表有保留期 → 数据可靠性达标
- **M3（阶段 3 完成）**：聚合、上限、索引统一 → 查询口径一致
- **M4（阶段 4 完成）**：迁移可断点续传、有进度、可取消 → 运维可用性达标
- **M5（阶段 5 完成）**：趋势图回填 + 体验细节 → 功能完整

## 5. 涉及的关键代码位置（速查）

| 模块 | 文件 |
|---|---|
| 采样判定 | `Server/ScadaServer.Runtime/Processing/VariableValueProcessor.cs` |
| 异步落库 | `Server/ScadaServer.WebApi/HostedServices/HistoryRecorder.cs` |
| 时序库访问 | `Server/ScadaServer.Infrastructure/Influx/InfluxStore.cs` |
| MySQL 仓储 | `Server/ScadaServer.Infrastructure/Repositories/VariableHistoryRepository.cs` |
| 查询服务 | `Server/ScadaServer.Application/Services/HistoryAppService.cs` |
| 查询接口 | `Server/ScadaServer.WebApi/Controllers/HistoryController.cs` |
| 迁移服务 | `Server/ScadaServer.Infrastructure/Services/HistoryMigrationService.cs` |
| 历史库配置 | `Server/ScadaServer.Application/Services/DatabaseConfigAppService.cs`、`Server/ScadaServer.Domain/Entities/DatabaseConfig.cs` |
| 清理任务 | `Server/ScadaServer.Runtime/Tasks/ClearHistoryTaskExecutor.cs` |
| 备份任务 | `Server/ScadaServer.Runtime/Tasks/BackupTaskExecutor.cs` |
| 前端查询页 | `Client/src/components/HistoricalQueryView.vue` |
| 前端 API | `Client/src/api/historyApi.ts` |
| 前端趋势 | `Client/src/components/widgets/TrendChartWidget.vue`、`Client/src/utils/trendHistory.ts` |
| 前端库管理 | `Client/src/components/DatabaseManagementView.vue` |

## 6. 实施原则

1. **最小改动**：优先在现有类/方法内扩展，不引入新框架；新增服务仿照项目既有模式（如 `SystemLogCleanupHostedService`）。
2. **每阶段独立可发布**：阶段间通过 API 契约兼容，不要求前后端同步发版。
3. **验证后再推进**：每阶段执行其文档末尾的验证清单，通过后再进入下一阶段。
4. **项目约束遵守**：
   - MySQL EF Core 查询谓词使用 `==`，禁止 `string.Equals(xx, StringComparison)`（翻译限制）；
   - EF 迁移新增字段必须带默认值，避免存量行为 null；
   - API 响应遵循项目 `.data` 解包约定（如适用）；
   - 变更分批删除参照 `SystemLogCleanupHostedService` 的 `ORDER BY Id LIMIT n` 模式。
5. **回滚**：数据库迁移（索引/新字段）设计为可回滚；行为开关（如 MySQL 清理保留期默认 0=关闭）保证回滚后无副作用。
