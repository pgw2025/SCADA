# 01 · API 参考：一个脚本里到底能调用多少函数

> 本文回答一个问题：**在系统脚本的代码框里，我能用哪些函数？各有什么用？**
> 结论先给：**宿主注入 4 个 + 引擎约定钩子 2 个 + Jint 3.1.3 内置 ECMAScript 标准库约 300 个可调用成员**。
> 除此之外的一切（`console`、`fetch`、`setTimeout`、`require`……）**全部不存在**。

---

## 一、结论速览

```
系统脚本可用函数全景
│
├─ 第 1 层：宿主注入（白名单，仅此 4 个）        ← 与 SCADA 运行时交互的唯一通道
│   ├─ log(...)
│   ├─ read(deviceKey, variableKey)
│   ├─ getQuality(deviceKey, variableKey)
│   └─ write(deviceKey, variableKey, value)
│
├─ 第 2 层：引擎约定钩子（2 个，名字写死不能改）
│   ├─ run()             ← Manual / Periodic / Schedule 触发
│   └─ onChange(ev)      ← OnChange 触发，ev 是事件对象
│
├─ 第 3 层：Jint 3.1.3 内置 ECMAScript 标准库（约 300 个成员）
│   ├─ 全局函数与值              11 + 4
│   ├─ Math 方法 + 常量          35 + 8
│   ├─ JSON                      2
│   ├─ String  静态 + 原型        3 + ~35
│   ├─ Array   静态 + 原型        3 + ~31
│   ├─ Number  常量 + 方法        ~20
│   ├─ Object  静态 + 原型        21 + 11
│   ├─ Date    静态 + 原型        3 + ~40
│   ├─ RegExp / Map / Set / WeakMap / WeakSet   ~60
│   ├─ Promise（6 + 3，但**不可依赖**，见 §3.11）
│   ├─ TypedArray / ArrayBuffer / DataView      ~35
│   ├─ Error 家族                 7
│   └─ Symbol / Reflect / Proxy / globalThis    ~25
│
└─ 第 4 层：不存在的东西（用了就抛 ReferenceError → 计入失败 → 3 次熔断）
    console · setTimeout · setInterval · fetch · XMLHttpRequest
    require · module · process · localStorage · alert · FileSystem · .NET 类型
```

> **关于计数的说明**：第 3 层按 Jint 3.1.3 的 ES2020 覆盖清单统计，含原型方法与静态成员。
> 少数 ES2022+ 才加入的方法（如 `Array.prototype.at`、`String.prototype.at`、`Object.hasOwn`）
> 是否可用取决于 Jint 该版本的实现进度，**建议先在试运行里 `log(typeof [].at)` 确认再使用**。
> 真正决定脚本能力上限的是第 1 层——只有 4 个，务必吃透。

---

## 二、第 1 层：宿主注入的 4 个 API（重点）

这 4 个是**唯一**能与 SCADA 运行时打交道的方式。它们在沙箱创建时由 `ScriptSandbox.RegisterApi()` 注入，
本质上是 .NET 委托（`Action<JsValue[]>` / `Func<...>`），不是 JS 函数，因此：

- **不能被重新赋值**也能工作（严格模式下给已声明的全局赋值会报错，别写 `log = ...`）；
- **没有 `.length` / `.name`** 等 JS 函数属性，别做函数式编程的花活；
- **调用是同步的**，`write()` 会真实阻塞等待驱动返回（有上界）。

### 2.1 `log(...)` —— 输出日志

| 项目 | 说明 |
|---|---|
| 签名 | `log(...args)` |
| 参数 | 任意个数、任意类型的 JS 值 |
| 返回值 | `undefined` |
| 权限 | 无需授权 |
| 用途 | 把内容写入本次执行的输出缓冲，最终进入「执行记录.Output」并实时推送到前端控制台 |

**行为细节**

- 多个参数用**空格**拼接：`log("温度", 25.3, "℃")` → `温度 25.3 ℃`
- `undefined` 会格式化成字符串 `"undefined"`（不是空）
- 每条 `log` 自动追加换行
- 输出会在执行结束后**截断至 8000 字符**落库，超长部分只存在于本次实时推送中

