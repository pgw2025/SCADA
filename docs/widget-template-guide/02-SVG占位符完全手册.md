# 02 · SVG 占位符完全手册

> 本文是 **svg 轨** 的核心。读完你应该能回答：有哪些占位符、每个的取值边界、
> 为什么不能写表达式、以及在没有表达式的情况下怎么把液位/进度/指针做出来。
>
> 全部结论来自源码实证：
> - 上下文接口：`Client/src/utils/svgTemplate.ts` → `SvgBindingContext`
> - 上下文组装：`Client/src/components/widgets/SvgTemplateWidget.vue`
> - 替换实现：同文件 `bindSvgTemplate`
> - 派生值计算：`Client/src/components/widgets/useWidgetBase.ts`

---

## 一、替换算法（先把原理说透）

```ts
// Client/src/utils/svgTemplate.ts
export const bindSvgTemplate = (svg: string, ctx: SvgBindingContext): string =>
  svg.replace(/\{([a-zA-Z0-9_]+)\}/g, (m, k: string) => {
    const v = (ctx as unknown as Record<string, unknown>)[k];
    return v === undefined || v === null ? m : String(v);
  });
```

逐条拆解这段 4 行代码带出来的全部行为：

| 行为 | 结论 |
|---|---|
| 匹配规则 `\{([a-zA-Z0-9_]+)\}` | key 只认 **字母 / 数字 / 下划线**。`{font-size}`、`{110 - 0.8 * x}`、`{ value }`（带空格）**都不会被匹配** |
| 大小写敏感 | `{Value}` ≠ `{value}`，前者原样输出 |
| 替换方式 | **纯字符串替换**，不是模板引擎、不是表达式求值 |
| `undefined` / `null` 的处理 | **保留原始字面量** `{key}`，**不是**替换成空串 |
| 其它值 | `String(v)`：`true` → `"true"`；`42.5` → `"42.5"`；`63.492063492063494` → 原样全精度 |
| 作用范围 | 全文本替换 —— **文本节点和属性值都生效**（`fill="{activeColor}"` 合法） |
| 替换次数 | 全局 `g`，同一占位符可出现多次 |
| 执行时机 | `computed`，值变化即重算；每次重算都会先跑一遍 `sanitizeSvg` |

---

## 二、14 个有效占位符（逐个深挖）

### 2.1 总表

| 占位符 | 类型 | 来源 | 空值/缺省时 |
|---|---|---|---|
| `{value}` | `number \| boolean` | 绑定变量原始值 | 未绑定 → `false` → 输出 `"false"` |
| `{numValue}` | `number` | `useWidgetBase.numValue` | — |
| `{boolValue}` | `boolean` | `useWidgetBase.boolValue` | — |
| `{normalizedPercent}` | `number` 0~100 | `useWidgetBase.normalizedPercent` | — |
| `{state}` | `string` | `boolValue ? onText : offText` | — |
| `{unit}` | `string` | `propOr('unit','')` | `''` → 输出空 |
| `{label}` | `string` | `component.label ?? ''` | 未填 → 输出空 |
| `{activeColor}` | `string` | `propOr('activeColor','#10b981')` | — |
| `{inactiveColor}` | `string` | `propOr('inactiveColor','#94a3b8')` | — |
| `{alertColor}` | `string` | `useWidgetBase.alertColor` | — |
| `{thresholdMin}` | `number \| null` | `useWidgetBase.thresholdMin` | **`null` → 残留字面量** |
| `{thresholdMax}` | `number \| null` | `useWidgetBase.thresholdMax` | **`null` → 残留字面量** |
| `{fontSize}` | `number` | `propOr('fontSize',12)` | — |
| `{quality}` | `string` | `props.quality ?? ''` | **质量正常 → 输出空串** |

### 2.2 逐条说明

#### `{value}` — 原始值，原样输出

```ts
value: props.value        // number | boolean
```

