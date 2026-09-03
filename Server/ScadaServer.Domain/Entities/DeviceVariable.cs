using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 设备变量实体，描述"某个变量在具体设备中的实现（实例化）"。
    /// <para>
    /// 与 <see cref="ModelVariable"/>（变量"是什么"：变量键、名称、数据类型、缩放、死区等模板定义）不同，
    /// <see cref="DeviceVariable"/> 描述该变量在某一台具体设备（<see cref="Device"/>）上的落地实现：
    /// 实际寄存器地址、位偏移、轮询间隔，以及相对模板的缩放 / 死区覆盖值。
    /// </para>
    /// <para>
    /// 关系：<see cref="Device"/> 1:N <see cref="DeviceVariable"/>，<see cref="ModelVariable"/> 1:N <see cref="DeviceVariable"/>。
    /// 即一个 <see cref="ModelVariable"/> 模板可被多台设备各自实例化出一条 <see cref="DeviceVariable"/>。
    /// </para>
    /// <para>
    /// 实现级覆盖语义：所有带 <c>Override</c> 后缀的字段，以及 <see cref="Address"/> / <see cref="BitOffset"/> /
    /// <see cref="PollingIntervalMs"/> 允许为 null，表示"未在该设备实例上显式指定时，回退到所关联
    /// <see cref="ModelVariable"/> 模板的对应值"。运行期 / 驱动层（本阶段未改动）解析实际地址、缩放、轮询时，
    /// 应优先取设备实例值，为空再取模板值。
    /// </para>
    /// </summary>
    [Table("DeviceVariables")]
    public class DeviceVariable
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        /// <summary>
        /// 关联设备ID（外键，指向 <see cref="Device"/>）
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 关联设备导航属性（对应 <see cref="DeviceId"/>）
        /// </summary>
        [ForeignKey(nameof(DeviceId))]
        public Device? Device { get; set; }

        /// <summary>
        /// 关联模型变量ID（外键，指向变量模板 <see cref="ModelVariable"/>）
        /// </summary>
        public int ModelVariableId { get; set; }

        /// <summary>
        /// 关联模型变量导航属性（对应 <see cref="ModelVariableId"/>）
        /// </summary>
        [ForeignKey(nameof(ModelVariableId))]
        public ModelVariable? ModelVariable { get; set; }

        /// <summary>
        /// 设备实例上的实际寄存器地址（本字段为 <see cref="ModelVariable.Address"/> 的迁入归属，是变量的权威实现地址）。
        /// <para>
        /// 允许为空：过渡期可回退到 <see cref="ModelVariable.Address"/> 模板值（该模板字段已标记 <c>[Obsolete]</c>，后续将移除）；
        /// 新设备建议始终显式赋值，不再依赖模板。
        /// </para>
        /// </summary>
        public string? Address { get; set; }

        /// <summary>
        /// 位偏移（用于位操作，本字段为 <see cref="ModelVariable.BitOffset"/> 的迁入归属）。
        /// <para>
        /// 允许为空：过渡期可回退到 <see cref="ModelVariable.BitOffset"/> 模板值（该模板字段已标记 <c>[Obsolete]</c>，后续将移除）。
        /// </para>
        /// </summary>
        public int? BitOffset { get; set; }

        /// <summary>
        /// 设备实例结构化地址（JSON，<see cref="ScadaServer.Domain.Addresses.AddressConfig"/>）。
        /// <para>
        /// 地址的<strong>权威机读形态</strong>：前端仅编辑本字段，后端据此经
        /// <see cref="ScadaServer.Domain.Addresses.AddressConfigSerializer.ToDisplay"/> 自动生成
        /// <see cref="Address"/> 展示串并持久化，保证"JSON 权威、字符串展示冗余"的一致性。
        /// </para>
        /// </summary>
        [Column(TypeName = "longtext")]
        public string? AddressConfigJson { get; set; }

        /// <summary>
        /// 是否启用该设备实例变量，默认 true。
        /// <para>
        /// 与 <see cref="Device.IsEnabled"/>（设备级启用）、<see cref="ModelVariable"/>（模板）相互独立，
        /// 用于单独停用某台设备上某变量的采集，而不影响模板或其它设备。
        /// </para>
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 设备实例上的轮询间隔（毫秒，本字段为 <see cref="ModelVariable.PollingIntervalMs"/> 的迁入归属，是权威值）。
        /// <para>
        /// 允许为空：过渡期可回退到 <see cref="ModelVariable.PollingIntervalMs"/> 模板值（该模板字段已标记 <c>[Obsolete]</c>，后续将移除）；
        /// 同时设备级 <see cref="Device.PollingInterval"/> 可作为更上层默认。
        /// </para>
        /// </summary>
        public int? PollingIntervalMs { get; set; }

        /// <summary>
        /// 工程换算表达式覆盖值（设备实例级）。
        /// <para>允许为空：为空时使用 <see cref="ModelVariable.ScaleExpression"/> 模板值（模板为空即恒等变换）。</para>
        /// </summary>
        [MaxLength(200)]
        public string? ScaleExpressionOverride { get; set; }

        /// <summary>
        /// 死区值覆盖值（用于变化检测）。
        /// <para>允许为空：为空时使用 <see cref="ModelVariable.DeadBand"/> 模板值。</para>
        /// </summary>
        public double? DeadBandOverride { get; set; }

        /// <summary>
        /// 读写权限覆盖值。
        /// <para>
        /// 允许为空：为空时回退到 <see cref="ModelVariable.IsReadOnly"/> 模板值（继承模板权限）；
        /// true = 强制只读；false = 强制可写。用于单台设备上对某变量权限做差异化覆盖，不影响模板及其它设备。
        /// </para>
        /// </summary>
        public bool? IsReadOnlyOverride { get; set; }

        /// <summary>
        /// 变量级连接覆盖（阶段 4 新增，可空 FK → <see cref="DeviceConnection"/>）。
        /// <para>空 = 使用设备默认连接（<c>Device.ConnectionId</c>）。存量数据迁移时按所属设备回填；
        /// 运行时本阶段不读该列，列的启用放阶段 6。</para>
        /// </summary>
        public int? ConnectionId { get; set; }

        /// <summary>
        /// 原始数据类型字符串形式（阶段 4 新增，记录性字段，如 "REAL" / "DINT"）。
        /// <para>存量回填取 <see cref="ModelVariable.DataType"/> 的字符串形式；驱动仍按
        /// <c>DataTypeEnum</c> 解释，本阶段不启用校验。</para>
        /// </summary>
        [MaxLength(32)]
        public string? RawDataType { get; set; }

        /// <summary>
        /// 扩展数据（JSON 格式，设备实例级附加信息，如设备厂商私有参数等）
        /// </summary>
        [Column(TypeName = "longtext")]
        public Dictionary<string, string>? ExtensionData { get; set; }
    }
}
