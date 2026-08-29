namespace ScadaServer.Application.DTOs
{
    public class ExposedInterfaceDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string RouteUrl { get; set; } = string.Empty;
        public string RequestMethod { get; set; } = string.Empty;
        public int DeviceId { get; set; }
        public string ExposedKey { get; set; } = string.Empty;
        public bool Active { get; set; }
    }
}
