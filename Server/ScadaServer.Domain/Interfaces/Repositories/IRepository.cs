using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace ScadaServer.Domain.Interfaces.Repositories
{
    /// <summary>
    /// 通用仓储接口，支持领域实体 TDomain
    /// </summary>
    /// <typeparam name="TDomain">领域实体</typeparam>
    /// <typeparam name="TKey">主键类型</typeparam>
    public interface IRepository<TDomain, TKey>
        where TDomain : class, new()
    {
        #region 查询

        /// <summary>
        /// 根据主键获取实体
        /// </summary>
        Task<TDomain?> GetByIdAsync(TKey id);

        /// <summary>
        /// 获取全部列表
        /// </summary>
        Task<List<TDomain>> GetListAsync();

        /// <summary>
        /// 条件查询列表
        /// </summary>
        Task<List<TDomain>> GetListAsync(Expression<Func<TDomain, bool>> predicate);

        /// <summary>
        /// 分页查询
        /// </summary>
        Task<List<TDomain>> GetPagedListAsync(
            int pageIndex,
            int pageSize,
            Expression<Func<TDomain, bool>>? predicate = null);

        /// <summary>
        /// 总数
        /// </summary>
        Task<int> CountAsync();

        /// <summary>
        /// 条件总数
        /// </summary>
        Task<int> CountAsync(Expression<Func<TDomain, bool>> predicate);

        /// <summary>
        /// 判断是否存在
        /// </summary>
        Task<bool> AnyAsync(Expression<Func<TDomain, bool>> predicate);

        #endregion

        #region 新增

        Task InsertAsync(TDomain domain);
        Task InsertRangeAsync(IEnumerable<TDomain> domains);

        #endregion

        #region 更新

        Task UpdateAsync(TDomain domain);
        Task UpdateRangeAsync(IEnumerable<TDomain> domains);

        #endregion

        #region 删除

        Task DeleteAsync(TKey id);
        Task DeleteAsync(TDomain domain);
        Task DeleteRangeAsync(Expression<Func<TDomain, bool>> predicate);

        #endregion
    }
}