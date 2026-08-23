namespace ScadaServer.Application.DTOs
{
    public class UpdateDeviceStatusDto
    {
        public int DeviceId { get; set; }
        public string NewStatus { get; set; }
    }
}
