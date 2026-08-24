using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProtocolSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 新增 Protocols 表：协议真相源（通信方式从 DataModel.Type 中剥离为独立概念）。
            // 之前的迁移（InitialCreate / AddVendorModelToDataModel / MoveProtocolToDataModel /
            // AddDeviceVariables）均未创建本表，故此处为唯一需新建的表，且不影响任何已有数据。
            migrationBuilder.CreateTable(
                name: "Protocols",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Key = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DriverKey = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Protocols", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Protocol.Key 业务键唯一索引（驱动工厂按 Key 派发）。
            migrationBuilder.CreateIndex(
                name: "ix_protocol_key",
                table: "Protocols",
                column: "Key",
                unique: true);

            // DataModels 新增可空外键 ProtocolId，关联 Protocols.Id。
            // 可空以兼容尚未绑定协议的过渡期数据；删除协议采用 Restrict，不级联删除数据模型。
            migrationBuilder.AddColumn<int>(
                name: "ProtocolId",
                table: "DataModels",
                type: "int",
                nullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropIndex(
                name: "ix_protocol_key",
                table: "Protocols");

            migrationBuilder.DropTable(
                name: "Protocols");
        }
    }
}
