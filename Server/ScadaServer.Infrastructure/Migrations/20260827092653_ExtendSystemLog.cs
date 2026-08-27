using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtendSystemLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "SystemLogs",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Level",
                table: "SystemLogs",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "SystemLogs",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "SystemLogs",
                type: "varchar(45)",
                maxLength: 45,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Operation",
                table: "SystemLogs",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Operator",
                table: "SystemLogs",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RelatedId",
                table: "SystemLogs",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_systemlog_category_timestamp",
                table: "SystemLogs",
                columns: new[] { "Category", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_systemlog_timestamp",
                table: "SystemLogs",
                column: "Timestamp");

            // 历史数据回填：存量 SystemLog 行分类补为 Runtime（新增列默认值 ''，避免 NOT NULL 迁移失败）。
            migrationBuilder.Sql(
                "UPDATE `SystemLogs` SET `Category` = 'Runtime' WHERE `Category` = '' OR `Category` IS NULL;");

            // 历史数据迁移：将旧 ConfigLog（组态工程/页面审计）并入 SystemLogs 统一表，
            // Category=Operation，Source=配置审计，Operation=UPDATE，Operator/IpAddress 沿用，Content 拼设备ID与变更描述。
            // ConfigLog 表本身保留不删（兼容旧控制器），仅停止新增写入方。
            migrationBuilder.Sql(
                @"INSERT INTO `SystemLogs` (`Timestamp`, `Category`, `Level`, `Source`, `Operation`, `Operator`, `IpAddress`, `RelatedId`, `Content`)
                  SELECT `CreateTime`, 'Operation', 'Information', '配置审计', 'UPDATE', `Operator`, NULL, CAST(`DeviceId` AS CHAR), CONCAT('设备#', `DeviceId`, '：', `ChangeDesc`)
                  FROM `ConfigLog`
                  WHERE `DeviceId` IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_systemlog_category_timestamp",
                table: "SystemLogs");

            migrationBuilder.DropIndex(
                name: "ix_systemlog_timestamp",
                table: "SystemLogs");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "SystemLogs");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "SystemLogs");

            migrationBuilder.DropColumn(
                name: "Operation",
                table: "SystemLogs");

            migrationBuilder.DropColumn(
                name: "Operator",
                table: "SystemLogs");

            migrationBuilder.DropColumn(
                name: "RelatedId",
                table: "SystemLogs");

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "SystemLogs",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Level",
                table: "SystemLogs",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(16)",
                oldMaxLength: 16)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
