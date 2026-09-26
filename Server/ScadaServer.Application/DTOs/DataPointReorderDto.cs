using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 模板变量排序项：用于 reorder 批量排序。
    /// Id 在所属模型内唯一，Sort 为该模型内展示顺序（归一化连续值）。
    /// </summary>
    public class DataPointOrderItemDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "ID 必须为正数")]
        public int Id { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "排序值必须为非负数")]
        public int Sort { get; set; }
    }

    /// <summary>
    /// 模板变量统一排序请求体：按所属数据模型（ModelId）全量提交排序，模型间互不干扰。
    /// 排序是纯展示序，不影响采集/换算/历史，无需热加载运行时。
    /// </summary>
    public class DataPointReorderDto
    {
        /// <summary>目标数据模型ID，排序范围限定在该模型内</summary>
        [Range(1, int.MaxValue, ErrorMessage = "模型ID 必须为正数")]
        public int ModelId { get; set; }

        /// <summary>该模型下模板变量全量排序（缺省值按 Sort 升序、Id 兜底）</summary>
        public List<DataPointOrderItemDto> Items { get; set; } = new();
    }
}
