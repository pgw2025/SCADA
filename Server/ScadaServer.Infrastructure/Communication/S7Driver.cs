using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
    /// <para>
    /// 地址与数据类型的职责划分：地址（DeviceVariable.Address）只描述
    /// <b>位置</b>（Area / DB 号 / 字节偏移 / 位偏移）与 <b>访问宽度</b>（1/2/4 字节）；
    /// 值的<b>解释类型</b>（BIT/BYTE/INT/DINT/REAL/...）唯一由
    /// <see cref="IRuntimeVariable.DataType"/>（来源 ModelVariable.DataType）决定。
    /// 地址助记符（DBW/DBD/DBR 等）不再决定数据类型，消除"DBD 固定按 DINT 解析"与
    /// DataType=REAL 的冲突。
    /// </para>
    /// </summary>
    public class S7Driver : IProtocolDriver
    {
        #region 字段与常量

        /// <summary>PLC 对象。S7netplus 的 Plc 非线程安全，所有访问必须经由 _plcLock 串行化。</summary>
        private Plc? _plc;

        /// <summary>
        /// PLC 访问互斥锁。保证任意时刻仅一个采集任务与 PLC 通信，避免 Socket 异常/数据错乱。
        /// <para>
        /// 生命周期约定：本信号量<b>永不 Dispose</b>。纯 WaitAsync/Release 用法下
        /// SemaphoreSlim 不会分配底层等待句柄（仅访问 AvailableWaitHandle 或带
        /// CancellationToken 的 WaitAsync 才会分配），不 Dispose 不泄漏任何资源；
        /// 反之，在仍有并发等待者时 Dispose 信号量本身就是竞态源（等待者抛
        /// ObjectDisposedException 或挂死），且"入口检查 → WaitAsync"之间存在
        /// 无法消除的间隙。因此以"不 Dispose"换取与任意并发组合的绝对安全。
        /// 释放语义由 <see cref="_state"/> 状态机承担：终态后所有方法在锁内复检即退出，
        /// 锁本身始终处于已 Release 状态，随 Driver 实例一同被 GC 回收。
        /// </para>
        /// </summary>
        private readonly SemaphoreSlim _plcLock = new SemaphoreSlim(1, 1);

        /// <summary>生命周期状态：Active（活动，可接受全部操作）。</summary>
        private const int StateActive = 0;

        /// <summary>生命周期状态：Closed（终态：DisposeAsync 已进入释放流程，含"释放中/已释放"）。</summary>
        private const int StateClosed = 1;

        /// <summary>
        /// 生命周期状态（Active → Closed 单向迁移）。
        /// <para>
        /// 迁移仅由 <see cref="DisposeAsync"/> 通过 Interlocked.Exchange 原子完成，
        /// 保证：a) 并发/重复 Dispose 只有一个调用者执行释放流程；
        /// b) 所有操作方法在获取 _plcLock 之后<b>锁内复检</b>本状态，
        ///    与 DisposeAsync 的 PLC 关闭在锁上完全串行化，杜绝使用已关闭的 Plc。
        /// 读侧使用 Volatile.Read 保证可见性（写侧 Interlocked 具有全栅栏语义）。
        /// </para>
        /// </summary>
        private int _state;

        /// <summary>点表诊断用：地址非法标记。</summary>
        private const string InvalidAddress = "INVALID_ADDRESS";

        /// <summary>点表诊断用：PLC 读取异常标记。</summary>
        private const string ReadError = "READ_ERROR";

        /// <summary>日志组件。驱动为每设备独立实例，日志天然按设备隔离。</summary>
        private readonly ILogger<S7Driver> _logger;

        /// <summary>
        /// 最近一次成功解析的连接上下文（ConnectAsync 捕获），供读/写/释放日志定位 PLC。
        /// Driver 无常驻 DeviceId 字段，此为日志专用快照，不参与业务逻辑。
        /// </summary>
        private string? _deviceKey;
        private string? _lastIp;
        private int _lastRack;
        private int _lastSlot;

        /// <summary>
        /// 通信失败日志闸门：首次失败 Warning（含异常详情），持续失败降为 Debug，
        /// 恢复时记 Information 并复位。避免 PLC 长时间离线时高频采集把日志刷爆。
        /// </summary>
        private bool _commFailureLogged;

        /// <summary>
        /// 初始化 S7 驱动。日志由 ProtocolDriverFactory 注入（ILoggerFactory.CreateLogger）。
        /// </summary>
        public S7Driver(ILogger<S7Driver> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

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
        /// 助记符仅决定访问宽度（X/B=1 字节、W=2 字节、D/R=4 字节），不决定数据类型；
        /// 值的解释类型由 IRuntimeVariable.DataType 决定（DBR 与 DBD 同为 4 字节宽度）。
        /// </summary>
        private static readonly Regex S7AddressRegex = new Regex(
            @"^(?:DB(?<db>\d+)\.)?(?<type>DBX|DBB|DBW|DBD|DBR|I|Q|M|IB|IW|ID|QB|QW|QD|MB|MW|MD)(?<offset>\d+)(?:\.(?<bit>\d+))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        #endregion

        #region 连接管理

        public async Task<bool> ConnectAsync(IRuntimeDevice device)
        {
            if (Volatile.Read(ref _state) != StateActive)
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
                // 锁内复检：入口检查（无锁）与获锁之间存在间隙，DisposeAsync 可能已原子迁移状态。
                // 终态下直接放弃连接，避免"连接成功即被 Dispose 关闭"之外的资源窗口。
                if (Volatile.Read(ref _state) != StateActive)
                    return false;

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

                // 捕获连接上下文快照，供后续读/写/释放日志定位 PLC（不参与业务逻辑）
                _deviceKey = device.Key;
                _lastIp = config.IpAddress;
                _lastRack = config.Rack;
                _lastSlot = config.Slot;

                _logger.LogDebug("S7 连接开始 Device={DeviceKey} Ip={Ip}:{Port} Rack={Rack} Slot={Slot} Cpu={CpuType}",
                    device.Key, config.IpAddress, config.Port, config.Rack, config.Slot, cpuType);

                Plc? newPlc = null;
                try
                {
                    newPlc = new Plc(cpuType, config.IpAddress, config.Port, (short)config.Rack, (short)config.Slot);
                    await newPlc.OpenAsync();

                    if (newPlc.IsConnected)
                    {
                        _plc = newPlc;
                        _commFailureLogged = false; // 新连接：复位通信失败闸门
                        _logger.LogInformation("S7 PLC 连接成功 Device={DeviceKey} Ip={Ip}:{Port} Rack={Rack} Slot={Slot}",
                            device.Key, config.IpAddress, config.Port, config.Rack, config.Slot);
                        return true;
                    }

                    // OpenAsync 未抛异常但连接未建立：清理并返回失败
                    ClosePlcInstance(newPlc);
                    _logger.LogWarning("S7 PLC 连接未建立（OpenAsync 未抛异常但 IsConnected=false）Device={DeviceKey} Ip={Ip}:{Port} Rack={Rack} Slot={Slot}",
                        device.Key, config.IpAddress, config.Port, config.Rack, config.Slot);
                    return false;
                }
                catch (Exception ex)
                {
                    // 连接失败：自动清理 _plc，避免半开连接残留
                    ClosePlcInstance(newPlc);
                    _plc = null;
                    _logger.LogWarning(ex, "S7 PLC 连接失败 Device={DeviceKey} Ip={Ip}:{Port} Rack={Rack} Slot={Slot}",
                        device.Key, config.IpAddress, config.Port, config.Rack, config.Slot);
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
            if (Volatile.Read(ref _state) != StateActive || variable == null)
                return null;

            await _plcLock.WaitAsync();
            try
            {
                // 锁内复检：DisposeAsync 可能已在入口检查与获锁之间迁移状态
                if (Volatile.Read(ref _state) != StateActive)
                    return null;

                if (_plc == null || !_plc.IsConnected)
                {
                    LogCommFailure("Read", variable.Address, null);
                    return null;
                }

                // 地址只提供位置与宽度，值的解释类型由 DataType 决定：
                // 按地址宽度读取原始字节，再按 DataType 提取，消除地址助记符
                // 与 DataType 的类型冲突（如 DB1.DBD20 + REAL 按 REAL 解析而非 DINT）。
                // 地址来源：RuntimeVariable.Address（由 DeviceVariable.Address 解析，驱动不感知 ModelVariable）。
                var info = ParseAddress(variable.Address);
                if (info == null)
                {
                    // 点表配置错误（非通信故障）：Debug 级即可，批量路径会反馈 INVALID_ADDRESS 供诊断
                    _logger.LogDebug("S7 地址非法 Variable={VariableKey} Address={Address}", variable.Key, variable.Address);
                    return null;
                }

                var typeError = ValidateTypeMatch(info, variable.DataType);
                if (typeError != null)
                {
                    _logger.LogDebug("S7 地址与数据类型不匹配 Variable={VariableKey} Address={Address} DataType={DataType}：{Reason}",
                        variable.Key, variable.Address, variable.DataType, typeError);
                    return null;
                }

                var buffer = await _plc.ReadBytesAsync(info.S7Area, info.DbNumber, info.ByteOffset, info.ByteLength);
                if (buffer == null || buffer.Length < info.ByteLength)
                {
                    LogCommFailure("Read", variable.Address, null);
                    return null;
                }

                var value = ExtractValue(buffer, info, variable.DataType, info.ByteOffset);
                if (value != null)
                    NoteCommRecovered();

                return value;
            }
            catch (Exception ex)
            {
                // 防止单个变量异常导致整个采集循环崩溃；日志经闸门限流（首次 Warning，持续 Debug）
                LogCommFailure("Read", variable.Address, ex);
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
        /// 返回字典的值为两类之一：真实读取值（bool/byte/ushort/short/uint/int/float，S7 驱动的真实值不会是 string），
        /// 或诊断标记字符串 —— <see cref="ReadError"/>（未连接 / 读取失败）与 <see cref="InvalidAddress"/>
        /// （地址非法，或地址宽度/位形式与 <see cref="IRuntimeVariable.DataType"/> 不匹配的点表配置错误）。
        /// 调用方应先比对标记再做类型转换；该约定与接口 <c>ReadAsync</c> 以 null 表示失败的语义不同
        /// （接口签名共享于所有驱动，统一需接口级变更）。
        /// </remarks>
        public async Task<IDictionary<string, object>> ReadBatchAsync(IEnumerable<IRuntimeVariable> variables)
        {
            var results = new Dictionary<string, object>();
            if (Volatile.Read(ref _state) != StateActive || variables == null)
                return results;

            await _plcLock.WaitAsync();
            try
            {
                // 锁内复检：DisposeAsync 可能已在入口检查与获锁之间迁移状态
                if (Volatile.Read(ref _state) != StateActive)
                    return results;

                // 1) 解析地址并校验地址-数据类型匹配：非法地址或类型不匹配（点表配置错误）
                //    立即反馈 INVALID_ADDRESS，空变量跳过。
                //    地址只提供位置与宽度；值的解释类型由 RuntimeVariable.DataType 决定。
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

                    // 地址宽度/位形式与 DataType 不匹配：视为点表配置错误，按非法地址反馈
                    if (ValidateTypeMatch(info, v.DataType) != null)
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
                    LogCommFailure("ReadBatch", null, null);
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
                        catch (Exception ex)
                        {
                            // 该簇读取异常：整簇标记 READ_ERROR，便于点表诊断；日志经闸门限流
                            foreach (var item in cluster)
                                results[item.Variable.Key] = ReadError;
                            LogCommFailure("ReadBatch",
                                $"{group.Key.S7Area} DB={dbNumber} Offset={minOffset} Len={length}", ex);
                            continue;
                        }

                        // S7netplus 读取失败时可能返回 null；同时校验返回长度，杜绝下游越界
                        if (buffer == null || buffer.Length < length)
                        {
                            foreach (var item in cluster)
                                results[item.Variable.Key] = ReadError;
                            LogCommFailure("ReadBatch",
                                $"{group.Key.S7Area} DB={dbNumber} Offset={minOffset} Len={length}", null);
                            continue;
                        }

                        // 至少一个簇读取成功：记录通信恢复（若此前处于失败状态）
                        NoteCommRecovered();

                        // 5) 按变量 DataType 解析（缓冲区长度已校验，ExtractValue 内的边界检查为兜底防御）
                        foreach (var item in cluster)
                        {
                            var value = ExtractValue(buffer, item.Info, item.Variable.DataType, minOffset);
                            results[item.Variable.Key] = value ?? (object)ReadError;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 整体兜底：任何意外异常均不抛出，避免采集宿主崩溃。
                // 内层簇读取异常已被捕获，到达此处的大多是驱动自身缺陷（如分组/聚簇逻辑错误）→ Error。
                _logger.LogError(ex, "S7 批量读取发生未预期异常 Device={DeviceKey} Ip={Ip} Rack={Rack} Slot={Slot}",
                    _deviceKey, _lastIp, _lastRack, _lastSlot);
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

            // 正常批量读取统计（单行 Debug，含失败计数；不含变量值，避免高频采集刷量）
            _logger.LogDebug("S7 批量读取完成 Device={DeviceKey} Ip={Ip} Total={Total} Ok={Ok} ReadError={ReadError} InvalidAddress={InvalidAddress}",
                _deviceKey, _lastIp, results.Count,
                results.Values.Count(x => x is not string),
                results.Values.Count(x => ReferenceEquals(x, ReadError)),
                results.Values.Count(x => ReferenceEquals(x, InvalidAddress)));

            return results;
        }

        /// <summary>
        /// 从读取缓冲区中按 <see cref="DataTypeEnum"/> 提取值。
        /// 地址信息仅提供位置（字节/位偏移）；值的解释类型由 DataType 决定。
        /// 越界或解析失败返回 null。
        /// </summary>
        private static object? ExtractValue(byte[] buffer, S7AddressInfo info, DataTypeEnum dataType, int minOffset)
        {
            int rel = info.ByteOffset - minOffset;
            if (rel < 0)
                return null;

            switch (dataType)
            {
                case DataTypeEnum.BOOL or DataTypeEnum.BIT:
                    if (rel >= buffer.Length) return null;
                    return (buffer[rel] & (1 << info.BitOffset)) != 0;

                case DataTypeEnum.BYTE:
                    if (rel >= buffer.Length) return null;
                    return buffer[rel];

                case DataTypeEnum.INT:
                    if (rel + 1 >= buffer.Length) return null;
                    return S7.Net.Types.Int.FromByteArray(new byte[] { buffer[rel], buffer[rel + 1] });

                case DataTypeEnum.UINT16 or DataTypeEnum.WORD:
                    if (rel + 1 >= buffer.Length) return null;
                    return S7.Net.Types.Word.FromByteArray(new byte[] { buffer[rel], buffer[rel + 1] });

                case DataTypeEnum.DINT:
                    if (rel + 3 >= buffer.Length) return null;
                    return S7.Net.Types.DInt.FromByteArray(new byte[] { buffer[rel], buffer[rel + 1], buffer[rel + 2], buffer[rel + 3] });

                case DataTypeEnum.UINT32:
                    if (rel + 3 >= buffer.Length) return null;
                    return S7.Net.Types.DWord.FromByteArray(new byte[] { buffer[rel], buffer[rel + 1], buffer[rel + 2], buffer[rel + 3] });

                case DataTypeEnum.REAL or DataTypeEnum.FLOAT:
                    if (rel + 3 >= buffer.Length) return null;
                    return S7.Net.Types.Real.FromByteArray(new byte[] { buffer[rel], buffer[rel + 1], buffer[rel + 2], buffer[rel + 3] });

                default:
                    // 不支持的类型已在 ValidateTypeMatch 阶段拒绝，此处为兜底防御
                    return null;
            }
        }

        /// <summary>
        /// 校验地址与 <see cref="DataTypeEnum"/> 的匹配关系：
        /// 位类型（BIT/BOOL）必须使用位地址；非位类型的字节宽度必须与地址宽度一致。
        /// 返回 null 表示匹配合法；否则返回明确的错误描述（供调用方拒绝/诊断）。
        /// </summary>
        private static string? ValidateTypeMatch(S7AddressInfo info, DataTypeEnum dataType)
        {
            // 位类型：必须使用位地址（DBX/I/Q/M + .bit 后缀，地址宽度恒为 1 字节）
            if (dataType == DataTypeEnum.BIT || dataType == DataTypeEnum.BOOL)
            {
                if (!info.HasBit)
                    return $"数据类型 {dataType} 需要位地址（如 DB1.DBX0.1 / M0.0），当前地址不是位形式";
                return null;
            }

            // 非位类型不允许使用位地址
            if (info.HasBit)
                return $"地址为位形式，数据类型 {dataType} 应为 BOOL/BIT，或改用字节/字/双字地址";

            // 类型期望的访问宽度（字节）；-1 表示 S7 地址助记符无法表达该类型
            int expected = dataType switch
            {
                DataTypeEnum.BYTE => 1,
                DataTypeEnum.INT or DataTypeEnum.UINT16 or DataTypeEnum.WORD => 2,
                DataTypeEnum.DINT or DataTypeEnum.UINT32 or DataTypeEnum.REAL or DataTypeEnum.FLOAT => 4,
                _ => -1
            };

            if (expected < 0)
                return $"数据类型 {dataType} 不受 S7 驱动支持（可用：BOOL/BIT、BYTE、INT、UINT16、WORD、DINT、UINT32、REAL、FLOAT）";

            if (info.ByteLength != expected)
                return $"地址宽度（{info.ByteLength} 字节）与数据类型 {dataType}（{expected} 字节）不匹配";

            return null;
        }

        #endregion

        #region 写入

        public async Task WriteAsync(IRuntimeVariable variable, object value)
        {
            if (Volatile.Read(ref _state) != StateActive || variable == null)
                throw new InvalidOperationException("驱动已释放或变量无效");

            if (string.IsNullOrWhiteSpace(variable.Address))
                throw new ArgumentException("变量地址为空，无法写入", nameof(variable));

            await _plcLock.WaitAsync();
            try
            {
                // 锁内复检：DisposeAsync 可能已在入口检查与获锁之间迁移状态
                if (Volatile.Read(ref _state) != StateActive)
                    throw new InvalidOperationException("驱动已释放");

                if (_plc == null || !_plc.IsConnected)
                {
                    _logger.LogWarning("S7 写入失败：PLC 未连接 Device={DeviceKey} Ip={Ip} Rack={Rack} Slot={Slot} Variable={VariableKey} Address={Address}",
                        _deviceKey, _lastIp, _lastRack, _lastSlot, variable.Key, variable.Address);
                    throw new InvalidOperationException("PLC 未连接");
                }

                // 地址-数据类型匹配校验：宽度/位形式不匹配时在驱动层明确拒绝，
                // 不再把类型转换交给 S7netplus 按地址助记符隐式推断（其类型语义与 DataType 冲突）。
                var info = ParseAddress(variable.Address)
                    ?? throw new ArgumentException($"变量地址 [{variable.Address}] 不是合法的 S7 地址", nameof(variable));

                var typeError = ValidateTypeMatch(info, variable.DataType);
                if (typeError != null)
                    throw new InvalidOperationException($"变量 [{variable.Key}] 地址 {variable.Address} 与数据类型不匹配：{typeError}");

                if (info.HasBit)
                {
                    // 位写入：按地址串写位（S7netplus 原生支持 DBX/I/Q/M 位地址）
                    var bitValue = ConvertToBit(value);
                    if (bitValue == null)
                    {
                        _logger.LogWarning("S7 写入失败：值转换失败 Device={DeviceKey} Ip={Ip} Variable={VariableKey} Address={Address} DataType={DataType} Value={Value}",
                            _deviceKey, _lastIp, variable.Key, variable.Address, variable.DataType, value);
                        throw new InvalidOperationException($"无法将值 [{value}] 转换为数据类型 {variable.DataType}");
                    }

                    try
                    {
                        await _plc.WriteAsync(variable.Address, bitValue.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "S7 位写入通信失败 Device={DeviceKey} Ip={Ip} Rack={Rack} Slot={Slot} Variable={VariableKey} Address={Address}",
                            _deviceKey, _lastIp, _lastRack, _lastSlot, variable.Key, variable.Address);
                        throw;
                    }

                    return;
                }

                // 非位写入：按 DataType 转换为 S7 大端字节序列后写原始字节，
                // 目标解释类型由 DataType 决定（如 DB1.DBD20 + REAL 写 4 字节 REAL 位模式）。
                var bytes = ConvertToS7Bytes(variable.DataType, value);
                if (bytes == null)
                {
                    _logger.LogWarning("S7 写入失败：值转换失败 Device={DeviceKey} Ip={Ip} Variable={VariableKey} Address={Address} DataType={DataType} Value={Value}",
                        _deviceKey, _lastIp, variable.Key, variable.Address, variable.DataType, value);
                    throw new InvalidOperationException($"无法将值 [{value}] 转换为数据类型 {variable.DataType}");
                }

                try
                {
                    await _plc.WriteBytesAsync(info.S7Area, info.DbNumber, info.ByteOffset, bytes);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "S7 写入通信失败 Device={DeviceKey} Ip={Ip} Rack={Rack} Slot={Slot} Variable={VariableKey} Address={Address}",
                        _deviceKey, _lastIp, _lastRack, _lastSlot, variable.Key, variable.Address);
                    throw;
                }
            }
            finally
            {
                _plcLock.Release();
            }
        }

        /// <summary>将写入值转换为位值（BOOL/BIT）。转换失败返回 null。</summary>
        private static bool? ConvertToBit(object value)
        {
            try
            {
                return Convert.ToBoolean(value);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
            {
                return null;
            }
        }

        /// <summary>
        /// 将写入值按 <see cref="DataTypeEnum"/> 转换为 S7 大端字节序列。
        /// BYTE -> 1 字节；INT/UINT16/WORD -> 2 字节；DINT/UINT32/REAL/FLOAT -> 4 字节。
        /// 转换失败（字符串格式错误/数值溢出/类型不兼容）返回 null，由调用方以明确异常拒绝。
        /// </summary>
        private static byte[]? ConvertToS7Bytes(DataTypeEnum dataType, object value)
        {
            try
            {
                switch (dataType)
                {
                    case DataTypeEnum.BYTE:
                        return new[] { Convert.ToByte(value) };
                    case DataTypeEnum.INT:
                        return S7.Net.Types.Int.ToByteArray(Convert.ToInt16(value));
                    case DataTypeEnum.UINT16 or DataTypeEnum.WORD:
                        return S7.Net.Types.Word.ToByteArray(Convert.ToUInt16(value));
                    case DataTypeEnum.DINT:
                        return S7.Net.Types.DInt.ToByteArray(Convert.ToInt32(value));
                    case DataTypeEnum.UINT32:
                        return S7.Net.Types.DWord.ToByteArray(Convert.ToUInt32(value));
                    case DataTypeEnum.REAL or DataTypeEnum.FLOAT:
                        return S7.Net.Types.Real.ToByteArray(Convert.ToSingle(value));
                    default:
                        // BOOL/BIT 走位写入路径；其余类型已在 ValidateTypeMatch 阶段拒绝
                        return null;
                }
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
            {
                // 覆盖 Convert 的全部典型转换失败：字符串格式错误（Format）、数值溢出（Overflow）、类型不兼容（InvalidCast）
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
            // 终态（DisposeAsync 已进入释放流程）：PLC 由 DisposeAsync 负责最终关闭
            if (Volatile.Read(ref _state) != StateActive)
                return;

            await _plcLock.WaitAsync();
            try
            {
                // 锁内复检：获锁前 DisposeAsync 可能已迁移状态，此时由其负责关闭，本调用直接返回
                if (Volatile.Read(ref _state) != StateActive)
                    return;

                _logger.LogDebug("S7 断开连接 Device={DeviceKey} Ip={Ip} Rack={Rack} Slot={Slot}",
                    _deviceKey, _lastIp, _lastRack, _lastSlot);
                await ClosePlcUnderLockAsync();
            }
            finally
            {
                _plcLock.Release();
            }
        }

        /// <summary>
        /// 在已持有 _plcLock 的前提下关闭当前 _plc 并清空引用（DisconnectAsync 与 DisposeAsync 共用）。
        /// 关闭动作卸载到线程池执行，保持原有防止 Close 阻塞设计。
        /// </summary>
        private async Task ClosePlcUnderLockAsync()
        {
            if (_plc == null)
                return;

            await Task.Run(() => ClosePlcInstance(_plc));
            _plc = null;
        }

        /// <summary>安全关闭单个 Plc 实例（不触碰 _plc 字段、不再次加锁）。</summary>
        private void ClosePlcInstance(Plc? plc)
        {
            if (plc == null)
                return;
            try
            {
                if (plc.IsConnected)
                    plc.Close();
            }
            catch (Exception ex)
            {
                // 关闭过程中的异常（如套接字已断开）不阻断释放流程，降为 Debug 记录
                // （连接本就断开时 Close 抛异常属常态，Warning 会随每次断线刷屏）
                _logger.LogDebug(ex, "S7 PLC 关闭时出现异常（已忽略，通常为套接字已断开）Device={DeviceKey} Ip={Ip}",
                    _deviceKey, _lastIp);
            }
        }

        /// <summary>
        /// 释放驱动：关闭 PLC 连接并进入终态（Active → Closed 单向迁移）。
        /// <para>
        /// 并发/重复调用安全：Interlocked.Exchange 原子占位，仅第一个调用者执行释放流程，
        /// 其余调用（无论并发中还是终态后）直接返回——不会重复关闭 Plc、不会重复释放。
        /// </para>
        /// <para>
        /// 与在途操作（Connect/Read/Write/Disconnect）的并发安全：
        /// 状态迁移先于获锁发生，随后在锁内关闭 PLC；在途操作在获锁后经锁内复检发现终态即退出，
        /// 与 PLC 关闭在 _plcLock 上完全串行化。若在途的 ConnectAsync 已持有锁并先完成连接，
        /// 本方法获锁后同样会关闭该新连接——任何交错下 Plc 恰好被关闭一次，无泄漏。
        /// </para>
        /// <para>
        /// 注：_plcLock 按"永不 Dispose"约定保留（见字段注释），不持有非托管资源，
        /// 此处确保其最终处于已 Release 状态后随 Driver 一同被 GC 回收。
        /// </para>
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            // 原子占位：Active→Closed 仅允许一次；返回值非 Active 说明已有 Dispose 在途/完成
            if (Interlocked.Exchange(ref _state, StateClosed) != StateActive)
                return;

            _logger.LogInformation("S7 驱动释放 Device={DeviceKey} Ip={Ip} Rack={Rack} Slot={Slot}",
                _deviceKey, _lastIp, _lastRack, _lastSlot);

            await _plcLock.WaitAsync();
            try
            {
                // 终态下最终确保 PLC 已关闭并清空引用（ConnectAsync 竞态插入的新连接也会在此被关闭）
                await ClosePlcUnderLockAsync();
            }
            finally
            {
                _plcLock.Release();
            }
        }

        #endregion

        #region 日志辅助

        /// <summary>
        /// 通信失败日志（闸门限流）：首次失败记 Warning（含异常详情与 PLC 定位信息），
        /// 持续失败降为 Debug——PLC 长时间离线期间高频采集（每秒每设备）不会刷爆日志，
        /// 且首条 Warning 已携带完整异常（SocketException 等）供定位。
        /// </summary>
        /// <param name="operation">操作名（Read / ReadBatch）</param>
        /// <param name="address">涉及的地址或簇范围（可为 null）</param>
        /// <param name="ex">通信异常（无异常时为 null，如仅是未连接）</param>
        private void LogCommFailure(string operation, string? address, Exception? ex)
        {
            if (!_commFailureLogged)
            {
                _commFailureLogged = true;
                if (ex == null)
                {
                    _logger.LogWarning("S7 通信失败：PLC 未连接 Device={DeviceKey} Ip={Ip} Rack={Rack} Slot={Slot} Operation={Operation} Address={Address}",
                        _deviceKey, _lastIp, _lastRack, _lastSlot, operation, address ?? "-");
                }
                else
                {
                    _logger.LogWarning(ex, "S7 通信异常 Device={DeviceKey} Ip={Ip} Rack={Rack} Slot={Slot} Operation={Operation} Address={Address}",
                        _deviceKey, _lastIp, _lastRack, _lastSlot, operation, address ?? "-");
                }
            }
            else
            {
                _logger.LogDebug("S7 通信持续失败（详情见首条 Warning）Device={DeviceKey} Ip={Ip} Operation={Operation} Address={Address}",
                    _deviceKey, _lastIp, operation, address ?? "-");
            }
        }

        /// <summary>
        /// 通信恢复标记：此前处于失败状态时记 Information（含 PLC 定位信息）并复位闸门；
        /// 正常运行期间调用为无操作（不产生日志）。
        /// </summary>
        private void NoteCommRecovered()
        {
            if (!_commFailureLogged)
                return;

            _commFailureLogged = false;
            _logger.LogInformation("S7 通信恢复 Device={DeviceKey} Ip={Ip} Rack={Rack} Slot={Slot}",
                _deviceKey, _lastIp, _lastRack, _lastSlot);
        }

        #endregion

        #region 地址解析

        /// <summary>
        /// 解析 S7 地址字符串为内部表示（位置 + 访问宽度，不含数据类型语义）。
        /// <b>任意非法地址（含超大数字导致的数值溢出）均安全返回 null，绝不抛出异常。</b>
        /// 非法地址（格式错误、数字溢出、Bit 偏移超出 0~7、位类型缺失 bit 后缀等）返回 null。
        /// 值的解释类型不在此判定——统一由 <see cref="IRuntimeVariable.DataType"/> 决定。
        /// </summary>
        internal S7AddressInfo? ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return null;

            var match = S7AddressRegex.Match(address.Trim());
            if (!match.Success)
                return null;

            string typeStr = match.Groups["type"].Value.ToUpperInvariant();

            // 数字段全部使用 TryParse 安全解析：正则的 \d+ 可匹配任意长数字串
            // （如 "M999999999999999999999"），int.Parse 会抛 OverflowException——
            // 本方法承诺对任意用户输入只返回 null，不依赖外层 catch 兜底。
            if (!int.TryParse(match.Groups["offset"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int offset))
                return null;

            bool hasBit = match.Groups["bit"].Success && !string.IsNullOrEmpty(match.Groups["bit"].Value);
            int bit = 0;
            if (hasBit && !int.TryParse(match.Groups["bit"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out bit))
                return null;

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

                if (!int.TryParse(match.Groups["db"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out db) || db < 1)
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
                HasBit = hasBit,
                DbNumber = db
            };

            // 地址助记符仅决定访问宽度（位/字节=1、字=2、双字=4），不决定值的解释类型：
            // 值类型统一由 ModelVariable.DataType（经 RuntimeVariable.DataType）决定。
            // DBR 与 DBD 同为 4 字节宽度（DBR 是 REAL 的习惯地址写法，仅表宽度）。
            switch (typeStr)
            {
                // DataBlock
                case "DBX": info.S7Area = DataType.DataBlock; info.ByteLength = 1; break;
                case "DBB": info.S7Area = DataType.DataBlock; info.ByteLength = 1; break;
                case "DBW": info.S7Area = DataType.DataBlock; info.ByteLength = 2; break;
                case "DBD": info.S7Area = DataType.DataBlock; info.ByteLength = 4; break;
                case "DBR": info.S7Area = DataType.DataBlock; info.ByteLength = 4; break;
                // Input
                case "I":  info.S7Area = DataType.Input;  info.ByteLength = 1; break;
                case "IB": info.S7Area = DataType.Input;  info.ByteLength = 1; break;
                case "IW": info.S7Area = DataType.Input;  info.ByteLength = 2; break;
                case "ID": info.S7Area = DataType.Input;  info.ByteLength = 4; break;
                // Output
                case "Q":  info.S7Area = DataType.Output; info.ByteLength = 1; break;
                case "QB": info.S7Area = DataType.Output; info.ByteLength = 1; break;
                case "QW": info.S7Area = DataType.Output; info.ByteLength = 2; break;
                case "QD": info.S7Area = DataType.Output; info.ByteLength = 4; break;
                // Memory
                case "M":  info.S7Area = DataType.Memory; info.ByteLength = 1; break;
                case "MB": info.S7Area = DataType.Memory; info.ByteLength = 1; break;
                case "MW": info.S7Area = DataType.Memory; info.ByteLength = 2; break;
                case "MD": info.S7Area = DataType.Memory; info.ByteLength = 4; break;
                default:
                    return null;
            }

            return info;
        }

        #endregion

        #region 内部类型

        /// <summary>
        /// S7 地址解析结果：仅描述位置（区域/DB 号/字节偏移/位偏移）与访问宽度，
        /// 不携带数据类型语义（值的解释类型由 IRuntimeVariable.DataType 决定）。
        /// （internal 供单元测试直接断言解析结果）
        /// </summary>
        internal sealed class S7AddressInfo
        {
            /// <summary>S7 存储区域（DataBlock / Input / Output / Memory）。</summary>
            public DataType S7Area { get; set; }

            /// <summary>DB 号（非 DB 区域为 0）。</summary>
            public int DbNumber { get; set; }

            /// <summary>字节偏移。</summary>
            public int ByteOffset { get; set; }

            /// <summary>位偏移（仅位地址有效，0~7）。</summary>
            public int BitOffset { get; set; }

            /// <summary>是否为位地址（DBX/I/Q/M 助记符且带 .bit 后缀）。</summary>
            public bool HasBit { get; set; }

            /// <summary>地址访问宽度（字节）：位/字节地址 1，字地址 2，双字地址 4。</summary>
            public int ByteLength { get; set; }
        }

        #endregion
    }
}
