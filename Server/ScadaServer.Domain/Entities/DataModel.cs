using SqlSugar;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 数据模型实体
    /// </summary>
    [SugarTable("DataModels")]
    public class DataModel : EntityBase
    {
        /// <summary>
        /// 模型名称
        /// </summary>
        [SugarColumn(Length = 100, IsNullable = false)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 模型描述
        /// </summary>
        [SugarColumn(Length = 500)]
        public string? Description { get; set; }

        /// <summary>
        /// 设备类型（决定驱动类型）
        /// </summary>
        public DeviceType Type { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 模型包含的变量列表
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public List<ModelVariable>? Variables { get; set; }
    }
}