using System.Text.Json;
using ScadaServer.Domain.Entities;
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

        public async Task<bool> ConnectAsync(Device device, string configJson)
        {
            // 虚拟设备不需要真实连接，仅校验配置 JSON 格式。
            if (!string.IsNullOrWhiteSpace(configJson))
            {
                try
                {
                    JsonDocument.Parse(configJson);
                }
                catch (JsonException ex)
                {
                    throw new ArgumentException($"虚拟设备配置 JSON 格式无效: {ex.Message}");
                }
            }

            _connected = true;
            await Task.Delay(10);
            return true;
        }

        public async Task<object> ReadAsync(ModelVariable variable)
        {
            if (!_connected) return null;
            return await Task.FromResult(GenerateValue(variable));
        }

        public async Task<IDictionary<string, object>> ReadBatchAsync(IEnumerable<ModelVariable> variables)
        {
            var results = new Dictionary<string, object>();
            if (!_connected) return results;

            foreach (var v in variables)
            {
                var value = GenerateValue(v);
                if (value != null) results[v.Key] = value;
            }

            return results;
        }

        public Task SubscribeAsync(IEnumerable<ModelVariable> variables, Action<string, object> onValueChanged)
        {
            // 虚拟设备采用轮询模式，订阅为空实现
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(IEnumerable<ModelVariable> variables)
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

        private object GenerateValue(ModelVariable variable)
        {
            double min = variable.Min ?? 0;
            double max = variable.Max ?? 100;
            if (max <= min) max = min + 100;

            double sample;
            lock (_randLock)
            {
                sample = min + _random.NextDouble() * (max - min);
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
