using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 工作单元接口，用于管理数据库事务
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        /// <summary>
        /// 开始事务
        /// </summary>
        void BeginTran();

        /// <summary>
        /// 提交事务
        /// </summary>
        Task CommitTranAsync();

        /// <summary>
        /// 回滚事务
        /// </summary>
        Task RollbackTranAsync();

        /// <summary>
        /// 异步开始事务
        /// </summary>
        /// <returns>事务范围对象</returns>
        Task<ITransactionScope> BeginTransactionAsync();

        /// <summary>
        /// 在重试策略（MySqlRetryingExecutionStrategy）内执行事务。
        /// 当 DbContext 配置了 EnableRetryOnFailure 时，用户手动开启的事务必须置于
        /// Database.CreateExecutionStrategy 返回的 strategy 内部，否则 SaveChanges 会抛出
        /// "does not support user-initiated transactions" 异常。
        /// </summary>
        /// <typeparam name="TResult">事务内业务逻辑返回的结果类型</typeparam>
        /// <param name="action">事务内的业务逻辑（按需在内部调用仓储读写，返回结果）</param>
        /// <returns>业务逻辑返回的结果</returns>
        Task<TResult> ExecuteInTransactionAsync<TResult>(Func<ITransactionScope, Task<TResult>> action);
    }

    /// <summary>
    /// 事务范围接口
    /// </summary>
    public interface ITransactionScope : IAsyncDisposable
    {
        /// <summary>
        /// 提交事务
        /// </summary>
        Task CommitAsync();

        /// <summary>
        /// 回滚事务
        /// </summary>
        Task RollbackAsync();
    }
}

