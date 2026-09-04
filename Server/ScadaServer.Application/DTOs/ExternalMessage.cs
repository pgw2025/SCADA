namespace ScadaServer.Application.DTOs
{
    public enum ExternalMessageCategory
    {
        Alarm,
        DeviceStatus,
        SystemAlarm,
        SystemError,
        ScriptExecution
    }

    public class ExternalMessage
    {
        public ExternalMessageCategory Category { get; set; }
        public string Title { get; set; } = string.Empty;
        public string MarkdownText { get; set; } = string.Empty;
        public string? HtmlBody { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
