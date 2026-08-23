namespace ScadaServer.Infrastructure.Communication
{
    public interface IDeviceRuntimeManager
    {
        Task RefreshDevice(int deviceId);
        Task ReloadAll();
    }
}
