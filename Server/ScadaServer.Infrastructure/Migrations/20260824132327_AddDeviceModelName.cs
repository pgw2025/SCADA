using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceModelName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DataModels 表新增 Vendor 和 ModelName 两列（与 Domain 实体 DataModel.Vendor / DataModel.ModelName 对齐）。
            // 两列均为可空字符串，不影响现有存量数据；全新库通过本迁移一次性建列。
            migrationBuilder.AddColumn<string>(
                name: "Vendor",
                table: "DataModels",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ModelName",
                table: "DataModels",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelName",
                table: "DataModels");

            migrationBuilder.DropColumn(
                name: "Vendor",
                table: "DataModels");
        }
    }
}
