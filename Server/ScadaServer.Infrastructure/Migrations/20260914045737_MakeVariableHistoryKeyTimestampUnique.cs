using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeVariableHistoryKeyTimestampUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 先清洗存量重复：(VariableKey, Timestamp) 同组保留最小 Id，其余删除，否则唯一索引创建会失败。
            // （历史写入此前无唯一约束，可能存在因"已提交但客户端判定失败"重试而累积的重复点。）
            migrationBuilder.Sql(
                "DELETE h1 FROM `VariableHistory` h1 " +
                "INNER JOIN `VariableHistory` h2 " +
                "  ON h1.`VariableKey` = h2.`VariableKey` " +
                " AND h1.`Timestamp` = h2.`Timestamp` " +
                " AND h1.`Id` > h2.`Id`;");

            // 重建为唯一索引：配合 1062 幂等写入（HistoryRecorder），
            // 防止 MySQL 重试、补偿队列/落盘重放造成同一采样点重复入库。
            migrationBuilder.DropIndex(
                name: "ix_variablehistory_key_timestamp",
                table: "VariableHistory");
            migrationBuilder.CreateIndex(
                name: "ix_variablehistory_key_timestamp",
                table: "VariableHistory",
                columns: new[] { "VariableKey", "Timestamp" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回退：恢复为非唯一索引
            migrationBuilder.DropIndex(
                name: "ix_variablehistory_key_timestamp",
                table: "VariableHistory");
            migrationBuilder.CreateIndex(
                name: "ix_variablehistory_key_timestamp",
                table: "VariableHistory",
                columns: new[] { "VariableKey", "Timestamp" });
        }
    }
}
