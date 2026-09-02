#nullable disable

#pragma warning disable CS0618 // Type or member is obsolete

using Microsoft.EntityFrameworkCore.Migrations;

namespace ScadaServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceScaleWithExpression : Migration
    {
        /// <summary>
        /// 缩放字段改造：ScaleSlope/ScaleOffset（线性 y=ax+b）→ ScaleExpression（公式字符串，x=原始值）。
        /// <para>
        /// 执行顺序刻意为"加新列 → 数据回填 → 删旧列"，杜绝脚手架默认"先删后加"造成的存量数据丢失：
        /// 1) ModelVariables：Slope=1 &amp; Offset=0（恒等）回填 NULL；否则按 a*x / x+b / a*x+b 形态回填；
        /// 2) DeviceVariables：两个 Override 均为 NULL 时保持 NULL（继承模板语义不变），
        ///    任一非 NULL 才生成覆盖公式；
        /// 3) CAST(double AS CHAR) 可能产出科学计数法（如 1e-05），为合法 JS 数值字面量，运行时 Jint 可直接解析。
        /// </para>
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) 新列（先加，保证回填时有落点）
            migrationBuilder.AddColumn<string>(
                name: "ScaleExpression",
                table: "ModelVariables",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ScaleExpressionOverride",
                table: "DeviceVariables",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // 2) 数据回填：线性参数 → 公式字符串
            migrationBuilder.Sql(@"
UPDATE `ModelVariables`
SET `ScaleExpression` = CASE
    WHEN `ScaleSlope` = 1 AND `ScaleOffset` = 0 THEN NULL
    WHEN `ScaleSlope` = 1 THEN CONCAT('x+', CAST(`ScaleOffset` AS CHAR))
    WHEN `ScaleOffset` = 0 THEN CONCAT(CAST(`ScaleSlope` AS CHAR), '*x')
    ELSE CONCAT(CAST(`ScaleSlope` AS CHAR), '*x+', CAST(`ScaleOffset` AS CHAR))
END;");

            migrationBuilder.Sql(@"
UPDATE `DeviceVariables`
SET `ScaleExpressionOverride` = CASE
    WHEN `ScaleSlopeOverride` IS NULL AND `ScaleOffsetOverride` IS NULL THEN NULL
    WHEN `ScaleSlopeOverride` IS NULL THEN CONCAT('x+', CAST(`ScaleOffsetOverride` AS CHAR))
    WHEN `ScaleOffsetOverride` IS NULL THEN CONCAT(CAST(`ScaleSlopeOverride` AS CHAR), '*x')
    ELSE CONCAT(CAST(`ScaleSlopeOverride` AS CHAR), '*x+', CAST(`ScaleOffsetOverride` AS CHAR))
END;");

            // 3) 删除旧列
            migrationBuilder.DropColumn(
                name: "ScaleSlope",
                table: "ModelVariables");

            migrationBuilder.DropColumn(
                name: "ScaleOffset",
                table: "ModelVariables");

            migrationBuilder.DropColumn(
                name: "ScaleSlopeOverride",
                table: "DeviceVariables");

            migrationBuilder.DropColumn(
                name: "ScaleOffsetOverride",
                table: "DeviceVariables");
        }

        /// <summary>
        /// 回滚：加回旧列并尽力从公式反解析线性参数。
        /// <para>
        /// 注意：公式为不可逆信息——仅 <c>a*x</c> / <c>x+b</c> / <c>a*x+b</c> 三种线性形态可还原；
        /// 自定义公式（如 Math.round(x*10)/10）将退化为 Slope=1 / Offset=0（恒等），需人工核对。
        /// </para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1) 加回旧列
            migrationBuilder.AddColumn<double>(
                name: "ScaleSlope",
                table: "ModelVariables",
                type: "double",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<double>(
                name: "ScaleOffset",
                table: "ModelVariables",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "ScaleSlopeOverride",
                table: "DeviceVariables",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ScaleOffsetOverride",
                table: "DeviceVariables",
                type: "double",
                nullable: true);

            // 2) 尽力回填：仅解析线性形态 a*x+b（REGEXP_SUBSTR 需 MySQL 8.0+）。
            //    NULL 表达式 = 恒等 → Slope=1 / Offset=0（默认值已覆盖，无需 UPDATE）。
            migrationBuilder.Sql(@"
UPDATE `ModelVariables`
SET `ScaleSlope`  = COALESCE(CAST(REGEXP_SUBSTR(`ScaleExpression`, '^-?[0-9.eE+-]+(?=\*x)') AS DECIMAL(30,15)), 1),
    `ScaleOffset` = COALESCE(CAST(REGEXP_SUBSTR(`ScaleExpression`, '(?<=\+)-?[0-9.eE+-]+$') AS DECIMAL(30,15)), 0)
WHERE `ScaleExpression` IS NOT NULL;");

            migrationBuilder.Sql(@"
UPDATE `DeviceVariables`
SET `ScaleSlopeOverride`  = CAST(REGEXP_SUBSTR(`ScaleExpressionOverride`, '^-?[0-9.eE+-]+(?=\*x)') AS DECIMAL(30,15)),
    `ScaleOffsetOverride` = CAST(REGEXP_SUBSTR(`ScaleExpressionOverride`, '(?<=\+)-?[0-9.eE+-]+$') AS DECIMAL(30,15))
WHERE `ScaleExpressionOverride` IS NOT NULL;");

            // 3) 删除新列
            migrationBuilder.DropColumn(
                name: "ScaleExpression",
                table: "ModelVariables");

            migrationBuilder.DropColumn(
                name: "ScaleExpressionOverride",
                table: "DeviceVariables");
        }
    }
}
