using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExposedInterfaceRouteUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 开放接口路由唯一索引兜底：同一 (请求方法, 路由路径) 全局唯一。
            // 应用层已在 ExposedInterfaceAppService 做等价校验，此处为并发/直接写库时的数据库级约束。
            migrationBuilder.CreateIndex(
                name: "ix_exposedinterfaces_route_method",
                table: "ExposedInterfaces",
                columns: new[] { "RouteUrl", "RequestMethod" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_exposedinterfaces_route_method",
                table: "ExposedInterfaces");
        }
    }
}