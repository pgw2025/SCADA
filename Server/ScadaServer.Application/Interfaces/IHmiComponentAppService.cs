using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IHmiComponentAppService
    {
        Task<HmiComponentDto?> GetByIdAsync(int id);
        Task<List<HmiComponentDto>> GetListAsync();
        /// <summary>创建组件，返回新建的自增Id</summary>
        Task<int> CreateAsync(HmiComponentDto dto);
        /// <summary>更新组件，返回是否存在并更新成功</summary>
        Task<bool> UpdateAsync(HmiComponentDto dto);
        Task DeleteAsync(int id);
    }
}

