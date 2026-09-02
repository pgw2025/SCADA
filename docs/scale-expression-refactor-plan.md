# 改造方案：ModelVariable 缩放字段 → 公式表达式（ScaleExpression）

> 状态：**待评审，未实施**。本文只做方案与代码设计，不改动任何现有代码。
> 目标：删除 `ModelVariable.ScaleSlope` / `ScaleOffset`，替换为一个支持公式运算的字符串属性 `ScaleExpression`。

---

## 0. 现状盘点（代码实证）

### 0.1 定义层

| 位置 | 现状 |
|---|---|
| `Domain/Entities/ModelVariable.cs:106,111` | `double ScaleSlope = 1.0` / `double ScaleOffset = 0.0` |
| `Domain/Entities/DeviceVariable.cs:105,111` | `double? ScaleSlopeOverride` / `ScaleOffsetOverride`（实例级覆盖） |
| `Domain/Interfaces/IRuntimeVariable.cs:51,54` | 接口暴露 `ScaleSlope` / `ScaleOffset` |
| `Runtime/Variables/VariableRuntime.cs:59,62` | `Instance?.ScaleSlopeOverride ?? Definition.ScaleSlope` |

### 0.2 消费层（**关键发现**）

**当前没有任何代码真正用 Scale 做值换算。** 全仓库搜索 `ScaleSlope|ScaleOffset` 只命中"定义 + DTO 映射 + 导入导出 + 迁移快照"，采集链路 `DeviceWorker.ReadAsync()` 之后、写入链路 `RuntimeManager.WriteVariableAsync()` 之前都没有换算调用。

这意味着：
1. 本次改造**不会破坏现有运行时行为**（因为缩放本来就没生效）；
2. 但也意味着**改造后必须补上消费点**，否则公式配了也白配 —— 本方案第 3.6/3.7/3.8 节专门解决。

### 0.3 完整影响面清单（15 个后端文件 + 5 个前端文件）

后端（不含 `Migrations/*.Designer.cs` 自动生成快照）：

```
Domain/Entities/ModelVariable.cs                 ★ 字段定义
Domain/Entities/DeviceVariable.cs                ★ 覆盖字段
Domain/Interfaces/IRuntimeVariable.cs            ★ 接口
Domain/Readme.md                                   文档（275 行提及）
Runtime/Variables/VariableRuntime.cs             ★ 运行时解析
Runtime/Devices/DeviceWorker.cs                  ★ 新增：采集正向换算
Runtime/RuntimeManager.cs                        ★ 新增：写入反向换算
Runtime/DataConversion/                          ★ 新增：求值器（目录已存在且为空）
Application/DTOs/ModelVariableDto.cs             ★ DTO
Application/DTOs/ModelVariableMapper.cs          ★ 映射
Application/DTOs/DeviceVariableDto.cs            ★ DTO
Application/DTOs/VariableTransferDto.cs          ★ 导入行
Application/Services/ModelVariableAppService.cs  ★ 导入/映射/校验（410,411,445,446,495,496）
Application/Services/DeviceVariableAppService.cs ★ 141,142,170,171
Application/Services/DeviceAppService.cs         ★ 142,143
Application/ImportExport/VariableExportService.cs★ 表头 20 / 写值 57,58,96,97
Application/ImportExport/CsvParser.cs            ★ 表头 19 / 解析 117,118
Infrastructure/Persistence/ScadaDbContext.cs     ★ 新增 MaxLength 配置
Infrastructure/Migrations/                       ★ 新增一条迁移
```

前端：

```
src/types.ts                      341,342 / 367,368 / 1201,1202
src/api/modelApi.ts               68,69
src/components/DataModelView.vue  159,160 / 244,245 / 265,266 / 328,329 / 1115-1136
src/components/DeviceVariableView.vue  690,695
swagger.json                      自动生成，需重新导出
```

---

## 1. 需要你拍板的 4 个决策点

### D1｜公式引擎选型

| 方案 | 说明 | 优点 | 缺点 |
|---|---|---|---|
| **A. 复用 Jint（推荐）** | 项目已在 Runtime + Application 引用 `Jint 3.1.3`，表达式即 JS 表达式子集 | 零新增依赖、语法与"系统脚本"一致、`Math.pow/log/sqrt` 开箱可用、支持科学计数法字面量 | 需处理线程安全与超时；每次求值走 JS 解释器 |
| B. 自研解析器 → `Expression.Lambda` 编译 | 手写词法/语法分析，编译成 `Func<double,double>` | 无副作用、绝对安全、性能最优（编译后接近原生）、可严格白名单 | 约 200~300 行新增代码，函数需自己实现 |
| C. 引入 NCalc / DynamicExpresso | 新增 NuGet | 成熟 | 新依赖，与项目现有 Jint 技术栈重复 |

**推荐 A**：与项目现有技术栈一致（你一贯偏好一致性与最小改动）；性能通过"一次编译、缓存委托"解决（见 3.5）。
**若你对"用户可写 JS"的安全面有顾虑，选 B** —— 代价是多写解析器，但彻底杜绝副作用。

