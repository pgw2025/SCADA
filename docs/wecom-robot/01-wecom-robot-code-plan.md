# 企业微信群机器人（WeCom）渠道接入 —— 完整代码修改方案（方案 A）

> 本文是**代码修改方案**，不含任何已落盘后的源码改动，仅给出每个文件需要修改的精确内容，供评审后开分支实施。

## 修订记录

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-09-10 | v1 | 初稿：逐文件代码修改方案 |
| 2026-09-10 | v2 | 复审修正（详见 §3 坑位表 #9–#14 与 §5）：① `SanitizeMarkdown` 改为**按标签对**剥除 hex 色（v1 全量剥除会误杀企微原生枚举色 `warning\|info\|comment`；评审讨论稿的简化版又会误删枚举标签的闭合符）；② `SaveAsync` 由整体替换改为**节点级合并**，且必须按 `JsonElement` 处理既有节点（`Dictionary<string,object>` 模式匹配对 STJ 反序列化产物恒不命中，直接套用会让合并退化为覆盖、WebPush 节照样丢失）；③ `WeComConfigDto` 采用**可空无初始化器**声明，否则「旧客户端 PUT 缺 weCom 字段 → 模型绑定得到 new() 空实例 → 配置被静默重置」，null 兜底根本不会触发；④ 补 `logFilterChannel` 联合类型扩展；⑤ 补 tab-channels 状态绿点条件；⑥ 截断后追加提示后缀 |

## 0. 设计结论与命名约定

企业微信群机器人（webhook 型，`https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=KEY`）与钉钉群机器人同构，作为**第 4 个渠道**接入既有通知管线：

- 实现 `IExternalMessageSender` 接口 → `ExternalNotificationService` 自动为其建立独立队列、限流、重试、扇出与投递记录。
- 协议差异（与钉钉相比）：markdown 无独立 `title` 字段、无 HMAC 加签、`content ≤ 4096 字节`、font 标签只支持 `<font color="warning|comment|info">` 枚举色。

**全链路命名约定（必须一致，否则重试链路失效）：**

| 层 | 值 |
|---|---|
| `Sender.Name` | `WeCom` |
| 渠道 ID（前端 / 投递记录 `Channel`） | `weCom` |
| 配置节（`NotificationOptions` / `appsettings` / DTO） | `WeCom` |
| HttpClient 注册名 | `WeCom` |

> 关键坑：`NotificationLogService.RetryAsync` → `IsChannelEnabled(log.Channel)`，`log.Channel` 里存的是 `MapChannel` 之后的 `weCom`，与 `Sender.Name` 做**大小写不敏感**比较。因此 `WeCom` ⟷ `weCom` 天然匹配；**切勿**命名成 `weChatWork` / `qyWeChat` 等与 `Name` 不一致的串。

---

## 1. 后端改动

### 1.1 新增文件 `Server/ScadaServer.Infrastructure/Communication/WeComRobotClient.cs`

完整文件内容（仿 `DingTalkRobotClient`）：

