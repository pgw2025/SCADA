namespace ScadaServer.Application.DTOs
{
    public class SensorDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public string VariableKey { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public double LastValue { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }
}