### D2｜写入反算（eng → raw）

线性模型天然可逆（`raw = (y - b) / a`），任意公式不可逆（`x*x`、`Math.log(x)` 无自动反函数）。

| 方案 | 行为 |
|---|---|
| **A. 不做反算（最小改动）** | 只有正向 `ScaleExpression`。配置了公式的变量，写入时把工程值**直接下发**给驱动，并在 UI/日志提示。等价于今天的行为 |
| B. 追加 `InverseScaleExpression`（可空） | 正向 + 反向两个字段，反向为空时降级为 A |
| C. 配置公式即禁止写入 | 语义最严格，但会砍掉一批可写模拟量的能力 |

**推荐 A 落地、B 预留**：本次只加 `ScaleExpression` 一个字段；`VariableScaling.ToRaw()` 预留反算入口（见 3.6），将来要支持 B 时只需加一个字段 + 改一行。

### D3｜DeviceVariable 的 Override 怎么办

| 方案 | 说明 |
|---|---|
| **A. 同步改为 `ScaleExpressionOverride`（推荐）** | 保持"模板定义 + 实例覆盖"的既有语义，`null = 继承模板` |
| B. 删除设备级缩放覆盖 | 缩放完全收归模板层，字段更少，但丧失"同型号不同设备标定差异"的能力 |
| C. 不动 | **不推荐**：模板用公式、实例用线性覆盖，语义分裂，运行时无法统一求值 |

**推荐 A**。改动量与 B 相当，且不丢能力。

### D4｜旧列处理

| 方案 | 说明 |
|---|---|
| **A. 本迁移中直接删除（推荐）** | 符合你的要求；`Down()` 尽力用正则解析 `a*x+b` 回填 |
| B. 加新列 + 回填 + 保留旧列一版，下次迁移再删 | 回滚零风险，但要多一次迁移 |

项目仍在 dev 高频迭代期（近 20 条迁移），**推荐 A**。

---

## 2. 目标模型

### 2.1 字段定义

```csharp
// Domain/Entities/ModelVariable.cs
/// <summary>
/// 工程换算表达式（原始值 → 工程值）。以 x 代表驱动读到的原始值。
/// <para>为空 / 空白 = 恒等变换（工程值 = 原始值），等价于旧的 Slope=1 &amp; Offset=0。</para>
/// <para>示例："x*0.1"、"(x-4000)/160"、"Math.round(x*10)/10"、"x*1.8+32"。</para>
/// </summary>
[MaxLength(200)]
public string? ScaleExpression { get; set; }
```

```csharp
// Domain/Entities/DeviceVariable.cs（D3-A）
/// <summary>
/// 工程换算表达式覆盖值。允许为空：为空时使用 <see cref="ModelVariable.ScaleExpression"/> 模板值。
/// </summary>
[MaxLength(200)]
public string? ScaleExpressionOverride { get; set; }
```

### 2.2 语法约定

- 输入变量：**小写 `x`**（原始值，double）
- 支持：`+ - * / % ( )`、数字（含 `1e-3`）、`Math.*` 白名单函数
- 白名单函数：`abs min max pow sqrt exp log log10 round floor ceil sign sin cos tan asin acos atan`
- 长度上限 200；空 = 恒等
- **不参与换算的类型**：`BOOL`/`BIT`（数字量）、字符串型 —— 原样透传

### 2.3 旧值等价映射（迁移回填）

| 旧 Slope / Offset | 新 ScaleExpression |
|---|---|
| 1 / 0 | `NULL`（恒等） |
| a / 0 | `a*x` |
| 1 / b | `x+b` |
| a / b | `a*x+b` |

---

## 3. 后端改动（逐文件）

### 3.1 `Domain/Entities/ModelVariable.cs`

删除 106–111 行，替换为：

```csharp
        /// <summary>
        /// 工程换算表达式（原始值 → 工程值）：以 x 代表驱动读到的原始值。
        /// <para>
        /// 替代原 ScaleSlope / ScaleOffset 的线性模型，支持任意一元公式：
        /// "x*0.1"、"(x-4000)/160"、"x*1.8+32"、"Math.round(x*10)/10"。
        /// </para>
        /// <para>
        /// 为空 / 全空白 = 恒等变换（工程值即原始值），等价于旧默认 Slope=1 &amp; Offset=0。
        /// 语法与长度（≤200）由应用层 <c>ScaleExpressionValidator</c> 校验；
        /// 运行时由 Runtime 层 <c>ScaleExpression</c> 编译求値。
        /// </para>
        /// </summary>
        [MaxLength(200)]
        public string? ScaleExpression { get; set; }
```

> 同文件 137 行注释里"缩放等实现细节由设备实例决定"保持有效，无需改。

### 3.2 `Domain/Entities/DeviceVariable.cs`

删除 102–111 行，替换为：

```csharp
        /// <summary>
        /// 工程换算表达式覆盖值（设备实例级）。
        /// <para>允许为空：为空时使用 <see cref="ModelVariable.ScaleExpression"/> 模板值（模板为空即恒等）。</para>
        /// </summary>
        [MaxLength(200)]
        public string? ScaleExpressionOverride { get; set; }
```

