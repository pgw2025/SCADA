using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <summary>
    /// 变量历史记录重构（阶段2/3/4）数据库迁移：
    /// 1. DatabaseConfigs 新增迁移断点字段（MigrateLastId / MigrateStatus / MigrateUpdatedAt，阶段4）；
    /// 2. VariableHistory 新增 Timestamp 单列索引（阶段2 保留期清理）；
    /// 3. VariableHistory 新增 (DeviceKey(191), VariableKey, Timestamp) 前缀复合索引（阶段3，DeviceKey 为 longtext 需前缀）。
    /// </summary>
    public partial class VariableHistoryRefactorIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MigrateLastId",
                table: "DatabaseConfigs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MigrateStatus",
                table: "DatabaseConfigs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "MigrateUpdatedAt",
                table: "DatabaseConfigs",
                type: "datetime(6)",
                nullable: true);

            // Timestamp 单列索引（保留期清理 DELETE ... WHERE Timestamp < cutoff 走索引）
            migrationBuilder.CreateIndex(
                name: "ix_variablehistory_timestamp",
                table: "VariableHistory",
                column: "Timestamp");

            // (DeviceKey(191), VariableKey, Timestamp) 前缀复合索引：
            // DeviceKey 为 longtext，MySQL 不允许全列索引，需前缀长度 191（utf8mb4 下 191 字符 ≈ 767 字节，InnoDB 索引上限内）。
            // 用原生 SQL 手动创建（EF 流畅 API 无法表达前缀索引，故不在 DbContext/ModelSnapshot 声明）。
            migrationBuilder.Sql(
                "CREATE INDEX `ix_variablehistory_device_variable_timestamp` " +
                "ON `VariableHistory` (`DeviceKey`(191), `VariableKey`, `Timestamp`);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX `ix_variablehistory_device_variable_timestamp` ON `VariableHistory`;");

            migrationBuilder.DropIndex(
                name: "ix_variablehistory_timestamp",
                table: "VariableHistory");

            migrationBuilder.DropColumn(
                name: "MigrateUpdatedAt",
                table: "DatabaseConfigs");

            migrationBuilder.DropColumn(
                name: "MigrateStatus",
                table: "DatabaseConfigs");

            migrationBuilder.DropColumn(
                name: "MigrateLastId",
                table: "DatabaseConfigs");
        }
    }
}
