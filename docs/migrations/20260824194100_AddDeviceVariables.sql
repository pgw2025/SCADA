-- ============================================================================
-- 迁移: AddDeviceVariables
-- 阶段: SCADA 架构重构 第六阶段（新增设备变量表 + 数据回填）
-- 数据库: MySQL 8.0.36 (Pomelo.EntityFrameworkCore.MySql)
-- 说明:
--   1. 新建 DeviceVariables 表，承载变量在"具体设备"上的实现
--      (Address / BitOffset / PollingIntervalMs / 缩放覆盖 / 扩展数据)。
--   2. 建立 (DeviceId, ModelVariableId) 唯一索引，保证 1:N 关系下无重复实例。
--   3. 数据回填: 遍历 Devices，按 ModelId 关联 ModelVariables，把模板上的
--      Address / BitOffset / PollingIntervalMs 复制到 DeviceVariable。
--   4. 源表 ModelVariables 的对应列【保留不删】，故数据零丢失。
--   5. 本脚本与同名 EF 迁移 (20260824194100_AddDeviceVariables.cs) 等价，
--      可二选一执行（推荐在可运行 `dotnet ef` 的环境用 EF 迁移以保证快照一致）。
-- ============================================================================

-- ---------------------------------------------------------------------------
-- 1) 建表
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `DeviceVariables` (
    `Id`                   INT            NOT NULL AUTO_INCREMENT,
    `DeviceId`             INT            NOT NULL,
    `ModelVariableId`      INT            NOT NULL,
    `Address`              LONGTEXT       NULL,
    `BitOffset`            INT            NULL,
    `IsEnabled`            TINYINT(1)     NOT NULL DEFAULT 1,
    `PollingIntervalMs`    INT            NULL,
    `ScaleSlopeOverride`   DOUBLE         NULL,
    `ScaleOffsetOverride`  DOUBLE         NULL,
    `DeadBandOverride`     DOUBLE         NULL,
    `ExtensionData`        LONGTEXT       NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_DeviceVariables_Devices_DeviceId`
        FOREIGN KEY (`DeviceId`) REFERENCES `Devices` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_DeviceVariables_ModelVariables_ModelVariableId`
        FOREIGN KEY (`ModelVariableId`) REFERENCES `ModelVariables` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ---------------------------------------------------------------------------
-- 2) 唯一索引 (DeviceId + ModelVariableId)
-- ---------------------------------------------------------------------------
CREATE UNIQUE INDEX `ix_devicevariable_device_model`
    ON `DeviceVariables` (`DeviceId`, `ModelVariableId`);

-- ---------------------------------------------------------------------------
-- 3) 数据回填（在唯一索引之后执行：若已存在重复 (Device, MV) 对会显式报错，便于排查脏数据）
--    仅复制 Address / BitOffset / PollingIntervalMs；
--    Scale 覆盖字段留 NULL => 运行时回退到 ModelVariable 模板值（语义不变，数据不丢）。
-- ---------------------------------------------------------------------------
INSERT INTO `DeviceVariables`
    (`DeviceId`, `ModelVariableId`, `Address`, `BitOffset`, `PollingIntervalMs`, `IsEnabled`)
SELECT
    d.`Id`,
    mv.`Id`,
    mv.`Address`,
    mv.`BitOffset`,
    mv.`PollingIntervalMs`,
    1
FROM `Devices` d
INNER JOIN `ModelVariables` mv ON mv.`ModelId` = d.`ModelId`;

-- ===========================================================================
-- 数据校验 SQL（迁移后执行，确认无丢失 / 无孤儿 / 无重复）
-- ===========================================================================

-- (a) 数量核对: 期望行数 = 所有 (Device × 其模型变量) 组合数
SELECT
    (SELECT COUNT(*)
     FROM `Devices` d
     INNER JOIN `ModelVariables` mv ON mv.`ModelId` = d.`ModelId`) AS expected_rows,
    (SELECT COUNT(*) FROM `DeviceVariables`)                  AS actual_rows;

-- (b) 孤儿校验: DeviceVariables.DeviceId / ModelVariableId 必须都能在父表找到
SELECT 'orphan_device' AS check_name, COUNT(*) AS bad_rows
FROM `DeviceVariables` dv
LEFT JOIN `Devices` d ON d.`Id` = dv.`DeviceId`
WHERE d.`Id` IS NULL;

SELECT 'orphan_modelvariable' AS check_name, COUNT(*) AS bad_rows
FROM `DeviceVariables` dv
LEFT JOIN `ModelVariables` mv ON mv.`Id` = dv.`ModelVariableId`
WHERE mv.`Id` IS NULL;

-- (c) 重复校验: (DeviceId, ModelVariableId) 不应出现多于 1 行
SELECT `DeviceId`, `ModelVariableId`, COUNT(*) AS cnt
FROM `DeviceVariables`
GROUP BY `DeviceId`, `ModelVariableId`
HAVING cnt > 1;

-- (d) 复制一致性抽查: 回填值应与源模板一致（刚迁移完应返回 0 行）
--     <=> 为 MySQL NULL 安全等于，避免 NULL 参与 <> 比较导致漏判
SELECT dv.`DeviceId`, dv.`ModelVariableId`
FROM `DeviceVariables` dv
INNER JOIN `ModelVariables` mv ON mv.`Id` = dv.`ModelVariableId`
WHERE NOT (dv.`Address`            <=> mv.`Address`)
   OR NOT (dv.`BitOffset`          <=> mv.`BitOffset`)
   OR NOT (dv.`PollingIntervalMs`  <=> mv.`PollingIntervalMs`)
LIMIT 50;

-- (e) 信息性统计: 目标列存在 NULL 的行数（说明源模板本身该字段为空，属正常）
SELECT
    SUM(CASE WHEN `Address` IS NULL THEN 1 ELSE 0 END)           AS null_address,
    SUM(CASE WHEN `BitOffset` IS NULL THEN 1 ELSE 0 END)         AS null_bitoffset,
    SUM(CASE WHEN `PollingIntervalMs` IS NULL THEN 1 ELSE 0 END) AS null_polling
FROM `DeviceVariables`;

-- ===========================================================================
-- 回滚方案
-- ===========================================================================
-- 方式 A（推荐，EF 工具链）:
--   dotnet ef database update MoveProtocolToDataModel
--      -> 触发本迁移的 Down(): DROP TABLE DeviceVariables（索引与外键随之移除）。
--   或（若本迁移是最后一条）:
--   dotnet ef migrations remove
--
-- 方式 B（直接 SQL，幂等）:
--   DROP TABLE IF EXISTS `DeviceVariables`;
--
-- 数据安全说明:
--   * 回滚只删除 DeviceVariables 表（派生/复制数据），源表 ModelVariables 的
--     Address / BitOffset / PollingIntervalMs 始终未被删除，故【源数据零丢失】。
--   * 若回滚后需重新上线，重新执行本脚本第 3 步的 INSERT...SELECT 即可重新生成，
--     无需从备份恢复。
-- ===========================================================================
