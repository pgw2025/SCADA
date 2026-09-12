using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 历史数据维护服务实现（scoped）。
    /// <para>提供 MySQL 历史数据清理等运维能力，供历史清理计划任务与保留期自动清理复用。</para>
    /// </summary>
    public class HistoryMaintenanceAppService : IHistoryMaintenanceAppService
    {
        private const int CleanupBatchSize = 2000;
        private const int CleanupBatchDelayMs = 200;

        private readonly IVariableHistoryRepository _repository;

        public HistoryMaintenanceAppService(IVariableHistoryRepository repository)
        {
            _repository = repository;
        }

        /// <inheritdoc/>
        public Task<long> CleanupMySqlBeforeAsync(DateTime cutoffUtc, CancellationToken token)
        {
            return _repository.DeleteBeforeAsync(cutoffUtc, CleanupBatchSize, CleanupBatchDelayMs, token);
        }
    }
}
