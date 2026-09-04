using Microsoft.EntityFrameworkCore;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// HMI 组件模板仓储实现。
    /// 对应实体 <see cref="HmiWidgetTemplate"/>，数据表 HmiWidgetTemplates（组件库模板元数据：
    /// 模板键、渲染类型、图标、SVG 源码、默认属性 JSON 等）。
    /// 复用基类 <see cref="RepositoryBase{TEntity,TKey}"/> 的通用增删改查，
    /// 额外提供按唯一键 <see cref="TemplateKey"/> 的查询（导入冲突检测 / 种子幂等）。
    /// </summary>
    public class HmiWidgetTemplateRepository : RepositoryBase<HmiWidgetTemplate, int>, IHmiWidgetTemplateRepository
    {
        /// <summary>
        /// 初始化 HMI 组件模板仓储。
        /// </summary>
        /// <param name="db">共享的 EF Core 数据库上下文（由依赖注入提供）。</param>
        public HmiWidgetTemplateRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>按唯一键查询模板；未找到返回 null。</summary>
        public async Task<HmiWidgetTemplate?> GetByKeyAsync(string templateKey)
            => await Db.Set<HmiWidgetTemplate>()
                .FirstOrDefaultAsync(t => t.TemplateKey == templateKey);
    }
}
