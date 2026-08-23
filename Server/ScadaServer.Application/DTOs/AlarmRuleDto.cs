namespace ScadaServer.Application.DTOs
{
    public class AlarmRuleDto
    {
        public int Id { get; set; }
        public int SensorId { get; set; }
        public string Condition { get; set; }
        public double Threshold { get; set; }
        public string Severity { get; set; }
        public bool IsEnabled { get; set; }
    }
}
