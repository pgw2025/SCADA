using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataPointMappingMinMaxOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MaxOverride",
                table: "DataPointMappings",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MinOverride",
                table: "DataPointMappings",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxOverride",
                table: "DataPointMappings");

            migrationBuilder.DropColumn(
                name: "MinOverride",
                table: "DataPointMappings");
        }
    }
}
