using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    public class MqttServerDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "服务器名称不能为空")]
        [StringLength(100, ErrorMessage = "名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Broker地址不能为空")]
        public string BrokerUrl { get; set; } = string.Empty;

        [Range(1, 65535, ErrorMessage = "端口需在 1-65535 之间")]
        public int Port { get; set; } = 1883;

        public string ClientId { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 密码（仅写入用）。列表/详情接口不回传明文；无值时接口以空字符串返回。
        /// 编辑时该字段留空表示保持原密码不变。
        /// </summary>
        public string? Password { get; set; }

        public string TopicPrefix { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用：停用时断开连接且不再发布，默认 true。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 该服务器下已关联的变量数量（列表/详情查询时由后端填充）。
        /// </summary>
        public int VariableCount { get; set; }
    }

    /// <summary>
    /// 测试连接请求（不落库）。与 MqttServerDto 分离：
    /// 测试时尚未命名是正常场景，不应触发 Name 必填校验。
    /// </summary>
    public class MqttTestConnectionDto
    {
        [Required(ErrorMessage = "Broker地址不能为空")]
        public string BrokerUrl { get; set; } = string.Empty;

        [Range(1, 65535, ErrorMessage = "端口需在 1-65535 之间")]
        public int Port { get; set; } = 1883;

        public string ClientId { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string? Password { get; set; }
    }
}