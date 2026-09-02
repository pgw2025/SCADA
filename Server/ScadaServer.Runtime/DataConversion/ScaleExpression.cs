using System.Collections.Concurrent;
using Jint;

namespace ScadaServer.Runtime.DataConversion;

/// <summary>
/// 工程换算表达式求值器（基于 Jint，与"系统脚本"共用同一 JS 引擎技术栈）。
/// <para>
/// 设计要点：
/// 1. 一次编译、永久缓存：表达式 → JS 函数 → .NET 委托 <see cref="Func{Double, Double}"/>，
///    采集热路径上只有一次委托调用，无词法/语法分析开销；
/// 2. 线程安全：Jint Engine 实例非线程安全，每个缓存项自带互斥门，求值时短临界区串行；
/// 3. 沙箱：Strict + 限制递归/语句数/超时，表达式包在函数体内且从不执行语句级代码；
/// 4. 失败降级：编译或求值失败均返回 false，由调用方决定保持原始值或降级处理，
///    绝不抛穿到采集循环（配置错误最多导致该变量按原始值上报，不影响其它变量）。
/// </para>
/// <para>
/// 语法白名单校验在保存前由 Application 层 <c>ScaleExpressionValidator</c> 完成；
/// 本类不重复做字符白名单，但对"可解析但求值结果非有限数"的表达式（如 x/0）在求值时拦截。
/// </para>
/// </summary>
public static class ScaleExpression
{
    /// <summary>表达式输入变量名。</summary>
    public const string InputVariable = "x";

    /// <summary>表达式最大长度（与实体 [MaxLength]、应用层校验器保持一致）。</summary>
    public const int MaxLength = 200;

    /// <summary>单次求值超时上限（毫秒）。</summary>
    private const int EvaluateTimeoutMs = 50;

    /// <summary>缓存项上限，超出后整体清空，防止异常配置无限堆积。</summary>
    private const int CacheLimit = 4096;

    private sealed class Compiled
    {
        /// <summary>编译该函数的引擎实例（非线程安全，所有调用须经 <see cref="Gate"/> 串行）。</summary>
        public Engine Engine = null!;

        /// <summary>编译产物：全局函数名（经 Engine.Invoke 调用）。</summary>
        public string FunctionName = string.Empty;

        /// <summary>该委托的互斥门。</summary>
        public object Gate { get; } = new();
    }

    // key = 表达式原文。表达式天然不可变，无需失效策略；配置改了就是新 key，旧项自然淘汰（超限清空）。
    private static readonly ConcurrentDictionary<string, Compiled> Cache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 求值：工程值 = f(<paramref name="raw"/>)。表达式为空视为恒等，直接返回 <paramref name="raw"/>。
    /// </summary>
    /// <returns>求值成功返回 true；表达式非法、超时、结果非有限数（NaN/Infinity）返回 false。</returns>
    public static bool TryEvaluate(string? expression, double raw, out double result)
    {
        result = raw;
        if (string.IsNullOrWhiteSpace(expression)) return true;   // 恒等

        var compiled = GetOrCompile(expression);
        if (compiled == null) return false;

        try
        {
            double value;
            lock (compiled.Gate)
            {
                value = compiled.Engine.Invoke(compiled.FunctionName, raw).AsNumber();
            }

            if (double.IsNaN(value) || double.IsInfinity(value)) return false;
            result = value;
            return true;
        }
        catch (Exception)
        {
            // 除零/对数越界产生的 ±Infinity 已在上一步拦截；此处兜住 Jint 内部异常与求值超时。
            return false;
        }
    }

    /// <summary>
    /// 仅编译不做实际求值，用于需要确认"表达式是否可编译"的场景（如诊断/测试）。
    /// </summary>
    public static bool TryCompile(string? expression, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(expression)) return true;
        var compiled = GetOrCompile(expression);
        if (compiled != null) return true;
        error = "表达式无法解析或求值试探失败";
        return false;
    }

    private static Compiled? GetOrCompile(string expression)
    {
        if (Cache.TryGetValue(expression, out var hit)) return hit;

        try
        {
            var engine = new Engine(o => o
                .Strict()
                .LimitRecursion(2)
                .MaxStatements(64)
                .TimeoutInterval(TimeSpan.FromMilliseconds(EvaluateTimeoutMs)));

            // 包成函数体：只定义不调用，编译期即可捕获语法错误，且天然隔离语句级副作用。
            const string fnName = "__scale";
            engine.Execute($"function {fnName}({InputVariable}) {{ return ({expression}); }}");

            // 试探求值：确认引用合法、结果有限（如误用大写 X 会在此暴露 ReferenceError）。
            var probe = engine.Invoke(fnName, 1d).AsNumber();
            if (double.IsNaN(probe) || double.IsInfinity(probe)) return null;

            if (Cache.Count > CacheLimit) Cache.Clear();

            var compiled = new Compiled { Engine = engine, FunctionName = fnName };
            return Cache.GetOrAdd(expression, compiled);
        }
        catch (Exception)
        {
            return null;   // 非法表达式不缓存，避免错误配置在缓存里长期占位
        }
    }
}