- **不做任何格式化**。如果采集值是 `42.50000000000001`，画面上就是这么长一串。
- 需要固定小数位 → **模板层做不到**，请在变量侧解决（设备变量的缩放 / 死区 / 换算脚本），
  或在 `var-display` 类图元上用 `decimals` 属性。
- 布尔变量输出 `"true"` / `"false"`。想在 SVG 上显示中文状态，用 `{state}` 而不是 `{value}`。
- **未绑定变量**时 `props.value` 为 `false` → 会渲染出 `"false"`。
  如果不想看到它，别在显眼位置放 `{value}`，或把默认 props 设成 `showValue=false` 并接受浮签关闭。

#### `{numValue}` — 数值化

```ts
typeof value === 'number' ? value : (value ? 100 : 0)
```

布尔 → `100` / `0`。所以给一个开关量做「0/100 的柱高」时，`{numValue}` 和 `{normalizedPercent}` 常常等价。

#### `{boolValue}` — 布尔化

```ts
typeof value === 'boolean' ? value : value > 0
```

输出 `"true"` / `"false"`。注意它**不能直接用作属性值**（SVG 属性不认 `true`），
最常见的误用是 `fill="{boolValue}"` —— 一定要配合 `{activeColor}` / `{state}` 之类使用。

#### `{normalizedPercent}` — 量程归一化（最有用）

```ts
const lo = minValue, hi = maxValue;
if (hi <= lo) return numValue > 0 ? 100 : 0;        // 量程非法防除零
return Math.min(100, Math.max(0, ((numValue - lo) / (hi - lo)) * 100));
```

- 结果被 **clamp 到 0~100**，超出量程不会溢出。
- `minValue` / `maxValue` 取自：`组件 props → 模板 defaultProps → 硬兜底 0/100`。
- `maxValue` 还有一道 `|| 100` 防零（`Number(propOr('maxValue', 100)) || 100`）。
- **是全精度浮点数**，比如 `17.57142857142857`。作为 `height` 使用完全没问题，
  但如果你打算把它当文本显示，画面上会是一长串 —— 别这么干。
- 这是 SVG 轨里唯一能表达「连续量」的入口，本文 §五所有技巧基本都在围着它转。

#### `{state}` — 状态文案

```ts
state: boolValue ? onText : offText
// onText  = component.props.onText  || '开启'
// offText = component.props.offText || '关闭'
```

⚠ `onText` / `offText` **不走** `propOr` 三级兜底，只读 `component.props`。
所以它们**不会**回退到模板的 `defaultPropsJson` —— 想改默认值，只有两条路：

1. 在 `defaultPropsJson` 里写上 `onText`/`offText`，它们会在**落布时**被快照进组件 props（之后就固定了）；
2. 或者用 `propSchemaJson` 暴露出来让用户在属性面板上填（推荐，见 [04](./04-属性Schema编写指南.md)）。

> 反过来说：存量组件的 `onText` 一旦落布就固化了，改模板默认值不会影响它们。

#### `{unit}` — 单位

走 `propOr('unit','')`：组件 props → 模板默认 → `''`。
空串是安全的：`<text>{value}{unit}</text>` 在无单位时输出 `42.5`，有单位时 `42.5MPa`。

#### `{label}` — 组件标签

```ts
label: props.component.label ?? ''
```

⚠ **不是** `props.label`，是画布组件的「标签」字段（属性面板最上面那个文本框）。
落布时默认等于模板 `Name`，用户通常会改成「1#锅炉」之类。

#### `{activeColor}` / `{inactiveColor}` — 运行色 / 停止色

走 `propOr`，硬兜底分别是 `#10b981`（绿）/ `#94a3b8`（灰）。
颜色必须是 **`#rrggbb` 十六进制** —— 属性面板的取色器只接受 hex，写 `red`、`rgb(255,0,0)` 会让取色器显示异常
（SVG 本身能渲染，但用户一进面板就会看到错的）。

> 注意：内置种子的 `activeColor` 默认是 `#3b82f6`（蓝），而代码硬兜底是 `#10b981`（绿）。
> 管理页给 SVG 模板预填的也是 `#3b82f6`。**两者不一致**，以你模板里写的值为准。

