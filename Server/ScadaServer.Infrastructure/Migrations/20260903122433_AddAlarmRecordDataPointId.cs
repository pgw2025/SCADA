using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlarmRecordDataPointId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DataPointId",
                table: "AlarmRecords",
                type: "int",
                nullable: true);

            // 存量回填：按设备主模型（Devices.ModelId → DataPoints.ModelId）匹配 VariableKey，
            // 匹配不上的记录保持 NULL（无主模型 / 变量 Key 不在主模型内 / 历史遗留键）。
            migrationBuilder.Sql("""
                UPDATE `AlarmRecords` ar
                JOIN `Devices` d ON d.`Id` = ar.`DeviceId`
                JOIN `DataPoints` dp ON dp.`ModelId` = d.`ModelId` AND dp.`Key` = ar.`VariableKey`
                SET ar.`DataPointId` = dp.`Id`
                WHERE ar.`DataPointId` IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataPointId",
                table: "AlarmRecords");
        }
    }
}
