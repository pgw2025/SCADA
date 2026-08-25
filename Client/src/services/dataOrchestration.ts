import { devices } from '../store/deviceStore';
import { dataConversions } from '../store/configStore';
import { addLog, systemConfig } from '../store/index';
import { HubConnectionState } from '@microsoft/signalr';
import { signalRConnection } from '../services/socketService';

export const setDeviceVariableValue = (
  deviceId: number | null,
  variableKey: string,
  newValue: number | boolean
) => {
  if (deviceId != null) {
    // 阶段3：复合绑定（deviceId + variableKey），精准写入指定设备
    const dev = devices.value.find((d) => String(d.id) === String(deviceId));
    if (dev && dev.variables && dev.variables[variableKey] !== undefined) {
      dev.variables[variableKey] = newValue;

      if (!dev.variableTimestamps) {
        dev.variableTimestamps = {};
      }
      const pad2 = (n: number) => n.toString().padStart(2, '0');
      const d = new Date();
      dev.variableTimestamps[variableKey] = `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;

      // Propagate linkages
      propagateDataLinkages(String(dev.id), variableKey, newValue);

      // Post log
      addLog('核心控制器', `写变量 [设备${deviceId}.${variableKey}] -> ${newValue} (${typeof newValue === 'boolean' ? (newValue ? 'ON/合闸' : 'OFF/开路') : newValue})`, 'info');
    }
    // 写通道形态（REST / Hub 上行）见阶段4
    if (!systemConfig.value.isSimulationActive) {
      writeVariableToBackend(variableKey, newValue);
    }
    return;
  }

  // 遗留：仅按变量名跨设备写入（兼容未绑定设备的旧调用方）
  devices.value.forEach((dev) => {
    if ((dev.status === 'online' || dev.status === 1) && dev.variables && dev.variables[variableKey] !== undefined) {
      dev.variables[variableKey] = newValue;

      if (!dev.variableTimestamps) {
        dev.variableTimestamps = {};
      }
      const pad2 = (n: number) => n.toString().padStart(2, '0');
      const d = new Date();
      dev.variableTimestamps[variableKey] = `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;

      // Propagate linkages
      propagateDataLinkages(String(dev.id), variableKey, newValue);

      // Post log
      addLog('核心控制器', `写变量 [${variableKey}] -> ${newValue} (${typeof newValue === 'boolean' ? (newValue ? 'ON/合闸' : 'OFF/开路') : newValue})`, 'info');
    }
  });

  // Call backend API if simulation data is deactivated
  if (!systemConfig.value.isSimulationActive) {
    writeVariableToBackend(variableKey, newValue);
  }
};

// === PROPAGATION LOGIC FOR DATA CONVERSIONS ===
export const propagateDataLinkages = (startDeviceId: string, startVariableKey: string, newValue: number | boolean) => {
  const queue: { deviceId: string; variableKey: string; value: number | boolean }[] = [];
  queue.push({ deviceId: startDeviceId, variableKey: startVariableKey, value: newValue });

  const visited = new Set<string>();
  visited.add(`${startDeviceId}:${startVariableKey}`);

  while (queue.length > 0) {
    const current = queue.shift()!;

    // Find active conversions that take current node as source
    const matched = dataConversions.value.filter(
      c => c.active && c.sourceDeviceId === current.deviceId && c.sourceVariableKey === current.variableKey
    );

    for (const conv of matched) {
      const dstKey = `${conv.targetDeviceId}:${conv.targetVariableKey}`;
      if (!visited.has(dstKey)) {
        visited.add(dstKey);

        const targetDev = devices.value.find(d => String(d.id) === String(conv.targetDeviceId));
        if (targetDev && targetDev.variables) {
          targetDev.variables[conv.targetVariableKey] = current.value;

          if (!targetDev.variableTimestamps) {
            targetDev.variableTimestamps = {};
          }
          const pad2 = (n: number) => n.toString().padStart(2, '0');
          const d = new Date();
          targetDev.variableTimestamps[conv.targetVariableKey] = `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;

          queue.push({
            deviceId: conv.targetDeviceId,
            variableKey: conv.targetVariableKey,
            value: current.value
          });
        }
      }
    }
  }
};

export const writeVariableToBackend = async (variableKey: string, value: any) => {
  if (systemConfig.value.isSimulationActive) return;

  // SignalR socket write first
  if (signalRConnection.value && signalRConnection.value.state === HubConnectionState.Connected) {
    try {
      await signalRConnection.value.invoke("WritePlcVariable", variableKey, value);
      addLog('SignalR 写入', `下行写指令成功 (WebSocket): [${variableKey}] = ${value}`, 'info');
      return;
    } catch (err: any) {
      addLog('SignalR 写入', `Websocket 下发失败: ${err.message}，正在尝试使用 REST API 写入...`, 'warning');
    }
  }
};

// Synchronize all dev/custom simulator variables back to the active HMI components values!
export const getDeviceVariableValue = (deviceId: number | null, variableKey: string): number | boolean => {
  if (deviceId != null) {
    // 阶段3：复合绑定，精准读取指定设备的变量
    const dev = devices.value.find((d) => String(d.id) === String(deviceId));
    if (dev && dev.variables && dev.variables[variableKey] !== undefined) {
      return dev.variables[variableKey];
    }
    return 0;
  }
  // 遗留：按变量名跨设备查找第一个在线设备
  for (const dev of devices.value) {
    if ((dev.status === 'online' || dev.status === 1) && dev.variables && dev.variables[variableKey] !== undefined) {
      return dev.variables[variableKey];
    }
  }
  return 0;
};
