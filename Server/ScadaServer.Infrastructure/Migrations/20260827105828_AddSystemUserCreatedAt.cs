using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemUserCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "SystemUsers",
                type: "datetime(6)",
                nullable: false,
                // 仅供已有表加列时占位；下方立即用 UTC 当前时间回填所有存量行。
                defaultValue: new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // 存量行回填为当前 UTC 时间（与代码侧 DateTime.UtcNow 保持一致，避免本地/UTC 混用差 8 小时）。
            migrationBuilder.Sql("UPDATE SystemUsers SET CreatedAt = UTC_TIMESTAMP(6) WHERE CreatedAt <= '2000-12-31 23:59:59';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "SystemUsers");
        }
    }
}
