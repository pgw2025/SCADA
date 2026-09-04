using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropDataModelProtocolIdUseConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DataModels_Protocols_ProtocolId",
                table: "DataModels");

            migrationBuilder.DropIndex(
                name: "IX_DataModels_ProtocolId",
                table: "DataModels");

            migrationBuilder.DropColumn(
                name: "ProtocolId",
                table: "DataModels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProtocolId",
                table: "DataModels",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_DataModels_ProtocolId",
                table: "DataModels",
                column: "ProtocolId");

            migrationBuilder.AddForeignKey(
                name: "FK_DataModels_Protocols_ProtocolId",
                table: "DataModels",
                column: "ProtocolId",
                principalTable: "Protocols",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