#### `{alertColor}` — 阈值告警色（把「判断」外包出去）

```ts
isHighAlert = !isBool && thresholdMax != null && numValue >= thresholdMax
isLowAlert  = !isBool && thresholdMin != null && numValue <= thresholdMin
alertColor  = isHighAlert ? '#ef4444'   // 红
            : isLowAlert  ? '#f59e0b'   // 琥珀
            : activeColor
```

⚠ 三个细节：

1. **布尔值永远不告警**：`typeof value === 'boolean'` 时直接 `false`，也就是开关量永远是 `activeColor`。
2. **边界是闭区间**：`>=` 上限、`<=` 下限。值恰好等于阈值时算告警。
3. **没有阈值 = 不告警**：`thresholdMax` 为 `null` 时 `isHighAlert` 恒 false，`alertColor` 退化成 `activeColor`。

这是模板里唯一能表达「超阈值变色」的方式 —— **不要试图自己写条件**，写不出来。

#### `{thresholdMin}` / `{thresholdMax}` — 阈值（危险）

```ts
// useWidgetBase
const v = component.props.thresholdMax;
if (v !== undefined && v !== null && !isNaN(Number(v))) return Number(v);
const def = defDefaults.thresholdMax;
return (def !== undefined && def !== null && !isNaN(Number(def))) ? Number(def) : null;
```

**这是唯一一组可能为 `null` 的占位符，而 `null` 会导致字面量残留。**

```xml
<!-- 假设 thresholdMax 为 null -->
<rect y="{thresholdMax}" .../>     →  <rect y="{thresholdMax}" .../>   ← 非法属性值，浏览器可能整段丢弃
<text>上限 {thresholdMax}</text>   →  <text>上限 {thresholdMax}</text>  ← 画面上出现大括号
```

**结论：SVG 模板的 `defaultPropsJson` 里，`thresholdMin` / `thresholdMax` 一定要给数字，不要给 `null`。**
管理页预填的 `DEFAULT_SVG_PROPS` 给的是 `10` / `90`，请保持这个习惯。
（内置种子 `var-display` 用的是 `null`，但它是 builtin 轨，SFC 里做了判空处理，svg 轨没有这个待遇。）

另一个陷阱：`Number('')` 是 `0`，`isNaN(0)` 是 `false` —— 所以阈值被设成空串时会变成 **0**，而不是「不设」。
只有 `propSchemaJson` 里 `nullable: true` 的 number 控件清空时才写入真正的 `null`。

#### `{fontSize}` — 字号

走 `propOr('fontSize', 12)`，硬兜底 `12`。
注意它来自 **props** 而不是 SVG 的 `font-size` 计算值，所以可以直接用在 `font-size="{fontSize}"` 上。

#### `{quality}` — 变量质量（语义反直觉）

```ts
quality: props.quality ?? ''
// 上游：ScadaPlayerView.componentQualities
//   if (bindDeviceId != null && bindVariableKey) {
//     const q = device.variableMeta[bindVariableKey]?.quality;
//     if (q && q !== 'Good') result[component.id] = String(q);   // ← 只记录非 Good
//   }
```

⚠ **质量正常时 `{quality}` 输出的是空串，不是 `"Good"`。**
上游只在非 Good 时才往 map 里放值，其余情况取到 `undefined` → `?? ''`。

这带来一个**非常好用的副作用**：

```xml
<text x="10" y="14" fill="#ef4444" font-size="9">{quality}</text>
```

- 质量正常 → 空文本，视觉上完全不存在；
- 通信中断 → 显示 `Bad` / `CommunicationError` 之类。

这是 svg 轨里唯一天然的「条件显示」手段（见 §五-5）。

---

## 三、不存在的占位符（别被速查表骗了）

管理页「占位符速查表」里列了 15 项，但上下文只有 14 项。差异：

| 速查表列出 | 是否在上下文中 | 实际行为 |
|---|---|---|
| `{qualityBad}` | ❌ **不在** | **原样渲染成 `{qualityBad}`** |

