using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 联动规则应用服务实现：负责联动规则（变量触发后写另一变量值）的增删改查。
    /// 规则仅做持久化，实际联动执行由运行时引擎实时处理。
    /// </summary>
    public class LinkageRuleAppService : ILinkageRuleAppService
    {
        /// <summary>联动规则仓储，提供持久化能力。</summary>
        private readonly ILinkageRuleRepository _repository;

        /// <summary>构造函数：注入联动规则仓储。</summary>
        public LinkageRuleAppService(ILinkageRuleRepository repository)
        {
            _repository = repository;
        }

        /// <summary>按主键获取联动规则，不存在时返回 null。</summary>
        public async Task<LinkageRuleDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        /// <summary>获取全部联动规则列表。</summary>
        public async Task<List<LinkageRuleDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        /// <summary>新增联动规则，并将生成的主键写回 DTO。</summary>
        public async Task CreateAsync(LinkageRuleDto dto)
        {
            var entity = MapToEntity(dto);
            await _repository.InsertAsync(entity);
            dto.Id = entity.Id;
        }

        /// <summary>按 DTO 携带的主键更新既有规则；记录不存在时直接返回。</summary>
        public async Task UpdateAsync(LinkageRuleDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null) return;
            entity.Name = dto.Name;
            entity.DeviceId = dto.DeviceId;
            entity.VariableKey = dto.VariableKey;
            entity.Condition = dto.Condition;
            entity.Threshold = dto.Threshold;
            entity.ActionType = dto.ActionType;
            entity.LinkageVariableKey = dto.LinkageVariableKey;
            entity.LinkageValue = dto.LinkageValue;
            entity.Active = dto.Active;
            await _repository.UpdateAsync(entity);
        }

        /// <summary>删除指定联动规则；记录不存在时静默忽略。</summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }

        /// <summary>将联动规则实体映射为 DTO。</summary>
        private static LinkageRuleDto MapToDto(LinkageRule entity) => new()
        {
            Id = entity.Id,
            Name = entity.Name,
            DeviceId = entity.DeviceId,
            VariableKey = entity.VariableKey,
            Condition = entity.Condition,
            Threshold = entity.Threshold,
            ActionType = entity.ActionType,
            LinkageVariableKey = entity.LinkageVariableKey,
            LinkageValue = entity.LinkageValue,
            Active = entity.Active
        };

        /// <summary>将联动规则 DTO 映射为实体。</summary>
        private static LinkageRule MapToEntity(LinkageRuleDto dto) => new()
        {
            Name = dto.Name,
            DeviceId = dto.DeviceId,
            VariableKey = dto.VariableKey,
            Condition = dto.Condition,
            Threshold = dto.Threshold,
            ActionType = dto.ActionType,
            LinkageVariableKey = dto.LinkageVariableKey,
            LinkageValue = dto.LinkageValue,
            Active = dto.Active
        };
    }
}
