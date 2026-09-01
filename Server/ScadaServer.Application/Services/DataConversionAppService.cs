using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 数据转发（源/目标变量映射）应用服务实现：负责数据转换规则的增删改查（CRUD）。
    /// 规则定义源设备变量到目标设备变量的读取/写入映射，实际转换由运行时引擎执行。
    /// </summary>
    public class DataConversionAppService : IDataConversionAppService
    {
        /// <summary>数据转换仓储，提供持久化能力。</summary>
        private readonly IDataConversionRepository _repository;

        /// <summary>构造函数：注入数据转换仓储。</summary>
        public DataConversionAppService(IDataConversionRepository repository) { _repository = repository; }

        /// <summary>按主键获取数据转换规则，不存在时返回 null。</summary>
        public async Task<DataConversionDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new DataConversionDto
            {
                Id = entity.Id,
                Name = entity.Name,
                SourceDeviceId = entity.SourceDeviceId,
                SourceVariableKey = entity.SourceVariableKey,
                TargetDeviceId = entity.TargetDeviceId,
                TargetVariableKey = entity.TargetVariableKey,
                Active = entity.Active
            };
        }

        /// <summary>获取全部数据转换规则列表。</summary>
        public async Task<List<DataConversionDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new DataConversionDto
            {
                Id = entity.Id,
                Name = entity.Name,
                SourceDeviceId = entity.SourceDeviceId,
                SourceVariableKey = entity.SourceVariableKey,
                TargetDeviceId = entity.TargetDeviceId,
                TargetVariableKey = entity.TargetVariableKey,
                Active = entity.Active
            }).ToList();
        }

        /// <summary>新增数据转换规则，并将生成的主键写回 DTO。</summary>
        public async Task CreateAsync(DataConversionDto dto)
        {
            var entity = new DataConversion
            {
                Name = dto.Name,
                SourceDeviceId = dto.SourceDeviceId,
                SourceVariableKey = dto.SourceVariableKey,
                TargetDeviceId = dto.TargetDeviceId,
                TargetVariableKey = dto.TargetVariableKey,
                Active = dto.Active
            };
            await _repository.InsertAsync(entity);
            // InsertAsync 内部 SaveChangesAsync 后自增 Id 已回填到实体，同步回写 DTO 供接口返回
            dto.Id = entity.Id;
        }

        /// <summary>更新数据转换规则；记录不存在时静默忽略。</summary>
        public async Task UpdateAsync(DataConversionDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Name = dto.Name;
                entity.SourceDeviceId = dto.SourceDeviceId;
                entity.SourceVariableKey = dto.SourceVariableKey;
                entity.TargetDeviceId = dto.TargetDeviceId;
                entity.TargetVariableKey = dto.TargetVariableKey;
                entity.Active = dto.Active;
                await _repository.UpdateAsync(entity);
            }
        }

        /// <summary>删除数据转换规则；记录不存在时静默忽略。</summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }
    }
}

