using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class SystemLogAppService : ISystemLogAppService
    {
        private readonly ISystemLogRepository _repository;
        public SystemLogAppService(ISystemLogRepository repository) { _repository = repository; }

        public async Task<SystemLogDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return ToDto(entity);
        }

        /// <inheritdoc/>
        public async Task<SystemLogPagedResultDto> QueryAsync(SystemLogQueryDto query)
        {
            var q = query ?? new SystemLogQueryDto();
            var pageIndex = q.PageIndex < 1 ? 1 : q.PageIndex;
            var pageSize = q.PageSize < 1 ? 20 : (q.PageSize > 100 ? 100 : q.PageSize);

            var (total, items) = await _repository.QueryAsync(
                string.IsNullOrWhiteSpace(q.Category) ? null : q.Category.Trim(),
                q.Levels,
                q.Keyword,
                q.Source,
                q.StartTime,
                q.EndTime,
                pageIndex,
                pageSize);

            return new SystemLogPagedResultDto
            {
                Total = total,
                Items = items.Select(ToDto).ToList()
            };
        }

        public async Task<List<SystemLogDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(ToDto).ToList();
        }

        public async Task CreateAsync(SystemLogDto dto)
        {
            var entity = new SystemLog
            {
                Timestamp = dto.Timestamp == default ? DateTime.Now : dto.Timestamp,
                Category = string.IsNullOrWhiteSpace(dto.Category) ? "Runtime" : dto.Category,
                Level = dto.Level,
                Source = dto.Source,
                Operation = dto.Operation,
                Operator = dto.Operator,
                IpAddress = dto.IpAddress,
                RelatedId = dto.RelatedId,
                Content = dto.Content
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(SystemLogDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Timestamp = dto.Timestamp;
                entity.Category = dto.Category;
                entity.Level = dto.Level;
                entity.Source = dto.Source;
                entity.Operation = dto.Operation;
                entity.Operator = dto.Operator;
                entity.IpAddress = dto.IpAddress;
                entity.RelatedId = dto.RelatedId;
                entity.Content = dto.Content;
                await _repository.UpdateAsync(entity);
            }
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }

        /// <inheritdoc/>
        public async Task<int> ClearAsync(string? category, DateTime? startTime, DateTime? endTime)
        {
            // 必须显式时间范围，防止误删全表（前端已二次确认，后端兜底）
            if (!startTime.HasValue && !endTime.HasValue)
            {
                throw new ScadaServer.Domain.Exceptions.BusinessException("清理日志必须指定时间范围，防止误删全部日志。", 400);
            }

            var result = await _repository.ClearAsync(category, startTime, endTime);
            return result;
        }

        private static SystemLogDto ToDto(SystemLog e) => new()
        {
            Id = e.Id,
            Timestamp = e.Timestamp,
            Category = e.Category,
            Level = e.Level,
            Source = e.Source,
            Operation = e.Operation,
            Operator = e.Operator,
            IpAddress = e.IpAddress,
            RelatedId = e.RelatedId,
            Content = e.Content
        };
    }
}
