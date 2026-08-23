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

