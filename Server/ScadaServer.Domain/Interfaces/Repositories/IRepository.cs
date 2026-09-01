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

        /// <summary>
        /// 新增单条实体到数据库
        /// </summary>
        /// <param name="domain">待新增的实体</param>
        Task InsertAsync(TDomain domain);

        /// <summary>
        /// 批量新增多条实体（单次提交，用于大批量初始化场景）
        /// </summary>
        /// <param name="domains">待新增的实体集合</param>
        Task InsertRangeAsync(IEnumerable<TDomain> domains);

        #endregion

        #region 更新

        /// <summary>
        /// 更新单条实体（按主键定位）
        /// </summary>
        /// <param name="domain">待更新的实体（需包含主键）</param>
        Task UpdateAsync(TDomain domain);

        /// <summary>
        /// 批量更新多条实体
        /// </summary>
        /// <param name="domains">待更新的实体集合</param>
        Task UpdateRangeAsync(IEnumerable<TDomain> domains);

        #endregion

        #region 删除

        /// <summary>
        /// 按主键删除实体
        /// </summary>
        /// <param name="id">主键值</param>
        Task DeleteAsync(TKey id);

        /// <summary>
        /// 删除指定实体（按主键定位）
        /// </summary>
        /// <param name="domain">待删除的实体</param>
        Task DeleteAsync(TDomain domain);

        /// <summary>
        /// 按条件批量删除实体
        /// </summary>
        /// <param name="predicate">删除条件表达式</param>
        Task DeleteRangeAsync(Expression<Func<TDomain, bool>> predicate);

        #endregion
    }
}