# 巡检 SQL：阶段 5 DeviceDataModel 双写一致性（供阶段 7 回归复用）

> 归属：db-refactor 阶段 5（DeviceDataModel 多对多）收尾留档。
> 目的：抽查 `Devices.ModelId`（主模型快捷列）与 `DeviceDataModels.IsPrimary=1` 绑定行**严格一致**（双写单点收敛于 DeviceAppService + DeviceDataModelAppService 事务内）；同时校验回填完整性、唯一约束与悬挂引用。阶段 7 回归、或任何涉及设备主模型/绑定操作改动后复用。
> 执行方式：连接 `scada` 库直接执行（MySQL）。以下 8 组查询全部只读，期望输出见各节。

---

## 0. 前置说明

- 双写不变量（每轮巡检核心）：
  `Device.ModelId` 非空的设备，在 `DeviceDataModels` 中**恰好存在一条** `IsPrimary=1` 且 `DataModelId = Device.ModelId` 的绑定行。
- 数据源：回填迁移 `BackfillDeviceDataModels`（20260903101915）按 `Devices.ModelId` 生成 `IsPrimary=1` 行；后续主模型变更只经 `SetPrimaryAsync` / `BindAsync(isPrimary=true)` / 设备创建（`CreateAsync` 双写）。
- 阶段 5 运行时语义：附加（非主）绑定**不参与采集**，运行时只认 `Device.Model`（主模型）。

---

## 1. 总量核对（回填完整性）

期望：`1.1 = 1.2 = 1.3`（绑定行数 = 设备数 = 有主模型设备数），且等于有主模型设备数；本阶段验收时三值相等且 ≥ 迁移前设备数。

```sql
-- 1.1 绑定行总数
SELECT COUNT(*) FROM DeviceDataModels;
-- 1.2 设备总数
SELECT COUNT(*) FROM Devices;
-- 1.3 有主模型的设备数
SELECT COUNT(*) FROM Devices WHERE ModelId IS NOT NULL;
```

## 2. 每设备主行唯一性

期望：输出 `cnt=1` 一行为全部设备数；出现 `cnt=0`（缺主行）或 `cnt>1`（多主行）即违反不变量。

```sql
SELECT cnt, COUNT(*) AS device_count FROM (
  SELECT DeviceId, SUM(IsPrimary = 1) AS cnt
  FROM DeviceDataModels GROUP BY DeviceId
) t GROUP BY cnt;
```

## 3. 双写失同步巡检（核心）

期望：**0 行**。任何输出行都表示 `Device.ModelId` 与 `IsPrimary=1` 主行不一致或主行缺失（应检查 SetPrimary/Bind 双写路径）。

```sql
SELECT d.Id, d.Key, d.ModelId,
       (SELECT DataModelId FROM DeviceDataModels b
         WHERE b.DeviceId = d.Id AND b.IsPrimary = 1) AS bind_primary
FROM Devices d
WHERE d.ModelId IS NOT NULL
  AND ((SELECT COUNT(*) FROM DeviceDataModels b
         WHERE b.DeviceId = d.Id AND b.IsPrimary = 1 AND b.DataModelId = d.ModelId) = 0);
```

## 4. (DeviceId, DataModelId) 重复绑定

期望：**0 行**（受唯一索引约束，异常时先查是否唯一索引被绕过或旧数据）。

```sql
SELECT DeviceId, DataModelId, COUNT(*) AS c
FROM DeviceDataModels GROUP BY DeviceId, DataModelId HAVING c > 1;
```

## 5. 悬挂绑定（DataModel 已不存在）

期望：**0 行**。出现则说明有代码绕过 FK Restrict 或手动删除了 DataModels 行。

```sql
SELECT b.Id, b.DeviceId, b.DataModelId
FROM DeviceDataModels b
LEFT JOIN DataModels m ON m.Id = b.DataModelId
WHERE m.Id IS NULL;
```

## 6. 绑定未发布/停用模型（应用层应拦截）

期望：**0 行**。`BindAsync` 要求目标模型 `IsPublished=1`；出现未发布绑定说明该校验失效。

```sql
SELECT b.Id, b.DeviceId, b.DataModelId, m.Name, m.IsPublished
FROM DeviceDataModels b
JOIN DataModels m ON m.Id = b.DataModelId
WHERE m.IsPublished = 0;
```

## 7. 版本快照抽查

说明：`Version` 取绑定时刻 `DataModel.Version`，回填时对空值以 `'1.0'` 兜底（迁移脚本 COALESCE）。抽查主行版本应等于对应 `DataModels.Version`（空则 '1.0'）。

```sql
-- 7.1 主行版本快照 vs 模型当前版本（不一致时输出，期望 0 行）
SELECT b.DeviceId, b.DataModelId, b.Version AS bind_version, m.Version AS model_version
FROM DeviceDataModels b
JOIN DataModels m ON m.Id = b.DataModelId
WHERE b.IsPrimary = 1
  AND b.Version <> COALESCE(NULLIF(m.Version, ''), '1.0');

-- 7.2 全量绑定快照预览（人工核对）
SELECT DeviceId, DataModelId, Version, IsPrimary, IsEnabled, CreatedAt
FROM DeviceDataModels ORDER BY DeviceId, Id;
```

## 8. 设备缺绑定检查

期望：**0 行**（对 `ModelId IS NOT NULL` 的设备而言）；无 ModelId 设备可不参与绑定。

```sql
SELECT d.Id, d.Key, d.ModelId
FROM Devices d
WHERE NOT EXISTS (SELECT 1 FROM DeviceDataModels b WHERE b.DeviceId = d.Id);
```

---

## 9. 修复指引（若某节出现非预期输出）

| 现象 | 可能根因 | 处置 |
|---|---|---|
| 3/8 失同步或缺主行 | SetPrimary/Bind/CreateAsync 事务内双写漏执行或回滚不一致 | 对故障设备重跑 SetPrimary 纠正主行；核对事务边界（UoW ExecuteInTransactionAsync） |
| 2 出现多主行 | 双写并发竞态或历史脏数据 | 保留与 `Device.ModelId` 一致的一行，事务内降级其余主行 |
| 4/5/6 异常 | 绕过应用层直改库 | 先修数据，再追查写入来源；正常路径由唯一索引/FK/IsPublished 校验兜底 |
| 应用回滚场景 | 旧代码只读写 `Device.ModelId` | 绑定表闲置无害；重建/切主经旧路径会破坏双写，需重跑本节巡检确认或手工补绑定行 |

---

## 10. 阶段 5 基线执行结果（2026-09-03）

| 检查 | 期望 | 实际 |
|---|---|---|
| 1. 绑定行数 / 设备数 / 有主模型设备数 | 三者相等 | 2 / 2 / 2 ✅ |
| 2. 每设备 IsPrimary=1 恰好 1 条 | 分布仅 (1, 设备数) | (1, 2) ✅ |
| 3. 双写失同步 | 0 | 0 ✅ |
| 4. 重复绑定 | 0 | 0 ✅ |
| 5. 悬挂绑定 | 0 | 0 ✅ |
| 6. 未发布模型绑定 | 0 | 0 ✅ |
| 7. 主行版本快照一致 | 0 | 0 ✅（快照均为 '1.0'：源模型 Version 为空走 COALESCE 兜底，符合迁移脚本预期）|
| 8. 缺绑定设备 | 0 | 0 ✅ |

结论：本地 `scada` 库迁移 `20260903101820_AddDeviceDataModelBindings` + `20260903101915_BackfillDeviceDataModels` 应用成功，双写不变量全量通过，无回填脏数据。
