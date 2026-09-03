using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 6.6 表重命名对齐目标设计（无损）：
    /// ModelVariables→DataPoints、DeviceVariables→DataPointMappings，
    /// FK 列 ModelVariableId→DataPointId；索引与约束名按 EF 新模型对齐重建。
    /// 由脚手架迁移手工改写为 Rename 语义（原始 Drop/Create 会丢数据，已弃用）。
    /// </summary>
    public partial class RenameDataPointTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) 表名
            migrationBuilder.RenameTable(name: "ModelVariables", newName: "DataPoints");
            migrationBuilder.RenameTable(name: "DeviceVariables", newName: "DataPointMappings");

            // 2) 映射表 FK 列改名（MySQL 自动同步其所在索引定义）
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` RENAME COLUMN `ModelVariableId` TO `DataPointId`;");

            // 3) 重建 FK 约束为 EF 新模型默认名（MySQL 不支持 RENAME CONSTRAINT，先删后加；仅元数据操作）
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` DROP FOREIGN KEY `FK_DeviceVariables_ModelVariables_ModelVariableId`;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` DROP FOREIGN KEY `FK_DeviceVariables_Devices_DeviceId`;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` DROP FOREIGN KEY `FK_DeviceVariables_DeviceConnections_ConnectionId`;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPoints` DROP FOREIGN KEY `FK_ModelVariables_DataModels_ModelId`;");

            // 4) 清理 FK 隐式索引，按新模型建显式索引（ix_* 自定义索引随列改名自动更新，保持原名）
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` DROP INDEX `FK_DeviceVariables_ModelVariables_ModelVariableId`;");
            migrationBuilder.Sql(
                "CREATE INDEX `IX_DataPointMappings_DataPointId` ON `DataPointMappings` (`DataPointId`);");

            // 5) 以新约束名重建 FK
            migrationBuilder.Sql(
                "ALTER TABLE `DataPoints` ADD CONSTRAINT `FK_DataPoints_DataModels_ModelId` " +
                "FOREIGN KEY (`ModelId`) REFERENCES `DataModels` (`Id`) ON DELETE RESTRICT;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` ADD CONSTRAINT `FK_DataPointMappings_DataPoints_DataPointId` " +
                "FOREIGN KEY (`DataPointId`) REFERENCES `DataPoints` (`Id`) ON DELETE RESTRICT;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` ADD CONSTRAINT `FK_DataPointMappings_DeviceConnections_ConnectionId` " +
                "FOREIGN KEY (`ConnectionId`) REFERENCES `DeviceConnections` (`Id`) ON DELETE RESTRICT;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` ADD CONSTRAINT `FK_DataPointMappings_Devices_DeviceId` " +
                "FOREIGN KEY (`DeviceId`) REFERENCES `Devices` (`Id`) ON DELETE CASCADE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 反向：删除新约束/索引，恢复旧约束名与隐式索引
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` DROP FOREIGN KEY `FK_DataPointMappings_DataPoints_DataPointId`;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` DROP FOREIGN KEY `FK_DataPointMappings_Devices_DeviceId`;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` DROP FOREIGN KEY `FK_DataPointMappings_DeviceConnections_ConnectionId`;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPoints` DROP FOREIGN KEY `FK_DataPoints_DataModels_ModelId`;");
            migrationBuilder.Sql(
                "DROP INDEX `IX_DataPointMappings_DataPointId` ON `DataPointMappings`;");

            migrationBuilder.Sql(
                "ALTER TABLE `DataPoints` ADD CONSTRAINT `FK_ModelVariables_DataModels_ModelId` " +
                "FOREIGN KEY (`ModelId`) REFERENCES `DataModels` (`Id`) ON DELETE RESTRICT;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` ADD CONSTRAINT `FK_DeviceVariables_ModelVariables_ModelVariableId` " +
                "FOREIGN KEY (`DataPointId`) REFERENCES `DataPoints` (`Id`) ON DELETE RESTRICT;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` ADD CONSTRAINT `FK_DeviceVariables_DeviceConnections_ConnectionId` " +
                "FOREIGN KEY (`ConnectionId`) REFERENCES `DeviceConnections` (`Id`) ON DELETE RESTRICT;");
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` ADD CONSTRAINT `FK_DeviceVariables_Devices_DeviceId` " +
                "FOREIGN KEY (`DeviceId`) REFERENCES `Devices` (`Id`) ON DELETE CASCADE;");

            // 列与表名还原（FK 隐式索引在旧约束名下由 MySQL 自动补齐）
            migrationBuilder.Sql(
                "ALTER TABLE `DataPointMappings` RENAME COLUMN `DataPointId` TO `ModelVariableId`;");
            migrationBuilder.RenameTable(name: "DataPointMappings", newName: "DeviceVariables");
            migrationBuilder.RenameTable(name: "DataPoints", newName: "ModelVariables");
        }
    }
}
