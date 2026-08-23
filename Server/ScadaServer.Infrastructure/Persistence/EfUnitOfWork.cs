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
