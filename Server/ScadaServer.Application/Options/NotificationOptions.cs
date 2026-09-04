namespace ScadaServer.Application.Options
{
    public class NotificationOptions
    {
        public const string SectionName = "Notification";

        public DingTalkOptions DingTalk { get; set; } = new();
        public EmailOptions Email { get; set; } = new();
        public ExternalPushPolicy Push { get; set; } = new();
    }

    public class DingTalkOptions
    {
        public bool Enabled { get; set; }
        public string Webhook { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
    }

    public class EmailOptions
    {
        public bool Enabled { get; set; }
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 465;
        public bool UseSsl { get; set; } = true;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string FromName { get; set; } = "SCADA 报警中心";
        public List<string> To { get; set; } = new();
    }

    public class ExternalPushPolicy
    {
        public bool PushAlarm { get; set; } = true;
        public bool PushDeviceOffline { get; set; } = true;
        public bool PushDeviceOnline { get; set; } = false;
        public int DeviceStatusDebounceMinutes { get; set; } = 5;
        public bool PushSystemAlarm { get; set; } = true;
        public bool PushSystemError { get; set; } = true;
        public bool PushScript { get; set; } = true;
        public int MaxPerMinutePerChannel { get; set; } = 15;
        public int MaxAttempts { get; set; } = 2;
        public int RetryBaseDelayMs { get; set; } = 1000;
        public int QueueCapacity { get; set; } = 2048;
    }
}
