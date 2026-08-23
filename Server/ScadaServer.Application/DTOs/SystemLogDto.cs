namespace ScadaServer.Application.DTOs
{
    public class SystemLogDto
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Source { get; set; }
        public string Content { get; set; }
    }
}
