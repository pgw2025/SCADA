using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// HMI组件实体
    /// </summary>
    [Table("HmiComponents")]
    public class HmiComponent : EntityBase
    {
        /// <summary>
        /// 关联的页面ID
        /// </summary>
        public int PageId { get; set; }

        /// <summary>
        /// 组件类型（如：按钮、图表、仪表等）
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// 组件名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// X坐标
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Y坐标
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// 宽度
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 高度
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Z轴层级（用于层叠显示）
        /// </summary>
        public int ZIndex { get; set; }

        /// <summary>
        /// 归属图层 ID（前端图层 uid，如 'layer-default' / 'layer-1725000000000-ab12'）。
        /// NULL=未归属，前端回退首个图层。图层实体存于 ScadaPage.LayersJson，此处仅为引用，无外键。
        /// </summary>
        public string? LayerId { get; set; }

        /// <summary>
        /// 绑定字段（变量键，旧绑定模型，保留以兼容存量数据）
        /// </summary>
        public string BindField { get; set; } = string.Empty;

        /// <summary>
        /// 组件标签（前端组件显示名，原只能塞进 PropsJson，现独立成列；可选）
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// 绑定设备ID（阶段3 绑定模型预埋，复合绑定 deviceId + variableKey；可选）
        /// </summary>
        public int? BindDeviceId { get; set; }

        /// <summary>
        /// 绑定变量键（阶段3 绑定模型预埋；可选，未绑定组件为空）
        /// </summary>
        public string? BindVariableKey { get; set; }

        /// <summary>
        /// 组件属性（JSON格式）
        /// </summary>
        [Column(TypeName = "text")]
        public string PropsJson { get; set; } = string.Empty;
    }
}