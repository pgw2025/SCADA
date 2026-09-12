# 03 · 默认属性 JSON 编写指南

> 本文只解决一个问题：`DefaultPropsJson` 这个文本框里**填什么、怎么填、为什么这么填**。
> 它直接决定：组件落布时长什么样、运行时兜底值是什么、阈值/量程从哪来。
>
> 源码依据：
> - 兜底计算：`Client/src/components/widgets/useWidgetBase.ts`（`propOr` / `defDefaults`）
> - 落布快照：`Client/src/components/WidgetLibrary.vue`
> - 预填值：`Client/src/components/WidgetTemplateManagementView.vue`（`DEFAULT_SVG_PROPS`）
> - 字段真相源：`Client/src/builtinSeeds.ts`（`baseProps`）

---

## 1. 它是什么 / 不是什么

| 它 **是** | 它 **不是** |
|---|---|
| 一个**合法 JSON 对象字符串**（`{"key":"value"}`），不是 JSON 数组 | 不是属性面板的表单定义（那是 `PropSchemaJson`） |
| 落布时**整段深拷贝**到每个组件实例的 `PropsJson` | 不是运行期的「全局变量」，改了只对「新键」生效（见 §4 快照边界） |
| 占位符 / 派生计算的**输入源**之一 | 不是 `alertColor` 这种派生值的存放处（见 §3 注） |
| 唯一真相源：**你没填的属性 → 这里兜底** | 不是取色器、不是表单——它只是数据 |

后端只校验「是不是合法 JSON」（`JsonDocument.Parse`），**不校验结构**。
结构写错不会在保存时报错，只在**前端渲染时**才暴露（比如占位符拿到 `undefined` 残留字面量）。

---

## 2. 三级兜底链（全篇最重要的图）

渲染时每个属性值按以下顺序解析（`useWidgetBase.propOr`）：

```
┌─① 组件实例 PropsJson[键]        ← 用户/落布快照的值（最优先）
│       存在且 非 undefined/null/'' ？
│       是 ──→ 直接用它
│       否 ↓
├─② 模板当前 DefaultPropsJson[键]  ← 本指南在写的东西（实时读取）
│       存在且 非 undefined/null/'' ？
│       是 ──→ 用它
│       否 ↓
└─③ 代码硬兜底 / Schema default     ← 写死在 useWidgetBase / propSchemas 里
```

**关键事实**：`②` 读的是**当前模板**的 `DefaultPropsJson`，而且是 `computed`（每次重算）。
所以「改模板默认值」对存量组件有没有影响，取决于**该键在组件 PropsJson 里是否存在**——
这正是 §4 快照边界的核心，也是最容易翻车的地方。

---

## 3. 可用字段字典（能写进 defaultPropsJson 的键）

不是随便写都有效。**只有被占位符或 `useWidgetBase` 消费的键才有意义**。
下面按「谁消费」分组：

### 3.1 SVG 占位符直接读这些键（svg 轨必看）

| 键 | 类型 | 被谁读 | 不写时的兜底 | 说明 |
|---|---|---|---|---|
| `activeColor` | string(hex) | `{activeColor}` | `#10b981` | 运行色 |
| `inactiveColor` | string(hex) | `{inactiveColor}` | `#94a3b8` | 停止/底色 |
| `fontSize` | number | `{fontSize}` | `12` | 字号 |
| `minValue` | number | `{normalizedPercent}` 计算 | `0` | 量程下限 |
| `maxValue` | number | `{normalizedPercent}` 计算 | `100`（`|| 100` 防零） | 量程上限 |
| `unit` | string | `{unit}` | `''` | 单位，拼在数值后 |
| `thresholdMin` | number(`null` 可) | `{thresholdMin}` + `alertColor` 计算 | `null` | 低限预警 |
| `thresholdMax` | number(`null` 可) | `{thresholdMax}` + `alertColor` 计算 | `null` | 高限报警 |
| `onText` | string | `{state}` | `'开启'`（**仅快照**） | 开状态文案 |
| `offText` | string | `{state}` | `'关闭'`（**仅快照**） | 关状态文案 |

> ⚠ **`alertColor` 不能写进 defaultPropsJson**。它是 `useWidgetBase` 派生出来的
> （超上限红 / 低于下限琥珀 / 否则 activeColor），你只能写 `thresholdMin/Max` 去**间接影响**它。
> 同理 `numValue` / `boolValue` / `normalizedPercent` / `value` 都是运行期计算的，不是属性。

### 3.2 builtin 轨额外消费的键（复用 SFC 时）

如果你用 builtin 轨（`RenderType='tank'` 之类），SFC 还会读更多键，
完整清单见 `Client/src/builtinSeeds.ts` 的 `baseProps`：

```
activeColor, inactiveColor, maxValue, minValue, unit, showValue,
showLabel, fontSize, bold, align, thresholdMax, thresholdMin
+ title-header 专属：headerStyle / headerTitle / headerSubtitle / headerGlowColor …
```

这些键**同时**会进 `defaultPropsJson` 快照，所以你的预设变体模板只要填它们即可。

### 3.3 写了也没用的键

`alertColor`、`numValue`、`boolValue`、`normalizedPercent`、`value`、`quality`、
`width`、`height` —— 这些是运行期计算或只读的，写进 JSON 会被 `propOr` 当作「未知键」忽略。

---

## 4. 快照语义的真实边界（写默认值前必读）

落布那一刻，`defaultPropsJson` 被**整段深拷贝**进组件 `PropsJson`（见 01 §4/§5）。
这意味着：

