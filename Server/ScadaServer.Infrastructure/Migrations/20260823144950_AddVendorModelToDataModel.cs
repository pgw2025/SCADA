using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorModelToDataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsStored",
                table: "ModelVariables");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "ModelVariables");

            migrationBuilder.DropColumn(
                name: "DriverName",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "DataModels");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "AlarmRules");

            migrationBuilder.RenameColumn(
                name: "SensorId",
                table: "AlarmRules",
                newName: "Level");

            migrationBuilder.RenameColumn(
                name: "IsEnabled",
                table: "AlarmRules",
                newName: "Active");

            migrationBuilder.AlterColumn<int>(
                name: "StoreMode",
                table: "ModelVariables",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "VendorModel",
                table: "DataModels",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "Condition",
                table: "AlarmRules",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "DeviceId",
                table: "AlarmRules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "AlarmRules",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "VariableKey",
                table: "AlarmRules",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LinkageRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    VariableKey = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Condition = table.Column<int>(type: "int", nullable: false),
                    Threshold = table.Column<double>(type: "double", nullable: false),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    LinkageVariableKey = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LinkageValue = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Active = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkageRules", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinkageRules");

            migrationBuilder.DropColumn(
                name: "VendorModel",
                table: "DataModels");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "AlarmRules");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "AlarmRules");

            migrationBuilder.DropColumn(
                name: "VariableKey",
                table: "AlarmRules");

            migrationBuilder.RenameColumn(
                name: "Level",
                table: "AlarmRules",
                newName: "SensorId");

            migrationBuilder.RenameColumn(
                name: "Active",
                table: "AlarmRules",
                newName: "IsEnabled");

            migrationBuilder.AlterColumn<string>(
                name: "StoreMode",
                table: "ModelVariables",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsStored",
                table: "ModelVariables",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "ModelVariables",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DriverName",
                table: "Devices",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "DataModels",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Condition",
                table: "AlarmRules",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Severity",
                table: "AlarmRules",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
