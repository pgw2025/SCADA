using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaCodeUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 清洗空字符串编码为 NULL（MySQL 唯一索引允许多个 NULL，互不冲突），
            // 防止存量空编码在唯一索引下被误判为重复值。
            migrationBuilder.Sql("UPDATE Areas SET Code = NULL WHERE Code = '';");

            migrationBuilder.CreateIndex(
                name: "ix_areas_code",
                table: "Areas",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_areas_code",
                table: "Areas");
        }
    }
}