（`useWidgetBase` 里确实有 `qualityBad` 这个 computed，但 `SvgTemplateWidget` 组装上下文时没有带上它。）

同样**不在**上下文中、但很容易顺手写出来的：

`{minValue}` `{maxValue}` `{onText}` `{offText}` `{width}` `{height}` `{align}` `{bold}`
`{showValue}` `{showLabel}` `{strokeColor}` `{fillColor}` `{numText}` …

它们都会**原样输出**。这有好的一面：**拼错会立刻在画面上看见**，不会静默失败。
坏的一面是当它们出现在属性值里时会让属性非法。

> 速查表的「插入」按钮只是往光标处插字符串，不做校验 —— 最终以本文的 14 项为准。

---

## 四、属性 vs 文本：占位符能放哪

**哪儿都能放**，因为替换发生在字符串层面：

```xml
<rect fill="{activeColor}"                      <!-- 颜色 -->
      height="{normalizedPercent}"               <!-- 长度 -->
      font-size="{fontSize}"                     <!-- 字号 -->
      stroke-width="2" opacity="{numValue}" />   <!-- 透明度（0~100 → 会溢出到 1 以上，慎用） -->
<text>{label} {value}{unit} {state}</text>       <!-- 文本 -->
<circle r="{normalizedPercent}" />               <!-- 半径 -->
```

⚠ 注意 `opacity`：SVG 的 `opacity` 取值是 **0~1**，而 `{normalizedPercent}` 是 **0~100**。
直接写 `opacity="{normalizedPercent}"` 在值 > 1 时会被当作 1（完全不透明），不是报错但行为不对。
要做透明度渐变，得用 §五-4 的单位换算技巧把 0~100 映射成 0~1。

---

## 五、无表达式，如何表达数学关系

这是 svg 轨最大的限制，也是最能拉开模板作者水平的地方。
**请不要试图写 `{100 - normalizedPercent}` 或者 `{normalizedPercent * 1.2}`，它们不会被替换。**

下面 6 个技巧可以实现绝大多数常见需求。

### 技巧 1：百分比属性 + 嵌套 `<svg>`（★★★ 最推荐）

SVG 的 `<rect width height>` 支持百分比，相对于**当前视口**。
而嵌套 `<svg>` 元素会建立一个新视口 —— 于是可以在罐体内部单独搞一套坐标系。

```xml
<svg width="100%" height="100%" viewBox="0 0 120 160" xmlns="http://www.w3.org/2000/svg">
  <!-- 外壳 -->
  <rect x="10" y="10" width="100" height="140" rx="12" fill="#1e293b" stroke="{inactiveColor}" stroke-width="3"/>

  <!-- 内嵌视口：这块区域就是 100% -->
  <svg x="20" y="20" width="80" height="120" viewBox="0 0 80 120" preserveAspectRatio="none">
    <g transform="translate(0,120) scale(1,-1)">   <!-- 翻转让它从底部往上长 -->
      <rect x="0" y="0" width="80" height="{normalizedPercent}%" fill="{activeColor}"/>
    </g>
  </svg>
</svg>
```

要点：
- `preserveAspectRatio="none"` 必须写，否则嵌套 viewBox 会被重新等比适配。
- 翻转技巧：`translate(0, H) scale(1,-1)` 把坐标系上下颠倒，于是「从顶部往下长的矩形」
  在视觉上变成「从底部往上长」。
- **不用 id、不失真、圆角不受影响**。

### 技巧 2：`pathLength` + `stroke-dasharray`（★★★ 任意形状的进度）

`pathLength` 可以把任意路径的「逻辑长度」重定义为 100，
然后 `stroke-dasharray="{normalizedPercent} 100"` 就表示「画前 pct% 的线，剩下留空」。

**环形进度**：

```xml
<circle cx="60" cy="60" r="46" fill="none" stroke="{inactiveColor}" stroke-width="10"/>
<circle cx="60" cy="60" r="46" fill="none" stroke="{alertColor}" stroke-width="10"
        pathLength="100" stroke-dasharray="{normalizedPercent} 100"
        transform="rotate(-90 60 60)"/>
```

