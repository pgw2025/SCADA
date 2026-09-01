using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// MQTT 服务器连接配置 DTO（定义将设备变量对外发布的 MQTT Broker）。
    /// </summary>
    public class MqttServerDto
    {
        /// <summary>服务器配置ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>服务器名称；必填，最长 100 字符（校验特性）</summary>
        [Required(ErrorMessage = "服务器名称不能为空")]
        [StringLength(100, ErrorMessage = "名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Broker 地址；必填（校验特性）</summary>
        [Required(ErrorMessage = "Broker地址不能为空")]
        public string BrokerUrl { get; set; } = string.Empty;

        /// <summary>端口号；范围 1-65535（校验特性），默认 1883</summary>
        [Range(1, 65535, ErrorMessage = "端口需在 1-65535 之间")]
        public int Port { get; set; } = 1883;

        /// <summary>MQTT 客户端 ID</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>连接用户名</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 密码（仅写入用）。列表/详情接口不回传明文；无值时接口以空字符串返回。
        /// 编辑时该字段留空表示保持原密码不变。
        /// </summary>
        public string? Password { get; set; }

        /// <summary>主题前缀，用于拼接各变量的推送主题</summary>
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
        /// <summary>Broker 地址；必填（校验特性）</summary>
        [Required(ErrorMessage = "Broker地址不能为空")]
        public string BrokerUrl { get; set; } = string.Empty;

        /// <summary>端口号；范围 1-65535（校验特性），默认 1883</summary>
        [Range(1, 65535, ErrorMessage = "端口需在 1-65535 之间")]
        public int Port { get; set; } = 1883;

        /// <summary>MQTT 客户端 ID</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>连接用户名（可空，未启用认证留空）</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>连接密码（可空，未启用认证留空）</summary>
        public string? Password { get; set; }
    }
}