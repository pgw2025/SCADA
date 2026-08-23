using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class DatabaseConfigRepository : RepositoryBase<DatabaseConfig, int>, IDatabaseConfigRepository
    {
        public DatabaseConfigRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}