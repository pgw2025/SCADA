using ScadaServer.Application.Interfaces;

namespace ScadaServer.Infrastructure.Communication
{
    public class MqttHandler : IMqttService
    {
        public async Task PublishAsync(string topic, string payload)
        {
            await Task.CompletedTask;
        }

        public async Task SubscribeAsync(string topic)
        {
            await Task.CompletedTask;
        }
    }
}