`rotate(-90 60 60)` 让起点从 3 点钟方向挪到 12 点钟方向。

**直线进度**：

```xml
<line x1="10" y1="30" x2="310" y2="30" stroke="{inactiveColor}" stroke-width="16" stroke-linecap="round"/>
<line x1="10" y1="30" x2="310" y2="30" stroke="{alertColor}"     stroke-width="16" stroke-linecap="round"
      pathLength="100" stroke-dasharray="{normalizedPercent} 100"/>
```

**任意弧线**（270° 表盘弧）：

```xml
<path d="M20 100 A46 46 0 1 1 100 100" fill="none" stroke="{inactiveColor}" stroke-width="6"/>
<path d="M20 100 A46 46 0 1 1 100 100" fill="none" stroke="{alertColor}" stroke-width="6"
      pathLength="100" stroke-dasharray="{normalizedPercent} 100"/>
```

⚠ 一个渲染细节：`stroke-linecap="round"` 时，**长度为 0 的 dash 会被画成一个圆点**。
因此 `pct = 0` 时进度条不是「没有」，而是一个小圆点。
在意的话用 `stroke-linecap="butt"`（默认值）。

### 技巧 3：重复 `transform` 实现线性组合

`transform` 列表里的多个变换会依次作用（左乘），因此：

```
transform="rotate(A) rotate(B)"  ≡  rotate(A + B)
transform="rotate(-100) rotate({p}) rotate({p})"  ≡  rotate(2*p - 100)
```

**能表达的数学形式**：`a × pct + b`，其中 `a` 是**正整数**（靠重复次数实现），`b` 是**任意常量**。
（想要 0.5 倍做不到，因为没法除。）

**200° 表盘指针**：

```xml
<g transform="translate(60,60)">
  <g transform="rotate(-100) rotate({normalizedPercent}) rotate({normalizedPercent})">
    <path d="M0 0 L0 -40" stroke="{alertColor}" stroke-width="3" stroke-linecap="round"/>
  </g>
  <circle cx="0" cy="0" r="4" fill="{alertColor}"/>
</g>
```

`pct` 0→100 对应指针 `-100°`→`+100°`，正好 200° 扫过。
系数只能是整数，所以**先设计好量程对应的角度，再反推需要的系数**（2 倍 = 200°、3 倍 = 300°）。

### 技巧 4：嵌套 `scale` 做单位换算

把「0~100」映射到「0~H」。原理：一个高度为 100 的矩形，
先 `scale(1, pct)` 变成 `100 × pct`，再 `scale(1, k)` 变成 `100 × k × pct`，令 `k = H / 10000` 即可。

**通用公式**（底边在 `Ybot`，高度 `H` 的生长矩形）：

```xml
<g transform="translate(0,{Ybot}) scale(1,{H/10000}) scale(1,{normalizedPercent})">
  <rect x="{X}" y="-100" width="{W}" height="100" fill="{activeColor}"/>
</g>
```

例：`Ybot=150`、`H=130` → `scale(1,0.013)`。

⚠ 这个技巧的副作用：**y 方向被缩放，圆角 `rx/ry` 会被拉扁**，描边宽度也会被拉扁（`stroke-width` 同样受缩放影响）。
能用技巧 1 就别用这个。它的价值在于**不需要 id**，且兼容性最好。

### 技巧 5：空字符串 = 天然的条件显示

`{quality}` 在正常情况下是空串，`{unit}`、`{label}` 在没填时也是空串。
于是「有就显示、没有就不显示」不需要任何判断：

```xml
<!-- 只在通信异常时出现的红色角标 -->
<g>
  <circle cx="12" cy="12" r="5" fill="#ef4444"/>
  <text x="12" y="15" font-size="8" text-anchor="middle" fill="#fff">!</text>
  <text x="22" y="15" font-size="9" fill="#ef4444">{quality}</text>
</g>
```

