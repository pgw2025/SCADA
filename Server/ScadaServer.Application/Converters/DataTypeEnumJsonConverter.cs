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
    /// 通过 <c>[JsonConverter(typeof(DataTypeEnumJsonConverter))]</c> 特性标注在
    /// <see cref="DTOs.DataPointDto"/> / <see cref="DTOs.DataPointMappingDto"/> 的 DataType 属性上。
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

        /// <summary>
        /// 将 JSON 令牌反序列化为 <see cref="DataTypeEnum"/>。
        /// 支持三种输入：数字令牌、常见前端别名（大小写不敏感）、后端规范枚举名（大小写不敏感）。
        /// </summary>
        /// <param name="reader">UTF-8 JSON 读取器，已定位在待解析的令牌上</param>
        /// <param name="typeToConvert">目标类型，此处恒为 <see cref="DataTypeEnum"/></param>
        /// <param name="options">当前序列化的 <see cref="JsonSerializerOptions"/></param>
        /// <returns>解析得到的 <see cref="DataTypeEnum"/> 枚举值</returns>
        /// <exception cref="JsonException">值缺失/为空，或无法识别的数据类型时抛出</exception>
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

        /// <summary>
        /// 将 <see cref="DataTypeEnum"/> 序列化为后端规范枚举成员的字符串名称（如 "FLOAT"、"BOOL"）。
        /// </summary>
        /// <param name="writer">UTF-8 JSON 写入器，用于写出字符串</param>
        /// <param name="value">要序列化的 <see cref="DataTypeEnum"/> 枚举值</param>
        /// <param name="options">当前序列化的 <see cref="JsonSerializerOptions"/></param>
        public override void Write(Utf8JsonWriter writer, DataTypeEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
