namespace ScadaServer.Application.DTOs
{
    public class MqttServerDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string BrokerUrl { get; set; }
        public int Port { get; set; }
        public string ClientId { get; set; }
        public string Username { get; set; }
        public string TopicPrefix { get; set; }
    }
}