### 3.3 `Domain/Interfaces/IRuntimeVariable.cs`

**删除** 50–54 行（`ScaleSlope` / `ScaleOffset`）。

理由：该接口是给**协议驱动**用的只读视图，驱动不应感知缩放（与"驱动只认地址/数据类型"的设计一致）；缩放是 Runtime 层的值转换职责。

> 若你希望驱动侧也能读到表达式做边缘计算，可改为 `string? ScaleExpression { get; }`，但不推荐。

### 3.4 `Runtime/Variables/VariableRuntime.cs`

删除 58–62 行，替换为：

```csharp
    /// <summary>工程换算表达式（原始值→工程值）。来源：DeviceVariable.ScaleExpressionOverride 优先，否则模板 ScaleExpression。</summary>
    public string? ScaleExpression => Instance?.ScaleExpressionOverride ?? Definition.ScaleExpression;

    /// <summary>反算表达式（工程值→原始值，预留）。当前版本返回 null：未配置时写入按原值下发。</summary>
    public string? InverseScaleExpression =>
        Instance?.InverseScaleExpressionOverride ?? Definition.InverseScaleExpression;
```

> 若 D2 选 A（不做反算），`InverseScaleExpression` 两行**不要加**，`VariableScaling.ToRaw()` 直接返回原值即可。

### 3.5 【新增】`Runtime/DataConversion/ScaleExpression.cs`

`DataConversion/` 目录已存在且为空，正好落在这里。

```csharp
using System.Collections.Concurrent;
using Jint;

namespace ScadaServer.Runtime.DataConversion;

/// <summary>
/// 工程换算表达式求值器（基于 Jint，与"系统脚本"共用同一 JS 引擎）。
/// <para>
/// 设计要点：
/// 1. 一次编译、永久缓存：表达式 → JS 函数 → .NET 委托 <see cref="Func{Double, Double}"/>，
///    采集热路径上只有一次委托调用，无词法/语法分析开销；
/// 2. 线程安全：Jint Engine 实例非线程安全，每个缓存项自带互斥门；
/// 3. 沙箱：Strict + 限制递归/语句数/超时，杜绝死循环拖死采集线程；
/// 4. 失败降级：编译或求值失败均返回 false，由调用方决定保持原始值或降级处理，绝不抛穿到采集循环。
/// </para>
/// </summary>
public static class ScaleExpression
{
    /// <summary>表达式输入变量名。</summary>
    public const string InputVariable = "x";

    /// <summary>表达式最大长度（与实体 [MaxLength] 保持一致）。</summary>
    public const int MaxLength = 200;

    /// <summary>单次求值超时上限（毫秒）。</summary>
    private const int EvaluateTimeoutMs = 50;

    private sealed class Compiled
    {
        /// <summary>求值委托（绑定在 <see cref="Engine"/> 上，非线程安全）。</summary>
        public Func<double, double> Fn = null!;

        /// <summary>该委托的互斥门。</summary>
        public object Gate { get; } = new();
    }

    // key = 表达式原文；表达式天然不可变，故无需失效策略，重复配置自动复用。
    private static readonly ConcurrentDictionary<string, Compiled> Cache =
        new(StringComparer.Ordinal);

    /// <summary>缓存项上限，超出后整体清空，防止恶意/异常配置无限堆积。</summary>
    private const int CacheLimit = 4096;

    /// <summary>
    /// 求值：工程值 = f(<paramref name="raw"/>)。表达式为空视为恒等，直接返回 <paramref name="raw"/>。
    /// </summary>
    /// <returns>求值成功返回 true；表达式非法、超时、结果非有限数返回 false。</returns>
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
                value = compiled.Fn(raw);
            }

            if (double.IsNaN(value) || double.IsInfinity(value)) return false;
            result = value;
            return true;
        }
        catch (Exception)
        {
            // 除零 / Math.log(0) 返回 ±Infinity 已在上一步拦截；此处兜住 Jint 内部异常与超时。
            return false;
        }
    }

    /// <summary>
    /// 仅编译不做实际求值，用于配置保存前的语法体检（也用于暴露"是否可编译"）。
    /// </summary>
    public static bool TryCompile(string? expression, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(expression)) return true;
        var compiled = GetOrCompile(expression);
        if (compiled != null) return true;
        error = "表达式无法解析或执行";
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
            engine.Execute($"function __scale({InputVariable}) {{ return ({expression}); }}");
            var fn = engine.GetValue("__scale").As<Func<double, double>>();

            // 试探求值：确认引用合法、结果有限（如误用大写 X 会在此暴露 ReferenceError）。
            var probe = fn(1d);
            if (double.IsNaN(probe) || double.IsInfinity(probe)) return null;

            if (Cache.Count > CacheLimit) Cache.Clear();

            var compiled = new Compiled { Fn = fn };
            return Cache.GetOrAdd(expression, compiled);
        }
        catch (Exception)
        {
            return null;   // 非法表达式不缓存，避免错误配置在缓存里长期占位
        }
    }
}
```

