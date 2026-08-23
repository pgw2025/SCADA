namespace ScadaServer.Application.DTOs
{
    public class ScheduledTaskDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string CronExpression { get; set; }
        public string ParamsJson { get; set; }
        public bool Active { get; set; }
    }
}
