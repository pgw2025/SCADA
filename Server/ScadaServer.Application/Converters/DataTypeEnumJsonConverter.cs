using System.Text.Json;
using System.Text.Json.Serialization;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.Converters
{
    /// <summary>
    /// 解决前后端数据类型枚举命名不一致导致的反序列化失败：
    /// 后端 <see cref="DataTypeEnum"/> 成员为 INT / REAL / BOOL / DINT / BYTE / BIT / FLOAT / DOUBLE / STRING 等，
    /// 前端曾使用 Boolean / Int16 / Int32 / Float / Double / String / Integer / Word / Char 等习惯命名。
    /// 反序列化时按大小写不敏感 + 常见别名映射解析，序列化时输出后端规范枚举名。
    /// </summary>
    public class DataTypeEnumJsonConverter : JsonConverter<DataTypeEnum>
    {
        // 前端习惯命名 -> 后端枚举的别名映射（大小写不敏感）
        private static readonly Dictionary<string, DataTypeEnum> AliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Boolean", DataTypeEnum.BOOL },
            { "Int8", DataTypeEnum.INT },
            { "Int16", DataTypeEnum.INT },
            { "Int32", DataTypeEnum.DINT },
            { "Int64", DataTypeEnum.INT64 },
            { "UInt8", DataTypeEnum.INT },
            { "UInt16", DataTypeEnum.UINT16 },
            { "UInt32", DataTypeEnum.UINT32 },
            { "UInt64", DataTypeEnum.UINT64 },
            { "Float", DataTypeEnum.FLOAT },
            { "Double", DataTypeEnum.DOUBLE },
            { "Real", DataTypeEnum.REAL },
            { "String", DataTypeEnum.STRING },
            { "Word", DataTypeEnum.WORD },
            { "Char", DataTypeEnum.CHAR },
            { "Byte", DataTypeEnum.BYTE }
        };

        public override DataTypeEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            {
                return (DataTypeEnum)number;
            }

            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
            {
                throw new JsonException("数据类型不能为空");
            }

            // 1. 优先匹配常见前端别名
            if (AliasMap.TryGetValue(str, out var aliased))
            {
                return aliased;
            }

            // 2. 兼容直接传入规范枚举成员名（如 "BOOL"、"FLOAT"），大小写不敏感
            if (Enum.TryParse<DataTypeEnum>(str, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            throw new JsonException($"无法识别的数据类型: {str}");
        }

        public override void Write(Utf8JsonWriter writer, DataTypeEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
