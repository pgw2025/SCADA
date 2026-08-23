using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLastKnownStatusToDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 仅新增 LastKnownStatus 持久化列。其余 schema 改动（DriverName/Type 移除、
            // VendorModel 新增、AlarmRules 重构、LinkageRules 表）已在
            // 20260823144950_AddVendorModelToDataModel 中完成，为避免对已是终态的
            // 历史库重复执行危险操作，此处不再包含它们。
            migrationBuilder.AddColumn<int>(
                name: "LastKnownStatus",
                table: "Devices",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastKnownStatus",
                table: "Devices");
        }
    }
}
