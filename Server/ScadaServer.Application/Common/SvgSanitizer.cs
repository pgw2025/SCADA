using System.Text.RegularExpressions;

namespace ScadaServer.Application.Common
{
    /// <summary>
    /// SVG 模板清洗（入库前执行；白名单 URL：锚点 / 内嵌图片 / 站内相对路径）。
    /// 已知边界（登记不修）：animate attributeName=href 动画钓鱼，风险低。
    /// </summary>
    public static class SvgSanitizer
    {
        /// <summary>SVG 模板源码最大长度（256KB）</summary>
        private const int MaxLength = 256 * 1024;

        /// <summary>&lt;script&gt; 块（含自闭合），大小写不敏感、跨行匹配</summary>
        private static readonly Regex ScriptRe =
            new(@"<script[\s\S]*?</script>|<script[^>]*/>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>&lt;foreignObject&gt; 块（可注入任意 HTML）</summary>
        private static readonly Regex ForeignObjectRe =
            new(@"<foreignObject[\s\S]*?</foreignObject>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>事件属性（onclick、onload 等）：一律移除</summary>
        private static readonly Regex EventAttrRe =
            new(@"\son\w+\s*=\s*(?<q>""|')(?<v>[\s\S]*?)\k<q>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>URL 属性（href / xlink:href / src）：非白名单一律清空值</summary>
        private static readonly Regex UrlAttrRe =
            new(@"(?<attr>href|xlink:href|src)\s*=\s*(?<q>""|')(?<v>[\s\S]*?)\k<q>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>CSS 内 url(...) 引用：非白名单一律置空</summary>
        private static readonly Regex StyleUrlRe =
            new(@"url\(\s*(?<q>""|')?(?<v>[\s\S]*?)(?(q)\k<q>|[\s)]?)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 清洗 SVG 模板：移除脚本 / foreignObject / 事件属性，URL 收紧白名单。
        /// 返回空字符串表示输入为空；超长截断至 <see cref="MaxLength"/>。
        /// </summary>
        public static string Sanitize(string svg)
        {
            if (string.IsNullOrWhiteSpace(svg)) return string.Empty;

            var result = ScriptRe.Replace(svg, string.Empty);
            result = ForeignObjectRe.Replace(result, string.Empty);
            result = EventAttrRe.Replace(result, string.Empty);

            result = UrlAttrRe.Replace(result, m =>
                IsSafeUrl(m.Groups["v"].Value) ? m.Value
                    : $"{m.Groups["attr"].Value}={m.Groups["q"].Value}{m.Groups["q"].Value}");

            result = StyleUrlRe.Replace(result, m =>
                IsSafeUrl(m.Groups["v"].Value.Trim()) ? m.Value : "url()");

            return result.Length > MaxLength ? result[..MaxLength] : result;
        }

        /// <summary>
        /// URL 白名单：锚点 / data:image 内嵌图片 / 站内相对路径。
        /// 注意：故意不放行 http(s)，外部资源加载由后端与服务端双重拦截（审查 A12）。
        /// </summary>
        private static bool IsSafeUrl(string url)
            => url.StartsWith("#")
            || url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("/");
    }
}
