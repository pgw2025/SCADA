using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModelVariableConstraintsFkRestrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devices_DataModels_ModelId",
                table: "Devices");

            migrationBuilder.DropForeignKey(
                name: "FK_DeviceVariables_ModelVariables_ModelVariableId",
                table: "DeviceVariables");

            migrationBuilder.AlterColumn<string>(
                name: "Unit",
                table: "ModelVariables",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ModelVariables",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "ModelVariables",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ModelVariables",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_modelvariable_model_key",
                table: "ModelVariables",
                columns: new[] { "ModelId", "Key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_DataModels_ModelId",
                table: "Devices",
                column: "ModelId",
                principalTable: "DataModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceVariables_ModelVariables_ModelVariableId",
                table: "DeviceVariables",
                column: "ModelVariableId",
                principalTable: "ModelVariables",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ModelVariables_DataModels_ModelId",
                table: "ModelVariables",
                column: "ModelId",
                principalTable: "DataModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devices_DataModels_ModelId",
                table: "Devices");

            migrationBuilder.DropForeignKey(
                name: "FK_DeviceVariables_ModelVariables_ModelVariableId",
                table: "DeviceVariables");

            migrationBuilder.DropForeignKey(
                name: "FK_ModelVariables_DataModels_ModelId",
                table: "ModelVariables");

            migrationBuilder.DropIndex(
                name: "ix_modelvariable_model_key",
                table: "ModelVariables");

            migrationBuilder.AlterColumn<string>(
                name: "Unit",
                table: "ModelVariables",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldMaxLength: 32,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ModelVariables",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "ModelVariables",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ModelVariables",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldMaxLength: 500,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_DataModels_ModelId",
                table: "Devices",
                column: "ModelId",
                principalTable: "DataModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceVariables_ModelVariables_ModelVariableId",
                table: "DeviceVariables",
                column: "ModelVariableId",
                principalTable: "ModelVariables",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