> **待实测确认**：Jint 3.1.3 的 `Options.MaxStatements(int)` 与 `TimeoutInterval` 在**委托调用**路径上是否生效。若实测不生效，退化为"编译期保证纯表达式（已包在函数体内、无循环/无外部引用）+ 长度上限 200"，风险可控，因为语法白名单已在保存前拦截了绝大部分危险输入。
> 备选：改用 `Engine.PrepareScript(source)` + `engine.Evaluate(prepared)`（Jint 3.1.3 已确认包含 `PrepareScript` / `ScriptPreparation` 类型）。

### 3.6 【新增】`Runtime/DataConversion/VariableScaling.cs`

```csharp
using ScadaServer.Runtime.Variables;

namespace ScadaServer.Runtime.DataConversion;

/// <summary>
/// 变量值工程换算门面：采集方向 raw→engineering，写入方向 engineering→raw。
/// <para>
/// 换算规则：数字量（bool）与字符串原样透传；仅数值型参与表达式求值；
/// 表达式为空 / 求值失败均返回原始值，保证采集链路永不被一条坏配置打断。
/// </para>
/// </summary>
public static class VariableScaling
{
    /// <summary>采集方向：驱动原始值 → 工程值。</summary>
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
    /// 写入方向：工程值 → 驱动原始值。
    /// <para>当前版本：若未配置反算表达式（<see cref="VariableRuntime.InverseScaleExpression"/> 为 null），
    /// 直接下发工程值（与改造前行为一致）。后续支持反算时只需替换本方法体。</para>
    /// </summary>
    public static object? ToRaw(VariableRuntime vr, object? engineering)
    {
        if (engineering is null) return null;
        var inverse = vr.InverseScaleExpression;
        if (string.IsNullOrWhiteSpace(inverse)) return engineering;
        if (engineering is bool) return engineering;
        if (!TryToNumber(engineering, out var y)) return engineering;
        return ScaleExpression.TryEvaluate(inverse, y, out var x) ? x : engineering;
    }

    /// <summary>尽力把运行时值转成 double（bool 计为 0/1，与 DeviceWorker.TryToNumber 语义一致）。</summary>
    private static bool TryToNumber(object? value, out double result)
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
                return double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out result);
            case IConvertible c:
                try { result = c.ToDouble(System.Globalization.CultureInfo.InvariantCulture); return true; }
                catch { return false; }
            default: return false;
        }
    }
}
```

> 可选后续：`DeviceWorker.TryToNumber` 与本方法重复，可在验收后统一收敛（本次不动，避免扩大改动面）。

### 3.7 `Runtime/Devices/DeviceWorker.cs` —— 接入正向换算

在 165 行 `var newValue = await _runtime.Driver.ReadAsync(vr);` 之后、169 行 null 判定之前插入：

```csharp
                            var newValue = await _runtime.Driver.ReadAsync(vr);

                            // 工程换算（raw → engineering）：表达式为空即恒等，求值失败保持原始值。
                            // 放在 null 判定之前不影响语义——null 的换算结果仍是 null。
                            newValue = VariableScaling.ToEngineering(vr, newValue);

                            // 驱动可能返回 null（例如虚拟设备未连接、订阅型驱动暂无数据）。
```

文件头 `using` 追加：

```csharp
using ScadaServer.Runtime.DataConversion;
```

> **注意**：换算后的值同时进入后续的历史存储（`TryRecordHistory` 用 `vr.Value` 计算 numericValue）、报警判定（`CheckAlarmsAndNotify`）、死区比较 —— 与"Min/Max 量程、死区、报警阈值都是工程单位"的语义一致，符合预期。

### 3.8 `Runtime/RuntimeManager.cs` —— 接入反向换算

写入前（585 行）转换，同时**保持限幅校验用工程值**：

```csharp
            // 写入方向：工程值 → 驱动原始值（未配置反算表达式时原样下发，与改造前一致）。
            var rawValue = VariableScaling.ToRaw(vr, value);

            try
            {
                await runtime.Driver.WriteAsync(vr, rawValue)
                    .WaitAsync(TimeSpan.FromMilliseconds(_deviceWriteTimeoutMs));
            }
```

`using` 追加同上。

> 571–577 行的 Min/Max 限幅仍对 `value`（工程值）校验 —— 正确，不动。
> 606 行 `vr.Value = value;` 仍写工程值 —— 正确，前端展示与广播都用工程值。

### 3.9 `Application/DTOs/ModelVariableDto.cs`

删除 83–87 行，替换为：

```csharp
    /// <summary>
    /// 工程换算表达式（原始值 → 工程值），以 x 代表原始值。
    /// 为空 = 恒等变换（等价旧 Slope=1 &amp; Offset=0）。示例："x*0.1"、"(x-4000)/160"。
    /// </summary>
    [StringLength(200, ErrorMessage = "换算表达式不能超过200个字符")]
    public string? ScaleExpression
    {
        get => _scaleExpression;
        set => _scaleExpression = value?.Trim() ?? string.Empty;
    }
    private string _scaleExpression = string.Empty;
```

