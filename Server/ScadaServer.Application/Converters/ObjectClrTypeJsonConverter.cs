using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScadaServer.Application.Converters
{
    /// <summary>
    /// 将 object 类型的 JSON 值反序列化为 CLR 原始类型（bool/long/double/string），
    /// 而非默认的 JsonElement。JsonElement 不实现 IConvertible，会导致
    /// Convert.ToInt16 等驱动层类型转换抛 InvalidCastException。
    /// 序列化（写）行为与默认一致：按运行时类型输出。
    /// 在 <see cref="WebApi.Extensions.AuthenticationExtensions"/> 中全局注册到
    /// JSON 序列化选项的 Converters 集合，作用于所有 object 类型属性。
    /// </summary>
    public class ObjectClrTypeJsonConverter : JsonConverter<object>
    {
        /// <summary>
        /// 将 JSON 令牌反序列化为对应的 CLR 原始类型：
        /// 布尔→bool、数字→long（Integer 语义）或 double（小数/越界）、字符串→string、
        /// 空值→null，其余复杂结构维持默认 <see cref="JsonElement"/>。
        /// </summary>
        /// <param name="reader">UTF-8 JSON 读取器，已定位在待解析的令牌上</param>
        /// <param name="typeToConvert">目标类型，此处为 object</param>
        /// <param name="options">当前序列化的 <see cref="JsonSerializerOptions"/></param>
        /// <returns>解析得到的 CLR 原始类型值；若为 null 令牌返回 null</returns>
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                // 整数优先 long（兼容 Convert.ToInt16/ToInt32/ToByte），
                // 小数或超出 long 范围回落 double（兼容 Convert.ToSingle/ToDouble）
                JsonTokenType.Number => reader.TryGetInt64(out var l) ? l : reader.GetDouble(),
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Null => null,
                // 对象/数组等复杂结构维持默认 JsonElement 行为
                _ => JsonElement.ParseValue(ref reader)
            };
        }

        /// <summary>
        /// 按运行时类型将值序列化为 JSON，与默认 object 写出行为保持一致。
        /// </summary>
        /// <param name="writer">UTF-8 JSON 写入器</param>
        /// <param name="value">要序列化的对象值（可能为 null）</param>
        /// <param name="options">当前序列化的 <see cref="JsonSerializerOptions"/></param>
        public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
        {
            // 按运行时类型序列化，与默认 object 写出行为保持一致；
            // 运行时类型（long/bool等）不会命中本转换器，无递归风险
            JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object), options);
        }
    }
}