using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// MQTT 服务器仓储实现。
    /// 对应实体 <see cref="MqttServer"/>，数据表 MqttServers（MQTT Broker 连接配置：
    /// 地址、端口、认证凭据、主题前缀及是否启用等）。
    /// 直接复用基类 <see cref="RepositoryBase{TEntity,TKey}"/> 的通用增删改查，无自定义查询逻辑。
    /// </summary>
    public class MqttServerRepository : RepositoryBase<MqttServer, int>, IMqttServerRepository
    {
        /// <summary>
        /// 初始化 MQTT 服务器仓储。
        /// </summary>
        /// <param name="db">共享的 EF Core 数据库上下文（由依赖注入提供）。</param>
        public MqttServerRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}