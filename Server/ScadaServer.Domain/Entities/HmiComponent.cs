using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// HMI组件实体
    /// </summary>
    [SugarTable("HmiComponents")]
    public class HmiComponent : EntityBase
    {
        /// <summary>
        /// 关联的页面ID
        /// </summary>
        public int PageId { get; set; }

        /// <summary>
        /// 组件类型（如：按钮、图表、仪表等）
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 组件名称
        /// </summary>
        public string Name { get; set; }

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
        /// 绑定字段（变量键）
        /// </summary>
        public string BindField { get; set; }

        /// <summary>
        /// 组件属性（JSON格式）
        /// </summary>
        [SugarColumn(ColumnDataType = "text")]
        public string PropsJson { get; set; }
    }
}