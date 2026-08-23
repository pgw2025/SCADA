using System.Text.Json;
using System.Text.RegularExpressions;
using S7.Net;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces;

namespace ScadaServer.Infrastructure.Communication
{
    public class S7Driver : IProtocolDriver
    {
        private Plc _plc;
        
        // 支持的格式：DB1.DBX0.0, DB1.DBW10, I0.1, Q0.0, M10.0, MW10, MD10, IB0, QB0, MB0 等
        private static readonly Regex S7AddressRegex = new Regex(
            @"^(?:DB(?<db>\d+)\.)?(?<type>DBX|DBB|DBW|DBD|I|Q|M|IB|IW|ID|QB|QW|QD|MB|MW|MD)(?<offset>\d+)(?:\.(?<bit>\d+))?$", 
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public async Task<bool> ConnectAsync(Device device, string configJson)
        {
            // 从 JSON 反序列化配置
            var config = JsonSerializer.Deserialize<S7Config>(configJson);
            if (config == null)
            {
                throw new ArgumentException("无效的 S7 协议配置");
            }

            var cpuType = config.CpuType?.ToUpper() switch
            {
                "S71200" => CpuType.S71200,
                "S71500" => CpuType.S71500,
                "S7300" => CpuType.S7300,
                "S7400" => CpuType.S7400,
                _ => CpuType.S71200
            };

            _plc = new Plc(cpuType, config.IpAddress, (short)config.Rack, (short)config.Slot);
            await _plc.OpenAsync();
            return _plc.IsConnected;
        }

        public async Task<object> ReadAsync(ModelVariable variable)
        {
            if (_plc == null || !_plc.IsConnected) return null;
            return await _plc.ReadAsync(variable.Address);
        }

        public async Task<IDictionary<string, object>> ReadBatchAsync(IEnumerable<ModelVariable> variables)
        {
            var results = new Dictionary<string, object>();
            if (_plc == null || !_plc.IsConnected) return results;

            // 1. 解析地址并按 S7.Net 的 DataType 和 DBNumber 分组
            var groups = variables.Select(v => new { Variable = v, Info = ParseAddress(v.Address) })
                                 .Where(x => x.Info != null)
                                 .GroupBy(x => new { x.Info.S7Area, x.Info.DbNumber });

            foreach (var group in groups)
            {
                var s7Area = group.Key.S7Area;
                int dbNumber = group.Key.DbNumber;
                var varInfos = group.ToList();

                // 2. 计算该组的地址范围
                int minOffset = varInfos.Min(x => x.Info.ByteOffset);
                int maxOffset = varInfos.Max(x => x.Info.ByteOffset + x.Info.ByteLength);
                int length = maxOffset - minOffset;

                // 3. 执行批量读取
                byte[] buffer = await _plc.ReadBytesAsync(s7Area, dbNumber, minOffset, length);

                // 4. 解析结果
                foreach (var item in varInfos)
                {
                    var info = item.Info;
                    var variable = item.Variable;
                    int relativeOffset = info.ByteOffset - minOffset;

                    object value = info.ValueType switch
                    {
                        "BIT" => (buffer[relativeOffset] & (1 << info.BitOffset)) != 0,
                        "BYTE" => buffer[relativeOffset],
                        "INT" => S7.Net.Types.Int.FromByteArray(new byte[] { buffer[relativeOffset], buffer[relativeOffset + 1] }),
                        "REAL" => S7.Net.Types.Real.FromByteArray(new byte[] { buffer[relativeOffset], buffer[relativeOffset + 1], buffer[relativeOffset + 2], buffer[relativeOffset + 3] }),
                        "DINT" => S7.Net.Types.DInt.FromByteArray(new byte[] { buffer[relativeOffset], buffer[relativeOffset + 1], buffer[relativeOffset + 2], buffer[relativeOffset + 3] }),
                        _ => null
                    };

                    if (value != null)
                    {
                        results[variable.Key] = value;
                    }
                }
            }

            return results;
        }

        public Task SubscribeAsync(IEnumerable<ModelVariable> variables, Action<string, object> onValueChanged)
        {
            // S7 doesn't support native subscription. Worker will handle polling.
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(IEnumerable<ModelVariable> variables)
        {
            return Task.CompletedTask;
        }

        public async Task DisconnectAsync()
        {
            if (_plc != null)
            {
                await Task.Run(() => _plc.Close());
                _plc = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
        }

        private S7AddressInfo ParseAddress(string address)
        {
            var match = S7AddressRegex.Match(address);
            if (!match.Success) return null;

            string typeStr = match.Groups["type"].Value.ToUpper();
            int offset = int.Parse(match.Groups["offset"].Value);
            int bit = match.Groups["bit"].Success ? int.Parse(match.Groups["bit"].Value) : 0;
            int db = match.Groups["db"].Success ? int.Parse(match.Groups["db"].Value) : 0;

            var info = new S7AddressInfo { ByteOffset = offset, BitOffset = bit, DbNumber = db };

            // 映射 S7.Net 的 DataType
            if (typeStr.StartsWith("DB"))
            {
                info.S7Area = DataType.DataBlock;
                info.ValueType = typeStr switch
                {
                    "DBX" => "BIT",
                    "DBB" => "BYTE",
                    "DBW" => "INT",
                    "DBD" => "DINT",
                    _ => "BYTE"
                };
            }
            else if (typeStr.StartsWith("I"))
            {
                info.S7Area = DataType.Input;
                info.ValueType = typeStr switch { "IB" => "BYTE", "IW" => "INT", "ID" => "DINT", _ => "BIT" };
            }
            else if (typeStr.StartsWith("Q"))
            {
                info.S7Area = DataType.Output;
                info.ValueType = typeStr switch { "QB" => "BYTE", "QW" => "INT", "QD" => "DINT", _ => "BIT" };
            }
            else if (typeStr.StartsWith("M"))
            {
                info.S7Area = DataType.Memory;
                info.ValueType = typeStr switch { "MB" => "BYTE", "MW" => "INT", "MD" => "DINT", _ => "BIT" };
            }

            info.ByteLength = info.ValueType switch
            {
                "BIT" => 1,
                "BYTE" => 1,
                "INT" => 2,
                "DINT" => 4,
                "REAL" => 4,
                _ => 1
            };

            return info;
        }

        private class S7AddressInfo
        {
            public DataType S7Area { get; set; }
            public int DbNumber { get; set; }
            public string ValueType { get; set; }
            public int ByteOffset { get; set; }
            public int BitOffset { get; set; }
            public int ByteLength { get; set; }
        }
    }
}
