using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 通信协议仓储实现，对应表 Protocols，用于管理设备通信协议定义（如 Modbus、Mqtt 等）。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="IProtocolRepository"/> 暴露给上层。
    /// </summary>
    public class ProtocolRepository : RepositoryBase<Protocol, int>, IProtocolRepository
    {
        public ProtocolRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}