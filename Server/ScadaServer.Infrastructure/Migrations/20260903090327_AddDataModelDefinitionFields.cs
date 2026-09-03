using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataModelDefinitionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "DataModels",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "DataModels",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Version",
                table: "DataModels",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "1.0")
                .Annotation("MySql:CharSet", "utf8mb4");

            // 存量回填（阶段 4.2 验收项）：Code 取 Name，重名按 Id 顺序追加 -2 / -3 去重后缀。
            // - 必须先回填去重再建唯一索引（见 AddDataModelCodeUniqueIndex 迁移），否则重名直接炸迁移；
            // - LEFT(...,100) 兜底：Name 最长 100，追加后缀后可能超长，截断以符合 varchar(100)；
            // - WHERE Code IS NULL 保证幂等，重复执行不会继续追加后缀。
            // 去重结果会在启动时的 DatabaseInitializer 完整性校验中记录到系统日志（重名计数）。
            migrationBuilder.Sql(@"
UPDATE DataModels d
JOIN (
    SELECT Id, Name, ROW_NUMBER() OVER (PARTITION BY Name ORDER BY Id) AS rn
    FROM DataModels
) t ON d.Id = t.Id
SET d.Code = LEFT(CASE WHEN t.rn = 1 THEN t.Name ELSE CONCAT(t.Name, '-', t.rn) END, 100)
WHERE d.Code IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "DataModels");

            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "DataModels");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "DataModels");
        }
    }
}
