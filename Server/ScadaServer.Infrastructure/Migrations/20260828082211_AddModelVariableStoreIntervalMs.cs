using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModelVariableStoreIntervalMs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StoreIntervalMs",
                table: "ModelVariables",
                type: "int",
                nullable: false,
                defaultValue: 300000);

            // 存量数据回填：保持 Storage 行为不突变。
            //  Change（枚举 1）/None（0）：默认 300000ms（5 分钟），已由默认值覆盖；
            //  周期类（Cycle=2 / Compressed=3 / Aggregated=4）：历史行为是"每轮采集都写"，
            //  为不改变原有的数据粒度，回填为该模板关联设备实例的最小轮询间隔，
            //  无实例（或实例未配轮询间隔）时回退 1000ms。
            migrationBuilder.Sql(
                """
                UPDATE `ModelVariables` mv
                LEFT JOIN (
                    SELECT `ModelVariableId`, MIN(`PollingIntervalMs`) AS `MinPoll`
                    FROM `DeviceVariables`
                    WHERE `PollingIntervalMs` IS NOT NULL AND `PollingIntervalMs` > 0
                    GROUP BY `ModelVariableId`
                ) dv ON dv.`ModelVariableId` = mv.`Id`
                SET mv.`StoreIntervalMs` = COALESCE(dv.`MinPoll`, 1000)
                WHERE mv.`StoreMode` IN (2, 3, 4);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoreIntervalMs",
                table: "ModelVariables");
        }
    }
}