质量正常时，`{quality}` 输出空文本 → 只剩一个孤零零的红点。
要连红点也一起消失，就把红点画成**一段长度由 `{quality}` 决定**的线？不行 —— 长度需要数字。

可行做法是：把红点的 `stroke-dasharray` 交给一个「非空即有色」的量……做不到。
**结论：svg 轨无法做真正的条件渲染，只能做「文本内容有无」级别的隐藏。**
需要整块显隐时，改用技巧 2 的 dasharray：把警示图形画成一条 `pathLength=100` 的粗线，
用 `stroke-dasharray` 控制显示长度 —— 但 dasharray 需要数字，而 `{quality}` 是字符串。

所以真正的整块显隐**做不到**。想要这个能力，请用 builtin 轨（写一个 SFC），或提需求扩展 `SvgBindingContext`
（正确做法是在上下文里加派生字段，比如 `qualityOpacity`，而不是引入表达式引擎）。

### 技巧 6：把「判断」外包给 `{alertColor}`

不要在模板里判断阈值，用颜色来表达状态即可：

```xml
<rect ... fill="{alertColor}"/>
```

后端语义已经固化：**超上限红 `#ef4444` / 低于下限琥珀 `#f59e0b` / 正常 `activeColor`**。
用户只要调阈值，颜色自动跟着变。

需要「正常=绿、预警=黄、报警=红」三段时，可以叠加两层：
底层画 `activeColor`，上层画 `alertColor` 的**边框**（`stroke="{alertColor}" fill="none"`），
这样正常时看不到异常色，异常时外框变色。

---

## 六、安全清洗（写 SVG 前先看这段）

入库前 `SvgSanitizer.Sanitize()`，渲染前 `sanitizeSvg()`，**两道**。
规则（`Server/ScadaServer.Application/Common/SvgSanitizer.cs` + `Client/src/utils/svgTemplate.ts`）：

| 规则 | 后果 |
|---|---|
| 移除 `<script>…</script>`（含自闭合） | 不能写脚本 |
| 移除 `<foreignObject>…</foreignObject>` | 不能内嵌 HTML |
| 移除所有 `on*` 事件属性 | `onclick` / `onload` 一律删掉 |
| `href` / `xlink:href` / `src` 走白名单 | 非白名单 → **值被清空**（不是删属性） |
| CSS `url(...)` 走白名单 | 非白名单 → 替换成 `url()` |
| 超过 256KB | 截断（前端同样 256KB，管理页会先拦一次） |

**URL 白名单（三项）**：

```
#锚点              → #myGradient
data:image/…       → data:image/png;base64,iVBORw0…
/开头的站内相对路径 → /uploads/xxx.png
```

⚠ **不放行 `http://` 和 `https://`**。
想在 SVG 里放图片，只能：① base64 内嵌（注意 256KB 总长限制）；② 上传到本系统后用 `/` 开头的相对路径。

