using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 同级画面/文件夹排序项：用于 reorder 批量排序。
    /// Id 在该端同父级下唯一，SortOrder 为该段连续值。
    /// </summary>
    public class FolderOrderItemDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "ID 必须为正数")]
        public int Id { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "排序值必须为非负数")]
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// 文件夹/画面统一排序请求体：同父级下按「文件夹段」（Folders）与「画面段」（Pages）
    /// 各自全量提交排序，两段互不干扰。
    /// </summary>
    public class FolderReorderDto
    {
        /// <summary>所属端（Desktop/Mobile），与目标父级一致</summary>
        public string? Platform { get; set; }

        /// <summary>目标父级文件夹ID；NULL=某端根级</summary>
        public int? ParentFolderId { get; set; }

        /// <summary>该父级下「文件夹段」全量排序（文件夹恒展示在前）</summary>
        public List<FolderOrderItemDto> Folders { get; set; } = new();

        /// <summary>该父级下「画面段」全量排序</summary>
        public List<FolderOrderItemDto> Pages { get; set; } = new();
    }
}