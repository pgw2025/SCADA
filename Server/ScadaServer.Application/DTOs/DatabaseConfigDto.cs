namespace ScadaServer.Application.DTOs
{
    public class DatabaseConfigDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string BackendType { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string DatabaseName { get; set; }
    }
}
