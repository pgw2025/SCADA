namespace ScadaServer.Application.Interfaces
{
    public interface IExternalMessageSender
    {
        string Name { get; }
        bool Enabled { get; }
        Task SendAsync(DTOs.ExternalMessage message, CancellationToken cancellationToken);
    }

    public interface IExternalNotificationQueue
    {
        bool HasEnabledChannels { get; }
        void Enqueue(DTOs.ExternalMessage message);
    }
}
