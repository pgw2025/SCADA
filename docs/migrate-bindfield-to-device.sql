-- ============================================================
-- SCADA 禁止裸 key 改造：存量组件迁移脚本
-- 配套前端改造（Client/src）：setDeviceVariableValue / getDeviceVariableValue
--       的 deviceId 已改为必填 number；运行态删除 flat 裸 key 取值表。
-- 本文件分三段：Phase 0 只读盘点、Phase 3 迁移、守护规则说明。
-- 执行前请先用第 2 步的 mysqldump 备份 HmiComponents！
-- ============================================================

-- ============================================================
-- Phase 0：盘点 bindField-only（无设备绑定）的组件（只读，不改数据）
-- ============================================================
-- 0.1 按「画面 / 组件类型 / 变量键」统计欠绑定组件数量
SELECT p.Name        AS 画面,
       c.Type        AS 组件类型,
       c.BindField   AS 变量键,
       COUNT(*)      AS 数量
FROM HmiComponents c
JOIN ScadaPages p ON c.ScadaPageId = p.Id
WHERE c.BindDeviceId IS NULL
  AND c.BindField <> ''
GROUP BY p.Name, c.Type, c.BindField
ORDER BY p.Name, c.Type, c.BindField;

-- 0.2 每个 bindField 变量键在几台设备上存在
--     =1 → 可自动回填（无歧义）；>1 → 歧义件，需人工在编辑器补绑
SELECT c.BindField                              AS 变量键,
       COUNT(DISTINCT dv.DeviceId)              AS 命中设备数
FROM HmiComponents c
LEFT JOIN ModelVariables mv ON mv.Key = c.BindField
LEFT JOIN DeviceVariables dv ON dv.ModelVariableId = mv.Id
WHERE c.BindDeviceId IS NULL
  AND c.BindField <> ''
GROUP BY c.BindField
ORDER BY 命中设备数 DESC, c.BindField;

-- ============================================================
-- Phase 3：存量组件迁移（破坏性，执行前务必备份！）
-- ============================================================
-- 1) 备份（在 shell 中执行，非此处）
--    mysqldump -h <host> -u <user> -p scada HmiComponents > HmiComponents_backup_$(date +%Y%m%d).sql

-- 2) 自动迁移：bindField 恰好只存在于「一台设备」的变量
--    回填 BindDeviceId + BindVariableKey；多设备歧义的组件保持不动，交由编辑器绑定检查面板人工补绑。
UPDATE HmiComponents c
INNER JOIN (
    SELECT mv.Key         AS varKey,
           dv.DeviceId    AS devId
    FROM ModelVariables mv
    INNER JOIN DeviceVariables dv ON dv.ModelVariableId = mv.Id
    GROUP BY mv.Key
    HAVING COUNT(DISTINCT dv.DeviceId) = 1
) uniq ON uniq.varKey = c.BindField
SET c.BindDeviceId    = uniq.devId,
    c.BindVariableKey  = c.BindField
WHERE c.BindDeviceId IS NULL
  AND c.BindField <> '';

-- 3) 验证：剩余待处理组件（期望 = 0，或仅剩确认的歧义件）
SELECT COUNT(*) AS 待处理组件数
FROM HmiComponents
WHERE BindDeviceId IS NULL
  AND BindField <> '';

-- 4)（可选）查看仍待人工处理的歧义件明细
SELECT c.Id, p.Name AS 画面, c.Type, c.BindField, COUNT(DISTINCT dv.DeviceId) AS 命中设备数
FROM HmiComponents c
JOIN ScadaPages p ON c.ScadaPageId = p.Id
LEFT JOIN ModelVariables mv ON mv.Key = c.BindField
LEFT JOIN DeviceVariables dv ON dv.ModelVariableId = mv.Id
WHERE c.BindDeviceId IS NULL
  AND c.BindField <> ''
GROUP BY c.Id, p.Name, c.Type, c.BindField
HAVING 命中设备数 > 1
ORDER BY 画面, c.BindField;

-- ============================================================
-- 守护规则（禁止裸 key，纳入 Code Review / CI grep）
-- ============================================================
-- 1. 禁止 setDeviceVariableValue(null, ...) / getDeviceVariableValue(null, ...)
--    —— 前端签名 deviceId 已为必填 number，TypeScript 编译期即拦截。
-- 2. 运行时展示/写入组件时，禁止再读取 component.bindField 作为取值来源
--    —— 仅 BindDeviceId + BindVariableKey 为合法绑定；bindField 仅作 DB 镜像列保留，不读取。
-- 3. 新增脚本 / 计划任务写操作时，必须配置目标设备（deviceId），
--    否则运行时报错（脚本）或置 failed（计划任务）。
-- 4. 后端 VirtualDriver 的手动写入值缓存键已加设备维度（deviceId:key），
--    即便将来驱动改为单例/共享实例也不会跨设备串值。
