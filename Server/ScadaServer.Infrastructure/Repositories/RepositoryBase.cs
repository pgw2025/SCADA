using SqlSugar;
using System.Linq.Expressions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 通用仓储基类，支持 Entity -> Domain 映射
    /// </summary>
    /// <typeparam name="TEntity">数据库实体</typeparam>
    /// <typeparam name="TDomain">领域实体/模型</typeparam>
    /// <typeparam name="TKey">主键类型</typeparam>
    public abstract class RepositoryBase<TEntity, TKey> : IRepository<TEntity, TKey>
        where TEntity : class, new()
    {
        protected readonly ISqlSugarClient Db;

        protected RepositoryBase(ISqlSugarClient db)
        {
            Db = db;
        }


        #region 查询

        public virtual async Task<TEntity?> GetByIdAsync(TKey id)
        {
            return await Db.Queryable<TEntity>().InSingleAsync(id);
        }

        public virtual async Task<List<TEntity>> GetListAsync()
        {
            return await Db.Queryable<TEntity>().ToListAsync();
        }

        public virtual async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await Db.Queryable<TEntity>().Where(predicate).ToListAsync();
        }

        public virtual async Task<List<TEntity>> GetPagedListAsync(
            int pageIndex,
            int pageSize,
            Expression<Func<TEntity, bool>>? predicate = null)
        {
            var query = Db.Queryable<TEntity>();
            if (predicate != null) query = query.Where(predicate);

            return await query.ToPageListAsync(pageIndex, pageSize);
        }

        public virtual async Task<int> CountAsync()
        {
            return await Db.Queryable<TEntity>().CountAsync();
        }

        public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await Db.Queryable<TEntity>().Where(predicate).CountAsync();
        }

        public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await Db.Queryable<TEntity>().Where(predicate).AnyAsync();
        }

        #endregion

        #region 新增

        public virtual async Task InsertAsync(TEntity entity)
        {
            await Db.Insertable(entity).ExecuteCommandAsync();
        }

        public virtual async Task InsertRangeAsync(IEnumerable<TEntity> entities)
        {
            await Db.Insertable(entities.ToList()).ExecuteCommandAsync();
        }

        #endregion

        #region 更新

        public virtual async Task UpdateAsync(TEntity entity)
        {
            await Db.Updateable(entity).ExecuteCommandAsync();
        }

        public virtual async Task UpdateRangeAsync(IEnumerable<TEntity> entities)
        {
            await Db.Updateable(entities.ToList()).ExecuteCommandAsync();
        }

        #endregion

        #region 删除

        public virtual async Task DeleteAsync(TKey id)
        {
            await Db.Deleteable<TEntity>(id).ExecuteCommandAsync();
        }

        public virtual async Task DeleteAsync(TEntity entity)
        {
            await Db.Deleteable(entity).ExecuteCommandAsync();
        }

        public virtual async Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate)
        {
            await Db.Deleteable<TEntity>().Where(predicate).ExecuteCommandAsync();
        }

        #endregion
    }
}