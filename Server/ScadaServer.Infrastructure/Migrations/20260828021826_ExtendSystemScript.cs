using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtendSystemScript : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TriggerType",
                table: "SystemScripts",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "SystemScripts",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CooldownMs",
                table: "SystemScripts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CronExpression",
                table: "SystemScripts",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "DeadBand",
                table: "SystemScripts",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureCount",
                table: "SystemScripts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastDurationMs",
                table: "SystemScripts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "SystemScripts",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastExecutedAt",
                table: "SystemScripts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeRead",
                table: "SystemScripts",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ScopeWrite",
                table: "SystemScripts",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "TimeoutMs",
                table: "SystemScripts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Tripped",
                table: "SystemScripts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "SystemScripts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WatchDeviceKey",
                table: "SystemScripts",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "WatchVariableKey",
                table: "SystemScripts",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CooldownMs",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "CronExpression",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "DeadBand",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "FailureCount",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "LastDurationMs",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "LastExecutedAt",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "ScopeRead",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "ScopeWrite",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "TimeoutMs",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "Tripped",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "WatchDeviceKey",
                table: "SystemScripts");

            migrationBuilder.DropColumn(
                name: "WatchVariableKey",
                table: "SystemScripts");

            migrationBuilder.AlterColumn<string>(
                name: "TriggerType",
                table: "SystemScripts",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(16)",
                oldMaxLength: 16)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "SystemScripts",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldMaxLength: 100)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