### 3.10 `Application/DTOs/ModelVariableMapper.cs`

27–28 行替换为：

```csharp
        ScaleExpression = v.ScaleExpression,
```

### 3.11 `Application/DTOs/DeviceVariableDto.cs`

54–58 行替换为：

```csharp
    /// <summary>工程换算表达式覆盖值。空 → 使用模板 ScaleExpression。</summary>
    [StringLength(200, ErrorMessage = "换算表达式不能超过200个字符")]
    public string? ScaleExpressionOverride { get; set; }
```

### 3.12 `Application/DTOs/VariableTransferDto.cs`

95–99 行替换为：

```csharp
    /// <summary>工程换算表达式（可空）</summary>
    public string? ScaleExpression { get; set; }
```

### 3.13 `Application/Services/ModelVariableAppService.cs`

**a) `MapRowToEntity`（410–411）**

```csharp
                ScaleExpression = row.ScaleExpression,
```

**b) `ApplyRowToEntity`（445–446）**

```csharp
            if (row.ScaleExpression is not null) entity.ScaleExpression = row.ScaleExpression;
```

**c) `MapToEntity`（495–496）**

```csharp
            entity.ScaleExpression = dto.ScaleExpression;
```

**d) `ValidateVariableLogic` 新增 D 项（478 行前插入）**

```csharp
            // D. 工程换算表达式校验：长度、字符/函数白名单、语法可解析、试算结果有限。
            var scaleError = ScaleExpressionValidator.Validate(dto.ScaleExpression);
            if (scaleError != null)
            {
                throw new BusinessException($"变量 '{dto.Name}' 的换算表达式非法：{scaleError}");
            }
```

**e)【新增】`Application/Services/ScaleExpressionValidator.cs`**

Application 层不引用 Runtime（依赖方向 Runtime → Application 接口），故校验器在 Application 层独立实现，规则与 Runtime 求值器对齐：

```csharp
using System.Text.RegularExpressions;
using Jint;

namespace ScadaServer.Application.Services;

/// <summary>
/// 工程换算表达式校验器（保存前体检）。
/// <para>三重校验：字符白名单 → 函数名白名单 → Jint 解析（只定义函数体，不调用，杜绝校验阶段执行用户代码）。</para>
/// </summary>
public static class ScaleExpressionValidator
{
    /// <summary>表达式最大长度，与 ModelVariable.ScaleExpression 的 [MaxLength] 一致。</summary>
    public const int MaxLength = 200;

    /// <summary>允许出现的 Math 函数白名单。</summary>
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.Ordinal)
    {
        "abs", "min", "max", "pow", "sqrt", "exp", "log", "log10",
        "round", "floor", "ceil", "sign", "sin", "cos", "tan", "asin", "acos", "atan"
    };

    /// <summary>标识符（函数名）提取：Math.xxx 或裸函数名，后跟左括号。</summary>
    private static readonly Regex IdentifierPattern =
        new(@"(?:Math\s*\.\s*)?([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

    /// <summary>剥离所有白名单函数调用后，剩余字符必须落在本集合内。</summary>
    private static readonly Regex AllowedChars =
        new(@"^[0-9x\s+\-*/%().,eE]*$", RegexOptions.Compiled);

    /// <summary>校验表达式；合法返回 null，非法返回中文错误原因。</summary>
    public static string? Validate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;      // 空 = 恒等，合法

        var expr = expression.Trim();
        if (expr.Length > MaxLength)
            return $"长度不能超过 {MaxLength} 个字符（当前 {expr.Length}）";

        // 1) 函数名白名单
        var stripped = IdentifierPattern.Replace(expr, m =>
            AllowedFunctions.Contains(m.Groups[1].Value) ? "(" : "\u0000(");
        if (stripped.Contains('\u0000'))
            return $"只允许使用白名单函数：{string.Join("/", AllowedFunctions)}";

        // 2) 字符白名单（剥离函数调用后）
        if (!AllowedChars.IsMatch(stripped))
            return "包含非法字符，仅允许数字、变量 x、运算符 + - * / % ( ) 与白名单函数";

        // 3) 语法解析（Jint 只解析不执行）
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
```

### 3.14 `Application/Services/DeviceVariableAppService.cs`

**a) `UpdateAsync`（141–142）**

```csharp
        // 覆盖表达式校验（与模板同规则）；非法直接拒绝，避免脏配置进入运行时。
        var scaleError = ScaleExpressionValidator.Validate(dto.ScaleExpressionOverride);
        if (scaleError != null)
            throw new BusinessException($"设备变量换算表达式覆盖值非法：{scaleError}");

        entity.ScaleExpressionOverride = dto.ScaleExpressionOverride;
        entity.DeadBandOverride = dto.DeadBandOverride;
```

**b) `MapToDto`（170–171）**

```csharp
        ScaleExpressionOverride = dv.ScaleExpressionOverride,
```

