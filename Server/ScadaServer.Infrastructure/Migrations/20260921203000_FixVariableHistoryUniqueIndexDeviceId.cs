using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <summary>
    /// 修正 VariableHistory 唯一索引的设备维度（见 docs/variable-history-refactor/08-唯一索引设备维度修复方案.md）：
    /// 将 (VariableKey, Timestamp) 唯一索引升级为 (VariableKey, Timestamp, DeviceId) 唯一索引，
    /// 消除跨设备同名变量在同刻采样时的 1062 冲突。
    /// <para>
    /// 列顺序取 (VariableKey, Timestamp, DeviceId)：所有查询最左等值列均为 VariableKey，
    /// 前缀有序可直接反向扫描 LIMIT 取数，无设备查询不退化。
    /// </para>
    /// </summary>
    public partial class FixVariableHistoryUniqueIndexDeviceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 约束放宽：旧 (VariableKey, Timestamp) 唯一蕴含新三元组唯一，无需清洗存量。
            migrationBuilder.DropIndex(
                name: "ix_variablehistory_key_timestamp",
                table: "VariableHistory");

            migrationBuilder.CreateIndex(
                name: "ux_variablehistory_key_timestamp_deviceid",
                table: "VariableHistory",
                columns: new[] { "VariableKey", "Timestamp", "DeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回退为收紧约束：若运行期间已写入"同 VariableKey+Timestamp 不同 DeviceId"的多行，本步骤会因冲突失败，
            // 需先按方案 §4.6 清洗存量（并全量备份）。
            migrationBuilder.DropIndex(
                name: "ux_variablehistory_key_timestamp_deviceid",
                table: "VariableHistory");

            migrationBuilder.CreateIndex(
                name: "ix_variablehistory_key_timestamp",
                table: "VariableHistory",
                columns: new[] { "VariableKey", "Timestamp" },
                unique: true);
        }
    }
}