```csharp
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>企业微信群机器人发送器（markdown 消息，webhook 型，无加签）。</summary>
    /// <remarks>
    /// 作为第四渠道接入既有通知管线（与钉钉/邮件/Web Push 并列），自动继承限流/重试/投递记录治理。
    /// 协议差异：
    ///   1) markdown 无独立 title 字段，仅 content；Title 前置为加粗标题。
    ///   2) 无 HMAC 加签，安全靠机器人「自定义关键词」+「IP 白名单」。
    ///   3) content ≤ 4096 字节（UTF-8），超出按字节安全截断并追加提示后缀。
    ///   4) font 标签只支持 warning|info|comment 枚举色：hex 色标签对剥除保留文本，
    ///      枚举色标签对合法原样保留（用户自定义模板可正常使用）。
    /// </remarks>
    public class WeComRobotClient : IExternalMessageSender
    {
        /// <summary>命名 HttpClient 注册名（WebApi 注册时配置 8s 超时，同钉钉）。</summary>
        public const string HttpClientName = "WeCom";

        private const int MaxContentBytes = 4096;

        /// <summary>超长截断提示（追加后总字节数仍 ≤ 4096）。</summary>
        private const string TruncateNotice = "\n\n（内容超长已截断）";

        /// <summary>hex 色 font 标签对（含闭合标签一起剥除，保留内部文本）。
        /// 只匹配 color="#xxxxxx" 形式；枚举色（warning|info|comment）不命中、原样保留。
        /// Singleline：兼容标签内含换行的正文。</summary>
        private static readonly Regex HexFontRegex = new(
            @"<font\s[^>]*color=[""']#[0-9a-fA-F]{3,8}[""'][^>]*>(.*?)</font>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly WeComOptions _options;
        private readonly ILogger<WeComRobotClient> _logger;

        public WeComRobotClient(
            IHttpClientFactory httpClientFactory,
            IOptions<NotificationOptions> options,
            ILogger<WeComRobotClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value.WeCom;
            _logger = logger;
        }

        public string Name => "WeCom";

        public bool Enabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Webhook);

        /// <summary>收件方摘要（webhook 含 key，不回显明文）。</summary>
        public string RecipientSummary => "企业微信群机器人";

        public async Task SendAsync(ExternalMessage message, CancellationToken cancellationToken)
        {
            var content = BuildContent(message);

            var payload = new
            {
                msgtype = "markdown",
                markdown = new { content }
            };

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(_options.Webhook, payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            // 企业微信业务失败（关键词不匹配/内容为空/超长）也返回 HTTP 200，必须解析 errcode。
            var result = JsonSerializer.Deserialize<WeComResponse>(body);
            if (result?.Errcode != 0)
            {
                throw new InvalidOperationException($"企业微信机器人拒绝消息（errcode={result?.Errcode}）：{result?.Errmsg ?? body}");
            }
        }

        /// <summary>构造 markdown content：标题前置加粗 → hex 色标签对清洗 → 字节级截断（超限时追加提示，总长仍 ≤ 4096）。</summary>
        private static string BuildContent(ExternalMessage message)
        {
            var body = SanitizeMarkdown($"**{message.Title}**\n\n{message.MarkdownText}");
            var budget = MaxContentBytes - Encoding.UTF8.GetByteCount(TruncateNotice);
            var truncated = TruncateUtf8(body, budget);
            return truncated.Length == body.Length ? body : truncated + TruncateNotice;
        }

        /// <summary>剥除企微不支持的 hex 色 font 标签对（保留内部文本）；枚举色标签对（warning|info|comment）合法保留。</summary>
        private static string SanitizeMarkdown(string? markdown)
            => string.IsNullOrEmpty(markdown)
                ? string.Empty
                : HexFontRegex.Replace(markdown, "$1");

        /// <summary>按 UTF-8 字节数安全截断，回退到上一个合法序列边界，避免截断在多字节字符中间产生乱码。</summary>
        private static string TruncateUtf8(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length <= maxBytes) return value;

            var length = maxBytes;
            while (length > 0 && (bytes[length - 1] & 0xC0) == 0x80)
            {
                length--;
            }
            return Encoding.UTF8.GetString(bytes, 0, length);
        }

        private sealed class WeComResponse
        {
            [JsonPropertyName("errcode")]
            public int Errcode { get; set; }

            [JsonPropertyName("errmsg")]
            public string? Errmsg { get; set; }
        }
    }
}
```

> **为什么必须按「标签对」正则替换而非「剥开标签 + 全量删 `</font>`」**：枚举色标签被保留时，其闭合标签也必须保留；全量 `.Replace("</font>", "")` 会把 `<font color="warning">严重</font>` 破坏成 `<font color="warning">严重`（markdown 染色失效）。标签对整体匹配天然规避该问题。

---

### 1.2 修改 `Server/ScadaServer.Application/Options/NotificationOptions.cs`

**变更点 1**：给 `NotificationOptions` 类新增一个属性（放在 `public WebPushOptions WebPush` 之后）：

```csharp
public WeComOptions WeCom { get; set; } = new();
```

**变更点 2**：新增 `WeComOptions` 类（建议放在 `WebPushOptions` 之后、`VapidOptions` 之前）：

```csharp
/// <summary>企业微信群机器人渠道选项（webhook 型，无加签；安全靠机器人「关键词」+「IP 白名单」）。</summary>
public class WeComOptions
{
    /// <summary>渠道总开关（Enabled=true 但 Webhook 为空时渠道仍视为禁用，与既有渠道语义一致）。</summary>
    public bool Enabled { get; set; }

    /// <summary>群机器人 webhook 完整地址（含 key）。</summary>
    public string Webhook { get; set; } = string.Empty;
}
```

