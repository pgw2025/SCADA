# 组件模板使用指南

> 面向**模板作者**（不是前端开发者）：如何在不改动一行代码的前提下，往组态组件库里加新图元。
> 本指南基于当前代码实证编写（2026-09），所有结论都注明了源码位置。
>
> 姊妹文档：`../widget-template/`（01~06）是 **设计与实施文档**，讲这套机制是怎么建起来的；
> 本目录是 **使用文档**，讲你今天怎么用它做东西。

---

## 一、三句话理解组件模板

1. 组件库里看到的每一项（锅炉、水泵、表盘……）都是数据库 `HmiWidgetTemplates` 表里的一行记录。
2. 每一行记录描述的是「**怎么画**」+「**默认长什么样**」+「**属性面板上能改什么**」三件事。
3. 你往表里插一条记录，组件库里就多一个图元 —— 不需要重新打包前端。

---

## 二、文档地图

| 文档 | 什么时候读 | 你会得到什么 |
|---|---|---|
| [01-核心概念与数据模型](./01-核心概念与数据模型.md) | 第一次上手 | 两条渲染轨、字段字典、导入导出、快照语义、两级匹配 |
| [02-SVG占位符完全手册](./02-SVG占位符完全手册.md) | 要写 SVG 轨模板 | 14 个有效占位符逐条深挖、替换算法、6 种「无表达式」绕行技巧、清洗白名单 |
| [03-默认属性JSON编写指南](./03-默认属性JSON编写指南.md) | 写 `defaultPropsJson` | 字段字典、三级兜底链、快照语义的真实边界、类型陷阱 |
| [04-属性Schema编写指南](./04-属性Schema编写指南.md) | 写 `propSchemaJson` | 条目结构、5 种控件、回退链、与 defaultProps 的对齐原则 |
| [05-示例组件库](./05-示例组件库.md) | 抄作业 | 11 个可直接导入的完整模板 + 逐行讲解 |
| [06-排障与最佳实践](./06-排障与最佳实践.md) | 做出来不对劲时 | 校验清单、20 个高频坑、性能与安全边界 |

`examples/` 目录里放着可直接导入的 `.widget.json` 文件，和 05 文档一一对应。

---

## 三、30 秒上手：新建第一个模板

1. 打开 **组件模板管理** 页（侧边栏 → `/widget-templates`，需要 Admin 角色）。
2. 点 **新建**，填：

