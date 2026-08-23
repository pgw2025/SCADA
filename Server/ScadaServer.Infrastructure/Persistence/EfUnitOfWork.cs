using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ScadaServer.Application.Interfaces;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Persistence
{
    /// <summary>
    /// EF Core 工作单元实现
    /// </summary>
    public class EfUnitOfWork : IUnitOfWork
    {
        private readonly ScadaDbContext _db;

        /// <summary>
        /// 初始化工作单元
        /// </summary>
        /// <param name="db">EF Core 数据库上下文</param>
        public EfUnitOfWork(ScadaDbContext db)
        {
            _db = db;
        }

        /// <inheritdoc/>
        public void BeginTran()
        {
            _db.Database.BeginTransaction();
        }

        /// <inheritdoc/>
        public async Task CommitTranAsync()
        {
            await _db.Database.CommitTransactionAsync();
        }

        /// <inheritdoc/>
        public async Task RollbackTranAsync()
        {
            await _db.Database.RollbackTransactionAsync();
        }

        /// <inheritdoc/>
        public async Task<ITransactionScope> BeginTransactionAsync()
        {
            var transaction = await _db.Database.BeginTransactionAsync();
            return new EfTransactionScope(transaction);
        }

        /// <inheritdoc/>
        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<ITransactionScope, Task<TResult>> action)
        {
            // 关键：DbContext 配置了 EnableRetryOnFailure（MySqlRetryingExecutionStrategy）。
            // 该策略不允许在策略外手动开启事务，因此必须把「开事务 + 业务 + 提交」整体包进策略内部。
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();
                var scope = new EfTransactionScope(transaction);
                try
                {
                    var result = await action(scope);
                    await scope.CommitAsync();
                    return result;
                }
                catch
                {
                    await scope.RollbackAsync();
                    throw;
                }
            });
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // 由 DI 容器管理生命周期，此处无需手动释放
        }

        /// <summary>
        /// 事务范围实现类
        /// </summary>
        private class EfTransactionScope : ITransactionScope
        {
            private readonly IDbContextTransaction _transaction;
            private bool _isCompleted = false;

            public EfTransactionScope(IDbContextTransaction transaction)
            {
                _transaction = transaction;
            }

            /// <inheritdoc/>
            public async Task CommitAsync()
            {
                await _transaction.CommitAsync();
                _isCompleted = true;
            }

            /// <inheritdoc/>
            public async Task RollbackAsync()
            {
                await _transaction.RollbackAsync();
                _isCompleted = true;
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (!_isCompleted)
                {
                    try
                    {
                        await _transaction.RollbackAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"事务自动回滚失败：{ex.Message}");
                    }
                    finally
                    {
                        _isCompleted = true;
                    }
                }

                await _transaction.DisposeAsync();
            }
        }
    }
}
