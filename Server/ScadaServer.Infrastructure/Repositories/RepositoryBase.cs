using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 通用仓储基类，支持 Entity -> Domain 映射
    /// </summary>
    /// <typeparam name="TEntity">数据库实体</typeparam>
    /// <typeparam name="TKey">主键类型</typeparam>
    public abstract class RepositoryBase<TEntity, TKey> : IRepository<TEntity, TKey>
        where TEntity : class, new()
    {
        protected readonly ScadaDbContext Db;

        protected RepositoryBase(ScadaDbContext db)
        {
            Db = db;
        }


        #region 查询

        public virtual async Task<TEntity?> GetByIdAsync(TKey id)
        {
            return await Db.Set<TEntity>().FindAsync(id);
        }

        public virtual async Task<List<TEntity>> GetListAsync()
        {
            return await Db.Set<TEntity>().ToListAsync();
        }

        public virtual async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await Db.Set<TEntity>().Where(predicate).ToListAsync();
        }

        public virtual async Task<List<TEntity>> GetPagedListAsync(
            int pageIndex,
            int pageSize,
            Expression<Func<TEntity, bool>>? predicate = null)
        {
            var query = Db.Set<TEntity>().AsQueryable();
            if (predicate != null) query = query.Where(predicate);

            return await query
                .OrderBy(e => EF.Property<int>(e, "Id"))
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public virtual async Task<int> CountAsync()
        {
            return await Db.Set<TEntity>().CountAsync();
        }

        public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await Db.Set<TEntity>().Where(predicate).CountAsync();
        }

        public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await Db.Set<TEntity>().Where(predicate).AnyAsync();
        }

        #endregion

        #region 新增

        public virtual async Task InsertAsync(TEntity entity)
        {
            await Db.Set<TEntity>().AddAsync(entity);
            await Db.SaveChangesAsync();
        }

        public virtual async Task InsertRangeAsync(IEnumerable<TEntity> entities)
        {
            await Db.Set<TEntity>().AddRangeAsync(entities);
            await Db.SaveChangesAsync();
        }

        #endregion

        #region 更新

        public virtual async Task UpdateAsync(TEntity entity)
        {
            Db.Set<TEntity>().Update(entity);
            await Db.SaveChangesAsync();
        }

        public virtual async Task UpdateRangeAsync(IEnumerable<TEntity> entities)
        {
            Db.Set<TEntity>().UpdateRange(entities);
            await Db.SaveChangesAsync();
        }

        #endregion

        #region 删除

        public virtual async Task DeleteAsync(TKey id)
        {
            var entity = await GetByIdAsync(id);
            if (entity != null)
            {
                Db.Set<TEntity>().Remove(entity);
                await Db.SaveChangesAsync();
            }
        }

        public virtual async Task DeleteAsync(TEntity entity)
        {
            Db.Set<TEntity>().Remove(entity);
            await Db.SaveChangesAsync();
        }

        public virtual async Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate)
        {
            var entities = await Db.Set<TEntity>().Where(predicate).ToListAsync();
            Db.Set<TEntity>().RemoveRange(entities);
            await Db.SaveChangesAsync();
        }

        #endregion
    }
}