---

### 1.3 修改 `Server/ScadaServer.WebApi/appsettings.json`

在 `"WebPush": { ... }` 节之后、`"Push": { ... }` 之前，新增：

```json
"WeCom": {
  "_comment": "企业微信群机器人（webhook 型，与钉钉同构）。Enabled=false 时零影响。webhook 含 key，勿泄入仓库；生产经 appsettings.dboverride.json 或环境变量 Notification__WeCom__Webhook 注入。",
  "Enabled": false,
  "Webhook": "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=在此替换"
},
```

> 同时更新顶部 `"Notification"` 的 `_comment` 措辞为「钉钉群机器人 + 企业微信群机器人 + SMTP 邮件」（可选）。

---

### 1.4 修改 `Server/ScadaServer.Application/DTOs/NotificationConfigDtos.cs`（v2：可空声明）

**变更点 1**：`NotificationConfigDto` 新增属性——**注意：可空、不初始化**，与 `DingTalk`/`Email` 的 `= new()` 风格刻意不同：

```csharp
/// <summary>企业微信配置片段。可空无初始化器：请求体缺失该字段时绑定为 null，
/// SaveAsync 据此沿用旧值——若用 = new() 初始化，旧客户端（PWA 离线壳缓存的旧 JS 包）
/// PUT 不带 weCom 字段时会绑定到空实例（Enabled=false），已启用渠道被静默重置。</summary>
public WeComConfigDto? WeCom { get; set; }
```

**变更点 2**：新增 `WeComConfigDto` 类（放在 `DingTalkConfigDto` 之后）：

```csharp
/// <summary>企业微信群机器人配置片段（webhook 型，无加签字段）。</summary>
public class WeComConfigDto
{
    public bool Enabled { get; set; }
    public string Webhook { get; set; } = string.Empty;
}
```

**变更点 3（可选）**：更新 `NotificationLogDto.Channel` 的注释，`dingTalk | email | webPush` → 追加 `| weCom`。

> 前端类型（§2.1）保持 `weCom: WeComConfig` 非可空：GET 恒返回完整对象，前端保存时整表单回传，两端语义一致。

---

### 1.5 修改 `Server/ScadaServer.Application/Interfaces/INotificationConfigService.cs`

新增接口方法：

```csharp
/// <summary>用临时提交的企业微信配置发送一条测试消息（不落盘）。</summary>
Task<NotificationTestResult> TestWeComAsync(WeComConfigDto dto);
```

---

### 1.6 修改 `Server/ScadaServer.Infrastructure/Services/NotificationConfigService.cs`（v2：null 保留 + 节点级合并）

**变更点 1**：`GetAsync` 返回值补 WeCom 映射（在 `Templates` 赋值前）：

```csharp
WeCom = new WeComConfigDto
{
    Enabled = o.WeCom.Enabled,
    Webhook = o.WeCom.Webhook
},
```

**变更点 2**：`SaveAsync` 中，`merged` 对象新增（`DingTalk` 与 `Email` 之间）——**null 时沿用旧值**（v2 修正，配合 §1.4 的可空声明）：

```csharp
// WeCom：请求体未携带该片段（旧客户端/脚本）时沿用旧值，防止已启用渠道被静默重置
WeCom = dto.WeCom is null
    ? current.WeCom
    : new WeComOptions
    {
        Enabled = dto.WeCom.Enabled,
        Webhook = (dto.WeCom.Webhook ?? string.Empty).Trim()
    },
```

**变更点 3**：`SaveAsync` 校验区（现有 Email 校验之后）新增：

```csharp
if (merged.WeCom.Enabled && string.IsNullOrWhiteSpace(merged.WeCom.Webhook))
{
    throw new BusinessException("启用企业微信通知时必须填写 Webhook 地址。");
}
```

**变更点 4（v2 重写：节点级合并替代整体替换）**：将现有 `SaveAsync` 尾部的 `var payload = new Dictionary<string, object> { ["Notification"] = ... }` 构造块、`var urlPrefix = _current.Value;` 占位行以及 `root["Notification"] = payload["Notification"];` 整体**替换**为：

