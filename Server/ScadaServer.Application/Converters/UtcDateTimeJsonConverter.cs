using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScadaServer.Application.Converters
{
    /// <summary>
    /// 统一时间序列化约定：项目事件时间一律为 UTC。
    /// EF Core + SQLite 存取的 DateTime 读回后 Kind 为 <see cref="DateTimeKind.Unspecified"/>
    /// （不带时区），System.Text.Json 默认对 Unspecified 不输出 "Z"，导致前端 new Date()
    /// 按浏览器本地时区解析而显示偏差 8 小时。
    /// 此转换器在本端 JSON 序列化时把所有 Kind 归一为 UTC 并输出带 "Z" 的 ISO 字符串，
    /// 反序列化时也对无时区字符串按 UTC 处理，保证前后端时间语义一致。
    /// 在 <see cref="WebApi.Extensions.AuthenticationExtensions"/> 的 AddJsonOptions 中全局限注册。
    /// </summary>
    public class UtcDateTimeJsonConverter : JsonConverter<DateTime>
    {
        private const string OutputFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        /// <summary>
        /// 把 JSON 令牌反序列化为 <see cref="DateTime"/>。
        /// 带 "Z"/偏移的按 UTC 归一；无时区的字符串（SQLite Unspecified 形态）按 Utc 标注，
        /// 避免后续按本地时区误判。
        /// </summary>
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("不支持的时间值格式");
            }

            var raw = reader.GetString();

            if (string.IsNullOrWhiteSpace(raw))
            {
                return default;
            }

            var parsed = DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);

            return parsed.Kind switch
            {
                DateTimeKind.Utc => parsed,
                DateTimeKind.Local => parsed.ToUniversalTime(),
                _ => DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            };
        }

        /// <summary>
        /// 序列化时把 <see cref="DateTime"/> 归一为 UTC 并输出带 "Z" 的 ISO 字符串。
        /// Local → ToUniversalTime；Unspecified（SQLite 读回）→ 按 Utc 标注。
        /// </summary>
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            var utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
            writer.WriteStringValue(utc.ToString(OutputFormat, CultureInfo.InvariantCulture));
        }
    }
}