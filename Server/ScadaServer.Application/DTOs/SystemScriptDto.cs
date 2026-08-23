namespace ScadaServer.Application.DTOs
{
    public class SystemScriptDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public string TriggerType { get; set; }
        public int? IntervalSeconds { get; set; }
        public bool Active { get; set; }
    }
}
