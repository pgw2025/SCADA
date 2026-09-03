using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScadaServer.Domain.Addresses
{
    /// <summary>
    /// 设备变量结构化地址配置（JSON）。
    /// <para>
    /// 地址的<strong>权威机读形态</strong>：不同协议以 <see cref="Protocol"/> 判别器区分，
    /// 各协议字段相互独立（未使用字段保持默认值/空）。前端只编辑本结构，后端持久化它，
    /// 并由 <see cref="AddressConfigSerializer.ToDisplay"/> 自动生成可读/驱动可消费的展示串
    /// （写回 <c>DataPointMapping.Address</c>），保证"JSON = 权威源、字符串 = 展示冗余"的一致性。
    /// </para>
    /// </summary>
    public class AddressConfig
    {
        /// <summary>协议判别器：S7 / OPCUA / Modbus / Virtual。</summary>
        [JsonPropertyName("protocol")]
        public string Protocol { get; set; } = string.Empty;

        // ===== S7 =====
        /// <summary>存储区域：DB / I / Q / M。</summary>
        [JsonPropertyName("area")]
        public string? Area { get; set; }

        /// <summary>DB 号（仅 DB 区域有效，非 DB 区域为 0）。</summary>
        [JsonPropertyName("dbNumber")]
        public int DbNumber { get; set; }

        /// <summary>字节偏移。</summary>
        [JsonPropertyName("byteOffset")]
        public int ByteOffset { get; set; }

        /// <summary>位偏移（-1 表示非位地址；位地址仅 0~7）。</summary>
        [JsonPropertyName("bitOffset")]
        public int BitOffset { get; set; } = -1;

        /// <summary>访问宽度：BIT / BYTE / WORD / DWORD。</summary>
        [JsonPropertyName("width")]
        public string? Width { get; set; }

        // ===== OPC UA =====
        /// <summary>节点标识（如 "ns=2;i=5"）。</summary>
        [JsonPropertyName("nodeId")]
        public string? NodeId { get; set; }

        // ===== Modbus =====
        /// <summary>功能码（读 3/4，写 6/16）。</summary>
        [JsonPropertyName("function")]
        public int Function { get; set; } = 3;

        /// <summary>起始地址（协议层地址，如 40001）。</summary>
        [JsonPropertyName("startAddress")]
        public int StartAddress { get; set; }

        /// <summary>寄存器数量。</summary>
        [JsonPropertyName("registerCount")]
        public int RegisterCount { get; set; } = 1;

        /// <summary>位索引（位访问时使用，-1 表示非位）。</summary>
        [JsonPropertyName("bitIndex")]
        public int BitIndex { get; set; } = -1;
    }

    /// <summary>
    /// 地址配置的 JSON 序列化 / 反序列化 / 展示串生成 / 回填解析工具。
    /// 固定使用 camelCase 键名（与前端构造的 JSON 对齐），大小写不敏感读入。
    /// </summary>
    public static class AddressConfigSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private sealed class AddressConfigMin
        {
            [JsonPropertyName("protocol")] public string Protocol { get; set; } = string.Empty;
        }

        /// <summary>序列化为紧凑 JSON 字符串；输入为 null 时返回 null。</summary>
        public static string? Serialize(AddressConfig? config)
            => config == null ? null : JsonSerializer.Serialize(config, Options);

        /// <summary>反序列化；空串/非法 JSON 返回 null（不抛异常）。</summary>
        public static AddressConfig? Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<AddressConfig>(json, Options); }
            catch (JsonException) { return null; }
        }

        /// <summary>读取 JSON 中的协议判别器（用于按协议路由），失败返回空串。</summary>
        public static string PeekProtocol(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("protocol", out var p) ? p.GetString() ?? string.Empty : string.Empty;
            }
            catch (JsonException) { return string.Empty; }
        }

        /// <summary>
        /// 由结构化地址生成可读展示串（写回 DataPointMapping.Address）。
        /// 生成的串需被对应驱动的地址解析器接受：
        /// S7 → <c>S7Driver.ParseAddress</c>；OPC UA → NodeId；Modbus → 起始地址。
        /// 结构非法或协议不支持时返回 null。
        /// </summary>
        public static string? ToDisplay(AddressConfig? config)
        {
            if (config == null) return null;

            switch (config.Protocol.Trim().ToUpperInvariant())
            {
                case "S7":
                    return BuildS7Display(config);
                case "OPCUA":
                    return string.IsNullOrWhiteSpace(config.NodeId) ? null : config.NodeId.Trim();
                case "MODBUS":
                    return config.StartAddress >= 0 ? config.StartAddress.ToString() : null;
                case "VIRTUAL":
                    return null; // 虚拟设备无地址
                default:
                    return null;
            }
        }

        /// <summary>
        /// 由历史展示串反向构建结构化地址（一次性回填用），失败与不支持返回 null。
        /// </summary>
        public static AddressConfig? BuildFromDisplay(string? display, string protocol)
        {
            if (string.IsNullOrWhiteSpace(display)) return null;

            switch (protocol.Trim().ToUpperInvariant())
            {
                case "S7":
                    return ParseS7Display(display.Trim());
                case "OPCUA":
                    return new AddressConfig { Protocol = "OPCUA", NodeId = display.Trim() };
                case "MODBUS":
                    return int.TryParse(display.Trim(), out var sa) && sa >= 0
                        ? new AddressConfig { Protocol = "Modbus", Function = 3, StartAddress = sa, RegisterCount = 1 }
                        : null;
                default:
                    return null;
            }
        }

        private static string? BuildS7Display(AddressConfig c)
        {
            var area = c.Area?.Trim().ToUpperInvariant();
            var width = c.Width?.Trim().ToUpperInvariant();
            if (c.ByteOffset < 0) return null;
            bool isBit = c.BitOffset >= 0 && c.BitOffset <= 7 && width == "BIT";

            // BitOffset sentinel 为 -1（非位地址）；位地址偏移必须 0~7；非 BIT 宽度不允许携带位后缀。
            // 先判定越界（-1 表示非位，属合法 sentinel），再按“是否位”细分。
            if (c.BitOffset is > 7 or < -1) return null;
            if (width == "BIT" && (c.BitOffset < 0 || c.BitOffset > 7)) return null;
            if (width != "BIT" && c.BitOffset > -1) return null;

            switch (area)
            {
                case "DB":
                    if (c.DbNumber < 1) return null;
                    return isBit
                        ? $"DB{c.DbNumber}.DBX{c.ByteOffset}.{c.BitOffset}"
                        : $"DB{c.DbNumber}.DB{WidthSuffix(width)}{c.ByteOffset}";
                case "I":
                    return isBit ? $"I{c.ByteOffset}.{c.BitOffset}" : $"{WidthPrefix("I", width)}{c.ByteOffset}";
                case "Q":
                    return isBit ? $"Q{c.ByteOffset}.{c.BitOffset}" : $"{WidthPrefix("Q", width)}{c.ByteOffset}";
                case "M":
                    return isBit ? $"M{c.ByteOffset}.{c.BitOffset}" : $"{WidthPrefix("M", width)}{c.ByteOffset}";
                default:
                    return null;
            }
        }

        private static string WidthSuffix(string? width) => width switch
        {
            "BYTE" => "B",
            "WORD" => "W",
            "DWORD" => "D",
            _ => "B"
        };

        private static string WidthPrefix(string area, string? width) => (area, width) switch
        {
            ("I", "WORD") => "IW",
            ("I", "DWORD") => "ID",
            ("I", _) => "IB",
            ("Q", "WORD") => "QW",
            ("Q", "DWORD") => "QD",
            ("Q", _) => "QB",
            ("M", "WORD") => "MW",
            ("M", "DWORD") => "MD",
            _ => "MB"
        };

        private static AddressConfig? ParseS7Display(string s)
        {
            // 对齐 S7Driver.S7AddressRegex 的语法
            var match = System.Text.RegularExpressions.Regex.Match(
                s,
                @"^(?:DB(?<db>\d+)\.)?(?<type>DBX|DBB|DBW|DBD|DBR|I|Q|M|IB|IW|ID|QB|QW|QD|MB|MW|MD)(?<offset>\d+)(?:\.(?<bit>\d+))?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            var typeStr = match.Groups["type"].Value.ToUpperInvariant();
            if (!int.TryParse(match.Groups["offset"].Value, out var offset)) return null;

            // DB 前缀只能搭配 DB 类型；形如 DB1.MW10 / DB1.IB5 视为非法
            bool hasDbPrefix = match.Groups["db"].Success && !string.IsNullOrEmpty(match.Groups["db"].Value);
            if (hasDbPrefix && typeStr is not ("DBX" or "DBB" or "DBW" or "DBD" or "DBR")) return null;

            var hasBit = match.Groups["bit"].Success && !string.IsNullOrEmpty(match.Groups["bit"].Value);
            bool isBit = typeStr is "DBX" or "I" or "Q" or "M";
            if (isBit != hasBit) return null; // 位类型 must have .bit; 非位 must not

            int bit = hasBit ? (int.TryParse(match.Groups["bit"].Value, out var b) ? b : -1) : -1;
            if (bit is > 7 or < -1) return null;                   // 越界一律拒绝
            if (isBit && (bit is < 0 or > 7)) return null;         // 位类型必须 0~7
            if (!isBit && bit > -1) return null;                   // 非位类型不得携带位后缀

            string area; int db = 0; string width;
            switch (typeStr)
            {
                case "DBX": area = "DB"; width = "BIT"; break;
                case "DBB": area = "DB"; width = "BYTE"; break;
                case "DBW": area = "DB"; width = "WORD"; break;
                case "DBD":
                case "DBR": area = "DB"; width = "DWORD"; break;
                case "I": area = "I"; width = "BIT"; break;
                case "IB": area = "I"; width = "BYTE"; break;
                case "IW": area = "I"; width = "WORD"; break;
                case "ID": area = "I"; width = "DWORD"; break;
                case "Q": area = "Q"; width = "BIT"; break;
                case "QB": area = "Q"; width = "BYTE"; break;
                case "QW": area = "Q"; width = "WORD"; break;
                case "QD": area = "Q"; width = "DWORD"; break;
                case "M": area = "M"; width = "BIT"; break;
                case "MB": area = "M"; width = "BYTE"; break;
                case "MW": area = "M"; width = "WORD"; break;
                case "MD": area = "M"; width = "DWORD"; break;
                default: return null;
            }

            if (area == "DB")
            {
                if (!match.Groups["db"].Success || !int.TryParse(match.Groups["db"].Value, out db) || db < 1) return null;
            }

            return new AddressConfig
            {
                Protocol = "S7",
                Area = area,
                DbNumber = db,
                ByteOffset = offset,
                BitOffset = bit,
                Width = width
            };
        }
    }
}