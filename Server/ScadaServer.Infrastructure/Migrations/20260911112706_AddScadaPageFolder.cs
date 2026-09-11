using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScadaPageFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FolderId",
                table: "ScadaPages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "ScadaPages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ScadaPageFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ParentFolderId = table.Column<int>(type: "int", nullable: true),
                    Platform = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, defaultValue: "Desktop")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScadaPageFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScadaPageFolders_ScadaPageFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "ScadaPageFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScadaPageFolders_ScadaProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ScadaProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_scadepages_folderid",
                table: "ScadaPages",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "ix_scadepagefolders_parent",
                table: "ScadaPageFolders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "ix_scadepagefolders_project_platform",
                table: "ScadaPageFolders",
                columns: new[] { "ProjectId", "Platform" });

            migrationBuilder.AddForeignKey(
                name: "FK_ScadaPages_ScadaPageFolders_FolderId",
                table: "ScadaPages",
                column: "FolderId",
                principalTable: "ScadaPageFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 存量画面 SortOrder 回填：按 (ProjectId, Platform) 分区、Id 升序，从 1 起连续编号。
            // 保证改造后画面默认显示顺序与改造前（后端查询返回顺序）一致，避免全部归 0 导致顺序漂移。
            migrationBuilder.Sql(
                "UPDATE ScadaPages p " +
                "LEFT JOIN (" +
                "  SELECT Id, ROW_NUMBER() OVER (PARTITION BY ProjectId, Platform ORDER BY Id) AS rn " +
                "  FROM ScadaPages" +
                ") t ON p.Id = t.Id " +
                "SET p.SortOrder = t.rn;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScadaPages_ScadaPageFolders_FolderId",
                table: "ScadaPages");

            migrationBuilder.DropTable(
                name: "ScadaPageFolders");

            migrationBuilder.DropIndex(
                name: "ix_scadepages_folderid",
                table: "ScadaPages");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "ScadaPages");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "ScadaPages");
        }
    }
}
