namespace ScadaServer.Application.DTOs
{
    public class SensorDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public string VariableKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double LastValue { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }
}
