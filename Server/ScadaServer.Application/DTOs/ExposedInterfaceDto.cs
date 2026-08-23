namespace ScadaServer.Application.DTOs
{
    public class ExposedInterfaceDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string RouteUrl { get; set; }
        public string RequestMethod { get; set; }
        public int DeviceId { get; set; }
        public string ExposedKey { get; set; }
        public bool Active { get; set; }
    }
}
