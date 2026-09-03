# 巡检留档：阶段 6 清理前置门禁（6.1）与键值基线

> 归属：db-refactor 阶段 6（清理收尾与报警关联）步骤 6.1。
> 门禁纪律：本阶段 6.2/6.3 为删除/去双写类操作，执行前必须通过本节只读巡检；任何一项不通过则对应清理步骤顺延。
> 执行时间：2026-09-03（本地 `scada` 库，迁移已应用至阶段 5：39+2）。

---

## 1. 门禁巡检结果

| # | 巡检项 | SQL 要点 | 期望 | 实际 | 门禁 |
|---|---|---|---|---|---|
| 1 | 无连接设备 | `SELECT * FROM Devices WHERE ConnectionId IS NULL` | = 0（或刻意不采集设备） | 0（共 2 设备全有连接） | ✅ |
| 2 | JsonConfig ↔ Connection.ConfigJson 一致性 | 对 `ConnectionId` 非空设备比对 `Devices.JsonConfig = DeviceConnections.ConfigJson`（含 NULL 语义） | 0 不一致 | 0 不一致 / 0 NULL 缺口 | ✅ |
| 3 | 主模型双写一致 | `Device.ModelId` ↔ `DeviceDataModels(IsPrimary=1)` | 0 失同步 | 0 | ✅ |
| 4 | AccessMode/IsReadOnly 一致 | `(AccessMode='Read') ⇔ (IsReadOnly=1)` | 0 矛盾 | 0 | ✅ |
| 5 | 键值快照比对 | 阶段 0 基线缺失 → **本轮补建**（见 §3） | — | 基线已建，7.1 起可比对 | ✅（补建） |

### 1.1 补充：DeviceVariable.IsReadOnlyOverride 存量

巡检发现 `DeviceVariables.IsReadOnlyOverride` 非空 4 条（设备变量 1/2/5/6，值均为 0 = 设备级覆盖为可写）。本阶段 6.4 删列后置，该列及其 AccessModeOverride 改造评估保留到批次 B 再行处理，不影响本次删除类操作门禁。

### 1.2 键值计数（本次基线）

- Devices：2
- ModelVariables：7
- DeviceVariables：7

---

## 2. 可复用的门禁查询（阶段 7 回归复用）

```sql
-- 1. 无连接设备（期望 0）
SELECT Id, `Key`, ModelId FROM Devices WHERE ConnectionId IS NULL;

-- 2. JsonConfig 与 Connection.ConfigJson 不一致（期望 0；NULL 缺口单独看 2b）
SELECT d.Id, d.`Key`
FROM Devices d JOIN DeviceConnections c ON c.Id = d.ConnectionId
WHERE NOT (d.JsonConfig = c.ConfigJson
           OR (d.JsonConfig IS NULL AND c.ConfigJson IS NULL))
   OR (d.JsonConfig IS NULL AND c.ConfigJson IS NOT NULL AND c.ConfigJson <> '{}');

-- 3. 主模型双写失同步（期望 0）
SELECT d.Id FROM Devices d
WHERE d.ModelId IS NOT NULL
  AND (SELECT COUNT(*) FROM DeviceDataModels b
       WHERE b.DeviceId = d.Id AND b.IsPrimary = 1 AND b.DataModelId = d.ModelId) = 0;

-- 4. AccessMode/IsReadOnly 矛盾（期望 0）
SELECT COUNT(*) FROM ModelVariables mv
WHERE (mv.AccessMode = 'Read') <> (mv.IsReadOnly = 1);
```

> 注：6.2/6.3 落地后（JsonConfig 回退层删除、应用层停双写），第 2 项巡检将**不再适用**（`Devices.JsonConfig` 成为历史列，值不再刷新），届时以「`Devices.JsonConfig` 自 6.3 起不再变化」替代；第 1 项仍适用。

---

## 3. 键值快照基线（补建）

阶段 0 约定基线存放 `docs/db-refactor/_baseline/`（不入库，.gitignore 已排除）。本次补建：

| 文件 | 内容 | 行数 |
|---|---|---|
| `_baseline/Devices.csv` | Id / Key / AreaId / ModelId / ConnectionId / HasJsonConfig / Version | 2 |
| `_baseline/ModelVariables.csv` | Id / ModelId / Key / DataType / AccessMode / IsReadOnly / IsEnabled | 7 |
| `_baseline/DeviceVariables.csv` | Id / DeviceId / ModelVariableId / Address / ConnectionId | 7 |

比对方法（后续阶段/阶段 7 回归）：
```sql
-- 全量导出后与基线 CSV 逐行 diff（以 Id+Key 为锚点），期望零差异：
SELECT Id, `Key` FROM Devices ORDER BY Id;
SELECT Id, ModelId, `Key` FROM ModelVariables ORDER BY Id;
SELECT Id, DeviceId, ModelVariableId FROM DeviceVariables ORDER BY Id;
```
基线建立时间 2026-09-03 19:06（阶段 5 收尾后、阶段 6 删除类改动前），此后任何改动不得触碰 Key/地址映射。
