using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime.Scripting;

namespace ScadaServer.Runtime.Tasks
{
    /// <summary>
    /// 脚本执行器：调用系统脚本引擎（Jint 沙箱）执行指定脚本，复用其执行记录与熔断机制。
    /// <para>参数（ParamsJson）：scriptId（必填）。</para>
    /// </summary>
    public class ExecuteScriptTaskExecutor : IScheduledTaskExecutor
    {
        private readonly IScriptEngineHost _scriptEngine;
        private readonly ILogger<ExecuteScriptTaskExecutor> _logger;

        public ExecuteScriptTaskExecutor(IScriptEngineHost scriptEngine, ILogger<ExecuteScriptTaskExecutor> logger)
        {
            _scriptEngine = scriptEngine;
            _logger = logger;
        }

        public string Type => ScheduledTaskTypes.ExecuteScript;

        public async Task<string> ExecuteAsync(ScheduledTask task, CancellationToken token)
        {
            var scriptId = ParseScriptId(task.ParamsJson);

            var result = await _scriptEngine.RunAsync(scriptId, $"计划任务:{task.Name}");
            if (result.Result is "Success" or "Skipped")
            {
                var output = string.IsNullOrWhiteSpace(result.Output) ? "（无输出）" : result.Output;
                return $"脚本 {scriptId} 执行{result.Result}，耗时 {result.DurationMs ?? 0}ms：{Truncate(output, 500)}";
            }

            throw new InvalidOperationException($"脚本 {scriptId} 执行{result.Result}：{result.Error ?? "未知错误"}");
        }

        private int ParseScriptId(string? paramsJson)
        {
            JsonElement root;
            try
            {
                root = JsonDocument.Parse(string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson).RootElement;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "计划任务参数不是合法 JSON: {ParamsJson}", paramsJson);
                throw new InvalidOperationException($"任务参数不是合法 JSON: {ex.Message}");
            }

            if (!root.TryGetProperty("scriptId", out var el) || el.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException("缺少目标脚本参数（scriptId）");
            }
            return el.GetInt32();
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max] + "...";
    }
}
