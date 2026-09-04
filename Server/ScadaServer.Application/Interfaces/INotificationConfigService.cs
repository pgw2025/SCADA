using System.Threading.Tasks;
using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>运行时消息通知配置管理服务（钉钉 / SMTP）。</summary>
    public interface INotificationConfigService
    {
        /// <summary>获取当前生效的通知配置（敏感字段以掩码回显）。</summary>
        Task<NotificationConfigDto> GetAsync();

        /// <summary>保存通知配置到 override 文件（重启后生效；掩码/空敏感项 = 不改）。</summary>
        Task SaveAsync(NotificationConfigDto dto);

        /// <summary>用临时提交的钉钉配置发送一条测试消息（不落盘）。</summary>
        Task<NotificationTestResult> TestDingTalkAsync(DingTalkConfigDto dto);

        /// <summary>用临时提交的邮件配置发送一封测试邮件（不落盘）。</summary>
        Task<NotificationTestResult> TestEmailAsync(EmailConfigDto dto);
    }
}