```csharp
var path = GetOverridePath();
var root = await ReadOverrideRootAsync();

// 节点级合并（v2 修正）：只覆写本服务管理的五个子节，保留 override 文件中其他子节
// （WebPush/VAPID 等）。原「root["Notification"] = payload["Notification"]」整体替换会把
// WebPush 节（含 VAPID 私钥）一并抹掉——接入 WeCom 前就存在的隐患，本次一并修复。
//
// 注意：ReadOverrideRootAsync 的 JsonSerializer.Deserialize<Dictionary<string, object>>
// 产物中，嵌套节点是 JsonElement 而非 Dictionary<string, object>——用
// `is not Dictionary<string, object>` 模式匹配恒不命中，必须按 JsonValueKind.Object 枚举拷贝，
// 否则合并退化为覆盖，WebPush 节照样丢失。
var notif = new Dictionary<string, object>();
if (root.TryGetValue("Notification", out var existingNode)
    && existingNode is JsonElement { ValueKind: JsonValueKind.Object } existingObj)
{
    foreach (var prop in existingObj.EnumerateObject())
    {
        notif[prop.Name] = prop.Value; // JsonElement 值原样保留，序列化时按原样写出
    }
}

notif["DingTalk"] = new Dictionary<string, object>
{
    ["Enabled"] = merged.DingTalk.Enabled,
    ["Webhook"] = merged.DingTalk.Webhook,
    ["Secret"] = merged.DingTalk.Secret
};
notif["Email"] = new Dictionary<string, object>
{
    ["Enabled"] = merged.Email.Enabled,
    ["SmtpHost"] = merged.Email.SmtpHost,
    ["SmtpPort"] = merged.Email.SmtpPort,
    ["UseSsl"] = merged.Email.UseSsl,
    ["Username"] = merged.Email.Username,
    ["Password"] = merged.Email.Password,
    ["From"] = merged.Email.From,
    ["FromName"] = merged.Email.FromName,
    ["To"] = merged.Email.To
};
notif["WeCom"] = new Dictionary<string, object>
{
    ["Enabled"] = merged.WeCom.Enabled,
    ["Webhook"] = merged.WeCom.Webhook
};
notif["Push"] = SerializePush(merged.Push);
notif["Templates"] = SerializeTemplates(merged.Templates);
root["Notification"] = notif;

var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
await System.IO.File.WriteAllTextAsync(path, json);
_logger.LogInformation("通知配置已写入 override 文件：{Path}（重启后生效）。", path);
```

**变更点 5**：新增测试方法（放在 `TestEmailAsync` 之后、`SendTestAsync` 之前）：

```csharp
public async Task<NotificationTestResult> TestWeComAsync(WeComConfigDto dto)
{
    if (dto == null || string.IsNullOrWhiteSpace(dto.Webhook))
    {
        return new NotificationTestResult { Success = false, Message = "请先填写 Webhook 地址。" };
    }

    var opts = Options.Create(new NotificationOptions
    {
        WeCom = new WeComOptions
        {
            Enabled = true,
            Webhook = dto.Webhook.Trim()
        }
    });
    var sender = new WeComRobotClient(_httpClientFactory, opts, _loggerFactory.CreateLogger<WeComRobotClient>());

    return await SendTestAsync(sender, "企业微信", s => s.SendAsync(
        new ExternalMessage
        {
            Category = ExternalMessageCategory.SystemError,
            Title = "SCADA 通知测试",
            MarkdownText = $"## SCADA 通知测试\n- 来源：通知中心测试发送\n- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}"
        }, CancellationToken.None));
}
```

> `TestWeComAsync(WeComConfigDto dto)` 参数为非可空（`[FromBody]` 绑定缺字段时 `dto.Webhook` 为空串即可校验拦截）；接口签名（§1.5）与此一致。

**变更点 6**：`RecordTestOutcome` 的 channel switch 新增分支：

```csharp
"企业微信" => "weCom",
```

---

### 1.7 修改 `Server/ScadaServer.WebApi/Controllers/NotificationConfigController.cs`

在 `TestEmail` 之后新增：

