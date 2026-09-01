using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// HMI 组件仓储实现。
    /// 对应实体 <see cref="HmiComponent"/>，数据表 HmiComponents（HMI 页面上的可视化组件：
    /// 按钮、图表、仪表等，含类型、坐标尺寸、图层归属及 JSON 属性配置）。
    /// 直接复用基类 <see cref="RepositoryBase{TEntity,TKey}"/> 的通用增删改查，无自定义查询逻辑。
    /// </summary>
    public class HmiComponentRepository : RepositoryBase<HmiComponent, int>, IHmiComponentRepository
    {
        /// <summary>
        /// 初始化 HMI 组件仓储。
        /// </summary>
        /// <param name="db">共享的 EF Core 数据库上下文（由依赖注入提供）。</param>
        public HmiComponentRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}