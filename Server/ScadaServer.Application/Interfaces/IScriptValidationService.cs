using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 脚本校验服务：在保存/试运行前对脚本的结构化元数据与代码进行静态校验。
    /// <para>
    /// 校验内容：
    /// 1) 触发类型合法，及其专属必填/取值范围（IntervalSeconds / CronExpression / WatchDevice/Cron 等）；
    /// 2) 代码非空、语法可解析（Jint AST 解析，不执行）；
    /// 3) 基本结构提示（缺 run/onChange 钩子等，仅告警）。
    /// </para>
    /// </summary>
    public interface IScriptValidationService
    {
        /// <summary>
        /// 校验脚本，返回问题列表。有 Error 级问题则 <see cref="ScriptValidationResult.Valid"/> 为 false。
        /// </summary>
        ScriptValidationResult Validate(SystemScriptDto dto);
    }
}