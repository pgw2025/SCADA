using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtendScheduledTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastDurationMs",
                table: "ScheduledTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "ScheduledTasks",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRunAt",
                table: "ScheduledTasks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastStatus",
                table: "ScheduledTasks",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Idle")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRunAt",
                table: "ScheduledTasks",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastDurationMs",
                table: "ScheduledTasks");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "ScheduledTasks");

            migrationBuilder.DropColumn(
                name: "LastRunAt",
                table: "ScheduledTasks");

            migrationBuilder.DropColumn(
                name: "LastStatus",
                table: "ScheduledTasks");

            migrationBuilder.DropColumn(
                name: "NextRunAt",
                table: "ScheduledTasks");
        }
    }
}
