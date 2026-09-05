using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// Web Push 订阅记录（doc/pwa 阶段五 · 步骤 20，§5.3）。
    /// <para>
    /// 订阅与登录会话解耦（D3）：Endpoint 唯一键 upsert——同一浏览器换账号登录后重新 POST，
    /// 即把 UserId 更新为当前账号（换绑）；RenewalToken 为 SW 自动续订凭证（换绑时重签）。
    /// </para>
    /// <para>
    /// 不建 SystemUsers 外键（同 AlarmRecord 设计取舍）：用户删除不级联影响订阅表，
    /// 孤儿订阅由推送失败清理（410/404）与 renew 失效兜底。
    /// </para>
    /// </summary>
    [Table("PushSubscriptions")]
    public class PushSubscription
    {
        /// <summary>主键（自增）</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>推送服务 URL（浏览器订阅 endpoint），唯一索引（换绑 upsert 键）</summary>
        [Required]
        [MaxLength(512)]
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>客户端公钥（RFC 8291 p256dh）</summary>
        [MaxLength(256)]
        public string P256DH { get; set; } = string.Empty;

        /// <summary>认证密钥（RFC 8291 auth）</summary>
        [MaxLength(256)]
        public string Auth { get; set; } = string.Empty;

        /// <summary>当前绑定用户ID（SystemUsers.Id，换绑即更新；不建外键，见类注释）</summary>
        public int UserId { get; set; }

        /// <summary>SW 自动续订凭证（随机 opaque token，换绑时重签；D12）</summary>
        [MaxLength(128)]
        public string RenewalToken { get; set; } = string.Empty;

        /// <summary>订阅自带过期时间（部分浏览器返回，可为空）</summary>
        public DateTime? ExpirationTime { get; set; }

        /// <summary>设备/浏览器识别（设置页「我的设备」展示）</summary>
        [MaxLength(256)]
        public string UserAgent { get; set; } = string.Empty;

        /// <summary>创建时间（UTC）</summary>
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>最近推送成功时间（UTC，运维观测）</summary>
        public DateTime? LastPushAtUtc { get; set; }

        /// <summary>最近推送失败原因（410/404/超时…，运维观测）</summary>
        [MaxLength(64)]
        public string? LastErrorCode { get; set; }
    }
}
