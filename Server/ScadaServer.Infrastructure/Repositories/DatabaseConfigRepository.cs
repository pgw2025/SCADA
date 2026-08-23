using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class DatabaseConfigRepository : RepositoryBase<DatabaseConfig, int>, IDatabaseConfigRepository
    {
        public DatabaseConfigRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}