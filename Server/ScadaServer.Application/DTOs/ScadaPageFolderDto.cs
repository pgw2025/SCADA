using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 组态画面文件夹 DTO：用于组态画面列表的文件夹分类管理（仅 Desktop/Mobile 端）。
    /// </summary>
    public class ScadaPageFolderDto
    {
        /// <summary>文件夹ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>所属组态工程ID</summary>
        [Range(1, int.MaxValue, ErrorMessage = "请选择所属工程")]
        public int ProjectId { get; set; }

        /// <summary>父文件夹ID（自引用）。NULL 表示该端根级文件夹。</summary>
        public int? ParentFolderId { get; set; }

        /// <summary>文件夹归属端：仅 Desktop / Mobile（Popup 弹窗画面不建文件夹）。默认 Desktop。</summary>
        [StringLength(16, ErrorMessage = "归属端不能超过16个字符")]
        public string Platform { get; set; } = "Desktop";

        /// <summary>文件夹名称</summary>
        [Required(ErrorMessage = "文件夹名称不能为空")]
        [StringLength(100, ErrorMessage = "文件夹名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>同级「文件夹段」内排序（从 1 起连续编号；与画面段相互独立）</summary>
        public int SortOrder { get; set; }

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; }
    }
}