| 字段 | 填什么 |
|---|---|
| 模板键 TemplateKey | `my-first-tank`（全局唯一，只允许字母数字连字符，≤64） |
| 渲染类型 RenderType | **必须和模板键一模一样**（SVG 轨的硬约束） |
| 名称 / 分类 / 描述 | 随便填，`分类` 只能选 `equipment`/`sensors`/`structures`/`headers` |
| 渲染轨 RenderKind | 选 `svg` |
| 默认尺寸 | 例如 `120` × `160` |
| SVG 模板源码 | 见下方 |
| 默认属性 JSON | 切到 svg 轨后会**自动预填**，直接保留 |
| 属性 Schema JSON | `[]` 先留空（后果见 → [04](./04-属性Schema编写指南.md#回退链)） |

3. 把下面这段粘进 **SVG 模板源码** 框：

```xml
<svg width="100%" height="100%" viewBox="0 0 120 160" xmlns="http://www.w3.org/2000/svg">
  <rect x="10" y="10" width="100" height="140" rx="12"
        fill="#1e293b" stroke="{inactiveColor}" stroke-width="3"/>
  <svg x="20" y="20" width="80" height="120" viewBox="0 0 80 120" preserveAspectRatio="none">
    <g transform="translate(0,120) scale(1,-1)">
      <rect x="0" y="0" width="80" height="{normalizedPercent}%" fill="{activeColor}"/>
    </g>
  </svg>
  <text x="60" y="150" font-size="{fontSize}" text-anchor="middle"
        fill="#e2e8f0">{value}{unit}</text>
</svg>
```

4. 点 **保存** → 打开任意组态画面 → 组件库里已经出现「my-first-tank」，拖到画布上，给它绑一个变量即可。

> 这个例子里用了「嵌套 `<svg>` + 百分比」技巧来实现液位从底部往上长。
> 为什么不能写 `{120 - 1.2 * normalizedPercent}`？因为**占位符是纯字符串替换，不支持表达式**。
> 详见 [02 的绕行技巧一节](./02-SVG占位符完全手册.md#五无表达式如何表达数学关系)。

---

## 四、两条渲染轨怎么选

| | `builtin` 轨 | `svg` 轨 |
|---|---|---|
| 画什么 | 复用前端已有的 23 个 SFC 图元 | 你自己的 SVG 源码 |
| 需要写代码吗 | 不需要 | 不需要 |
| 能做什么 | 复用全部内置行为（动画、多变量、趋势、跳转……） | 纯展示：数值/颜色/长度/文本 |
| 能点击写值吗 | ✅（button / rounded-btn / switch / valve） | ❌ 渲染容器是 `pointer-events-none` |
| 改模板后 | SVG 源码改了**实时生效** | 同左 |
| 典型用途 | **预设变体**：把调好参数的罐体/表盘固化成一个新图元 | 内置图元覆盖不到的专属外观 |

**一个容易被忽略的强大用法**：builtin 轨的 `RenderType` 可以填任意内置渲染器名（`tank`、`gauge-dial`、`var-display`……），
于是「换个默认参数」就变成了一个新图元。例如做一个 `preset-gauge-temp`，
`renderType='gauge-dial'` + `maxValue=150` + `unit='℃'` + `thresholdMax=120`，
组件库里就多了一个「温度表盘」，落布即可用，连 schema 都能白嫖内置的。
完整示例见 [05 §B](./05-示例组件库.md#b-builtin-轨预设变体模板)。

---

## 五、改动前必须知道的 5 条铁律

1. **快照语义**：模板落布时，`defaultPropsJson` 会被**复制**到组件的 `PropsJson` 上。之后改模板的默认值，
   只有「组件 props 里不存在的键」才会被新默认值影响。详见 [01 §5](./01-核心概念与数据模型.md#5-快照语义的真实边界)。
2. **SVG 源码不快照**：改了源码，画布上所有该类型组件下一帧就变。改坏了也是立刻全坏 —— 先在预览框里确认。
3. **SVG 轨的 `RenderType` 必须等于 `TemplateKey`**，前后端双重校验，不一致直接报错。
4. **属性值占位符遇到 `null` 会残留字面量**。`{thresholdMax}` 在阈值为 null 时会渲染成字符串 `{thresholdMax}`，
   直接把一个合法属性变成非法值。所以 SVG 模板的 `defaultPropsJson` 里阈值一定要给**数字**。
5. **自定义 SVG 模板如果不写 `propSchemaJson`，属性面板会是空的**（空数组回退到内置 schema，自定义 key 命中不了）。

---

## 六、权限与接口速查

| 操作 | 权限 | 接口 |
|---|---|---|
| 查看模板列表 | 已登录 | `GET /api/HmiWidgetTemplate` |
| 新建 / 修改 / 删除 | `RequireAdmin` | `POST` / `PUT` / `DELETE /api/HmiWidgetTemplate/{id}` |
| 导入（单条/批量） | `RequireAdmin` | `POST /api/HmiWidgetTemplate/import` · `/import-bundle` |
| 导出（单条/批量） | `RequireAdmin` | `GET /api/HmiWidgetTemplate/{id}/export` · `POST /api/HmiWidgetTemplate/export-bundle` |

系统内置 24 条种子模板 `IsSystem=true`，**不可删除**（后端显式抛「系统内置模板不可删除」），但可以改名改参数改排序。

---

## 七、本指南涉及的源码位置

| 关注点 | 文件 |
|---|---|
| 模板实体与字段约束 | `Server/ScadaServer.Domain/Entities/HmiWidgetTemplate.cs` |
| DTO 与导入导出格式 | `Server/ScadaServer.Application/DTOs/HmiWidgetTemplateDto.cs` |
| 白名单校验 / 键唯一 / 导入冲突策略 | `Server/ScadaServer.Application/Services/HmiWidgetTemplateAppService.cs` |
| SVG 入库清洗 | `Server/ScadaServer.Application/Common/SvgSanitizer.cs` |
| 占位符替换与前端清洗 | `Client/src/utils/svgTemplate.ts` |
| 占位符上下文组装 | `Client/src/components/widgets/SvgTemplateWidget.vue` |
| SVG 轨渲染器 | 同上 |
| 通用属性三级兜底 | `Client/src/components/widgets/useWidgetBase.ts` |
| 内置 schema 定义 | `Client/src/propSchemas.ts` |
| schema 表单渲染 | `Client/src/components/inspector/PropSchemaForm.vue` |
| 模板运行时源 / 两级匹配 | `Client/src/widgetTemplates.ts` |
| 内置渲染器映射表 | `Client/src/builtinRenderers.ts` |
| 管理页（占位符速查表、实时预览、预填逻辑） | `Client/src/components/WidgetTemplateManagementView.vue` |