```csharp
/// <summary>测试发送企业微信群机器人消息（使用提交的临时值，不落盘）</summary>
[HttpPost("test-wecom")]
public async Task<IActionResult> TestWeCom([FromBody] WeComConfigDto dto)
    => Ok(await _service.TestWeComAsync(dto));
```

---

### 1.8 修改 `Server/ScadaServer.WebApi/Extensions/Application.Extensions.cs`

在 `services.AddHttpClient(DingTalkRobotClient.HttpClientName, ...)` 之后新增：

```csharp
services.AddHttpClient(WeComRobotClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(8));
```

在 `services.AddSingleton<IExternalMessageSender, EmailSender>();` 之后新增：

```csharp
// 企业微信渠道（doc/wecom-robot 方案 A）：Enabled=false 或 Webhook 为空时自动禁用（管线不扇出）
services.AddSingleton<IExternalMessageSender, WeComRobotClient>();
```

---

### 1.9 修改 `Server/ScadaServer.WebApi/HostedServices/ExternalNotificationService.cs`

`MapChannel` 方法新增分支：

```csharp
"WECOM" => "weCom",
```

---

## 2. 前端改动

### 2.1 修改 `Client/src/api/notificationApi.ts`

**绝对关键点：`NotificationConfig`、`NotificationLogItem.channel` 联合类型必须同步加入 `weCom`，否则前端类型检查失败。**

```ts
export interface WeComConfig {
  enabled: boolean;
  webhook: string;
}
```

`NotificationConfig` 新增字段：

```ts
export interface NotificationConfig {
  dingTalk: DingTalkConfig;
  weCom: WeComConfig;
  email: EmailConfig;
  push: PushPolicy;
  templates: NotificationTemplates;
}
```

`NotificationLogItem.channel` 联合类型：

```ts
channel: 'dingTalk' | 'weCom' | 'email' | 'webPush';
```

新增测试 API：

```ts
export const testWeCom = (dto: WeComConfig) =>
  http.post<NotificationTestResult>(`${base()}/test-wecom`, dto);
```

---

### 2.2 修改 `Client/src/components/NotificationCenterView.vue`

> 以下按「位置锚点 → 改动」给出。H、I 为 v2 复审补充项。

**A. import 块（`<script setup>` 顶部，约 33-44 行）**：在 `testDingTalk,` 之后新增 `testWeCom,`：

```ts
  testDingTalk,
  testWeCom,
  testEmail,
```

**B. 状态（约 51-57 行）**：新增测试态与结果：

```ts
const testingWeCom = ref(false);
const weComTestResult = ref<{ success: boolean; message: string; latencyMs?: number } | null>(null);
```

**C. form 初始值（约 354-387 行）**：在 `dingTalk: { ... },` 之后新增：

```ts
  weCom: {
    enabled: false,
    webhook: ''
  },
```

**D. onMounted 回填（约 394-396 行）**：新增：

```ts
      if (res.weCom) Object.assign(form.weCom, res.weCom);
```

**E. 新增测试函数（放在 `handleTestEmail` 之后）**：

```ts
const handleTestWeCom = async () => {
  testingWeCom.value = true;
  weComTestResult.value = null;
  try {
    const res = await testWeCom({ ...form.weCom });
    weComTestResult.value = res;
    showToast(res.message, res.success ? 'success' : 'error');
    addLog('系统设置', `企业微信推送验证：${res.message}`, res.success ? 'normal' : 'warning');
    loadDeliveryLogs();
  } catch (err: any) {
    weComTestResult.value = { success: false, message: '测试请求超时或网络不可达: ' + err?.message };
    showToast('企业微信通道连通性测试失败', 'error');
  } finally {
    testingWeCom.value = false;
  }
};
```

**F. 头部状态徽章（约 643-647 行，`邮件` 徽章之后）**：新增企微项：

```html
          <span class="text-slate-300 dark:text-slate-600">|</span>
          <span class="inline-flex items-center gap-1 font-medium" :class="form.weCom.enabled ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'">
            <span class="w-1.5 h-1.5 rounded-full" :class="form.weCom.enabled ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'"></span>
            企微
          </span>
```

**G. 渠道卡片（`SMTP Email Card` 结束 `</div>` 之后、grid 收尾 `</div>`（约 1023-1025 行）之前）**：新增企微卡片：

