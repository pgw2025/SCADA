using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;

namespace ScadaServer.Application.Interfaces
{
    public interface IAssetRepository
    {
        Task<List<Area>> GetAreasAsync();
        Task<List<DataModel>> GetModelsWithVariablesAsync();
        Task<Device> GetDeviceDetailAsync(int id);
        Task<List<Device>> GetDevicesAsync();
    }

    public interface IHmiRepository
    {
        Task<ScadaProject> GetProjectFullAsync(int id);
        Task<ScadaPage> GetPageWithComponentsAsync(int id);
        Task SavePageComponentsAsync(int pageId, List<HmiComponent> components);
    }

    public interface IAutomationRepository
    {
        Task<List<VariableTrigger>> GetTriggersByDeviceAsync(int deviceId);
        Task<List<DataConversion>> GetActiveConversionsAsync();
    }
}

