using ScadaServer.Application.Interfaces;
using SqlSugar;

namespace ScadaServer.Infrastructure.Persistence
{
    /// <summary>
    /// SqlSugar 工作单元实现
    /// </summary>
    public class SqlSugarUnitOfWork : IUnitOfWork
    {
        private readonly ISqlSugarClient _db;

        /// <summary>
        /// 初始化工作单元
        /// </summary>
        /// <param name="db">SqlSugar数据库客户端</param>
        public SqlSugarUnitOfWork(ISqlSugarClient db)
        {
            _db = db;
        }

        /// <inheritdoc/>
        public void BeginTran()
        {
            _db.AsTenant().BeginTran();
        }

        /// <inheritdoc/>
        public async Task CommitTranAsync()
        {
            await _db.AsTenant().CommitTranAsync();
        }

        /// <inheritdoc/>
        public async Task RollbackTranAsync()
        {
            await _db.AsTenant().RollbackTranAsync();
        }

        /// <inheritdoc/>
        public async Task<ITransactionScope> BeginTransactionAsync()
        {
            await _db.AsTenant().BeginTranAsync();
            return new TransactionScope(_db);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // 由 DI 容器管理生命周期，此处无需手动释放
        }

        /// <summary>
        /// 事务范围实现类
        /// </summary>
        private class TransactionScope : ITransactionScope
        {
            private readonly ISqlSugarClient _db;
            private bool _isCompleted = false;

            public TransactionScope(ISqlSugarClient db)
            {
                _db = db;
            }

            /// <inheritdoc/>
            public async Task CommitAsync()
            {
                await _db.AsTenant().CommitTranAsync();
                _isCompleted = true;
            }

            /// <inheritdoc/>
            public async Task RollbackAsync()
            {
                await _db.AsTenant().RollbackTranAsync();
                _isCompleted = true;
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (!_isCompleted)
                {
                    try
                    {
                        await _db.AsTenant().RollbackTranAsync();
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
            }
        }
    }
}