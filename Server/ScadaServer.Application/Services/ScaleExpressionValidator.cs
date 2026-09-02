using System.Text.RegularExpressions;
using Jint;

namespace ScadaServer.Application.Services;

/// <summary>
/// 工程换算表达式校验器（保存前体检）。
/// <para>
/// 与 Runtime 层的求值器（<c>ScadaServer.Runtime.DataConversion.ScaleExpression</c>）规则对齐，
/// 但独立实现：Application 层不引用 Runtime（依赖方向为 Runtime → Application 定义的接口），
/// 且校验阶段严禁执行用户代码。
/// </para>
/// <para>
/// 三重校验：
/// 1) 函数名白名单（仅允许 Math 常用纯函数，杜绝构造/全局对象访问）；
/// 2) 字符白名单（剥离函数调用后，仅剩数字、变量 x 与运算符）；
/// 3) Jint 语法解析——把表达式包成函数体，只定义不调用，因此只解析不执行。
/// </para>
/// </summary>
public static class ScaleExpressionValidator
{
    /// <summary>表达式最大长度，与 <c>ModelVariable.ScaleExpression</c> 的 [MaxLength] 保持一致。</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// 允许的 Math 函数白名单。均为无副作用的一元/二元纯函数，
    /// 不含随机数、日期、字符串解析等会引入不确定性的成员。
    /// </summary>
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.Ordinal)
    {
        "abs", "min", "max", "pow", "sqrt", "exp", "log", "log10",
        "round", "floor", "ceil", "sign", "sin", "cos", "tan", "asin", "acos", "atan"
    };

    /// <summary>标识符提取：形如 <c>Math.foo(</c> 或 <c>foo(</c> 的调用，捕获函数名。</summary>
    private static readonly Regex IdentifierPattern =
        new(@"(?:Math\s*\.\s*)?([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

    /// <summary>剥离白名单函数调用后，剩余字符必须全部落在本集合内。</summary>
    private static readonly Regex AllowedChars =
        new(@"^[0-9x\s+\-*/%().,eE]*$", RegexOptions.Compiled);

    /// <summary>
    /// 校验工程换算表达式。
    /// </summary>
    /// <param name="expression">待校验表达式（可空，空表示恒等变换）</param>
    /// <returns>合法返回 <c>null</c>；非法返回中文错误原因，可直接抛给前端展示。</returns>
    public static string? Validate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;      // 空 = 恒等，合法

        var expr = expression.Trim();
        if (expr.Length > MaxLength)
            return $"长度不能超过 {MaxLength} 个字符（当前 {expr.Length}）";

        // 1) 函数名白名单：把白名单调用替换为 "("，非法调用替换为含哨兵的串以便识别。
        var stripped = IdentifierPattern.Replace(expr, m =>
            AllowedFunctions.Contains(m.Groups[1].Value) ? "(" : "\u0000(");
        if (stripped.Contains('\u0000'))
            return $"只允许使用白名单函数：{string.Join("、", AllowedFunctions)}";

        // 2) 字符白名单：此时剩余串中不应再出现任何字母（除变量 x 与科学计数法的 e/E）。
        if (!AllowedChars.IsMatch(stripped))
            return "包含非法字符，仅允许数字、变量 x、运算符 + - * / % ( ) 与白名单函数";

        // 3) 语法解析（Jint 只解析不执行：包成函数体且从不调用）。
        try
        {
            var engine = new Engine();
            engine.Execute($"function __check(x) {{ return ({expr}); }}");
        }
        catch (Exception ex)
        {
            return $"语法错误：{ex.Message}";
        }

        return null;
    }
}
