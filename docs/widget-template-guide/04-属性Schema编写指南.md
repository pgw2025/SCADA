# 04 · 属性 Schema JSON 编写指南

> 本文解决：`PropSchemaJson` 里**写什么结构、每个控件长什么样、回退怎么走、为什么一定要和 defaultPropsJson 对齐**。
>
> 源码依据：
> - 条目结构：`Client/src/propSchemas.ts`（`PropSchemaItem` 接口）
> - 渲染器：`Client/src/components/inspector/PropSchemaForm.vue`
> - 取值回退：`PropSchemaForm.displayVal` / `onNumberInput`
> - schema 来源解析：`Client/src/widgetTemplates.ts`（`resolvePropSchema`）
> - InspectorPanel 调用：`Client/src/components/InspectorPanel.vue`（line 38 / 45）

---

## 1. 它是什么

`PropSchemaJson` 是一个 **JSON 数组字符串**，数组里每个元素描述属性面板上**一项可编辑字段**。
前端 `PropSchemaForm.vue` 按它渲染成 5 种控件之一，用户改的值写回组件 `PropsJson[键]`。

```jsonc
// 最简形态：空数组 → 面板无自定义项
"[]"

// 典型形态
"[
  { \"key\": \"activeColor\", \"label\": \"液体颜色\", \"type\": \"color\" },
  { \"key\": \"thresholdMax\", \"label\": \"高限报警\", \"type\": \"number\", \"nullable\": true }
]"
```

后端**只校验合法 JSON**，不校验结构。结构错误只有渲染时才暴露（比如 `type` 写错会落到最后的 `text` 分支）。

---

## 2. 条目结构（`PropSchemaItem`）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `key` | string | ✅ | **props 键名**，必须与 `defaultPropsJson` / 占位符的键**完全一致** |
| `label` | string | ✅ | 面板上显示的标签 |
| `type` | `'text'\|'number'\|'color'\|'select'\|'switch'` | ✅ | 控件类型（见 §3） |
| `default` | any | — | **展示兜底**：props 与 defDefaults 都缺省时显示什么 |
| `min` / `max` / `step` | number | — | 仅 `number` 生效（UI 限制，不强制校验输入） |
| `nullable` | boolean | — | 仅 `number`：勾选后可清空为 `null`（**阈值必开**） |
| `options` | `{value,label}[]` | — | 仅 `select`：候选项（`value` 可是 `string\|number\|boolean`） |
| `placeholder` | string | — | 输入框占位提示 |
| `help` | string | — | 控件下方的小字说明（switch 显示在右侧） |

---

## 3. 五种控件类型

| `type` | 渲染成 | 值类型 | 典型用途 |
|---|---|---|---|
| `text` | 文本框 | string | 单位、状态文案、标题文字 |
| `number` | 数字框（`min/max/step`） | number / null | 量程、阈值、字号、圆角 |
| `color` | 取色器 + hex 文本框 | string(hex) | 各种颜色 |
| `select` | 下拉框 | 选项 `value` 的原类型 | 对齐方式、边框线型、时间格式 |
| `switch` | 整行开关（checkbox） | boolean | 是否显示标签、是否加粗 |

### `number` 的两个关键行为（`PropSchemaForm`）

1. **回显**：`null` / 空 → 显示空框（`numDisplay`）；否则 `String(v)`。
2. **输入**：
   - `nullable: true` 且清空 → 写入真正的 `null`（这是**唯一**能正确清空阈值的方式）；
   - 否则 `parseFloat`，非法 → 回退到 `item.default ?? 0`。

### `select` 的类型保留

`option.value` 是 `number`（如 `borderWidth: 1.5`）时，提交会保持 number 类型，
不会退化成字符串。`onSelect` 按 `String(o.value)` 匹配后回传 `o.value` 原值。

### `color` 的类型保留

提交的是 `<input type="color">` 的 hex 字符串，**不会**转成别的格式。

---

## 4. 回退链（面板显示的值从哪来）

`PropSchemaForm.displayVal(item)`：

```
① props.props[key]         ← 组件实例当前值（含 0 / false / '' 都算「有值」）
    非 undefined/null/'' ？ 是 → 用它
    否 ↓
② defaults[key]           ← defDefaults = 模板当前 defaultPropsJson（resolvePropSchema 同源）
    非 undefined/null/'' ？ 是 → 用它
    否 ↓
③ item.default            ← 本 schema 条目的 default 字段
    否则 → number ? 0 : ''
```

> 注意 `defaults` 来自 `InspectorPanel` line 38：
> `getWidgetDef(type)?.defaultProps() ?? {}`，
> 而 `defaultProps()` 正是 `safeParse(defaultPropsJson, {})`（widgetTemplates.ts line 77）。
> **所以 `defaultPropsJson` 和 `propSchema` 共享同一份默认值真相源。**

**推论**：你给 `defaultPropsJson` 写了 `maxValue: 150`，但 `propSchemaJson` 里**没列** `maxValue` 这项，
面板上就不会出现「量程上限」输入框——用户改不了它，但默认值 150 仍在生效。
要让用户能改，必须在 schema 里也列出来（见 §5）。

---

## 5. 与 defaultPropsJson 的对齐原则（最重要的一条经验）

> **`propSchemaJson` 里的每个 `key`，都应该在 `defaultPropsJson` 里有对应值（或确实不需要默认值）。**
> 反过来，`defaultPropsJson` 里有、但 schema 没列的键，用户**改不了**，只能走默认值。

对照关系：