### 3.15 `Application/Services/DeviceAppService.cs`

142–143 行替换为：

```csharp
                ScaleExpressionOverride = dv.ScaleExpressionOverride,
```

### 3.16 `Application/ImportExport/VariableExportService.cs`

**列数 15 → 14，`DeadBand` 由第 14 列变 13 列，`IsReadOnly` 由 15 变 14。**

表头（17–21 行）：

```csharp
    private static readonly string[] Headers =
    {
        "Key", "Name", "DataType", "Unit", "Min", "Max", "Description", "Address",
        "StoreMode", "StoreIntervalMs", "UpdateMode", "ScaleExpression", "DeadBand", "IsReadOnly"
    };
```

Excel 写值（57–60 行）：

```csharp
            ws.Cell(row, 12).Value = v.ScaleExpression ?? string.Empty;
            ws.Cell(row, 13).Value = v.DeadBand;
            ws.Cell(row, 14).Value = v.IsReadOnly;
```

CSV 写值（96–99 行）：

```csharp
              .Append(Csv.Quote(v.ScaleExpression)).Append(',')
              .Append(Csv.Number(v.DeadBand)).Append(',')
              .Append(v.IsReadOnly ? "true" : "false")
```

> `Csv.Quote` 已做双引号包裹与转义，表达式里的逗号不会破坏列结构。

### 3.17 `Application/ImportExport/CsvParser.cs`

表头（16–20 行）同步改为 `..., "UpdateMode", "ScaleExpression", "DeadBand", "IsReadOnly"`。

解析（117–118 行）：

```csharp
        row.ScaleExpression = Cell(cells, colIndex, "ScaleExpression");
```

> `Cell(...)` 返回的是已去引号的字符串；空列返回空串。建议在此处把空串归一为 `null`：
> `var se = Cell(cells, colIndex, "ScaleExpression"); row.ScaleExpression = string.IsNullOrWhiteSpace(se) ? null : se;`

### 3.18 `Infrastructure/Persistence/ScadaDbContext.cs`

在 121–133 的 `ModelVariable` 配置块内追加（Pomelo 对无长度 `string` 默认映射 `longtext`，显式限长保持与既有风格一致）：

```csharp
            modelBuilder.Entity<ModelVariable>()
                .Property(m => m.ScaleExpression).HasMaxLength(200);
            modelBuilder.Entity<DeviceVariable>()
                .Property(d => d.ScaleExpressionOverride).HasMaxLength(200);
```

### 3.19 迁移

新建 `2026xxxx_ReplaceScaleWithExpression.cs`（请用 `dotnet ef migrations add` 生成骨架后填入下列 SQL）：

```csharp
public partial class ReplaceScaleWithExpression : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1) 新列
        migrationBuilder.AddColumn<string>(
            name: "ScaleExpression", table: "ModelVariables",
            type: "varchar(200)", maxLength: 200, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScaleExpressionOverride", table: "DeviceVariables",
            type: "varchar(200)", maxLength: 200, nullable: true);

        // 2) 数据回填：线性 Slope/Offset → 公式。恒等（1/0）保持 NULL。
        //    CAST(double AS CHAR) 可能产出科学计数法（如 1e-05），JS 语法合法，Jint 可直接解析。
        migrationBuilder.Sql(@"
UPDATE `ModelVariables`
SET `ScaleExpression` = CASE
    WHEN `ScaleSlope` = 1 AND `ScaleOffset` = 0 THEN NULL
    WHEN `ScaleSlope` = 1 THEN CONCAT('x+', CAST(`ScaleOffset` AS CHAR))
    WHEN `ScaleOffset` = 0 THEN CONCAT(CAST(`ScaleSlope` AS CHAR), '*x')
    ELSE CONCAT(CAST(`ScaleSlope` AS CHAR), '*x+', CAST(`ScaleOffset` AS CHAR))
END;");

        // 设备实例覆盖值回填：NULL 保持 NULL（继承模板），非 NULL 才生成公式。
        migrationBuilder.Sql(@"
UPDATE `DeviceVariables`
SET `ScaleExpressionOverride` = CASE
    WHEN `ScaleSlopeOverride` IS NULL AND `ScaleOffsetOverride` IS NULL THEN NULL
    WHEN `ScaleSlopeOverride` IS NULL THEN CONCAT('x+', CAST(`ScaleOffsetOverride` AS CHAR))
    WHEN `ScaleOffsetOverride` IS NULL THEN CONCAT(CAST(`ScaleSlopeOverride` AS CHAR), '*x')
    ELSE CONCAT(CAST(`ScaleSlopeOverride` AS CHAR), '*x+', CAST(`ScaleOffsetOverride` AS CHAR))
END;");

        // 3) 删除旧列
        migrationBuilder.DropColumn(name: "ScaleSlope",  table: "ModelVariables");
        migrationBuilder.DropColumn(name: "ScaleOffset", table: "ModelVariables");
        migrationBuilder.DropColumn(name: "ScaleSlopeOverride",  table: "DeviceVariables");
        migrationBuilder.DropColumn(name: "ScaleOffsetOverride", table: "DeviceVariables");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 回滚：加回旧列。公式为不可逆信息，仅能尽力解析 `a*x+b` 形式回填，
        // 自定义公式（如 Math.round(x*10)/10）将退化为 Slope=1 / Offset=0，需人工确认。
        migrationBuilder.AddColumn<double>(
            name: "ScaleSlope", table: "ModelVariables", type: "double", nullable: false, defaultValue: 1.0);
        migrationBuilder.AddColumn<double>(
            name: "ScaleOffset", table: "ModelVariables", type: "double", nullable: false, defaultValue: 0.0);
        migrationBuilder.AddColumn<double>(
            name: "ScaleSlopeOverride", table: "DeviceVariables", type: "double", nullable: true);
        migrationBuilder.AddColumn<double>(
            name: "ScaleOffsetOverride", table: "DeviceVariables", type: "double", nullable: true);

        migrationBuilder.Sql(@"
UPDATE `ModelVariables`
SET `ScaleSlope`  = COALESCE(CAST(REGEXP_SUBSTR(`ScaleExpression`, '^-?[0-9.]+(?=\*x)') AS DECIMAL(20,8)), 1),
    `ScaleOffset` = COALESCE(CAST(REGEXP_SUBSTR(`ScaleExpression`, '(?<=\+)-?[0-9.]+$') AS DECIMAL(20,8)), 0);");

        migrationBuilder.DropColumn(name: "ScaleExpression", table: "ModelVariables");
        migrationBuilder.DropColumn(name: "ScaleExpressionOverride", table: "DeviceVariables");
    }
}
```

