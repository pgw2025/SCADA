using System.Text.Json;
using System.Text.Json.Serialization;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.Converters
{
    /// <summary>
    /// 解决前后端设备类型枚举命名不一致的问题：
    /// 后端枚举成员为 OpcUa / S7 / Mqtt / Virtual ...，
    /// 前端使用全大写字符串 "OPCUA" / "S7" / "MQTT" / "Virtual"。
    /// 此转换器在序列化时输出前端约定的字符串，反序列化时按大小写不敏感解析。
    /// 在 <see cref="WebApi.Extensions.AuthenticationExtensions"/> 中全局注册到
    /// JSON 序列化选项的 Converters 集合，作用于设备类型的枚举属性。
    /// </summary>
    public class DeviceTypeJsonConverter : JsonConverter<DeviceType>
    {
        private static readonly Dictionary<DeviceType, string> SerializeMap = new()
        {
            { DeviceType.S7, "S7" },
            { DeviceType.ModbusTcp, "ModbusTcp" },
            { DeviceType.OpcUa, "OPCUA" },
            { DeviceType.Mqtt, "MQTT" },
            { DeviceType.Virtual, "Virtual" },
            { DeviceType.BACnet, "BACnet" },
            { DeviceType.DNP3, "DNP3" }
        };

        private static readonly Dictionary<string, DeviceType> DeserializeMap =
            SerializeMap.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 将 JSON 令牌反序列化为 <see cref="DeviceType"/>。
        /// 支持：数字令牌（按枚举数值）、约定字符串映射（大小写不敏感）、
        /// 后端枚举成员名（大小写不敏感）。
        /// </summary>
        /// <param name="reader">UTF-8 JSON 读取器，已定位在待解析的令牌上</param>
        /// <param name="typeToConvert">目标类型，此处恒为 <see cref="DeviceType"/></param>
        /// <param name="options">当前序列化的 <see cref="JsonSerializerOptions"/></param>
        /// <returns>解析得到的 <see cref="DeviceType"/> 枚举值</returns>
        /// <exception cref="JsonException">值缺失/为空，或无法识别的设备类型时抛出</exception>
        public override DeviceType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            {
                return (DeviceType)number;
            }

            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
            {
                throw new JsonException("设备类型不能为空");
            }

            // 优先匹配约定的字符串映射（大小写不敏感）
            if (DeserializeMap.TryGetValue(str, out var mapped))
            {
                return mapped;
            }

            // 兼容直接传入枚举成员名（如 "OpcUa"）或数字字符串
            if (Enum.TryParse<DeviceType>(str, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            throw new JsonException($"无法识别的设备类型: {str}");
        }

        /// <summary>
        /// 将 <see cref="DeviceType"/> 序列化为前端约定的字符串
        /// （如 S7 → "S7"、OpcUa → "OPCUA"、Mqtt → "MQTT"）。
        /// 未在映射表中的枚举值退化为调用 <c>ToString()</c> 输出成员名。
        /// </summary>
        /// <param name="writer">UTF-8 JSON 写入器，用于写出字符串</param>
        /// <param name="value">要序列化的 <see cref="DeviceType"/> 枚举值</param>
        /// <param name="options">当前序列化的 <see cref="JsonSerializerOptions"/></param>
        public override void Write(Utf8JsonWriter writer, DeviceType value, JsonSerializerOptions options)
        {
            var str = SerializeMap.TryGetValue(value, out var mapped) ? mapped : value.ToString();
            writer.WriteStringValue(str);
        }
    }
}
