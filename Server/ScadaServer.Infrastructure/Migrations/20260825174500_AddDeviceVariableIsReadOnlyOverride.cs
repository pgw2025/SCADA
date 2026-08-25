using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceVariableIsReadOnlyOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 设备变量实例级读写权限覆盖：null=继承模板 IsReadOnly（存量行保持 NULL，行为不变）。
            migrationBuilder.AddColumn<bool>(
                name: "IsReadOnlyOverride",
                table: "DeviceVariables",
                type: "tinyint(1)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsReadOnlyOverride",
                table: "DeviceVariables");
        }
    }
}
