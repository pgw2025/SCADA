namespace ScadaServer.Application.DTOs
{
    public class ConfigLogDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public string Operator { get; set; } = string.Empty;
        public string ChangeDesc { get; set; } = string.Empty;
        public DateTime CreateTime { get; set; }
    }
}