**⚠️ 最大的坑：`log()` 只在钩子内有效**

沙箱构造时会先执行脚本顶层代码，然后调用钩子前**清空输出缓冲**。因此写在 `run()` 外面的 `log()` 会被静默丢弃：

```js
log("这行不会出现在任何地方");   // ❌ 顶层 log，被清空，永远看不到

function run() {
  log("这行正常输出");            // ✅
}
```

**示例**

```js
function run() {
  var t = read("TANK01", "Temp");
  log("温度 =", t);                              // 温度 = 62.5
  log("质量 =", getQuality("TANK01", "Temp"));   // 质量 = Good
  log("快照 =", JSON.stringify({ temp: t }));    // 结构化输出，便于排查
}
```

---

### 2.2 `read(deviceKey, variableKey)` —— 读变量当前值

| 项目 | 说明 |
|---|---|
| 签名 | `read(deviceKey, variableKey)` |
| 参数 | `deviceKey` 设备键（string）；`variableKey` 变量键（string）。**两者大小写敏感**（Ordinal 比较） |
| 返回值 | 变量当前值（number / boolean / string，取决于变量数据类型）；**未授权、设备或变量不存在时返回 `null`** |
| 权限 | **读授权（ScopeRead）**，粒度为设备键 |
| 用途 | 获取运行时内存中的变量当前值（即**工程值**，已过量程换算表达式变换） |

**关键行为**

1. **越权不抛异常**，返回 `null`，并在输出中追加一行：
   ```
   [DENIED] read TANK01.Temp：设备 [TANK01] 不在读授权列表
   ```
   这是"脚本没反应"的头号原因。**空授权 = 拒绝全部**，没有通配、没有默认放行。
2. **不触发采集**：读的是运行时内存里的缓存值，不会去 PLC 上重新读一次。设备离线时读到的可能是最后一次的旧值——所以**读数前先看质量**（`getQuality`）。
3. **返回 `null` 的三种可能**：① 没授权 ② 设备不在运行中 ③ 变量名拼错。**三者对脚本表现完全一致**，必须靠 `getQuality` 与输出日志区分。
4. 数值型变量返回 JS `number`（即 .NET `double`）；数字量返回 `boolean`；字符串型返回 `string`。

**示例**

```js
function run() {
  var t = read("TANK01", "Temp");

  if (t === null) {
    log("读取失败：未授权 / 设备不在运行 / 变量名错误");
    return;                       // 早返回，别拿 null 去四则运算
  }
  log("当前温度 =", t, "，质量 =", getQuality("TANK01", "Temp"));
}
```

---

### 2.3 `getQuality(deviceKey, variableKey)` —— 读变量质量

| 项目 | 说明 |
|---|---|
| 签名 | `getQuality(deviceKey, variableKey)` |
| 参数 | 同 `read` |
| 返回值 | 质量字符串；设备不存在或**未授权**时返回 `"Unknown"` |
| 权限 | **读授权（ScopeRead）**，与 `read` 共用同一份授权列表 |
| 用途 | 判断读到的值是否可信——这是**唯一**能区分"值是 0"和"没读到"的手段 |

**返回值全集**（对应 `VariableQuality` 枚举）

| 值 | 含义 | 典型成因 |
|---|---|---|
| `Good` | 数据有效 | 正常采集 |
| `Bad` | 数据无效 | 驱动解析失败、地址错误 |
| `Uncertain` | 数据不确定 | 数值处于边界、部分可信 |
| `CommunicationError` | 通信错误 | 协议层异常（校验失败、报文错） |
| `DeviceOffline` | 设备离线 | 心跳丢失、连接断开 |
| `Timeout` | 通信超时 | 采集超时未返回 |
| `NotConnected` | 未连接 | 从未连上 / 主动断开 |
| `Initializing` | 初始化中 | 刚启动，尚未完成首轮采集 |
| `Unknown` | **设备不存在或未授权** | 拼错键 / 没勾读授权 |

