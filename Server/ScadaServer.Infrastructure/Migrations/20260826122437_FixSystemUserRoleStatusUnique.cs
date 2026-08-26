using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixSystemUserRoleStatusUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P0：存量数据规范化——角色/状态统一值域、用户名去空格并截断到列宽，随后收紧列类型并建唯一索引。
            migrationBuilder.Sql(
                """
                -- 用户名：去首尾空格并截断到 varchar(64)
                UPDATE SystemUsers SET Username = LEFT(TRIM(Username), 64);

                -- 角色：中文旧值映射到统一值域，未知值按最小权限原则归为 Viewer
                UPDATE SystemUsers SET Role = 'Admin'    WHERE Role IN ('超级管理员', '管理员');
                UPDATE SystemUsers SET Role = 'Operator' WHERE Role = '操作员';
                UPDATE SystemUsers SET Role = 'Viewer'   WHERE Role = '观察员';
                UPDATE SystemUsers SET Role = 'Viewer'   WHERE Role IS NULL OR Role NOT IN ('Admin', 'Operator', 'Viewer');

                -- 状态：统一为 Active/Inactive，未知/空值默认 Active
                UPDATE SystemUsers SET Status = 'Active'   WHERE LOWER(Status) = 'active';
                UPDATE SystemUsers SET Status = 'Inactive' WHERE LOWER(Status) = 'inactive';
                UPDATE SystemUsers SET Status = 'Active'   WHERE Status IS NULL OR Status NOT IN ('Active', 'Inactive');
                """
            );

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "SystemUsers",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_systemusers_username",
                table: "SystemUsers",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_systemusers_username",
                table: "SystemUsers");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "SystemUsers",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
