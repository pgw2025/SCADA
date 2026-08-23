namespace ScadaServer.Application.DTOs
{
    public class SystemConfigDto
    {
        public int Id { get; set; }
        public string SystemTitle { get; set; }
        public int PollIntervalMs { get; set; }
        public string MqttBrokerHost { get; set; }
        public int RetentionPeriodDays { get; set; }
    }
}
