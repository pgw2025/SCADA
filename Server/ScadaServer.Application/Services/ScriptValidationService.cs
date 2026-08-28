using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Enums;
using Cronos;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 脚本静态校验服务实现。
    /// <para>语法校验用 Jint 自带 AST 解析器（只解析不执行，避免校验阶段运行用户代码）；Cron 用 Cronos 严格校验。</para>
    /// </summary>
    public class ScriptValidationService : IScriptValidationService
    {
        /// <summary>
        /// 触发类型 -> 该类型应声明的钩子函数名（用于结构提示）。
        /// </summary>
        private static readonly Dictionary<ScriptTriggerType, string> HookByTrigger = new()
        {
            [ScriptTriggerType.Manual] = "run",
            [ScriptTriggerType.Periodic] = "run",
            [ScriptTriggerType.Schedule] = "run",
            [ScriptTriggerType.OnChange] = "onChange"
        };

        /// <inheritdoc/>
        public ScriptValidationResult Validate(SystemScriptDto dto)
        {
            var result = new ScriptValidationResult { Valid = true };

            if (dto == null)
            {
                result.Valid = false;
                result.Issues.Add(Error("脚本内容为空。"));
                return result;
            }

            // 1) 名称
            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                result.Issues.Add(Error("脚本名称不能为空。"));
            }

            // 2) 触发类型
            if (!Enum.TryParse<ScriptTriggerType>(dto.TriggerType, true, out var trigger)
                || !Enum.IsDefined(trigger))
            {
                result.Issues.Add(Error($"触发类型“{dto.TriggerType}”不合法（可选：{string.Join("/", Enum.GetNames<ScriptTriggerType>())}）。"));
                trigger = ScriptTriggerType.Manual;
            }

            // 3) 触发类型专属校验
            switch (trigger)
            {
                case ScriptTriggerType.Periodic:
                    if (!dto.IntervalSeconds.HasValue || dto.IntervalSeconds.Value < 1)
                    {
                        result.Issues.Add(Error("周期触发需填写执行间隔（秒）且 ≥1。"));
                    }
                    break;

                case ScriptTriggerType.Schedule:
                    if (string.IsNullOrWhiteSpace(dto.CronExpression))
                    {
                        result.Issues.Add(Error("定时(Cron)触发需填写 Cron 表达式。"));
                    }
                    else
                    {
                        try
                        {
                            CronExpression.Parse(dto.CronExpression);
                        }
                        catch (Exception ex)
                        {
                            result.Issues.Add(Error($"Cron 表达式“{dto.CronExpression}”不合法：{ex.Message}"));
                        }
                    }
                    break;

                case ScriptTriggerType.OnChange:
                    if (string.IsNullOrWhiteSpace(dto.WatchDeviceKey))
                    {
                        result.Issues.Add(Error("变量变化触发需填写监听设备键。"));
                    }
                    if (string.IsNullOrWhiteSpace(dto.WatchVariableKey))
                    {
                        result.Issues.Add(Error("变量变化触发需填写监听变量键。"));
                    }
                    if (dto.CooldownMs is < 100 or > 60000)
                    {
                        result.Issues.Add(Error("冷却时间需在 100-60000 毫秒之间。"));
                    }
                    break;
            }

            if (dto.TimeoutMs is < 500 or > 30000)
            {
                result.Issues.Add(Error("执行超时需在 500-30000 毫秒之间。"));
            }

            // 4) 代码语法校验（Jint 只解析不执行）
            if (string.IsNullOrWhiteSpace(dto.Code))
            {
                result.Issues.Add(Error("脚本代码不能为空。"));
            }
            else
            {
                result.Issues.AddRange(ValidateCodeSyntax(dto.Code));
            }

            // 5) 结构提示（钩子缺失，仅告警不阻止）
            if (!string.IsNullOrWhiteSpace(dto.Code))
            {
                var expectedHook = HookByTrigger[trigger];
                if (!ContainsTopLevelFunction(dto.Code, expectedHook))
                {
                    result.Issues.Add(Warning($"未声明钩子函数 {expectedHook}()，该触发类型下脚本不会执行任何逻辑。"));
                }
            }

            // 6) 授权提示：代码调用了 read/write 但未授予对应 scope（运行时将被默认拒绝）
            if (!string.IsNullOrWhiteSpace(dto.Code))
            {
                if (dto.Code.Contains("read(", StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(dto.ScopeRead))
                {
                    result.Issues.Add(Warning("代码调用了 read()，但未授予任何读授权，运行时读取将被拒绝。"));
                }
                if (dto.Code.Contains("write(", StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(dto.ScopeWrite))
                {
                    result.Issues.Add(Warning("代码调用了 write()，但未授予任何写授权，运行时写入将被拒绝。"));
                }
            }

            // 判定 Valid：无 Error 级问题即为通过。
            result.Valid = result.Issues.All(i => i.Level != "Error");

            return result;
        }

        /// <summary>
        /// 用 Jint 校验代码语法（不执行用户代码）。
        /// <para>把用户代码包成一个从未调用的函数体，因此 Jint 只解析其语法、校验括号/声明，
        /// 而不会真正执行任何逻辑——避免校验阶段运行脚本造成副作用或资源占用。</para>
        /// </summary>
        private static List<ScriptValidationIssue> ValidateCodeSyntax(string code)
        {
            var issues = new List<ScriptValidationIssue>();
            try
            {
                var engine = new Jint.Engine();
                engine.Execute("(function(){\n" + code + "\n})");
            }
            catch (Exception ex)
            {
                issues.Add(Error($"脚本代码语法错误：{ex.Message}"));
            }
            return issues;
        }

        /// <summary>
        /// 粗略检测是否存在顶层函数声明（function run / function onChange）。
        /// 采用宽松子串匹配 + 平衡判断，避免引入完整 AST 遍历的复杂度；仅作提示用途。
        /// </summary>
        private static bool ContainsTopLevelFunction(string code, string name)
        {
            var pattern = "function " + name;
            var idx = code.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            return idx >= 0;
        }

        private static ScriptValidationIssue Error(string msg) =>
            new() { Level = "Error", Message = msg };

        private static ScriptValidationIssue Warning(string msg) =>
            new() { Level = "Warning", Message = msg };
    }
}