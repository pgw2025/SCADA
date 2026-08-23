namespace ScadaServer.Application.DTOs
{
    public class ConfigLogDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public string Operator { get; set; }
        public string ChangeDesc { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
