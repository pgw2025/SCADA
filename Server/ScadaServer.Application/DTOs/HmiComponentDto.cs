using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    public class HmiComponentDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "页面ID不能为空")]
        [Range(1, int.MaxValue, ErrorMessage = "页面ID必须大于0")]
        public int PageId { get; set; }

        [Required(ErrorMessage = "组件类型不能为空")]
        public string Type { get; set; }

        [Required(ErrorMessage = "组件名称不能为空")]
        public string Name { get; set; }

        [Range(0, 100000, ErrorMessage = "X坐标超出合理范围")]
        public int X { get; set; }

        [Range(0, 100000, ErrorMessage = "Y坐标超出合理范围")]
        public int Y { get; set; }

        [Range(1, 100000, ErrorMessage = "宽度必须大于0")]
        public int Width { get; set; }

        [Range(1, 100000, ErrorMessage = "高度必须大于0")]
        public int Height { get; set; }

        [Range(0, 1000, ErrorMessage = "层级超出合理范围")]
        public int ZIndex { get; set; }

        public string BindField { get; set; }

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

        public string PropsJson { get; set; }
    }
}
