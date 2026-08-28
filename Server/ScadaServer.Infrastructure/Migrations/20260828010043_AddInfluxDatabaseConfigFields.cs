using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInfluxDatabaseConfigFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bucket",
                table: "DatabaseConfigs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "DatabaseConfigs",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCheckedAt",
                table: "DatabaseConfigs",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastStatus",
                table: "DatabaseConfigs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Org",
                table: "DatabaseConfigs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "DatabaseConfigs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bucket",
                table: "DatabaseConfigs");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "DatabaseConfigs");

            migrationBuilder.DropColumn(
                name: "LastCheckedAt",
                table: "DatabaseConfigs");

            migrationBuilder.DropColumn(
                name: "LastStatus",
                table: "DatabaseConfigs");

            migrationBuilder.DropColumn(
                name: "Org",
                table: "DatabaseConfigs");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "DatabaseConfigs");
        }
    }
}
