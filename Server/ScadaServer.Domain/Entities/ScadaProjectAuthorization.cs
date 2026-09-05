using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 组态工程授权实体：记录「某工程授权给某用户」的多对多关系。
    /// 复合主键 (ProjectId, UserId) 天然防止重复授权；工程/用户删除时由外键级联清理授权记录。
    /// </summary>
    [Table("ScadaProjectAuthorizations")]
    public class ScadaProjectAuthorization
    {
        /// <summary>
        /// 工程 Id（复合主键之一，FK → ScadaProjects）。
        /// </summary>
        public int ProjectId { get; set; }

        /// <summary>
        /// 被授权用户 Id（复合主键之一，FK → SystemUsers）。
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// 授权时间（UTC）。
        /// </summary>
        public DateTime GrantedAt { get; set; }
    }
}