> `REGEXP_SUBSTR` 需 MySQL 8.0+（项目现有迁移已用窗口函数/JSON，版本应无问题；执行前确认一次）。

---

## 4. 前端改动

### 4.1 `src/types.ts`

```diff
   // 工业级增强字段
-  scaleSlope: number;
-  scaleOffset: number;
+  scaleExpression?: string | null;   // 工程换算表达式（raw→eng），以 x 代表原始值；空=恒等
   deadBand?: number;
```
```diff
-  scaleSlopeOverride?: number | null;   // 实例级覆盖：缩放斜率，空=用模板值
-  scaleOffsetOverride?: number | null;  // 实例级覆盖：缩放偏移，空=用模板值
+  scaleExpressionOverride?: string | null; // 实例级覆盖：换算表达式，空=用模板值
```
```diff
-  scaleSlope?: number | null;
-  scaleOffset?: number | null;
+  scaleExpression?: string | null;
```
（第 1201–1202 行属于 `VariableImportRow`）

### 4.2 `src/api/modelApi.ts`

```diff
-          scaleSlope: v.scaleSlope || 1.0,
-          scaleOffset: v.scaleOffset || 0.0,
+          scaleExpression: v.scaleExpression ?? '',
```

### 4.3 `src/components/DataModelView.vue`

**a) ref（159–160）**

```diff
-const varScaleSlope = ref<number | ''>(1.0);
-const varScaleOffset = ref<number | ''>(0.0);
+const varScaleExpression = ref<string>('');
```

**b) 编辑回填（244–245）**

```diff
-  varScaleSlope.value = v.scaleSlope ?? 1.0;
-  varScaleOffset.value = v.scaleOffset ?? 0.0;
+  varScaleExpression.value = v.scaleExpression ?? '';
```

**c) 新建重置（265–266）**

```diff
-  varScaleSlope.value = 1.0;
-  varScaleOffset.value = 0.0;
+  varScaleExpression.value = '';
```

**d) 提交（328–329）**

```diff
-    scaleSlope: varScaleSlope.value === '' ? 1.0 : varScaleSlope.value,
-    scaleOffset: varScaleOffset.value === '' ? 0.0 : varScaleOffset.value,
+    scaleExpression: varScaleExpression.value.trim() === '' ? null : varScaleExpression.value.trim(),
```

**e) 表单 UI（1115–1136 整块替换）**

```html
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">
                工程换算表达式
              </label>
              <input
                v-model="varScaleExpression"
                type="text"
                placeholder="留空=原始值；例：x*0.1 或 (x-4000)/160"
                class="w-full bg-white dark:bg-slate-900 border border-orange-200 dark:border-orange-700 rounded p-1.5 focus:outline-none text-xs font-mono text-slate-800 dark:text-white"
              />
              <p class="mt-0.5 text-[10px] text-slate-400">
                用 <code class="font-mono text-orange-600 dark:text-orange-400">x</code> 表示原始值，支持
                <code class="font-mono">+ - * / % ( )</code> 与
                <code class="font-mono">Math.round/sqrt/pow</code> 等函数
              </p>
            </div>
```

> 建议（可选增强）：在输入框旁加一个"试算"按钮，输入一个原始值后端返回工程值。需要新增一个轻量接口 `POST /api/model-variable/preview-scale`。

### 4.4 `src/components/DeviceVariableView.vue`（D3-A）

686–702 的 `grid-cols-3` 改为 `grid-cols-2`：

