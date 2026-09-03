using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModelVariableDefinitionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessMode",
                table: "ModelVariables",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Read")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "ModelVariables",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRequired",
                table: "ModelVariables",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Sort",
                table: "ModelVariables",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // 存量回填（阶段 4.1 验收项）：
            // - AccessMode：由旧列 IsReadOnly 推导——只读 → Read；可写（旧语义 false）→ ReadWrite。
            //   旧布尔列无法表达"只写"，故可写一律归为 ReadWrite（与 SyncAccessMode 反推规则一致）。
            // - Sort：取 Id，保持既有展示顺序不变。
            // - IsEnabled：存量变量全部视为启用（新列默认 true，此处显式兜底）。
            migrationBuilder.Sql(@"
UPDATE ModelVariables
SET AccessMode = CASE WHEN IsReadOnly = 1 THEN 'Read' ELSE 'ReadWrite' END,
    Sort = Id,
    IsEnabled = 1;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessMode",
                table: "ModelVariables");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "ModelVariables");

            migrationBuilder.DropColumn(
                name: "IsRequired",
                table: "ModelVariables");

            migrationBuilder.DropColumn(
                name: "Sort",
                table: "ModelVariables");
        }
    }
}