**最佳实践：控制类脚本必须先判质量**

```js
function run() {
  var q = getQuality("TANK01", "Temp");
  if (q !== "Good") {
    log("数据不可信，跳过控制。质量 =", q);
    return;                       // 绝不用坏数据做控制决策
  }
  var t = read("TANK01", "Temp");
  // ... 控制逻辑
}
```

---

### 2.4 `write(deviceKey, variableKey, value)` —— 写变量

| 项目 | 说明 |
|---|---|
| 签名 | `write(deviceKey, variableKey, value)` |
| 参数 | 设备键、变量键、待写入值（number / boolean / string） |
| 返回值 | `true` 成功；`false` 失败（失败原因见输出中的 `[WRITE-FAIL]` / `[DENIED]` 行） |
| 权限 | **写授权（ScopeWrite）**，粒度精确到 `设备键.变量键`，**禁止设备级通配** |
| 用途 | 向设备下发控制指令（经驱动真实写入物理设备） |

**这是唯一有副作用、唯一会阻塞的 API，务必理解它的完整链路：**

```
write() 调用
  │
  ├─① 授权检查 ── 不在 ScopeWrite ──► 返回 false + [DENIED]，不写
  │
  ├─② 值拆箱（JS 值 → .NET object）
  │
  ├─③ dry-run 判断 ── 试运行模式 ──► 只打印 [DRY-RUN] 写入 xxx = v，返回 true，不真写
  │
  ▼
 ④ 设备运行中？ ──否──► false，"设备 [xxx] 不在运行中"
  │
  ▼
 ⑤ 驱动写入（真实网络 IO）
  │   前置校验（任一不满足即失败）：
  │   ├─ 变量存在？            ──否──► false
  │   ├─ 变量已启用？          ──否──► false
  │   ├─ 变量非只读？          ──否──► false「变量 [x] 为只读，禁止写入」
  │   ├─ 驱动就绪？            ──否──► false
  │   ├─ 设备已连接？          ──否──► false「设备未连接，无法写入」
  │   └─ 数值在 Min/Max 内？   ──否──► false「写入值 x 低于变量 [x] 下限 n」
  │
  ├─ 同步等待，上界 = Scripting:WriteBridgeTimeoutMs（默认 6000ms）
  │     └─ 超时 ──► 返回 false +「写入超时（>6000ms）：底层写入仍在进行，
  │                  最终结果以写入审计日志为准」
  │                 （底层成为**孤儿任务**继续跑，结果只在日志里）
  │
  ▼
 ⑥ 写成功后更新运行时内存态（Value = 写入值，IsChanged = false）
```

**⚠️ 六个必知的坑**

1. **授权粒度是 `设备键.变量键`**，不是设备键。勾了 `TANK01` 的读授权，`write("TANK01", "Pump", true)` 依然会被拒绝。
2. **超时返回 `false`，但写入可能最终成功了。** 网络慢时会出现"日志说失败、设备实际动作了"。以写入审计日志为准，不要盲目重试。
3. **写入会更新内存值并置 `IsChanged = false`**，这是引擎刻意设计的——避免下一轮轮询把你的写入当成"值变化"再广播一次。但如果你的脚本是 OnChange 触发，仍需用死区/冷却防**回声**。
4. **只读变量、超限数值会被服务端强校验拒绝**，前端输入框的 min/max 只是 HTML 约束，服务端这次才是硬闸门。
5. **布尔量不参与数值限幅**，但数值型写入会被 Min/Max 拦。
6. **写入来源固定记为 `"系统脚本"`**，审计日志里可据此区分是人工下发还是脚本下发。

**示例**

```js
function run() {
  var t = read("TANK01", "Temp");
  if (t === null || getQuality("TANK01", "Temp") !== "Good") return;

  if (t > 80) {
    var ok = write("TANK01", "CoolingPump", true);
    if (!ok) {
      log("冷却泵启动失败，请查输出中的 [WRITE-FAIL] / [DENIED] 行");
      // 决策：是否要告警？是否要重试？——不要无条件重试，可能是只读或超限
    } else {
      log("冷却泵已启动");
    }
  }
}
```

