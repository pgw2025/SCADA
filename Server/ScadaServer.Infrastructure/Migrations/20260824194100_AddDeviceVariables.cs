using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Pomelo.EntityFrameworkCore.MySql.Metadata;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceVariables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 新增 DeviceVariables 表：变量在"具体设备"上的实现实例（第四阶段 DeviceVariable 实体）。
            // 这些字段替代 ModelVariable 上即将废弃的 Address / BitOffset / PollingIntervalMs（第五阶段已标 [Obsolete]）。
            migrationBuilder.CreateTable(
                name: "DeviceVariables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    ModelVariableId = table.Column<int>(type: "int", nullable: false),
                    Address = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BitOffset = table.Column<int>(type: "int", nullable: true),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    PollingIntervalMs = table.Column<int>(type: "int", nullable: true),
                    ScaleSlopeOverride = table.Column<double>(type: "double", nullable: true),
                    ScaleOffsetOverride = table.Column<double>(type: "double", nullable: true),
                    DeadBandOverride = table.Column<double>(type: "double", nullable: true),
                    ExtensionData = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceVariables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceVariables_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceVariables_ModelVariables_ModelVariableId",
                        column: x => x.ModelVariableId,
                        principalTable: "ModelVariables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // 唯一索引：一台设备对同一模型变量只允许一条实例（1:N 关系下的自然主键约束）
            migrationBuilder.CreateIndex(
                name: "ix_devicevariable_device_model",
                table: "DeviceVariables",
                columns: new[] { "DeviceId", "ModelVariableId" },
                unique: true);

            // 数据回填：遍历所有 Device，按 ModelId 关联其 ModelVariables，
            // 将 ModelVariable 的 Address / BitOffset / PollingIntervalMs 复制到 DeviceVariable。
            // 源字段(ModelVariables.Address 等)保留不删，故本回填可安全重复执行（唯一索引会拦截重复行）。
            migrationBuilder.Sql(
                "INSERT INTO DeviceVariables (DeviceId, ModelVariableId, Address, BitOffset, PollingIntervalMs, IsEnabled) " +
                "SELECT d.Id, mv.Id, mv.Address, mv.BitOffset, mv.PollingIntervalMs, 1 " +
                "FROM Devices d INNER JOIN ModelVariables mv ON mv.ModelId = d.ModelId;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回滚：仅删除 DeviceVariables 表（索引与外键随表级联移除）。
            // 源表 ModelVariables 的 Address / BitOffset / PollingIntervalMs 未在本迁移中删除，故回滚后源数据零丢失。
            migrationBuilder.DropTable(name: "DeviceVariables");
        }
    }
}
