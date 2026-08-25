using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixProtocolDecoupling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // === 存量回填：在删除过渡列前，将旧 DataModels.Type(DeviceType) 映射为 Protocols.Id ===
            // 旧 Type 枚举：1=S7, 2=ModbusTcp, 3=OpcUa, 4=Mqtt, 5=Virtual。
            // Protocol.Key 与种子一致：S7 / OPCUA / VIRTUAL / MODBUSTCP / MQTT。
            // 先清掉可能存在的脏值（0/空），确保只对有效行做映射。
            migrationBuilder.Sql(
                "UPDATE `DataModels` SET `ProtocolId` = NULL WHERE `ProtocolId` IS NOT NULL AND `ProtocolId` <= 0;");

            migrationBuilder.Sql(
                """
                UPDATE `DataModels`
                SET `ProtocolId` = (
                    SELECT p.`Id` FROM `Protocols` p
                    WHERE p.`Key` = CASE `DataModels`.`Type`
                        WHEN 1 THEN 'S7'
                        WHEN 2 THEN 'MODBUSTCP'
                        WHEN 3 THEN 'OPCUA'
                        WHEN 4 THEN 'MQTT'
                        WHEN 5 THEN 'VIRTUAL'
                        ELSE NULL
                    END
                )
                WHERE `ProtocolId` IS NULL OR `ProtocolId` <= 0;
                """);

            // 仍未回填到任何协议的存量行，兜底指向 VIRTUAL 协议，避免置非空时失联。
            migrationBuilder.Sql(
                """
                UPDATE `DataModels`
                SET `ProtocolId` = (SELECT p.`Id` FROM `Protocols` p WHERE p.`Key` = 'VIRTUAL')
                WHERE `ProtocolId` IS NULL OR `ProtocolId` <= 0;
                """);

            migrationBuilder.DropColumn(
                name: "Address",
                table: "ModelVariables");

            migrationBuilder.DropColumn(
                name: "BitOffset",
                table: "ModelVariables");

            migrationBuilder.DropColumn(
                name: "PollingIntervalMs",
                table: "ModelVariables");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "DataModels");

            migrationBuilder.AlterColumn<int>(
                name: "ProtocolId",
                table: "DataModels",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "ModelVariables",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "BitOffset",
                table: "ModelVariables",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PollingIntervalMs",
                table: "ModelVariables",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "ProtocolId",
                table: "DataModels",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            // 回滚：先将 DataModels.ProtocolId 反向映射为旧 Type(DeviceType)，再添加列。
            migrationBuilder.Sql(
                """
                ALTER TABLE `DataModels` ADD COLUMN `Type` INT NOT NULL DEFAULT 0;
                """);

            migrationBuilder.Sql(
                """
                UPDATE `DataModels`
                SET `Type` = CASE `ProtocolId`
                    WHEN (SELECT p.`Id` FROM `Protocols` p WHERE p.`Key` = 'S7') THEN 1
                    WHEN (SELECT p.`Id` FROM `Protocols` p WHERE p.`Key` = 'MODBUSTCP') THEN 2
                    WHEN (SELECT p.`Id` FROM `Protocols` p WHERE p.`Key` = 'OPCUA') THEN 3
                    WHEN (SELECT p.`Id` FROM `Protocols` p WHERE p.`Key` = 'MQTT') THEN 4
                    WHEN (SELECT p.`Id` FROM `Protocols` p WHERE p.`Key` = 'VIRTUAL') THEN 5
                    ELSE 0
                END;
                """);
        }
    }
}