```html
          <!-- WeCom WebHook Card -->
          <div class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs flex flex-col overflow-hidden transition-all">
            <div class="p-5 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between bg-slate-50/50 dark:bg-slate-900/50">
              <div class="flex items-center gap-3">
                <div class="w-9 h-9 rounded-lg bg-teal-500/10 text-teal-600 dark:text-teal-400 flex items-center justify-center font-bold">
                  <Bell class="w-5 h-5" />
                </div>
                <div>
                  <h3 class="font-bold text-sm text-slate-900 dark:text-white flex items-center gap-2">
                    企业微信群机器人
                    <span
                      class="px-2 py-0.5 rounded text-[10px] font-semibold"
                      :class="form.weCom.enabled ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'"
                    >
                      {{ form.weCom.enabled ? '服务已启用' : '未开启' }}
                    </span>
                  </h3>
                  <p class="text-[11px] text-slate-400">企业微信群 WebHook 机器人，markdown 告警直达</p>
                </div>
              </div>
              <label class="relative inline-flex items-center cursor-pointer">
                <input type="checkbox" v-model="form.weCom.enabled" class="sr-only peer" />
                <div class="w-11 h-6 bg-slate-200 peer-focus:outline-none rounded-full peer dark:bg-slate-700 peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-teal-600"></div>
              </label>
            </div>

            <div class="p-5 space-y-4 flex-1">
              <div>
                <label class="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5">
                  WebHook 目标地址 <span class="text-rose-500">*</span>
                </label>
                <input
                  v-model="form.weCom.webhook"
                  type="text"
                  placeholder="https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=..."
                  class="w-full px-3 py-2 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-500 transition-all font-mono"
                />
                <p class="text-[11px] text-slate-400 mt-1">企业微信群「添加群机器人」后生成的 Webhook 完整路径。</p>
              </div>

              <div
                v-if="weComTestResult"
                class="p-3 rounded-lg text-xs flex items-start gap-2.5 transition-all"
                :class="weComTestResult.success ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950/40 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800' : 'bg-rose-50 text-rose-800 dark:bg-rose-950/40 dark:text-rose-300 border border-rose-200 dark:border-rose-800'"
              >
                <CheckCircle2 v-if="weComTestResult.success" class="w-4 h-4 text-emerald-500 shrink-0 mt-0.5" />
                <AlertCircle v-else class="w-4 h-4 text-rose-500 shrink-0 mt-0.5" />
                <div class="flex-1">
                  <div class="font-semibold">{{ weComTestResult.success ? '连通性验证成功' : '通道测试失败' }}</div>
                  <div class="text-[11px] mt-0.5 opacity-90">{{ weComTestResult.message }}</div>
                  <div v-if="weComTestResult.latencyMs" class="text-[10px] mt-1 font-mono opacity-80">响应耗时: {{ weComTestResult.latencyMs }}ms</div>
                </div>
              </div>
            </div>

            <div class="p-4 bg-slate-50/70 dark:bg-slate-900/70 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between">
              <span class="text-[11px] text-slate-400">建议在机器人设置中开启关键词校验与 IP 白名单</span>
              <button
                type="button"
                @click="handleTestWeCom"
                :disabled="testingWeCom || !form.weCom.webhook"
                class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium text-teal-700 dark:text-teal-300 bg-teal-50 dark:bg-teal-950/60 border border-teal-200 dark:border-teal-800/80 hover:bg-teal-100 dark:hover:bg-teal-900 transition-colors disabled:opacity-40 cursor-pointer"
              >
                <RotateCw v-if="testingWeCom" class="w-3.5 h-3.5 animate-spin" />
                <Send v-else class="w-3.5 h-3.5" />
                <span>{{ testingWeCom ? '握手测试中…' : '发送企微测试消息' }}</span>
              </button>
            </div>
          </div>
```

**H.（v2 补充）`logFilterChannel` 联合类型（约 69 行）**——只加 `<option>` 不扩类型时，选中 `weCom` 会导致 `v-model` 绑定到联合类型之外的值（`tsc --noEmit` 不检查 .vue，属静默隐患），必须同步扩展：

```ts
const logFilterChannel = ref<'all' | 'dingTalk' | 'weCom' | 'email' | 'webPush'>('all');
```

