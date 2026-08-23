namespace ScadaServer.Application.DTOs
{
    public class VariableTriggerDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int DeviceId { get; set; }
        public string VariableKey { get; set; }
        public string Condition { get; set; }
        public double Threshold { get; set; }
        public string ActionType { get; set; }
        public string AlarmLevel { get; set; }
        public string LinkageVariableKey { get; set; }
        public string LinkageValue { get; set; }
        public bool Active { get; set; }
    }
}
