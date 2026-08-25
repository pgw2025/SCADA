using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHmiComponentLabelBindingAndPageSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "ScadaPages",
                type: "int",
                nullable: false,
                defaultValue: 700);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "ScadaPages",
                type: "int",
                nullable: false,
                defaultValue: 1100);

            migrationBuilder.AddColumn<int>(
                name: "BindDeviceId",
                table: "HmiComponents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BindVariableKey",
                table: "HmiComponents",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "HmiComponents",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_HmiComponents_BindDeviceId",
                table: "HmiComponents",
                column: "BindDeviceId");

            migrationBuilder.AddForeignKey(
                name: "FK_HmiComponents_Devices_BindDeviceId",
                table: "HmiComponents",
                column: "BindDeviceId",
                principalTable: "Devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HmiComponents_Devices_BindDeviceId",
                table: "HmiComponents");

            migrationBuilder.DropIndex(
                name: "IX_HmiComponents_BindDeviceId",
                table: "HmiComponents");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "ScadaPages");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "ScadaPages");

            migrationBuilder.DropColumn(
                name: "BindDeviceId",
                table: "HmiComponents");

            migrationBuilder.DropColumn(
                name: "BindVariableKey",
                table: "HmiComponents");

            migrationBuilder.DropColumn(
                name: "Label",
                table: "HmiComponents");
        }
    }
}
