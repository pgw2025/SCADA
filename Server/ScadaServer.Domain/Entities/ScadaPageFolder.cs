using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// SCADA 画面文件夹实体：用于组态画面列表的文件夹分类管理。
    /// 支持通过 ParentFolderId 自引用实现多级嵌套；适用于 Desktop/Mobile/Popup 三端。
    /// </summary>
    [Table("ScadaPageFolders")]
    public class ScadaPageFolder
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// 关联的项目ID
        /// </summary>
        public int ProjectId { get; set; }

        /// <summary>
        /// 关联的项目
        /// </summary>
        public ScadaProject Project { get; set; } = null!;

        /// <summary>
        /// 父文件夹ID（自引用）。NULL 表示该端根级文件夹。
        /// </summary>
        public int? ParentFolderId { get; set; }

        /// <summary>
        /// 父文件夹（自引用导航）
        /// </summary>
        public ScadaPageFolder? Parent { get; set; }

        /// <summary>
        /// 子文件夹集合
        /// </summary>
        public List<ScadaPageFolder> Children { get; set; } = new();

        /// <summary>
        /// 该文件夹下的画面集合（ScadaPage.FolderId 反向引用）
        /// </summary>
        public List<ScadaPage> Pages { get; set; } = new();

        /// <summary>
        /// 文件夹归属端：Desktop（桌面端）/ Mobile（移动端）/ Popup（弹窗）。默认 Desktop。
        /// </summary>
        [StringLength(16)]
        public string Platform { get; set; } = "Desktop";

        /// <summary>
        /// 文件夹名称
        /// </summary>
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 同级「文件夹段」内排序（从 1 起连续编号；与画面段 SortOrder 相互独立）
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }
}