**已知边界（登记不修）**：`<animate attributeName="href">` 可被引到外部地址，风险低但需要留意。
`<animate>` 本身是允许的，可以做流向虚线动画（见 [05 示例 F](./05-示例组件库.md#f-my-pipe-flow-h--流向管道)）。

---

## 七、预览与真实运行的差异

管理页的「实时预览」用的是**固定示例上下文**（`WidgetTemplateManagementView.vue` → `SVG_PREVIEW_CTX`）：

```ts
{ value: 42.5, numValue: 42.5, boolValue: true, normalizedPercent: 55,
  state: '开启', unit: '℃', label: '示例组件',
  activeColor: '#10b981', inactiveColor: '#94a3b8', alertColor: '#ef4444',
  thresholdMin: 10, thresholdMax: 90, fontSize: 12, quality: 'Good' }
```

因此有 **三处预览 ≠ 运行**：

| 项 | 预览 | 真实运行 |
|---|---|---|
| `{quality}` | `'Good'` ← **会显示出来** | 质量正常时为 **空串** |
| `{alertColor}` | 固定 `#ef4444` | 由阈值动态决定，正常时是 `activeColor` |
| `{value}` / `{normalizedPercent}` | 固定 42.5 / 55 | 实时变化 |

**典型事故**：模板里写了 `<text>质量：{quality}</text>`，
预览里看到「质量：Good」以为没问题，上线后变成「质量：」。
**用 `{quality}` 时请以「空串是常态」来设计。**

---

## 八、多实例 id 冲突（重要）

svg 轨是通过 `v-html` 把源码**内联**进 DOM 的。
如果模板里用了 `id`（渐变、滤镜、clipPath、mask 都可能），同一画面放两个实例就会出现**重复的 id**。

浏览器对重复 id 的处理是「取第一个」，于是：

```
实例 A 的 <linearGradient id="grad">   ← 页面上第一个
实例 B 的 url(#grad)                    ← 解析到 A 的渐变
```

后果：
- 若两个实例参数相同，看起来正常（因为指向的渐变定义一样）；
- 一旦颜色不同（比如 `{activeColor}` 参与渐变），**两个实例会显示成同一个颜色**。

**规避建议（按优先级）**：

1. **不用 id** —— 需要渐变时用多个实色矩形叠加近似（见 [05 示例 A](./05-示例组件库.md#a-my-tank-level--竖式液位罐)）；
2. 用**极不可能撞名**的 id（如 `my-tank-level-grad-2026`），并在文档里注明「同页建议只放一个」；
3. 需要真正的多实例渐变时，走 builtin 轨写 SFC（可在 `setup` 里用 `useId()` 生成唯一 id）。

---

## 九、性能

每次绑定值变化，`SvgTemplateWidget` 的 `computed` 都会重跑：

```
sanitizeSvg(源码)   ← 4 次正则 replace + 2 次带回调的 replace
bindSvgTemplate()   ← 1 次全局正则 + N 次回调
v-html 重新解析整棵子树
```

参考量级：

| 场景 | 影响 |
|---|---|
| 源码 2KB、同页 20 个实例、1s 刷新 | 完全无感 |
| 源码 20KB、同页 50 个实例、200ms 刷新 | 明显掉帧 |
| 源码 100KB+ | 别这么干 |

优化建议：
- 源码精简，去掉编辑器导出的元数据、注释、无用的 `<defs>`；
- 数值精度适当（坐标保留 1~2 位小数）；
- 高频变量（100ms 级）不要配复杂 SVG；
- 纯装饰部分（外壳、刻度）尽量用少量路径表达。

---

## 十、速查卡

```xml
<!-- 数值文本 -->
<text>{value}{unit}</text>          <!-- 原始值，未格式化 -->
<text>{state}</text>                <!-- 开启 / 关闭 -->
<text>{label}</text>                <!-- 组件标签 -->

<!-- 颜色 -->
fill="{activeColor}"                <!-- 运行色 -->
fill="{inactiveColor}"              <!-- 停止色 -->
fill="{alertColor}"                 <!-- 超上限红 / 低于下限琥珀 / 否则运行色 -->

<!-- 尺寸 -->
height="{normalizedPercent}%"       <!-- 配合嵌套 <svg> 视口，最推荐 -->
pathLength="100" stroke-dasharray="{normalizedPercent} 100"   <!-- 任意形状进度 -->
transform="translate(0,150) scale(1,0.013) scale(1,{normalizedPercent})"  <!-- 单位换算 -->

<!-- 文本属性 -->
font-size="{fontSize}"              <!-- 字号 -->

<!-- 条件显示（只有这一种） -->
<text fill="#ef4444">{quality}</text>   <!-- 正常时为空，异常时显示 Bad / … -->

<!-- 绝对不要写 -->
y="{100 - normalizedPercent}"       <!-- ✗ 不替换 -->
fill="{boolValue}"                  <!-- ✗ 布尔不能当颜色 -->
opacity="{normalizedPercent}"       <!-- ✗ 0~100 会被当 0~1 截断 -->
y="{thresholdMax}"                  <!-- ✗ null 时残留字面量，务必给数字默认值 -->
fill="{qualityBad}"                 <!-- ✗ 不在上下文中 -->
```