---

## 三、第 2 层与第 3 层：钩子与标准库

### 3.1 钩子 `run()`

- **调用时机**：手动运行、周期触发、Cron 触发、计划任务 `execute_script`、事件联动 `runScript`、HMI 按钮触发。
- **约定**：无参数，无返回值（返回值被丢弃）。
- **未声明时**：输出 `[SKIP] 未声明 run() 钩子`，本次执行记为 **Success**（因为没有抛异常）。
  → 所以「脚本保存成功、每次都 Success、但啥也没干」，往往是钩子名写错了。

### 3.2 钩子 `onChange(ev)`

- **调用时机**：监听变量发生变化时（满足死区与冷却条件后）。
- **参数 `ev`** 是一个普通 JS 对象，字段固定 5 个：

| 字段 | 类型 | 说明 |
|---|---|---|
| `ev.deviceKey` | string | 触发的设备键 |
| `ev.variableKey` | string | 触发的变量键 |
| `ev.value` | any / null | 新值（null 表示无） |
| `ev.previous` | any / null | 旧值（用于判断变化方向、算速率） |
| `ev.quality` | string | 新值对应的质量字符串，取值同 `getQuality` |

- **未声明时**：输出 `[SKIP] 未声明 onChange(ev) 钩子`，同样记为 Success。

```js
function onChange(ev) {
  log("变化：" + ev.deviceKey + "." + ev.variableKey,
      ev.previous, "→", ev.value, "（", ev.quality, "）");

  if (ev.quality !== "Good") return;             // 坏数据不响应
  if (ev.value > ev.previous) {
    log("上升沿");
  }
}
```

> **注意**：`ev` 只在你监听的那个变量上产生。想读别的变量，照常用 `read()`。

### 3.3 全局函数与全局值（11 + 4）

| 函数 | 用途 | 脚本中的典型场景 |
|---|---|---|
| `parseInt(s, radix)` | 字符串转整数 | 解析字符串型变量里的数字 |
| `parseFloat(s)` | 字符串转浮点数 | 同上 |
| `isNaN(v)` | 是否 NaN | 防御性计算 |
| `isFinite(v)` | 是否有限数 | 除法前检查分母 |
| `eval(code)` | 执行字符串代码 | **不建议使用**（严格模式下作用域受限且有注入风险） |
| `encodeURI` / `encodeURIComponent` | URI 编码 | 拼接参数串（但没有 http 能力，用途有限） |
| `decodeURI` / `decodeURIComponent` | URI 解码 | 同上 |
| `escape` / `unescape` | 旧式转义（Annex B） | 基本用不到 |

全局值：`Infinity`、`NaN`、`undefined`、`globalThis`。

### 3.4 `Math`（35 个方法 + 8 个常量）—— 脚本里最常用的工具箱

**常量**：`E`、`LN10`、`LN2`、`LOG10E`、`LOG2E`、`PI`、`SQRT1_2`、`SQRT2`

| 分类 | 方法 | 典型用途 |
|---|---|---|
| 取整 | `abs` `ceil` `floor` `round` `trunc` `fround` | 工程量取整、显示值处理 |
| 极值 | `min` `max` | **输出限幅**（控制算法必备） |
| 幂与根 | `pow` `sqrt` `cbrt` `exp` `expm1` | 流量开方、指数滤波 |
| 对数 | `log` `log10` `log2` `log1p` | pH 换算、分贝 |
| 三角 | `sin` `cos` `tan` `asin` `acos` `atan` `atan2` | 位置/角度换算、矢量合成 |
| 双曲 | `sinh` `cosh` `tanh` `asinh` `acosh` `atanh` | 特殊标定曲线 |
| 符号 | `sign` | 判断变化方向 |
| 其它 | `hypot` `imul` `clz32` | 斜边、32 位整数运算 |
| 随机 | `random` | 仿真/测试数据（`0 ≤ r < 1`） |

