# 数据转换（变量联动转发）修复方案 —— 总览

> 状态：方案设计（未改代码）
> 范围：根因 A / B / C / D
> 关联代码：`ScadaServer.Runtime`（Bindings / RuntimeManager / Processing / Devices）+ `ScadaServer.WebApi.Controllers.DataConversionController` + `Client/src/components/DataConversionView.vue`

## 问题现象

数据转换（源变量 → 目标变量联动转发）**偶尔不转发数据**：源值已变化，但目标变量未收到写入。

## 根因摘要

| 根因 | 一句话描述 | 触发特征 | 关键位置 |
|---|---|---|---|
| A | 目标写入失败且无重试，偶发失败即永久丢失本次转发 | 偶发（网络抖动/写超时/目标离线/值越限） | `VariableBindingEngine.WriteTargetAsync`；`RuntimeManager.WriteVariableAsync` |
| B | 规则在设备未就绪时创建 → 被跳过且永不重试 | 新建/修改规则恰逢设备启动或重连 | `VariableBindingEngine.LoadAsync` |
| C | 目标变量只读/不存在/禁用 → 恒定不转发，前端无提示 | 恒定（配置问题，保存时未校验） | `DataConversionAppService`；`LoadAsync` |
| D | 绑定写入后回读被误判为回声/真实变化 → 多跳链旧值污染、值卡陈旧 | 多跳链（A→B→C）+ 设备写入生效延迟 | `VariableValueProcessor`（回声抑制时机与条件） |

## 修复策略总览

| 根因 | 修复核心 | 阶段 | 风险 |
|---|---|---|---|
| C | 保存期强校验 + 前端过滤 + 加载状态可见 | 阶段 1 | 低 |
| A | 失败分类 + 有限重试 + 离线补写（最新值）+ 越限 Clamp 策略 + 失败告警 | 阶段 2 | 中 |
| B | 设备注册/恢复后触发绑定重载 + pending 规则重试 | 阶段 3 | 中 |
| D | 绑定写入后静默窗口（窗口内目标回读一律不发布变化事件）；不做 Source 语义修正 | 阶段 4 | 中（需回归多跳一致性） |

## 文档索引

- [01-根因详析.md](./01-根因详析.md) —— A/B/C/D 逐根因代码走查与证据
- [02-修复方案设计.md](./02-修复方案设计.md) —— 逐根因修复设计（接入点/改动点/影响）
- [03-执行计划与验证.md](./03-执行计划与验证.md) —— 分阶段执行计划 + 测试与回归验收清单
- [04-方案评审与修订建议.md](./04-方案评审与修订建议.md) —— 复审结论（根因 D 修订为静默窗口、放弃 Source 修正）
