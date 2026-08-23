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

        public override void Write(Utf8JsonWriter writer, DeviceType value, JsonSerializerOptions options)
        {
            var str = SerializeMap.TryGetValue(value, out var mapped) ? mapped : value.ToString();
            writer.WriteStringValue(str);
        }
    }
}
