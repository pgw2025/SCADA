using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IDataModelAppService
    {
        Task<DataModelDto?> GetByIdAsync(int id);

        /// <summary>
        /// 获取数据模型列表。
        /// <paramref name="includeVariables"/> 为 false 时跳过模型变量查询，
        /// 用于列表页只展示模型概览、降低 N+1 查询开销；详情页请使用 <see cref="GetByIdAsync"/>（默认已聚合变量）。
        /// </summary>
        Task<List<DataModelDto>> GetListAsync(bool includeVariables = true);
        Task<DataModelDto> CreateAsync(CreateDataModelDto dto);
        Task<DataModelDto> UpdateAsync(DataModelDto dto);
        Task DeleteAsync(int id);
    }
}

