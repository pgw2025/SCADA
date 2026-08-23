using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 数据库版本实体（用于数据库迁移跟踪）
    /// </summary>
    [SugarTable("DbVersion")]
    public class DbVersion : EntityBase
    {
        /// <summary>
        /// 版本号
        /// </summary>
        public string Version { get; set; } = "";

        /// <summary>
        /// 应用时间
        /// </summary>
        public DateTime AppliedAt { get; set; } = DateTime.Now;
    }
}