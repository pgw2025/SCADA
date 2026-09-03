using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillDeviceDataModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 阶段 5 回填：把存量设备的"主模型"（Device.ModelId）落成一条 IsPrimary 绑定，
            // 使 DeviceDataModels 与 Device.ModelId 在迁移后即满足双写一致性不变量
            // （每台设备恰一条 IsPrimary=true，且其 DataModelId == Device.ModelId）。
            // Version 取绑定时刻模型的当前版本快照；无 Device/Model 关联行的设备自然不产生绑定（无左连可插）。
            // 幂等：仅当该 (DeviceId, DataModelId) 尚未存在时插入，重跑不产生重复行。
            migrationBuilder.Sql("""
                INSERT INTO DeviceDataModels (DeviceId, DataModelId, Version, IsPrimary, IsEnabled, CreatedAt, UpdatedAt)
                SELECT d.Id, d.ModelId, COALESCE(NULLIF(m.Version, ''), '1.0'), 1, 1, d.CreatedAt, d.UpdatedAt
                FROM Devices d
                JOIN DataModels m ON m.Id = d.ModelId
                WHERE NOT EXISTS (
                    SELECT 1 FROM DeviceDataModels b
                    WHERE b.DeviceId = d.Id AND b.DataModelId = d.ModelId
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 纯数据回填：Down 无需清理——若整体回退，AddDeviceDataModelBindings 的 Down 会 DropTable 一并清掉本迁移写入的数据。
        }
    }
}
