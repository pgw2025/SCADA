using Microsoft.EntityFrameworkCore;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 组态工程授权仓储实现，对应表 ScadaProjectAuthorizations。
    /// 复合主键实体（ProjectId + UserId），不继承 RepositoryBase&lt;TEntity, TKey&gt;（无单列主键），
    /// 直接注入 ScadaDbContext 操作。替换（Replace）语义与既有仓储一致：
    /// 在调用方事务范围内立即 SaveChanges（EfUnitOfWork 只提交 DB 事务，不负责冲刷变更）。
    /// </summary>
    public class ScadaProjectAuthorizationRepository : IScadaProjectAuthorizationRepository
    {
        private readonly ScadaDbContext _db;

        public ScadaProjectAuthorizationRepository(ScadaDbContext db)
        {
            _db = db;
        }

        /// <inheritdoc/>
        public async Task<List<int>> GetProjectIdsByUserIdAsync(int userId)
            => await _db.ScadaProjectAuthorizations
                .Where(x => x.UserId == userId)
                .Select(x => x.ProjectId)
                .ToListAsync();

        /// <inheritdoc/>
        public async Task<bool> IsAuthorizedAsync(int projectId, int userId)
            => await _db.ScadaProjectAuthorizations
                .AnyAsync(x => x.ProjectId == projectId && x.UserId == userId);

        /// <inheritdoc/>
        public async Task<List<ScadaProjectAuthorization>> GetByProjectIdAsync(int projectId)
            => await _db.ScadaProjectAuthorizations
                .Where(x => x.ProjectId == projectId)
                .ToListAsync();

        /// <inheritdoc/>
        public async Task ReplaceForProjectAsync(int projectId, IEnumerable<int> userIds)
        {
            var existing = await _db.ScadaProjectAuthorizations
                .Where(x => x.ProjectId == projectId)
                .ToListAsync();
            _db.ScadaProjectAuthorizations.RemoveRange(existing);

            var now = DateTime.UtcNow;
            _db.ScadaProjectAuthorizations.AddRange(
                userIds.Select(uid => new ScadaProjectAuthorization
                {
                    ProjectId = projectId,
                    UserId = uid,
                    GrantedAt = now
                }));

            // 立即落库：调用方在事务内调用（EfUnitOfWork 不负责冲刷变更），保证原子提交。
            await _db.SaveChangesAsync();
        }
    }
}
