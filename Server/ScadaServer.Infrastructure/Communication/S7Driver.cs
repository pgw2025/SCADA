using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using S7.Net;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Interfaces;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>
    /// 西门子 S7 系列 PLC 通信驱动（基于 S7netplus）。
    /// 负责连接管理、单点/批量读取、地址解析与线程安全的 PLC 访问。
    /// 接口严格遵循 IProtocolDriver，不引入任何第三方库。
    /// </summary>
    public class S7Driver : IProtocolDriver
    {
        #region 字段与常量

        /// <summary>PLC 对象。S7netplus 的 Plc 非线程安全，所有访问必须经由 _plcLock 串行化。</summary>
        private Plc? _plc;

        /// <summary>PLC 访问互斥锁。保证任意时刻仅一个采集任务与 PLC 通信，避免 Socket 异常/数据错乱/ObjectDisposedException。</summary>
        private readonly SemaphoreSlim _plcLock = new SemaphoreSlim(1, 1);

        /// <summary>已释放标志，防止重复 Dispose 与释放后的锁访问。</summary>
        private bool _disposed;

        /// <summary>点表诊断用：地址非法标记。</summary>
        private const string InvalidAddress = "INVALID_ADDRESS";

        /// <summary>点表诊断用：PLC 读取异常标记。</summary>
        private const string ReadError = "READ_ERROR";

        /// <summary>
        /// 支持地址格式（不区分大小写）：
        ///   DB1.DBX0.0 / DB1.DBW10 / DB1.DBD20 / DB1.DBR30
        ///   I0.0 / IB0 / IW0 / ID0
        ///   Q0.0 / QB0 / QW0 / QD0
        ///   M0.0 / MB0 / MW0 / MD0
        /// DBR 为新增 REAL 类型支持。
        /// </summary>
        private static readonly Regex S7AddressRegex = new Regex(
            @"^(?:DB(?<db>\d+)\.)?(?<type>DBX|DBB|DBW|DBD|DBR|I|Q|M|IB|IW|ID|QB|QW|QD|MB|MW|MD)(?<offset>\d+)(?:\.(?<bit>\d+))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        #endregion

        #region 连接管理

        public async Task<bool> ConnectAsync(IRuntimeDevice device)
        {
            if (_disposed)
                return false;

            var configJson = device.ConfigJson;
            if (string.IsNullOrWhiteSpace(configJson))
                throw new ArgumentException("S7 协议配置不能为空", nameof(device));

            var config = JsonSerializer.Deserialize<S7Config>(configJson);
            if (config == null)
                throw new ArgumentException("无效的 S7 协议配置");

            // 参数合法性检查
            ValidateConfig(config);

            await _plcLock.WaitAsync();
            try
            {
                // 已有连接：先安全断开旧连接，避免句柄/套接字泄漏
                if (_plc != null)
                {
                    ClosePlcInstance(_plc);
                    _plc = null;
                }

                var cpuType = config.CpuType?.ToUpperInvariant() switch
                {
                    "S71200" => CpuType.S71200,
                    "S71500" => CpuType.S71500,
                    "S7300" => CpuType.S7300,
                    "S7400" => CpuType.S7400,
                    _ => CpuType.S71200
                };

                Plc? newPlc = null;
                try
                {
                    newPlc = new Plc(cpuType, config.IpAddress, (short)config.Rack, (short)config.Slot);
                    await newPlc.OpenAsync();

                    if (newPlc.IsConnected)
                    {
                        _plc = newPlc;
                        return true;
                    }

                    // OpenAsync 未抛异常但连接未建立：清理并返回失败
                    ClosePlcInstance(newPlc);
                    return false;
                }
                catch (Exception)
                {
                    // 连接失败：自动清理 _plc，避免半开连接残留
                    ClosePlcInstance(newPlc);
                    _plc = null;
                    return false;
                }
            }
            finally
            {
                _plcLock.Release();
            }
        }

        /// <summary>校验 S7 连接参数：IP 合法性、Rack/Slot 范围。</summary>
        private static void ValidateConfig(S7Config config)
        {
            if (string.IsNullOrWhiteSpace(config.IpAddress) || config.IpAddress.Contains(' ', StringComparison.Ordinal))
                throw new ArgumentException("S7 配置的 IP 地址不合法（为空或含空格）", nameof(config.IpAddress));

            // 若是标准 IP 字面量则进一步校验；否则按主机名交由底层 DNS 解析
            if (IPAddress.TryParse(config.IpAddress, out var addr))
            {
                if (addr.Equals(IPAddress.Any) || addr.Equals(IPAddress.IPv6Any))
                    throw new ArgumentException("S7 配置的 IP 地址无效（0.0.0.0）", nameof(config.IpAddress));
            }

            if (config.Rack < 0 || config.Rack > 31)
                throw new ArgumentOutOfRangeException(nameof(config.Rack), "Rack 取值必须介于 0~31");
            if (config.Slot < 0 || config.Slot > 31)
                throw new ArgumentOutOfRangeException(nameof(config.Slot), "Slot 取值必须介于 0~31");
        }

        #endregion

        #region 读取

        public async Task<object> ReadAsync(IRuntimeVariable variable)
        {
            if (_disposed || variable == null)
                return null;

            await _plcLock.WaitAsync();
            try
            {
                if (_plc == null || !_plc.IsConnected)
                    return null;

                // 单点读取仍委托 S7netplus 解析地址（DBX/DBW/DBD/IB/IW/... 均原生支持）。
                // 地址来源：RuntimeVariable.Address（由 DeviceVariable.Address 解析，驱动不感知 ModelVariable）。
                return await _plc.ReadAsync(variable.Address);
            }
            catch (Exception)
            {
                // 防止单个变量异常导致整个采集循环崩溃
                return null;
            }
            finally
            {
                _plcLock.Release();
            }
        }

        public async Task<IDictionary<string, object>> ReadBatchAsync(IEnumerable<IRuntimeVariable> variables)
        {
            var results = new Dictionary<string, object>();
            if (_disposed || variables == null)
                return results;

            await _plcLock.WaitAsync();
            try
            {
                // 1) 解析地址：非法地址立即反馈 INVALID_ADDRESS，空变量跳过
                // 地址来源：RuntimeVariable.Address（DeviceVariable.Address 权威，驱动不感知 ModelVariable）。
                var valid = new List<(IRuntimeVariable Variable, S7AddressInfo Info)>();
                foreach (var v in variables)
                {
                    if (v == null)
                        continue;
                    if (string.IsNullOrWhiteSpace(v.Key))
                        continue; // 无 Key 无法上报，跳过

                    var info = ParseAddress(v.Address);
                    if (info == null)
                    {
                        results[v.Key] = InvalidAddress;
                        continue;
                    }
                    valid.Add((v, info));
                }

                // 2) 连接状态检查：未连接时其余变量全部标记 READ_ERROR
                if (_plc == null || !_plc.IsConnected)
                {
                    foreach (var item in valid)
                        results[item.Variable.Key] = ReadError;
                    return results;
                }

                // 3) 按 DataType + DBNumber 分组，减少通信往返
                var groups = valid.GroupBy(x => new { x.Info.S7Area, x.Info.DbNumber });

                foreach (var group in groups)
                {
                    int dbNumber = group.Key.DbNumber;
                    var varInfos = group.ToList();
                    if (varInfos.Count == 0)
                        continue;

                    // 4) 计算连续地址区间，一次读取
                    int minOffset = varInfos.Min(x => x.Info.ByteOffset);
                    int maxOffset = varInfos.Max(x => x.Info.ByteOffset + x.Info.ByteLength);
                    int length = maxOffset - minOffset;
                    if (length <= 0)
                        continue;

                    byte[]? buffer;
                    try
                    {
                        buffer = await _plc.ReadBytesAsync(group.Key.S7Area, dbNumber, minOffset, length);
                    }
                    catch (Exception)
                    {
                        // 该组读取异常：整组标记 READ_ERROR，便于点表诊断
                        foreach (var item in varInfos)
                            results[item.Variable.Key] = ReadError;
                        continue;
                    }

                    // S7netplus 读取失败时可能返回 null
                    if (buffer == null)
                    {
                        foreach (var item in varInfos)
                            results[item.Variable.Key] = ReadError;
                        continue;
                    }

                    // 5) 防越界解析
                    foreach (var item in varInfos)
                    {
                        var value = ExtractValue(buffer, item.Info, minOffset);
                        results[item.Variable.Key] = value ?? (object)ReadError;
                    }
                }
            }
            catch (Exception)
            {
                // 整体兜底：任何意外异常均不抛出，避免采集宿主崩溃
                foreach (var v in variables)
                {
                    if (v != null && !string.IsNullOrWhiteSpace(v.Key) && !results.ContainsKey(v.Key))
                        results[v.Key] = ReadError;
                }
            }
            finally
            {
                _plcLock.Release();
            }

            return results;
        }

        /// <summary>从批量读取缓冲区中按地址信息提取值，越界或解析失败返回 null。</summary>
        private static object? ExtractValue(byte[] buffer, S7AddressInfo info, int minOffset)
        {
            int rel = info.ByteOffset - minOffset;
            if (rel < 0)
                return null;

            switch (info.ValueType)
            {
                case "BIT":
                    if (rel >= buffer.Length) return null;
                    return (buffer[rel] & (1 << info.BitOffset)) != 0;

                case "BYTE":
                    if (rel >= buffer.Length) return null;
                    return buffer[rel];

                case "INT":
                    if (rel + 1 >= buffer.Length) return null;
                    return S7.Net.Types.Int.FromByteArray(new byte[] { buffer[rel], buffer[rel + 1] });

                case "REAL":
                    if (rel + 3 >= buffer.Length) return null;
                    return S7.Net.Types.Real.FromByteArray(new byte[] { buffer[rel], buffer[rel + 1], buffer[rel + 2], buffer[rel + 3] });

                case "DINT":
                    if (rel + 3 >= buffer.Length) return null;
                    return S7.Net.Types.DInt.FromByteArray(new byte[] { buffer[rel], buffer[rel + 1], buffer[rel + 2], buffer[rel + 3] });

                default:
                    return null;
            }
        }

        #endregion

        #region 订阅（S7 不支持原生订阅，由外部轮询驱动）

        public Task SubscribeAsync(IEnumerable<IRuntimeVariable> variables, Action<string, object> onValueChanged)
        {
            // S7 不支持原生订阅，采集由 Worker 轮询 ReadBatchAsync 实现。
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(IEnumerable<IRuntimeVariable> variables)
        {
            return Task.CompletedTask;
        }

        #endregion

        #region 断开与释放

        public async Task DisconnectAsync()
        {
            if (_disposed)
                return;

            await _plcLock.WaitAsync();
            try
            {
                if (_plc != null)
                {
                    // 保持原有防止 Close 阻塞设计：卸载到线程池执行
                    await Task.Run(() => ClosePlcInstance(_plc));
                    _plc = null;
                }
            }
            finally
            {
                _plcLock.Release();
            }
        }

        /// <summary>安全关闭单个 Plc 实例（不触碰 _plc 字段、不再次加锁）。</summary>
        private static void ClosePlcInstance(Plc? plc)
        {
            if (plc == null)
                return;
            try
            {
                if (plc.IsConnected)
                    plc.Close();
            }
            catch
            {
                // 关闭过程中的异常（如套接字已断开）忽略，确保资源释放
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            // 先断开连接（此时锁仍有效）
            await DisconnectAsync();

            // 再释放信号量，避免重复释放
            try
            {
                _plcLock.Dispose();
            }
            catch
            {
                // 忽略重复释放
            }
        }

        #endregion

        #region 地址解析

        /// <summary>
        /// 解析 S7 地址字符串为内部表示。
        /// 非法地址（格式错误、Bit 偏移超出 0~7、位类型缺失 bit 后缀等）返回 null。
        /// </summary>
        private S7AddressInfo? ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return null;

            var match = S7AddressRegex.Match(address.Trim());
            if (!match.Success)
                return null;

            string typeStr = match.Groups["type"].Value.ToUpperInvariant();
            int offset = int.Parse(match.Groups["offset"].Value, CultureInfo.InvariantCulture);
            bool hasBit = match.Groups["bit"].Success && !string.IsNullOrEmpty(match.Groups["bit"].Value);
            int bit = hasBit ? int.Parse(match.Groups["bit"].Value, CultureInfo.InvariantCulture) : 0;
            int db = match.Groups["db"].Success && !string.IsNullOrEmpty(match.Groups["db"].Value)
                ? int.Parse(match.Groups["db"].Value, CultureInfo.InvariantCulture)
                : 0;

            bool isBitType = typeStr is "DBX" or "I" or "Q" or "M";

            // 地址合法性检查：Bit 偏移只能 0~7
            if (hasBit && (bit < 0 || bit > 7))
                return null;
            // Bit 后缀仅允许出现在位类型（DBX/I/Q/M）上
            if (hasBit && !isBitType)
                return null;
            // 位类型必须携带 .bit 后缀
            if (isBitType && !hasBit)
                return null;

            var info = new S7AddressInfo
            {
                ByteOffset = offset,
                BitOffset = bit,
                DbNumber = db
            };

            switch (typeStr)
            {
                // DataBlock
                case "DBX": info.S7Area = DataType.DataBlock; info.ValueType = "BIT";  info.ByteLength = 1; break;
                case "DBB": info.S7Area = DataType.DataBlock; info.ValueType = "BYTE"; info.ByteLength = 1; break;
                case "DBW": info.S7Area = DataType.DataBlock; info.ValueType = "INT";  info.ByteLength = 2; break;
                case "DBD": info.S7Area = DataType.DataBlock; info.ValueType = "DINT"; info.ByteLength = 4; break;
                case "DBR": info.S7Area = DataType.DataBlock; info.ValueType = "REAL"; info.ByteLength = 4; break;
                // Input
                case "I":  info.S7Area = DataType.Input;  info.ValueType = "BIT";  info.ByteLength = 1; break;
                case "IB": info.S7Area = DataType.Input;  info.ValueType = "BYTE"; info.ByteLength = 1; break;
                case "IW": info.S7Area = DataType.Input;  info.ValueType = "INT";  info.ByteLength = 2; break;
                case "ID": info.S7Area = DataType.Input;  info.ValueType = "DINT"; info.ByteLength = 4; break;
                // Output
                case "Q":  info.S7Area = DataType.Output; info.ValueType = "BIT";  info.ByteLength = 1; break;
                case "QB": info.S7Area = DataType.Output; info.ValueType = "BYTE"; info.ByteLength = 1; break;
                case "QW": info.S7Area = DataType.Output; info.ValueType = "INT";  info.ByteLength = 2; break;
                case "QD": info.S7Area = DataType.Output; info.ValueType = "DINT"; info.ByteLength = 4; break;
                // Memory
                case "M":  info.S7Area = DataType.Memory; info.ValueType = "BIT";  info.ByteLength = 1; break;
                case "MB": info.S7Area = DataType.Memory; info.ValueType = "BYTE"; info.ByteLength = 1; break;
                case "MW": info.S7Area = DataType.Memory; info.ValueType = "INT";  info.ByteLength = 2; break;
                case "MD": info.S7Area = DataType.Memory; info.ValueType = "DINT"; info.ByteLength = 4; break;
                default:
                    return null;
            }

            return info;
        }

        #endregion

        #region 内部类型

        private sealed class S7AddressInfo
        {
            public DataType S7Area { get; set; }
            public int DbNumber { get; set; }
            public string ValueType { get; set; } = "BYTE";
            public int ByteOffset { get; set; }
            public int BitOffset { get; set; }
            public int ByteLength { get; set; }
        }

        #endregion
    }
}
