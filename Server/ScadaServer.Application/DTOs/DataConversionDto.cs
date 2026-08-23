namespace ScadaServer.Application.DTOs
{
    public class DataConversionDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int SourceDeviceId { get; set; }
        public string SourceVariableKey { get; set; }
        public int TargetDeviceId { get; set; }
        public string TargetVariableKey { get; set; }
        public bool Active { get; set; }
    }
}
