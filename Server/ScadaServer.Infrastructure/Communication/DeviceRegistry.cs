using System.Collections.Concurrent;
using ScadaServer.Domain.Entities;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>
    /// 线程安全的设备配置运行时注册表
    /// </summary>
    /// <remarks>
    /// 用于缓存设备的运行时配置信息，包括设备实体和关联的变量列表。
    /// 使用 ConcurrentDictionary 保证线程安全。
    /// </remarks>
    public class DeviceRegistry
    {
        private readonly ConcurrentDictionary<int, (Device Device, List<ModelVariable> Variables)> _deviceCache = new();

        /// <summary>
        /// 更新设备配置
        /// </summary>
        /// <param name="device">设备实体</param>
        /// <param name="variables">变量列表</param>
        public void UpdateDevice(Device device, List<ModelVariable> variables)
        {
            _deviceCache[device.Id] = (device, variables);
        }

        /// <summary>
        /// 移除设备配置
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        public void RemoveDevice(int deviceId)
        {
            _deviceCache.TryRemove(deviceId, out _);
        }

        /// <summary>
        /// 获取设备配置
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <returns>设备及其变量元组，未找到返回null</returns>
        public (Device Device, List<ModelVariable> Variables)? GetDeviceConfig(int deviceId)
        {
            if (_deviceCache.TryGetValue(deviceId, out var config))
            {
                return config;
            }
            return null;
        }

        /// <summary>
        /// 获取所有设备ID
        /// </summary>
        /// <returns>设备ID集合</returns>
        public IEnumerable<int> GetAllDeviceIds() => _deviceCache.Keys;
    }
}