```html
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">换算表达式（覆盖）</label>
              <input v-model="editingForm.scaleExpressionOverride" type="text"
                placeholder="留空=继承模板"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs font-mono focus:outline-none" />
            </div>
            <div>
              <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">死区</label>
              <input v-model.number="editingForm.deadBandOverride" type="number" step="0.1"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs font-mono focus:outline-none" />
            </div>
          </div>
```

137 行 `{ ...v, isReadOnlyOverride: v.isReadOnlyOverride ?? null }` 无需改（类型已覆盖）。

### 4.5 `swagger.json`

后端编译通过后重新导出覆盖 `Client/swagger.json`（该文件是生成的产物，勿手改）。

---

## 5. 执行顺序建议

```
1. Domain 实体 + 接口                    （3.1 / 3.2 / 3.3）
2. Application DTO / 映射 / 校验器        （3.9 ~ 3.15）
3. 导入导出                              （3.16 / 3.17）
4. DbContext + 迁移，跑通数据库           （3.18 / 3.19）
5. Runtime 求值器 + 换算门面              （3.5 / 3.6 / 3.4）
6. 采集 / 写入链路接入                    （3.7 / 3.8）
7. 前端类型 / API / 两个 Vue              （4.1 ~ 4.4）
8. 重新生成 swagger.json                 （4.5）
9. 单元测试 + 联调验收
```

每步结束 `dotnet build` 通过再进下一步。

---

## 6. 验收清单

**单元测试**（`ScadaServer.Infrastructure.Tests` 新增 `ScaleExpressionTests`）

| 用例 | 期望 |
|---|---|
| `null` / `""` / `"  "` | `TryEvaluate` 返回 true，结果 = 原值 |
| `"x*0.1"`, x=100 | 10 |
| `"(x-4000)/160"`, x=20000 | 100 |
| `"x*1.8+32"`, x=100 | 212 |
| `"Math.round(x*10)/10"`, x=1.23456 | 1.2 |
| `"1e-3*x"`, x=1000 | 1 |
| `"x/0"` | false（Infinity 拦截） |
| `"Math.log(0)"` / `"Math.sqrt(-1)"` | false |
| `"X*2"`（大写） | false，编译期即拦截 |
| `"while(1){}"` / `"process.exit()"` | false（字符白名单 + 函数白名单拦截） |
| `"a=1"` | false |
| 并发 1000 次求值同一/不同表达式 | 无异常、结果稳定 |

**联调验收**

1. 给一个模拟量配 `x*0.1`，用虚拟设备写原始值 1000 → 监控页、历史趋势、报警判定都按 100 计算。
2. 量程 Min/Max 配到工程单位（如 0~200），原始值 3000（工程 300）→ 触发越限报警。
3. 导出 CSV/XLSX → 表头含 `ScaleExpression` 且列数 14；不改内容直接回导 → 表达式保持一致。
4. 保存非法表达式（如 `x*`）→ 后端返回 400 中文错误，前端弹出提示。
5. 设备变量实例配覆盖表达式 → 该设备按覆盖值换算，同模板其它设备不受影响。
6. 迁移前后数据比对：`SELECT COUNT(*) FROM ModelVariables WHERE ScaleSlope<>1 OR ScaleOffset<>0` 与 `WHERE ScaleExpression IS NOT NULL` 行数一致。

---

## 7. 风险与注意事项

| 风险 | 说明 | 应对 |
|---|---|---|
| **缩放此前从未生效** | 改造后第一次真正启用换算，历史数据是按原始值入库存的 | 上线前确认是否有存量历史需要重算；或先灰度单个变量 |
| **写入反算缺失** | 配置了公式的可写变量，写入时下发的是工程值而非原始值 | 见 D2。当前项目写入链路本就未做反算，行为不变；如需严格语义须追加 `InverseScaleExpression` |
| **驱动类型差异** | S7/OPC UA/MQTT 各驱动返回的原始值形态不同（含 bool、字符串） | `VariableScaling` 对 bool/字符串原样透传，仅数值参与 |
| **Jint 引擎非线程安全** | 并发求值可能出错 | 每个缓存项独立 Engine + 独立互斥门 |
| **超时机制需实测** | `MaxStatements` / `TimeoutInterval` 在委托调用路径是否生效 | 见 3.5 备注；保存前的白名单校验已构成主要防线 |
| **`Down()` 不可逆** | 自定义公式回滚后丢失 | 回滚前导出变量 CSV 备份 |
| **导入导出列数变化** | 旧版 CSV（15 列）仍可导入：`CsvParser` 按**列名**匹配，旧文件缺 `ScaleExpression` 列则该字段为 null，不会报错 | 无需额外兼容代码，但旧文件的 Slope/Offset 数值**不会**被转换，需在发版说明中提示 |

---

## 8. 待你确认后我再动手

需要你给出 **D1 / D2 / D3 / D4** 四个选择（或直接说"按推荐来"），确认后我按第 5 节的顺序分步实施，每步 build 通过再推进，最后按你的 Git 习惯输出中文 commit。
