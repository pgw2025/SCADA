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
using ScadaServer.Domain.Enums;
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

        /// <summary>协议配置反序列化选项：属性名大小写不敏感，兼容 camelCase / PascalCase 两种存储格式。</summary>
        private static readonly JsonSerializerOptions ConfigJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// 批量读取聚簇的最大地址间隙（字节）。组内相邻变量的字节间隙不超过该值时才合并为一次读取；
        /// 间隙过大的孤立点（如 DB1.DBW0 与 DB1.DBW4000）单独成簇，避免为读几个字节而拖动整段区间。
        /// 注：S7netplus 的 ReadBytesAsync 对超过单次 PDU 的读取会在库内部自动分片为多次请求，驱动无需再按 PDU 切分。
        /// </summary>
        private const int MaxClusterGapBytes = 200;

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

            var config = JsonSerializer.Deserialize<S7Config>(configJson, ConfigJsonOptions);
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
                    newPlc = new Plc(cpuType, config.IpAddress, config.Port, (short)config.Rack, (short)config.Slot);
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

            if (config.Port < 1 || config.Port > 65535)
                throw new ArgumentOutOfRangeException(nameof(config.Port), "Port 取值必须介于 1~65535");

            if (config.Rack < 0 || config.Rack > 31)
                throw new ArgumentOutOfRangeException(nameof(config.Rack), "Rack 取值必须介于 0~31");
            if (config.Slot < 0 || config.Slot > 31)
                throw new ArgumentOutOfRangeException(nameof(config.Slot), "Slot 取值必须介于 0~31");
        }

        #endregion

        #region 读取

        public async Task<object?> ReadAsync(IRuntimeVariable variable)
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

        /// <summary>
        /// 批量读取多个变量值。
        /// </summary>
        /// <remarks>
        /// 返回字典的值为两类之一：真实读取值（bool/byte/short/int/float，S7 驱动的真实值不会是 string），
        /// 或诊断标记字符串 —— <see cref="ReadError"/>（未连接 / 读取失败）与 <see cref="InvalidAddress"/>（地址非法）。
        /// 调用方应先比对标记再做类型转换；该约定与接口 <c>ReadAsync</c> 以 null 表示失败的语义不同
        /// （接口签名共享于所有驱动，统一需接口级变更）。
        /// </remarks>
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

                    // 4) 组内按字节偏移升序排序后做近邻聚簇：仅当相邻变量的地址间隙
                    //    不超过 MaxClusterGapBytes 时才合并为一次读取；间隙过大的孤立点
                    //    单独成簇，避免为读几个字节而拖动整段区间（如 DB1.DBW0 与
                    //    DB1.DBW4000 同组时读取 4KB 无用数据）。
                    var ordered = group.OrderBy(x => x.Info.ByteOffset).ToList();
                    if (ordered.Count == 0)
                        continue;

                    var clusters = new List<List<(IRuntimeVariable Variable, S7AddressInfo Info)>>();
                    var current = new List<(IRuntimeVariable Variable, S7AddressInfo Info)> { ordered[0] };
                    int clusterEnd = ordered[0].Info.ByteOffset + ordered[0].Info.ByteLength;

                    for (int i = 1; i < ordered.Count; i++)
                    {
                        var item = ordered[i];
                        if (item.Info.ByteOffset - clusterEnd > MaxClusterGapBytes)
                        {
                            clusters.Add(current);
                            current = new List<(IRuntimeVariable Variable, S7AddressInfo Info)>();
                        }

                        current.Add(item);
                        clusterEnd = Math.Max(clusterEnd, item.Info.ByteOffset + item.Info.ByteLength);
                    }
                    clusters.Add(current);

                    foreach (var cluster in clusters)
                    {
                        int minOffset = cluster[0].Info.ByteOffset; // 升序排序后首元素即最小偏移
                        int maxOffset = cluster.Max(x => x.Info.ByteOffset + x.Info.ByteLength);
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
                            // 该簇读取异常：整簇标记 READ_ERROR，便于点表诊断
                            foreach (var item in cluster)
                                results[item.Variable.Key] = ReadError;
                            continue;
                        }

                        // S7netplus 读取失败时可能返回 null；同时校验返回长度，杜绝下游越界
                        if (buffer == null || buffer.Length < length)
                        {
                            foreach (var item in cluster)
                                results[item.Variable.Key] = ReadError;
                            continue;
                        }

                        // 5) 解析（缓冲区长度已校验，ExtractValue 内的边界检查为兜底防御）
                        foreach (var item in cluster)
                        {
                            var value = ExtractValue(buffer, item.Info, minOffset);
                            results[item.Variable.Key] = value ?? (object)ReadError;
                        }
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

        #region 写入

        public async Task WriteAsync(IRuntimeVariable variable, object value)
        {
            if (_disposed || variable == null)
                throw new InvalidOperationException("驱动已释放或变量无效");

            if (string.IsNullOrWhiteSpace(variable.Address))
                throw new ArgumentException("变量地址为空，无法写入", nameof(variable));

            await _plcLock.WaitAsync();
            try
            {
                if (_plc == null || !_plc.IsConnected)
                    throw new InvalidOperationException("PLC 未连接");

                // 按变量数据类型转换为设备期望类型（DBW->Int16 / DBD->Int32 / DBR->Single / DBX->bool）。
                // S7netplus WriteAsync(address, value) 会依据地址推断目标类型与字节长度，类型不匹配会抛异常。
                var converted = ConvertForWrite(variable.DataType, value);
                await _plc.WriteAsync(variable.Address, converted);
            }
            finally
            {
                _plcLock.Release();
            }
        }

        /// <summary>
        /// 将前端传入的原始值按变量数据类型转换为设备写入类型。
        /// 位 BOOL/BIT -> bool；BYTE -> byte；INT -> short；DINT -> int；REAL/FLOAT -> float；DOUBLE -> double。
        /// </summary>
        private static object ConvertForWrite(DataTypeEnum dataType, object value)
        {
            try
            {
                return dataType switch
                {
                    DataTypeEnum.BOOL or DataTypeEnum.BIT => (bool)Convert.ToBoolean(value),
                    DataTypeEnum.BYTE => Convert.ToByte(value),
                    DataTypeEnum.INT => (short)Convert.ToInt16(value),
                    DataTypeEnum.DINT => Convert.ToInt32(value),
                    DataTypeEnum.REAL or DataTypeEnum.FLOAT => Convert.ToSingle(value),
                    DataTypeEnum.DOUBLE => Convert.ToDouble(value),
                    _ => value
                };
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
            {
                // 覆盖 Convert 的全部典型转换失败：字符串格式错误（Format）、数值溢出（Overflow）、类型不兼容（InvalidCast）
                throw new InvalidOperationException($"无法将值 [{value}] 转换为数据类型 {dataType}", ex);
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

            // 必须先断开连接、再置位 _disposed：DisconnectAsync 开头会检查 _disposed，
            // 若先置位会导致其直接返回，PLC 连接永不关闭（句柄/套接字泄漏）
            await DisconnectAsync();

            _disposed = true;

            // 最后释放信号量，避免重复释放
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
            bool hasDb = match.Groups["db"].Success && !string.IsNullOrEmpty(match.Groups["db"].Value);

            bool isBitType = typeStr is "DBX" or "I" or "Q" or "M";
            bool isDbType = typeStr.StartsWith("DB", StringComparison.Ordinal);

            // 地址合法性检查：Bit 偏移只能 0~7
            if (hasBit && (bit < 0 || bit > 7))
                return null;
            // Bit 后缀仅允许出现在位类型（DBX/I/Q/M）上
            if (hasBit && !isBitType)
                return null;
            // 位类型必须携带 .bit 后缀
            if (isBitType && !hasBit)
                return null;

            // DB 区域地址（DBX/DBB/DBW/DBD/DBR）必须携带 "DBn." 前缀且 DB 号 ≥ 1（DB0 保留不可用，
            // 缺少前缀的 "DBW10"、DB 号为 0 的 "DB0.DBW10" 均视为非法，反馈为点表诊断错误）；
            // 非 DB 区域（I/Q/M 系列）不允许携带 DB 前缀（如 "DB1.MW10" 属区域与前缀误配，视为非法）
            int db;
            if (isDbType)
            {
                if (!hasDb)
                    return null;

                db = int.Parse(match.Groups["db"].Value, CultureInfo.InvariantCulture);
                if (db < 1)
                    return null;
            }
            else
            {
                if (hasDb)
                    return null;

                db = 0;
            }

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
