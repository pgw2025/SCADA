using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IAlarmRuleAppService
    {
        Task<AlarmRuleDto> GetByIdAsync(int id);
        Task<List<AlarmRuleDto>> GetListAsync();
        Task CreateAsync(AlarmRuleDto dto);
        Task UpdateAsync(AlarmRuleDto dto);
        Task DeleteAsync(int id);
    }
}

