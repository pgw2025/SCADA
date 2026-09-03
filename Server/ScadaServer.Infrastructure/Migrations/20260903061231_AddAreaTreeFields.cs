using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaTreeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Areas",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Areas",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Areas",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "AreaType",
                table: "Areas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Areas",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "Areas",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "Areas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sort",
                table: "Areas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Areas",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // 存量区域回填：默认作为根区域（ParentId 保持 NULL），类型 Area，启用，
            // 时间戳回填为当前时间。新行由实体 CLR 默认值（AreaType=Area、IsEnabled=true、UtcNow）兜底。
            migrationBuilder.Sql("""
                UPDATE Areas
                SET AreaType = 4, IsEnabled = 1, Sort = 0,
                    CreatedAt = CURRENT_TIMESTAMP(6), UpdatedAt = CURRENT_TIMESTAMP(6)
                WHERE Id IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_areas_parentid",
                table: "Areas",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Areas_Areas_ParentId",
                table: "Areas",
                column: "ParentId",
                principalTable: "Areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Areas_Areas_ParentId",
                table: "Areas");

            migrationBuilder.DropIndex(
                name: "ix_areas_parentid",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "AreaType",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "Sort",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Areas");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Areas",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldMaxLength: 100)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Areas",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldMaxLength: 500)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Areas",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
