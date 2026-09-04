using System.Text.Json;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>
    /// 虚拟驱动，用于无真实硬件环境下的联调与测试。
    /// 根据变量数据类型生成模拟值，不发起任何网络通信。
    /// </summary>
    public class VirtualDriver : IProtocolDriver
    {
        private bool _connected;
        private readonly Random _random = new();
        private readonly object _randLock = new();

        /// <summary>
        /// 解析后的虚拟设备配置。ConnectAsync 时填充。
        /// IntervalMs 当前作为建议轮询间隔保留(实际轮询周期仍由 Device.PollingInterval 决定),
        /// RandomValues 控制是否启用随机生成(为 false 时固定返回区间中点)。
        /// </summary>
        private VirtualConfig _config = new();

        /// <summary>
        /// 写入值存储（键 = "设备Id:变量Key"）。写入后 ReadAsync / ReadBatchAsync 优先返回该值，
        /// 使虚拟设备在刷新后仍能"读回"最后一次写入的值，贴近真实链路。
        /// 键带设备维度：即便将来驱动改为单例/共享实例，也不会跨设备串值。
        /// </summary>
        private int? _deviceId;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _writtenValues = new();

        /// <summary>为写入值缓存生成带设备维度的复合键。</summary>
        private string KeyOf(string key) => _deviceId.HasValue ? $"{_deviceId}:{key}" : key;

        public async Task<bool> ConnectAsync(IRuntimeConnection connection)
        {
            var configJson = connection.ConfigJson;

            // 虚拟设备不需要真实连接，但需真正解析 VirtualConfig，使前端表单字段被消费。
            if (!string.IsNullOrWhiteSpace(configJson))
            {
                try
                {
                    // 优先按 VirtualConfig 反序列化，字段缺失时保留默认值。
                    _config = JsonSerializer.Deserialize<VirtualConfig>(configJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? new VirtualConfig();
                }
                catch (JsonException ex)
                {
                    throw new ArgumentException($"虚拟设备配置 JSON 格式无效: {ex.Message}");
                }
            }
            else
            {
                _config = new VirtualConfig();
            }

            // 区间合法性兜底，避免后续 GenerateValue 除零或负区间。
            if (_config.IntervalMs < 10) _config.IntervalMs = 10;

            // 记录连接维度 ID，用于写入值缓存的复合键（P1 起连接参数取自连接上下文；
            // 共享前每设备独立连接实例，键天然设备隔离；共享后同连接设备为测试场景——
            // 真实驱动（S7/OPC UA）不依赖该缓存，不受影响）。
            _deviceId = connection.ConnectionId;

            _connected = true;
            await Task.Delay(10);
            return true;
        }

        public async Task<object?> ReadAsync(IRuntimeVariable variable)
        {
            if (!_connected) return null;
            // 写入过的变量优先返回最后一次写入值，否则生成模拟值。
            if (_writtenValues.TryGetValue(KeyOf(variable.Key), out var written))
            {
                return await Task.FromResult(written);
            }
            return await Task.FromResult(GenerateValue(variable));
        }

        public async Task WriteAsync(IRuntimeVariable variable, object value)
        {
            if (!_connected) throw new InvalidOperationException("虚拟设备未连接");

            // 落库写入值（原始值即可），供后续 ReadAsync / ReadBatchAsync 读回。
            _writtenValues[KeyOf(variable.Key)] = value;
            await Task.CompletedTask;
        }

        public async Task<IDictionary<string, object>> ReadBatchAsync(IEnumerable<IRuntimeVariable> variables)
        {
            var results = new Dictionary<string, object>();
            if (!_connected) return results;

            foreach (var v in variables)
            {
                // 与 ReadAsync 对齐：写入过的值优先返回，再退化到模拟生成。
                if (_writtenValues.TryGetValue(KeyOf(v.Key), out var written))
                {
                    results[v.Key] = written;
                    continue;
                }
                var value = GenerateValue(v);
                if (value != null) results[v.Key] = value;
            }

            return results;
        }

        public Task SubscribeAsync(IEnumerable<IRuntimeVariable> variables, Action<string, object> onValueChanged)
        {
            // 虚拟设备采用轮询模式，订阅为空实现
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(IEnumerable<IRuntimeVariable> variables)
        {
            return Task.CompletedTask;
        }

        public async Task DisconnectAsync()
        {
            _connected = false;
            await Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
        }

        private object GenerateValue(IRuntimeVariable variable)
        {
            double min = variable.Min ?? 0;
            double max = variable.Max ?? 100;
            if (max <= min) max = min + 100;

            double sample;
            if (_config.RandomValues)
            {
                lock (_randLock)
                {
                    sample = min + _random.NextDouble() * (max - min);
                }
            }
            else
            {
                // 关闭随机模式时固定返回区间中点，便于稳定回归测试。
                sample = (min + max) / 2;
            }

            return variable.DataType switch
            {
                DataTypeEnum.BOOL => (object)(sample >= (min + max) / 2),
                DataTypeEnum.BIT => (object)(sample >= (min + max) / 2),
                DataTypeEnum.BYTE => (object)(byte)Math.Round(sample),
                DataTypeEnum.INT => (object)(short)Math.Round(sample),
                DataTypeEnum.DINT => (object)(int)Math.Round(sample),
                DataTypeEnum.REAL => (object)Math.Round(sample, 2),
                _ => (object)Math.Round(sample, 2)
            };
        }
    }
}