| defaultPropsJson 有 | propSchemaJson 列了 | 用户能改？ | 显示默认值？ |
|---|---|---|---|
| ✅ | ✅ | ✅ | ✅（来自①或②） |
| ✅ | ❌ | ❌ | 生效但不可见 |
| ❌ | ✅（带 `default`） | ✅ | ✅（来自③ `item.default`） |
| ❌ | ✅（无 `default`） | ✅ | 显示空/0（③兜底 `0/''`） |

**推荐做法**：先写 `defaultPropsJson`（决定默认值与占位符输入），再写 `propSchemaJson`
（把要暴露给用户的键一一列出来，`default` 字段可省略——因为会从 `defaultPropsJson` 读）。

---

## 6. 空数组 `[]` 的回退行为（决定你偷不偷懒）

`resolvePropSchema(t)`（`widgetTemplates.ts` line 60）：

```ts
const fromDb = safeParse(t.propSchemaJson, null);
if (fromDb && fromDb.length > 0) return fromDb;     // ① 你有写 → 用你的
return BUILTIN_SCHEMAS[t.templateKey] ?? BUILTIN_SCHEMAS[t.renderType] ?? [];  // ② 兜底
```

| 你的模板 | `propSchemaJson` | 结果 |
|---|---|---|
| **builtin 轨**（`RenderType='tank'`） | `[]` | 回退到 `BUILTIN_SCHEMAS['tank']` → **白嫖内置面板** ✅ |
| **svg 轨**（自定义 key） | `[]` | `BUILTIN_SCHEMAS['my-tank']` 不存在 → `[]` → **面板空白** ❌ |
| 任意 | 写了条目 | 用你写的 |

**所以**：builtin 轨预设变体可以省事留 `[]`；**svg 轨自定义模板必须写 schema**，否则用户看不到任何可调项。

---

## 7. 逐控件写法示范

### 7.1 color

```jsonc
{ "key": "activeColor", "label": "液体颜色", "type": "color", "default": "#3b82f6" }
```
- 不写 `default` 时取色器兜底 `#3b82f6`（见 PropSchemaForm line 73）。

### 7.2 number（普通）

```jsonc
{ "key": "fontSize", "label": "字体大小", "type": "number", "min": 8, "max": 72, "step": 1 }
```

### 7.3 number（可清空阈值 —— 必须 `nullable`）

```jsonc
{ "key": "thresholdMax", "label": "高限报警值", "type": "number",
  "nullable": true, "placeholder": "默认不设", "help": "留空则不设高限" }
```
> 不写 `nullable`，清空会写入 `0` 而非 `null`（陷阱见 03 §5-2）。

### 7.4 select

```jsonc
{
  "key": "align", "label": "对齐方式", "type": "select",
  "options": [
    { "value": "left",   "label": "靠左对齐" },
    { "value": "center", "label": "居中对齐" },
    { "value": "right",  "label": "靠右对齐" }
  ],
  "default": "center"
}
```
> `value` 是字符串。若要数值选项（如边框粗细 `1.5`），`value` 写数字，提交保持 number。

### 7.5 switch

```jsonc
{ "key": "showLabel", "label": "显示外框标签名称", "type": "switch", "default": false }
```
> `help` 会显示在开关右侧，适合写注意事项。

### 7.6 text

```jsonc
{ "key": "unit", "label": "单位", "type": "text", "placeholder": "e.g. MPa" }
```

---

## 8. 一套「官方风格」的通用 schema（可直接套用）

参考 `propSchemas.ts` 的 `base` / `range` / `threshold` builder，一套 svg 轨模板常用组合：

```json
[
  { "key": "activeColor",  "label": "运行色",   "type": "color" },
  { "key": "inactiveColor","label": "底色",     "type": "color" },
  { "key": "minValue",     "label": "量程下限", "type": "number", "default": 0 },
  { "key": "maxValue",     "label": "量程上限", "type": "number", "default": 100 },
  { "key": "unit",         "label": "单位",     "type": "text", "placeholder": "e.g. MPa" },
  { "key": "thresholdMax", "label": "高限报警", "type": "number", "nullable": true },
  { "key": "thresholdMin", "label": "低限预警", "type": "number", "nullable": true },
  { "key": "fontSize",     "label": "字号",     "type": "number", "min": 8, "max": 72 },
  { "key": "onText",       "label": "开启文本", "type": "text", "default": "开启" },
  { "key": "offText",      "label": "关闭文本", "type": "text", "default": "关闭" }
]
```

> 键名与 03 §3 的字段字典一一对应，复制即用。
> 不需要的项（如纯装饰模板不关心阈值）直接删掉对应条目即可。

---

## 9. 常见错误

| 错误写法 | 后果 | 修正 |
|---|---|---|
| `key` 写成 `ActiveColor`（大小写） | 与占位符 `{activeColor}` 不匹配，面板改了不生效 | 与 defaultProps / 占位符**完全一致** |
| `type: "checkbox"` | 不在 5 种内 → 落到 `text` 分支，渲染成文本框 | 用 `switch` |
| 阈值 number 没 `nullable` | 用户清空 → 写入 `0`，阈值永远生效 | 加 `"nullable": true` |
| color 写了 `default: "red"` | 取色器显示异常 | 用 hex `#3b82f6` |
| select `value` 是数字却写成 `"1.5"` | 提交退化成字符串，SFC 比较失败 | `value` 用 `1.5`（无引号） |
| 整段是对象 `{}` 而非数组 `[]` | 解析为「非空但非数组」→ 可能报错或空白 | 顶层必须是 `[...]` |

---

## 10. 下一步

- 抄完整模板（含 defaultProps + schema + svg） → [05-示例组件库](./05-示例组件库.md)
- 做出来不对劲 → [06-排障与最佳实践](./06-排障与最佳实践.md)
