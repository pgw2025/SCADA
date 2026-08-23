using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 实体基类，所有实体的抽象基类
    /// </summary>
    public abstract class EntityBase
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }
    }
}