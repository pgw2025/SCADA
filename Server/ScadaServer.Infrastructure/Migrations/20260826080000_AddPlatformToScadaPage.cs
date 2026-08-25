using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformToScadaPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 组态画面归属端：桌面端(Desktop)/移动端(Mobile)，存量画面回填为 Desktop
            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "ScadaPages",
                type: "varchar(16)",
                nullable: false,
                defaultValue: "Desktop");

            // 运行态高频按 (ProjectId, Platform) 过滤；同时覆盖仅按 ProjectId 查询
            migrationBuilder.CreateIndex(
                name: "IX_ScadaPages_ProjectId_Platform",
                table: "ScadaPages",
                columns: new[] { "ProjectId", "Platform" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScadaPages_ProjectId_Platform",
                table: "ScadaPages");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "ScadaPages");
        }
    }
}
