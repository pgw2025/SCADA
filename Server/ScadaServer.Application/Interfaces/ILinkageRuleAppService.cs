using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    public interface ILinkageRuleAppService
    {
        Task<LinkageRuleDto?> GetByIdAsync(int id);
        Task<List<LinkageRuleDto>> GetListAsync();
        Task CreateAsync(LinkageRuleDto dto);
        Task UpdateAsync(LinkageRuleDto dto);
        Task DeleteAsync(int id);
    }
}
