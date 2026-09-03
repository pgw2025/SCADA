using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropIsReadOnlyAddAccessModeOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ===== 阶段 6.4b：读写权限统一为字符串 AccessMode / AccessModeOverride =====
            // 1) DataPoints.IsReadOnly（旧 bool 列）删除——模板权限以 DataPoint.AccessMode 为唯一权威
            //    （阶段 4 迁移已回填 AccessMode，两列自始同步，删列前无需再回填）。
            // 2) DataPointMappings.IsReadOnlyOverride（bool?）→ AccessModeOverride（string?）：
            //    先加新列 → 无损回填 → 再删旧列（避免脚手架"先删后加"丢数据）。

            // 1. 新增实例级覆盖列（先于删列执行，保证回填前旧值仍在）
            migrationBuilder.AddColumn<string>(
                name: "AccessModeOverride",
                table: "DataPointMappings",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // 2. 回填：IsReadOnlyOverride 语义平移为 AccessModeOverride
            //    true（强制只读）→ 'Read'；false（强制可写）→ 'ReadWrite'；null（继承模板）→ NULL。
            migrationBuilder.Sql(
                """
                UPDATE `DataPointMappings`
                SET `AccessModeOverride` = CASE
                    WHEN `IsReadOnlyOverride` = 1 THEN 'Read'
                    WHEN `IsReadOnlyOverride` = 0 THEN 'ReadWrite'
                    ELSE NULL
                END;
                """);

            // 3. 删除旧列（回填完成后才安全删除）
            migrationBuilder.DropColumn(
                name: "IsReadOnlyOverride",
                table: "DataPointMappings");

            // 4. 模板旧 bool 列删除（AccessMode 已权威，无数据依赖）
            migrationBuilder.DropColumn(
                name: "IsReadOnly",
                table: "DataPoints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 对称回滚：恢复两个旧 bool 列并按 AccessMode / AccessModeOverride 回填后，再删新列。

            // 1. 恢复模板 IsReadOnly（先加可空列，回填后再收紧为 NOT NULL，避免已有行插入失败）
            migrationBuilder.AddColumn<bool>(
                name: "IsReadOnly",
                table: "DataPoints",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE `DataPoints`
                SET `IsReadOnly` = CASE WHEN `AccessMode` = 'Read' THEN 1 ELSE 0 END;
                """);

            migrationBuilder.Sql(
                "ALTER TABLE `DataPoints` MODIFY COLUMN `IsReadOnly` tinyint(1) NOT NULL;");

            // 2. 恢复实例级 IsReadOnlyOverride（string 覆盖反解为 bool：Read→1 / ReadWrite→0 / 其余含 NULL→NULL）
            migrationBuilder.AddColumn<bool>(
                name: "IsReadOnlyOverride",
                table: "DataPointMappings",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE `DataPointMappings`
                SET `IsReadOnlyOverride` = CASE
                    WHEN `AccessModeOverride` = 'Read' THEN 1
                    WHEN `AccessModeOverride` = 'ReadWrite' THEN 0
                    ELSE NULL
                END;
                """);

            // 3. 删除新列
            migrationBuilder.DropColumn(
                name: "AccessModeOverride",
                table: "DataPointMappings");
        }
    }
}
