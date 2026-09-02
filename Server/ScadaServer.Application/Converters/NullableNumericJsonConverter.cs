using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScadaServer.Application.Converters
{
    /// <summary>
    /// 可空数值（int? / double? 等）的反序列化容错转换器。
    /// 解决前端 <c>v-model.number</c> 在用户清空数字输入框时提交空字符串（""），
    /// 而后端 <c>int?/double?</c> 默认反序列化会把 "" 判为类型错误导致接口 400 的问题。
    /// <para>
    /// 语义约定：JSON 空串/空白串 → null（表示"未配置，回退默认/模板值"），
    /// 合法数字串/数字令牌 → 对应数值；写方向行为与默认一致。
    /// 以 <see cref="JsonConverterFactory"/> 实现，一次性覆盖所有可空数值类型属性。
    /// </para>
    /// </summary>
    public class NullableNumericJsonConverterFactory : JsonConverterFactory
    {
        /// <summary>需要被处理的 CLR 可空数值类型。</summary>
        private static readonly HashSet<Type> SupportedNullables = new()
        {
            typeof(int?), typeof(long?), typeof(double?), typeof(decimal?),
            typeof(short?), typeof(byte?), typeof(float?)
        };

        /// <inheritdoc/>
        public override bool CanConvert(Type typeToConvert)
            => Nullable.GetUnderlyingType(typeToConvert) != null
               && SupportedNullables.Contains(typeToConvert);

        /// <inheritdoc/>
        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var underlying = Nullable.GetUnderlyingType(typeToConvert)!;
            var converterType = typeof(Converter<>).MakeGenericType(underlying);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }

        /// <summary>按底层数值类型特化的可空数值转换器。</summary>
        private sealed class Converter<T> : JsonConverter<T?>
            where T : struct
        {
            /// <inheritdoc/>
            public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.Null:
                        return null;
                    case JsonTokenType.Number:
                        return (T)JsonSerializer.Deserialize<T>(ref reader, options)!;
                    case JsonTokenType.String:
                        var str = reader.GetString();
                        // 空串/空白串按"未配置"处理，回退 null
                        if (string.IsNullOrWhiteSpace(str)) return null;
                        // 合法数字串交由默认解析（与 Number 令牌走同一路径），异常上抛由框架统一映射为 400
                        using (var doc = JsonDocument.Parse(str))
                        {
                            return (T)doc.RootElement.Deserialize<T>(options)!;
                        }
                    default:
                        // null 令牌之外的非目标形态（如对象/数组），交由默认反序列化抛类型错误
                        throw new JsonException($"无法将 JSON '{(reader.TokenType)}' 转换为 {typeof(T)}");
                }
            }

            /// <inheritdoc/>
            public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
            {
                if (value is null)
                {
                    writer.WriteNullValue();
                    return;
                }
                // 按底层类型写出（int/double/decimal/...），避免命中本工厂造成递归
                JsonSerializer.Serialize(writer, value.Value, typeof(T), options);
            }
        }
    }
}