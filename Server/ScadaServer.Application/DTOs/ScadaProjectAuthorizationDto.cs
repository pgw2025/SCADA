using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 工程已授权用户条目（GET /api/ScadaProject/{projectId}/authorizations 返回）。
    /// </summary>
    public class ScadaProjectAuthorizedUserDto
    {
        /// <summary>被授权用户 Id。</summary>
        public int UserId { get; set; }

        /// <summary>用户名。</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>用户角色（Admin / Operator / Viewer）。</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>用户状态（Active / Inactive）。</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>授权时间（UTC）。</summary>
        public DateTime GrantedAt { get; set; }
    }

    /// <summary>
    /// 保存工程授权请求体（PUT /api/ScadaProject/{projectId}/authorizations）。
    /// userIds 为最终授权的用户集合（全量覆盖语义）。
    /// </summary>
    public class SaveScadaProjectAuthorizationDto
    {
        /// <summary>目标授权用户 Id 集合（允许为空数组=清空授权）。</summary>
        [Required]
        public List<int> UserIds { get; set; } = new();
    }
}
