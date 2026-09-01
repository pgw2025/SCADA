using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 组态（HMI）页面组件 DTO（定义页面上每个组件的位置、大小与数据绑定）。
    /// </summary>
    public class HmiComponentDto
    {
        /// <summary>组件ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>所属组态页面ID；必填，需大于 0（校验特性）</summary>
        [Required(ErrorMessage = "页面ID不能为空")]
        [Range(1, int.MaxValue, ErrorMessage = "页面ID必须大于0")]
        public int PageId { get; set; }

        /// <summary>组件类型（如 开关/仪表/文本框）；必填（校验特性）</summary>
        [Required(ErrorMessage = "组件类型不能为空")]
        public string Type { get; set; } = string.Empty;

        /// <summary>组件名称；必填（校验特性）</summary>
        [Required(ErrorMessage = "组件名称不能为空")]
        public string Name { get; set; } = string.Empty;

        /// <summary>组件 X 坐标；范围 0-100000（校验特性）</summary>
        [Range(0, 100000, ErrorMessage = "X坐标超出合理范围")]
        public int X { get; set; }

        /// <summary>组件 Y 坐标；范围 0-100000（校验特性）</summary>
        [Range(0, 100000, ErrorMessage = "Y坐标超出合理范围")]
        public int Y { get; set; }

        /// <summary>组件宽度；需大于 0（校验特性）</summary>
        [Range(1, 100000, ErrorMessage = "宽度必须大于0")]
        public int Width { get; set; }

        /// <summary>组件高度；需大于 0（校验特性）</summary>
        [Range(1, 100000, ErrorMessage = "高度必须大于0")]
        public int Height { get; set; }

        /// <summary>组件层级（z-index）；范围 0-1000（校验特性）</summary>
        [Range(0, 1000, ErrorMessage = "层级超出合理范围")]
        public int ZIndex { get; set; }

        /// <summary>
        /// 归属图层 ID（前端图层 uid，可选）
        /// </summary>
        [MaxLength(64)]
        public string? LayerId { get; set; }

        /// <summary>组件绑定字段（数据字段名）</summary>
        public string BindField { get; set; } = string.Empty;

        /// <summary>
        /// 组件标签（前端显示名，可选）
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// 绑定设备ID（阶段3 绑定模型，可选）
        /// </summary>
        public int? BindDeviceId { get; set; }

        /// <summary>
        /// 绑定变量键（阶段3 绑定模型，可选）
        /// </summary>
        public string? BindVariableKey { get; set; }

        /// <summary>组件的扩展属性 JSON（样式、外观等）</summary>
        public string PropsJson { get; set; } = string.Empty;
    }
}
