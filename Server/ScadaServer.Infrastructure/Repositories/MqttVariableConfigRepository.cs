using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// MQTT 变量配置仓储实现。
    /// 对应实体 <see cref="MqttVariableConfig"/>，数据表 MqttVariableConfigs（某 MQTT 服务器与设备之间的变量发布订阅配置：
    /// 变量键、别名、自定义主题及是否启用等）。
    /// 直接复用基类 <see cref="RepositoryBase{TEntity,TKey}"/> 的通用增删改查，无自定义查询逻辑。
    /// </summary>
    public class MqttVariableConfigRepository : RepositoryBase<MqttVariableConfig, int>, IRepository<MqttVariableConfig, int>
    {
        /// <summary>
        /// 初始化 MQTT 变量配置仓储。
        /// </summary>
        /// <param name="db">共享的 EF Core 数据库上下文（由依赖注入提供）。</param>
        public MqttVariableConfigRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}