**I.（v2 补充）tab-channels 导航状态绿点（约 677 行）**——补上 `form.weCom.enabled`，否则仅启用企微时 tab 仍显示灰点：

```html
          <span class="w-2 h-2 rounded-full" :class="(form.dingTalk.enabled || form.weCom.enabled || form.email.enabled) ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-600'"></span>
```

**J. 日志筛选下拉（约 1609-1612 行）**：新增：

```html
              <option value="weCom">企业微信群机器人</option>
```

**K. 日志表格渠道着色/图标/文案（约 1691-1702 行）**：新增 `weCom` 分支：

- `:class` 对象新增：

```ts
                        'bg-teal-50 text-teal-700 dark:bg-teal-950/60 dark:text-teal-400 border border-teal-200/60 dark:border-teal-900': item.channel === 'weCom',
```

- 图标行新增（**必须插到 `Smartphone` 的 else 之前**，否则落入 else 分支）：

```html
                      <Bell v-else-if="item.channel === 'weCom'" class="w-3 h-3" />
```

- 文案三元改为嵌套判断：

```ts
                      {{ item.channel === 'dingTalk' ? '钉钉群' : (item.channel === 'weCom' ? '企微群' : (item.channel === 'email' ? 'SMTP邮件' : 'Web推送')) }}
```

**L. 日志详情弹窗渠道文案（约 1793 行）**：追加 `weCom` 判断：

```ts
              <span class="font-medium text-slate-800 dark:text-slate-200">{{ selectedLogDetail.channel === 'dingTalk' ? '钉钉群机器人' : (selectedLogDetail.channel === 'weCom' ? '企业微信群机器人' : (selectedLogDetail.channel === 'email' ? 'SMTP 邮件服务' : 'Web Push 推送')) }}</span>
```

**M. footer 路线图文案（约 1035 行）**：更新为：

```html
              <div class="text-[11px] text-slate-500 dark:text-slate-400">系统已内置 企业微信群机器人 (WeCom)，后续可扩展 飞书多维协作机器人 (Feishu) 与 通用 HTTP WebHook 接口。</div>
```

---

### 2.3 可选增强（不影响主链路，按需实施）

1. **订阅矩阵第三列**：「触发策略与防抖」表格（约 1070-1072 行表头、每个事件行）加「企业微信群机器人」列。注意：**当前 Push 策略是「事件级全局开关」**，钉钉/邮件两列绑定的是同一个 `form.push.pushXxx` 字段（纯展示），新增列同样绑同值即可，不改数据模型。若未来需要「渠道×事件」独立订阅矩阵，属于独立的 data model 改动，超出本需求。
2. **企业微信模板预览**（Tab3 仿真器）：`previewMode` 联合类型（约 78 行）加 `'wecom'`、模式切换按钮（约 1464-1482 行）加「企业微信效果」、仿真器 body（约 1489-1530 行）加 `v-if="previewMode === 'wecom'"` 渲染块（复用钉钉气泡样式，正文 = `**标题** + Markdown`，可在预览侧做 hex→枚举色转换演示）。

---

## 3. 关键决策与坑位清单

