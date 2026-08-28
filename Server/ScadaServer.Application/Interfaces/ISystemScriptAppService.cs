using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface ISystemScriptAppService
    {
        Task<SystemScriptDto?> GetByIdAsync(int id);
        Task<List<SystemScriptDto>> GetListAsync();
        Task CreateAsync(SystemScriptDto dto);
        Task UpdateAsync(SystemScriptDto dto);
        Task DeleteAsync(int id);

        /// <summary>
        /// 人工复位熔断状态（Tripped=false、FailureCount=0、LastError=null）。
        /// </summary>
        Task ResetTrippedAsync(int id);
    }
}

