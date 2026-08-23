using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveProtocolToDataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 协议真相源由 Device.Type 迁移至 DataModel.Type：
            // 1) DataModels 新增 Type 列（协议枚举，非空，新建库默认 S7=1）；
            // 2) Devices 删除 Type 列（冗余副本，驱动协议改由所绑定模型的 Type 推导）。
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "DataModels",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Devices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Devices",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.DropColumn(
                name: "Type",
                table: "DataModels");
        }
    }
}
