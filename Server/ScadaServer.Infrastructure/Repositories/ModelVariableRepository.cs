using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ModelVariableRepository : RepositoryBase<ModelVariable, int>, IModelVariableRepository
    {
        public ModelVariableRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 统一约定：模型变量列表按 ModelId、Key 排序，保证返回顺序稳定（消除前端列表抖动）。
        /// </summary>
        public override Task<List<ModelVariable>> GetListAsync()
            => Db.ModelVariables.AsNoTracking().OrderBy(mv => mv.ModelId).ThenBy(mv => mv.Key).ToListAsync();

        /// <summary>
        /// 带条件查询同样排序（如按 ModelId 取变量时按 Key 稳定排序）。
        /// </summary>
        public override Task<List<ModelVariable>> GetListAsync(Expression<Func<ModelVariable, bool>> predicate)
            => Db.ModelVariables.AsNoTracking().Where(predicate).OrderBy(mv => mv.Key).ToListAsync();
    }
}