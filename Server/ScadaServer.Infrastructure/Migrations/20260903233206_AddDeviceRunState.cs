using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceRunState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RunState",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RunStateChangedAt",
                table: "Devices",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RunState",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "RunStateChangedAt",
                table: "Devices");
        }
    }
}
