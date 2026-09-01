using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MergeDeviceConfigIntoDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Devices 加列（协议配置由独立表内联到 Device）
            migrationBuilder.AddColumn<string>(
                name: "JsonConfig",
                table: "Devices",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Devices",
                type: "int",
                nullable: false,
                defaultValue: 1);

            // 2. 回填：把 DeviceConfigs 数据并入 Devices（必须在 DropTable 之前执行，防止数据丢失）
            migrationBuilder.Sql(@"
                UPDATE Devices d
                INNER JOIN DeviceConfigs c ON c.DeviceId = d.Id
                SET d.JsonConfig = c.JsonConfig, d.Version = c.Version;");

            // 3. 删除独立表
            migrationBuilder.DropTable(
                name: "DeviceConfigs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回滚：重建 DeviceConfigs 表并从 Devices 回填，再删除 Device 上的列（避免数据丢失）
            migrationBuilder.CreateTable(
                name: "DeviceConfigs",
                columns: table => new
                {
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    JsonConfig = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceConfigs", x => x.DeviceId);
                    table.ForeignKey(
                        name: "FK_DeviceConfigs_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(@"
                INSERT INTO DeviceConfigs (DeviceId, JsonConfig, UpdatedAt, Version)
                SELECT Id, COALESCE(JsonConfig, '{}'), UpdatedAt, Version FROM Devices;");

            migrationBuilder.DropColumn(
                name: "JsonConfig",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Devices");
        }
    }
}
