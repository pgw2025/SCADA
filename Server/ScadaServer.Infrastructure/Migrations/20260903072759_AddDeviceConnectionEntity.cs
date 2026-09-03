using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceConnectionEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ControllerId",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeviceConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ControllerId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProtocolId = table.Column<int>(type: "int", nullable: false),
                    Host = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Port = table.Column<int>(type: "int", nullable: true),
                    ConfigJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeoutMs = table.Column<int>(type: "int", nullable: false),
                    ReconnectIntervalMs = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceConnections_Controllers_ControllerId",
                        column: x => x.ControllerId,
                        principalTable: "Controllers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceConnections_Protocols_ProtocolId",
                        column: x => x.ProtocolId,
                        principalTable: "Protocols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ConnectionId",
                table: "Devices",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ControllerId",
                table: "Devices",
                column: "ControllerId");

            migrationBuilder.CreateIndex(
                name: "ix_deviceconnections_controllerid",
                table: "DeviceConnections",
                column: "ControllerId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceConnections_ProtocolId",
                table: "DeviceConnections",
                column: "ProtocolId");

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_Controllers_ControllerId",
                table: "Devices",
                column: "ControllerId",
                principalTable: "Controllers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_DeviceConnections_ConnectionId",
                table: "Devices",
                column: "ConnectionId",
                principalTable: "DeviceConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devices_Controllers_ControllerId",
                table: "Devices");

            migrationBuilder.DropForeignKey(
                name: "FK_Devices_DeviceConnections_ConnectionId",
                table: "Devices");

            migrationBuilder.DropTable(
                name: "DeviceConnections");

            migrationBuilder.DropIndex(
                name: "IX_Devices_ConnectionId",
                table: "Devices");

            migrationBuilder.DropIndex(
                name: "IX_Devices_ControllerId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ControllerId",
                table: "Devices");
        }
    }
}
