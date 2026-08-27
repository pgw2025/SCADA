import { devices } from '../store/deviceStore';
import { dataConversions } from '../store/configStore';
import { addLog, systemConfig } from '../store/index';
import { writeDeviceVariable } from '../api/deviceApi';
import { showToast } from '../services/toastService';

export const setDeviceVariableValue = (
  deviceId: number,
  variableKey: string,
  newValue: number | boolean
) => {
  // 写前快照：记录受乐观更新影响的 (设备, 变量, 旧值)，供 REST 失败回滚（阶段4-3）
  const snapshots: { dev: any; key: string; val: any }[] = [];

  const applyOptimistic = (dev: any, key: string, value: number | boolean) => {
    if (!dev || !dev.variables || dev.variables[key] === undefined) return;
    snapshots.push({ dev, key, val: dev.variables[key] });

    dev.variables[key] = value;

    if (!dev.variableTimestamps) dev.variableTimestamps = {};
    const pad2 = (n: number) => n.toString().padStart(2, '0');
    const d = new Date();
    dev.variableTimestamps[key] = `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;

    // 数据换算联动（写入后按换算规则下推到目标变量）
    propagateDataLinkages(String(dev.id), key, value);

    addLog('核心控制器', `写变量 [设备${dev.id}.${key}] -> ${value} (${typeof value === 'boolean' ? (value ? 'ON/合闸' : 'OFF/开路') : value})`, 'info');
  };

  // 严格模式：deviceId 必填，精准写入指定设备（禁止裸 key 跨设备广播）
  const dev = devices.value.find((d) => String(d.id) === String(deviceId));
  if (!dev) {
    addLog('核心控制器', `写变量被忽略：未找到设备#${deviceId} (key=${variableKey})`, 'warning');
    return;
  }
  applyOptimistic(dev, variableKey, newValue);

  // 真机模式：经 REST 下发写指令（deviceId 已必填）
  if (!systemConfig.value.isSimulationActive) {
    writeVariableToBackend(deviceId, variableKey, newValue).catch((err: any) => {
      // 写失败：回滚本次乐观更新，避免 UI 与设备实际值不一致（后端 RuntimeManager 已拒绝写入）
      snapshots.forEach((s) => { if (s.dev.variables) s.dev.variables[s.key] = s.val; });
      const msg = err?.response?.data?.message || err?.message || '未知错误';
      showToast(`变量 [${variableKey}] 写入失败，已回滚：${msg}`, 'error');
      addLog('REST 写入', `写入失败并回滚: ${msg}`, 'warning');
    });
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
      c => c.active && String(c.sourceDeviceId) === String(current.deviceId) && c.sourceVariableKey === current.variableKey
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
            deviceId: String(conv.targetDeviceId),
            variableKey: conv.targetVariableKey,
            value: current.value
          });
        }
      }
    }
  }
};

export const writeVariableToBackend = async (
  deviceId: number,
  variableKey: string,
  value: any
) => {
  if (systemConfig.value.isSimulationActive) return;

  // 阶段4（方案 A · 仅 REST）：统一走 DeviceController 写端点（POST /api/Device/{id}/variables/{key}/write），
  // 全局 JWT FallbackPolicy 鉴权零成本；RuntimeManager 完成校验链
  // （设备在线→变量存在→启用→只读→驱动就绪→已连接）→ 写驱动 → SignalR 写后回读广播。
  // 不再依赖设计规范.md 中未实现的 SignalR Hub.WritePlcVariable（ScadaHub 当前为纯下行 [AllowAnonymous]）。
  await writeDeviceVariable(deviceId, variableKey, value);
  addLog('REST 写入', `下行写指令成功 (HTTP): 设备#${deviceId} [${variableKey}] = ${value}`, 'info');
};

// Synchronize all dev/custom simulator variables back to the active HMI components values!
export const getDeviceVariableValue = (deviceId: number, variableKey: string): number | boolean => {
  // 严格模式：deviceId 必填，精准读取指定设备的变量（禁止裸 key 跨设备查找）
  const dev = devices.value.find((d) => String(d.id) === String(deviceId));
  if (dev && dev.variables && dev.variables[variableKey] !== undefined) {
    return dev.variables[variableKey];
  }
  return 0;
};