| # | 决策/坑 | 说明 | 版本 |
|---|---|---|---|
| 1 | 渠道 ID 必须与 `Sender.Name` 大小写不敏感一致 | `WeCom` ⟷ `weCom`，否则 `IsChannelEnabled` 拦死重试 | v1 |
| 2 | markdown 无独立 `title` | SendAsync 把 `Title` 前置为 `**标题**\n\n` | v1 |
| 3 | 无加签 | `WeComOptions` 只需 `Enabled + Webhook` | v1 |
| 4 | 业务失败也 HTTP 200 | 必须解析 `errcode != 0` 抛异常，否则误判成功 | v1 |
| 5 | `content ≤ 4096` 字节 | 按 UTF-8 字节安全截断 + 追加截断提示（v2：提示后缀预留字节，总长不超限） | v1/v2 |
| 6 | font 标签只支持枚举色 | hex 色标签**对**剥除保留文本；枚举色标签**对**原样保留。不能全量剥（误杀枚举色），也不能只剥开标签全删 `</font>`（误删枚举色的闭合符） | **v2** |
| 7 | `NotificationConfig` / `channel` 联合类型 | 前端类型必须同步，否则 typecheck 失败 | v1 |
| 8 | 日志图标 else-if 顺序 | `weCom` 分支必须插到 `Smartphone`(else) 之前 | v1 |
| 9 | `WeComConfigDto` 可空无初始化器 | `= new()` 会让「旧客户端 PUT 缺 weCom 字段」绑定到空实例，null 兜底失效，配置被静默重置 | **v2** |
| 10 | `SaveAsync` 节点级合并 | 整体替换会抹掉 override 文件中的 WebPush 节（含 VAPID 私钥）——既有隐患，本次一并修复 | **v2** |
| 11 | 合并必须按 `JsonElement` 处理 | `Deserialize<Dictionary<string, object>>` 的嵌套产物是 `JsonElement`，`is Dictionary<string,object>` 恒不命中，直接套用会让合并退化为覆盖 | **v2** |
| 12 | `logFilterChannel` 联合类型同步扩展 | 只加 `<option>` 不扩 ref 类型 = v-model 绑定非法值（.vue 不受 tsc 检查，静默隐患） | **v2** |
| 13 | tab-channels 状态绿点 | 判断条件补 `form.weCom.enabled` | **v2** |
| 14 | `@` 提醒边界 | 企微 `markdown` 类型不支持 `mentioned_list`（仅 text 类型可用），一期不做 @ 是正确边界 | v1 复审确认 |

---

## 4. 建议实施顺序与验证

### 实施顺序

1. 后端：1.1 → 1.2 → 1.3 → 1.8 → 1.9 → 1.4 → 1.5 → 1.6 → 1.7（先让渠道能注册、能编译）。
2. 后端编译：`dotnet build`。
3. 前端：2.1 → 2.2（含 H/I），`npm run lint`（`tsc --noEmit`）。
4. 集成验证（见下）。

### 验证清单

**功能主链路**
- [ ] 配置企微 webhook → 保存 → 重启后端 → 前端「发送企微测试消息」成功，投递记录出现 `weCom`/`test` 行。
- [ ] 触发一次报警 → 投递记录出现 `weCom` 行、状态 Success；企微群收到 markdown 消息（标题加粗 + 正文）。
- [ ] 错误 webhook → 失败落库（errcode 信息可见）→ 修复后「一键重发」重试成功（验证 #1 命名链路）。
- [ ] 超长报警内容 → 收到「（内容超长已截断）」后缀且无乱码（验证 #5）。

**v2 修正项专项**
- [ ] 模板含 `<font color="warning">严重</font>` → 企微群内渲染为橙色且闭合正常（验证 #6）。
- [ ] 模板含 `<font color="#ef4444">` → 标签剥除、内部文本保留（验证 #6）。
- [ ] 保存配置后检查 `appsettings.dboverride.json`：`WeCom` 节写入，**`WebPush` 节（含 VAPID）原样保留**（验证 #10/#11）。
- [ ] 用不带 `weCom` 字段的请求体模拟旧客户端 `PUT /api/NotificationConfig` → 重启后 WeCom 配置未被重置（验证 #9）。
- [ ] 仅启用企微（钉钉/邮件全关）→ tab-channels 绿点亮起（验证 #13）；日志筛选切「企业微信群机器人」正常（验证 #12）。

---

## 5. 复审识别但未纳入主方案的可选优化（P3）

| # | 优化 | 取舍 |
|---|---|---|
| 1 | hex 色 → 枚举色映射（如 `#ef4444`→`warning`、`#10b981`→`info`）替代「剥除」 | 体验更好（保留染色语义），但需维护色值映射表；一期先剥除，二期可升级 |
| 2 | Webhook 前缀校验（`https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=`） | 防粘贴错 URL；钉钉也未做，属一致性取舍 |
| 3 | `ExternalNotificationService` 启动日志「钉钉/邮件通知渠道均未启用」措辞更新 | 纯文案，随实现顺手改即可 |
| 4 | 孤立的 hex font 开标签（无闭合）不匹配标签对正则，会原样残留 | 模板场景下不存在此类输入，不值得为它加二次清洗 |
| 5 | `WeComRobotClient._logger` 未使用 | 与 `DingTalkRobotClient` 对称（同样未使用），保持一致可接受 |

---

*本方案严格遵循「不改代码」：以上均为待实施内容，未对任何源码文件落盘。*