| 情形 | 结果 |
|---|---|
| 你**新增**一个键（如 `maxValue`） | 存量组件会自动拿到这个默认值 ✅ |
| 你**修改**一个已存在键的默认值 | 存量组件**不受影响**（它快照里已有该键，走分支①）❌ |
| 用户在面板上**清空**了某键（空串/null） | 判空后掉到分支②，**立刻**用你的新默认 ✅ |
| `0` / `false` | 是**有效值**，不会被判空 ✅ |
| `''` / `undefined` / `null` | 被判空，掉到下层 ⚠ |

**实践结论**：
1. 给模板加新属性是安全的，存量组件自动补全，不会显示空白。
2. 想让老组件跟着改默认，**只能手工改组件**，或等「应用默认值」功能（当前未实现）。
3. 判空规则是 `undefined / null / ''`，但 `0` 和 `false` 有效——
   所以 `minValue: 0`、`fontSize: 12` 都正常，`unit: ''` 也会绕一圈到 `''`（看不出区别但语义上走了一圈）。

---

## 5. 类型陷阱（按踩坑频率排序）

### 陷阱 1：数字要写数字，不要写字符串

```jsonc
// ✗ 错误：React/Vue 拿到的会是字符串 "90"，做数学时可能出问题
{ "thresholdMax": "90", "maxValue": "100" }

// ✓ 正确：纯数字
{ "thresholdMax": 90, "maxValue": 100 }
```

`normalizedPercent` 计算里 `Number(propOr('maxValue',100)) || 100` 会帮你转，
但 `thresholdMax` 的 `Number(v)` 也转——**只是保险**。规规矩矩写数字最稳。

### 陷阱 2：阈值是 `null`，但 SVG 占位符遇到 `null` 会**残留字面量**

```jsonc
// ✗ 危险：thresholdMax 为 null 时，<rect y="{thresholdMax}"> 渲染成 <rect y="{thresholdMax}">（非法）
{ "thresholdMax": null }

// ✓ 安全：给个数字
{ "thresholdMax": 90, "thresholdMin": 10 }
```

`alertColor` 计算时，`null` 表示「不设阈值」是 OK 的（只是不告警），
**但**一旦你把 `{thresholdMax}` 放进 SVG 属性值里，null 就会残留成字面量 `{thresholdMax}`，
让整段属性非法。结论：**SVG 模板的 `defaultPropsJson` 里阈值务必给数字。**

> 另一个暗坑：`Number('')` 是 `0`，`isNaN(0)` 是 `false`。
> 阈值的 number 控件**不**是 `nullable` 时，清空会写入 `0` 而非「不设」。
> 想真正清空阈值，schema 里该键必须 `nullable: true`（见 04 §3）。

### 陷阱 3：颜色必须是 `#rrggbb` 十六进制

```jsonc
// ✗ 取色器/解析异常：red / rgb(255,0,0) / #f00 都可能让面板显示错
{ "activeColor": "red" }

// ✓ 标准 hex
{ "activeColor": "#3b82f6" }
```

属性面板的取色器（`<input type="color">`）只认 hex。SVG 本身能渲染 `red`，
但用户一进面板就会看到错的颜色框。

### 陷阱 4：`onText` / `offText` 不走三级兜底

它们**只读组件 PropsJson**（不进 `propOr`）。所以：
- 想改默认文案，只能在 `defaultPropsJson` 里写 → 落布时快照进去（之后固化）；
- 或在 `propSchemaJson` 里暴露出来让用户填（推荐）。
- 存量组件的 `onText` 一旦落布就固化，改模板默认值对它**无效**。

### 陷阱 5：顶层必须是对象，不能是数组/裸值

```jsonc
// ✗ 整体必须是个 object 字符串
"[{\"activeColor\":\"#3b82f6\"}]"

// ✓
"{\"activeColor\":\"#3b82f6\"}"
```

注意：文本框里存的是**字符串化的 JSON**。换行和缩进无害，但引号要转义正确。
管理页「校验」按钮会跑 `JSON.parse`，通不过会标红。

---

## 6. 管理页自动预填的 `DEFAULT_SVG_PROPS`

切到 svg 轨、且 `defaultPropsJson` 为空时，管理页 `onRenderKindChange` 会**自动注入**：

```json
{
  "activeColor": "#3b82f6",
  "inactiveColor": "#94a3b8",
  "minValue": 0,
  "maxValue": 100,
  "unit": "℃",
  "fontSize": 12,
  "thresholdMin": 10,
  "thresholdMax": 90,
  "onText": "开启",
  "offText": "关闭"
}
```

> 注意预填给的是 `activeColor: '#3b82f6'`（蓝），而 `useWidgetBase` 硬兜底给的是 `#10b981`（绿）。
> **两者不一致**，以你模板 `defaultPropsJson` 里写的为准。预填是为了让你少写几行，不是圣旨。

---

## 7. 一个完整、可直接用的默认属性

对大多数 svg 轨模板，下面这段是最稳妥的起点（改改阈值和量程即可）：

```json
{
  "activeColor": "#3b82f6",
  "inactiveColor": "#94a3b8",
  "minValue": 0,
  "maxValue": 100,
  "unit": "",
  "fontSize": 12,
  "thresholdMin": 10,
  "thresholdMax": 90,
  "onText": "开启",
  "offText": "关闭"
}
```

如果模板**不关心阈值**（纯装饰/纯显示），把 `thresholdMin/Max` 删掉也无妨——
只是别在 SVG 里引用 `{thresholdMax}` 这种占位符（否则残留字面量）。

---

## 8. 下一步

- 要决定「属性面板上能改哪些键」 → [04-属性Schema编写指南](./04-属性Schema编写指南.md)
- 要抄完整模板 → [05-示例组件库](./05-示例组件库.md)（每个示例都带 `defaultPropsJson`）