```js
// 限幅输出：控制算法的标配
function clamp(v, lo, hi) { return Math.min(hi, Math.max(lo, v)); }

// 开方流量（差压式流量计）
var flow = Math.sqrt(Math.max(0, read("FT01", "DiffPress"))) * 12.5;

// 两位小数
var shown = Math.round(value * 100) / 100;
```

### 3.5 `JSON`（2 个）

`JSON.stringify(obj)` / `JSON.parse(str)`。
**主要用途是让 `log` 输出结构化信息**——沙箱里没有网络也没有文件，序列化不是为了传输，而是为了可读性。

```js
log("巡检结果 =", JSON.stringify({
  temp: read("TANK01", "Temp"),
  level: read("TANK01", "Level"),
  pump: read("TANK01", "Pump"),
  ts: new Date().toISOString()
}));
```

### 3.6 `String`（3 静态 + ~35 原型）

静态：`fromCharCode`、`fromCodePoint`、`raw`

常用原型方法：`length`、`charAt`、`indexOf`、`lastIndexOf`、`includes`、`startsWith`、`endsWith`、
`slice`、`substring`、`substr`、`split`、`replace`、`replaceAll`、`toLowerCase`、`toUpperCase`、
`trim`、`trimStart`、`trimEnd`、`padStart`、`padEnd`、`repeat`、`concat`、`match`、`matchAll`、`search`、
`normalize`、`localeCompare`、`codePointAt`、`charCodeAt`、`at`（版本相关）

```js
// 字符串型变量里抠数字：设备返回 "T=25.6C"
var raw = read("DEV01", "RawStr");          // "T=25.6C"
var num = parseFloat(String(raw).replace(/[^0-9.\-]/g, ""));   // 25.6
log("解析结果 =", num);
```

### 3.7 `Array`（3 静态 + ~31 原型）

静态：`Array.isArray`、`Array.from`、`Array.of`

常用原型：`map` `filter` `forEach` `reduce` `reduceRight` `find` `findIndex` `findLast`（版本相关）
`some` `every` `includes` `indexOf` `lastIndexOf` `slice` `splice` `concat` `join` `push` `pop`
`shift` `unshift` `reverse` `sort` `flat` `flatMap` `fill` `copyWithin` `entries` `keys` `values`

```js
// 遍历一组测点，找出超温的
var points = ["Temp1", "Temp2", "Temp3", "Temp4"];
var MAX = 75;

var over = points.filter(function (k) {
  var v = read("KILN01", k);
  return v !== null && v > MAX;
});

log("超温测点：", over.length ? over.join(", ") : "无");
```

### 3.8 `Number`（~20）

常量：`EPSILON`、`MAX_SAFE_INTEGER`、`MIN_SAFE_INTEGER`、`MAX_VALUE`、`MIN_VALUE`、`NaN`、
`NEGATIVE_INFINITY`、`POSITIVE_INFINITY`

静态：`Number.isFinite`、`Number.isNaN`、`Number.isInteger`、`Number.isSafeInteger`、
`Number.parseFloat`、`Number.parseInt`

原型：`toFixed(n)`、`toExponential`、`toPrecision`、`toString(radix)`、`valueOf`、`toLocaleString`

```js
var v = read("TANK01", "Level");
log("液位 =", Number(v).toFixed(2));        // 固定两位小数，日志更整齐
```

### 3.9 `Object` / `Date` / `RegExp`

- **Object**：`keys` `values` `entries` `assign` `freeze` `seal` `create` `defineProperty`
  `getPrototypeOf` `setPrototypeOf` `is` `fromEntries` `hasOwnProperty` `toString` 等
- **Date**：`new Date()` / `Date.now()` / `Date.parse` / `Date.UTC`
  原型：`getTime` `getFullYear` `getMonth` `getDate` `getHours` `getMinutes` `getSeconds`
  `getDay` `toISOString` `toLocaleString` `setXxx` 系列（约 40 个）
  > ⚠️ `new Date()` 取的是**服务器本地时间**；Cron 调度用的是 **Asia/Shanghai**。两者通常一致，跨时区部署时要注意。
