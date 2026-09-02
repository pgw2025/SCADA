using System.Globalization;

namespace ScadaServer.Runtime.DataConversion;

/// <summary>
/// 变量值工程换算门面：采集方向 raw → engineering，写入方向 engineering → raw。
/// <para>
/// 换算规则：
/// - 表达式为空 = 恒等变换，原样返回；
/// - 数字量（bool）与字符串等非数值原样透传（量程/死区/报警等语义只对模拟量有意义）；
/// - 表达式求值失败返回原始值，保证采集链路永不被一条坏配置打断。
/// </para>
/// </summary>
public static class VariableScaling
{
    /// <summary>采集方向：驱动原始值 → 工程值。在 DeviceWorker 读取成功后调用。</summary>
    public static object? ToEngineering(VariableRuntime vr, object? raw)
    {
        if (raw is null) return null;
        var expr = vr.ScaleExpression;
        if (string.IsNullOrWhiteSpace(expr)) return raw;
        if (raw is bool) return raw;                          // 数字量不换算
        if (!TryToNumber(raw, out var x)) return raw;         // 字符串等非数值原样
        return ScaleExpression.TryEvaluate(expr, x, out var y) ? y : raw;
    }

    /// <summary>
    /// 写入方向：工程值 → 驱动原始值。在 RuntimeManager.WriteVariableAsync 下发驱动前调用。
    /// <para>
    /// 当前版本：仅做恒等透传（本期未引入反算表达式字段，任意公式无自动反函数）。
    /// 后续若需要支持"配置了换算公式的可写变量"，在此追加反向表达式字段与求值即可，
    /// 调用点已就位，无需再改 RuntimeManager。
    /// </para>
    /// </summary>
    public static object? ToRaw(VariableRuntime vr, object? engineering)
    {
        // 预留扩展点：反算表达式为空时恒等透传（与改造前写入行为一致）。
        return engineering;
    }

    /// <summary>尽力把运行时值转成 double（bool 计为 0/1，与 DeviceWorker.TryToNumber 语义一致）。</summary>
    internal static bool TryToNumber(object? value, out double result)
    {
        result = 0;
        switch (value)
        {
            case bool b: result = b ? 1 : 0; return true;
            case double d: result = d; return true;
            case float f: result = f; return true;
            case int or short or ushort or byte or sbyte or uint or long or ulong:
                result = Convert.ToDouble(value); return true;
            case string s:
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            case IConvertible c:
                try { result = c.ToDouble(CultureInfo.InvariantCulture); return true; }
                catch { return false; }
            default: return false;
        }
    }
}
