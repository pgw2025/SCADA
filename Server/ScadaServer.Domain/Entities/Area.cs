using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 区域实体（用于设备分组管理）。
    /// <para>
    /// 目标设计：区域为一级组织实体，承载 工厂→车间→产线→区域 的树形结构，
    /// 通过 <see cref="ParentId"/> 自引用实现层级。区域负责回答"设备在哪里"，
    /// 与控制关系（Controller）、数据定义（DataModel）严格分离。
    /// </para>
    /// </summary>
    [Table("Areas")]
    public class Area
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// 父区域ID（NULL 表示根区域）。自引用外键，删除父区域前须无子区域引用。
        /// </summary>
        public int? ParentId { get; set; }

        /// <summary>
        /// 父区域导航属性（对应 <see cref="ParentId"/>）。
        /// </summary>
        [ForeignKey(nameof(ParentId))]
        public Area? Parent { get; set; }

        /// <summary>
        /// 子区域集合（树形遍历用，通常不随列表加载）。
        /// </summary>
        [InverseProperty(nameof(Parent))]
        public List<Area> Children { get; set; } = new();

        /// <summary>
        /// 区域名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 区域编码（稳定短码，如 BLR）。用于设备编号自动生成的前缀；留空时回退为 A{Id}。
        /// 库级唯一（NULL 可多条共存）；变更会影响后续新生成设备编号，不影响既有设备 Key。
        /// </summary>
        public string? Code { get; set; }

        /// <summary>
        /// 区域类型（Factory/Workshop/ProductionLine/Area/Warehouse），默认 Area。
        /// </summary>
        public AreaTypeEnum AreaType { get; set; } = AreaTypeEnum.Area;

        /// <summary>
        /// 区域描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 排序（同级内展示顺序），默认 0。
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 是否启用，默认 true。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 创建时间（UTC 存储）
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 更新时间（UTC 存储）
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