- **RegExp**：字面量 `/pattern/flags` 或 `new RegExp()`，`test` `exec` `match` `replace` 可用（见 §3.6 示例）

```js
var now = new Date();
var hour = now.getHours();
log("当前服务器时间：", now.toISOString(), "小时 =", hour);

// 只在工作时段（8:00–20:00）执行控制逻辑
if (hour >= 8 && hour < 20) {
  // ...
}
```

### 3.10 `Map` / `Set` / `WeakMap` / `WeakSet`

标准语义，约 60 个成员。脚本里最实用的是 **`Set` 去重**与 **Map 做临时查表**：

```js
// 把设备状态归类计数
var keys = ["P1", "P2", "P3", "P4"];
var running = new Set();
keys.forEach(function (k) {
  if (read("PUMPGRP", k) === true) running.add(k);
});
log("运行中泵组：", running.size, "台");
```

### 3.11 `Promise` —— **存在但不可依赖，请当它不存在**

Jint 提供了 `Promise`、`async/await` 语法与 `Promise.all/allSettled/any/race/resolve/reject`，
但**沙箱在 `Invoke` 返回后不会排空微任务队列**。后果：

```js
// ❌ 陷阱：这行几乎永远不会输出
Promise.resolve().then(function () { log("这里不会执行"); });

// ❌ 陷阱：await 之后的代码不会执行
async function run() {
  await something();
  log("这里不会执行");
}
```

**规则**：脚本内**禁止使用 `Promise` / `async` / `await`**。宿主 API 全是同步的，没有需要等待的东西。
（`write()` 内部虽然是真的网络 IO，但它已经在 .NET 侧同步等待完成了，对 JS 表现为同步调用。）

### 3.12 类型化数组 / `ArrayBuffer` / `DataView`（~35）

`Int8Array` `Uint8Array` `Uint8ClampedArray` `Int16Array` `Uint16Array` `Int32Array` `Uint32Array`
`Float32Array` `Float64Array` `BigInt64Array` `BigUint64Array`。
**脚本场景几乎用不到**——没有网络与文件，没有二进制协议要解。保留清单只为完整性。

### 3.13 `Error` 家族（7）与其它

`Error` `EvalError` `RangeError` `ReferenceError` `SyntaxError` `TypeError` `URIError`，
配合 `try / catch / throw / finally` 使用。

`Symbol`（`Symbol.for` / `keyFor` 与内建 symbol）、`Reflect`（13 个方法）、`Proxy`、`globalThis` 亦可用。

```js
function run() {
  try {
    var v = read("TANK01", "Temp");
    if (v === null) throw new Error("读取为空，中止本次控制");
    log("温度 =", v);
  } catch (e) {
    log("脚本内部异常：", e.message);   // 被 catch 住 = 本次执行仍是 Success
  }
}
```

> **catch 与不 catch 的区别**：
> - catch 住 → 本次执行结果 `Success`，失败计数清零；
> - 抛到沙箱外 → 结果 `Error`，`FailureCount +1`，连续 3 次**熔断**。
>
> 想让脚本"安静地跳过"就用 catch；想让引擎"重视这个错误并熔断"就别 catch。二者要按意图选。

---

## 四、第 4 层：明确不存在的东西（用了必炸）

沙箱创建时**只**做了三件事：`LimitRecursion(100)`、`TimeoutInterval(timeoutMs)`、`Strict(true)`，
外加注入 4 个 API。**没有** `AllowClr()`（无法访问 .NET 类型）、**没有** `EnableConsole()`。

