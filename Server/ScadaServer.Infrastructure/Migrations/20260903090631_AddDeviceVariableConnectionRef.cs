using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceVariableConnectionRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "DeviceVariables",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawDataType",
                table: "DeviceVariables",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_devicevariables_connectionid",
                table: "DeviceVariables",
                column: "ConnectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceVariables_DeviceConnections_ConnectionId",
                table: "DeviceVariables",
                column: "ConnectionId",
                principalTable: "DeviceConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 存量回填（阶段 4.3 验收项）：
            // 1) ConnectionId = 所属设备的 ConnectionId（设备尚无连接时保持 NULL，表示"跟随设备"）；
            // 2) RawDataType = 模板 DataType 的字符串形式。ModelVariables.DataType 以 int 存储
            //    （枚举序号），故此处按 DataTypeEnum 顺序显式映射为可读字符串，避免回填出裸数字。
            // 两条语句均带 IS NULL 条件，重复执行安全。
            migrationBuilder.Sql(@"
UPDATE DeviceVariables dv
JOIN Devices d ON d.Id = dv.DeviceId
SET dv.ConnectionId = d.ConnectionId
WHERE dv.ConnectionId IS NULL;
");

            migrationBuilder.Sql(@"
UPDATE DeviceVariables dv
JOIN ModelVariables mv ON mv.Id = dv.ModelVariableId
SET dv.RawDataType = CASE mv.DataType
    WHEN 0 THEN 'INT'    WHEN 1 THEN 'REAL'   WHEN 2 THEN 'BOOL'   WHEN 3 THEN 'DINT'  WHEN 4 THEN 'BYTE'
    WHEN 5 THEN 'BIT'    WHEN 6 THEN 'FLOAT'  WHEN 7 THEN 'DOUBLE' WHEN 8 THEN 'STRING' WHEN 9 THEN 'UINT16'
    WHEN 10 THEN 'UINT32' WHEN 11 THEN 'INT64' WHEN 12 THEN 'UINT64' WHEN 13 THEN 'WORD' WHEN 14 THEN 'CHAR'
    ELSE NULL END
WHERE dv.RawDataType IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeviceVariables_DeviceConnections_ConnectionId",
                table: "DeviceVariables");

            migrationBuilder.DropIndex(
                name: "ix_devicevariables_connectionid",
                table: "DeviceVariables");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "DeviceVariables");

            migrationBuilder.DropColumn(
                name: "RawDataType",
                table: "DeviceVariables");
        }
    }
}
