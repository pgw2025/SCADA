using System;
using System.Collections.Generic;
using System.Net;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 外部消息模板渲染引擎：把模板中的 <c>{占位符}</c> 替换为事件实际值。
    /// <para>无状态、可注册为单例。钉钉 Markdown 不转义；邮件 HTML 全量转义防注入。</para>
    /// </summary>
    public class NotificationTemplateEngine
    {
        /// <summary>
        /// 渲染模板。未知占位符（tokens 中不存在）原样保留在结果中，便于发现拼写错误。
        /// </summary>
        public string Render(string template, IReadOnlyDictionary<string, string?> tokens, bool htmlEncode = false)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            var result = template;
            foreach (var (key, value) in tokens)
            {
                var encoded = htmlEncode ? WebUtility.HtmlEncode(value ?? string.Empty) : (value ?? string.Empty);
                result = result.Replace("{" + key + "}", encoded);
            }
            return result;
        }
    }
}