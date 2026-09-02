using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// EF 保存异常的共享分类辅助：统一判断"是否数据库唯一键冲突"，
    /// 供各应用服务在创建/写入时做并发竞态兜底（预检通过但落库撞唯一索引）。
    /// </summary>
    public static class DbExceptionClassifier
    {
        /// <summary>
        /// 判断 EF 保存异常是否为 MySQL 唯一键冲突（错误码 1062，如设备标识、设备变量唯一索引）。
        /// </summary>
        /// <param name="ex">EF 保存异常（通常为 <see cref="DbUpdateException"/>）</param>
        /// <returns>唯一键冲突返回 true；否则 false</returns>
        public static bool IsUniqueIndexConflict(DbUpdateException ex)
            => ex.GetBaseException() is MySqlException mySql
                && (mySql.ErrorCode == MySqlErrorCode.DuplicateKeyEntry || mySql.Number == 1062);
    }
}