| 你可能会想用 | 结果 | 替代方案 |
|---|---|---|
| `console.log()` | `ReferenceError` → 计入失败 → 3 次熔断 | `log()` |
| `setTimeout()` / `setInterval()` | `ReferenceError` | 用「周期触发」或「Cron 触发」，宿主负责调度 |
| `fetch()` / `XMLHttpRequest` | `ReferenceError` | 无网络能力，沙箱设计上就不允许 |
| `require()` / `import` / `module` | 语法/引用错误 | 无模块系统，单文件脚本 |
| `process` / `fs` / `localStorage` | `ReferenceError` | 无持久化存储，状态只能存进变量（见示例 12/14） |
| `alert()` / `document` / `window` | `ReferenceError` | 无 DOM（服务端执行，不是浏览器） |
| .NET 互操作（`System.IO.File` 等） | 不可用 | 未启用 CLR 互操作，这是刻意的**安全边界** |

---

## 五、严格模式（`Strict(true)`）对写法的约束

沙箱强制严格模式，以下写法都会抛错并计入失败：

```js
counter = 0;              // ❌ 未声明就赋值 → ReferenceError（漏写 var/let/const）
function f(a, a) {}       // ❌ 重复形参名
with (obj) {}             // ❌ 禁用 with
delete someVariable;      // ❌ 不能 delete 变量
010                       // ❌ 八进制字面量
arguments.callee          // ❌ 禁用
```

另外沙箱设置了 **递归深度上限 100**：

```js
function fib(n) { return n < 2 ? n : fib(n - 1) + fib(n - 2); }
fib(200);                 // ❌ 递归超限 → 抛错（不是超时）
```

**写深递归请改迭代。**

---

## 六、JS 值 ↔ 运行时值的类型映射

`write()` 会把 JS 值拆箱成 .NET 对象后下发，映射关系如下：

| JS 传入 | 拆箱结果 | 说明 |
|---|---|---|
| `123` / `12.5` | `double` | 数值型变量 |
| `true` / `false` | `bool` | 数字量变量 |
| `"abc"` | `string` | 字符串型变量 |
| `null` / `undefined` | `null` | 会被后续校验拦截（通常写失败） |
| 对象 / 数组 | `ToObject()` | 驱动层一般不支持，写失败 |

反向（`read()` 返回）：

| 变量类型 | JS 收到 |
|---|---|
| 数值型（`double`/`int`/...） | `number` |
| 数字量（`bool`） | `boolean` |
| 字符串型 | `string` |
| 未授权 / 不存在 | `null` |

**防御性写法**（推荐每个读值都走一遍）：

```js
function num(deviceKey, varKey) {
  if (getQuality(deviceKey, varKey) !== "Good") return null;
  var v = read(deviceKey, varKey);
  return (typeof v === "number" && isFinite(v)) ? v : null;
}
```

---

## 七、速查卡（打印贴墙）

```
┌──────────────────────────────────────────────────────────────┐
│  宿主 API（4 个，唯一与 SCADA 交互的通道）                     │
│    log(...)                        → 输出日志（只在钩子内有效）│
│    read(dev, var)                  → 读值，失败返回 null       │
│    getQuality(dev, var)            → "Good" / "Bad" / ...     │
│    write(dev, var, val)            → 写值，返回 true / false   │
│                                                               │
│  钩子（2 个，名字固定）                                        │
│    run()          手动 / 周期 / Cron                          │
│    onChange(ev)   变量变化；ev = {deviceKey, variableKey,      │
│                   value, previous, quality}                   │
│                                                               │
│  授权                                                          │
│    读：设备键（ScopeRead）                                     │
│    写：设备键.变量键（ScopeWrite）                             │
│    空授权 = 拒绝全部，且不抛异常，只打 [DENIED]                │
│                                                               │
│  禁用                                                          │
│    console · setTimeout · fetch · require · process · DOM      │
│    async / await / Promise（不排空微任务，静默失效）           │
│                                                               │
│  约束                                                          │
│    严格模式 · 递归 ≤100 · 超时 500–30000ms（默认 2000）        │
│    写桥等待 ≤ Scripting:WriteBridgeTimeoutMs（默认 6000）      │
│    每次执行新建沙箱 → 无状态                                   │
└──────────────────────────────────────────────────────────────┘
```
