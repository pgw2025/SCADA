import * as api from '../api/deviceApi';
import * as store from '../store/deviceStore';
import { addLog, systemConfig } from '../store/index';
import { Device } from '../types';
import { parseApiError, ErrorResult } from '../utils/errorHandler';
import { normalizeDevices } from '../utils/deviceStatus';
import { extractApiError } from '../api/http';

export interface DeviceOperationResult<T = any> {
  success: boolean;
  data?: T;
  error?: ErrorResult;
}

/**
 * 同步设备列表到全局 store。
 *
 * - 传入上一次 devices 做实时值继承（normalizeDevices），避免轮询/全量刷新把 SignalR 已推送、
 *   且之后不再变化的值抹回 null 导致运行画面闪零。
 * - 当 options.realtime 为 true（登录、SignalR 握手成功/重连等显式全量刷新场景）时，
 *   额外逐设备拉取 /api/TelemetryData/{id}/realtime 回填最新实时值；轮询路径不传该参数，
 *   以免每 5 秒对全部设备发一轮 realtime 请求。
 * - 当 options.silent 为 true（周期轮询兜底路径）时不写同步日志，避免约 720 条/小时的日志刷屏。
 */
export const syncDevices = async (options?: { realtime?: boolean; silent?: boolean }) => {
  if (systemConfig.value.isSimulationActive) return;

  try {
    const { data } = await api.fetchDevicesFromBackend();
    const normalized = normalizeDevices(data, store.devices.value);
    store.setDevices(normalized);
    if (!options?.silent) {
      addLog('设备管理', `已从后端同步 ${normalized.length} 个设备`, 'normal');
    }

    if (options?.realtime) {
      await Promise.all((data ?? []).map(async (d: any) => {
        const id = Number(d?.id);
        if (!id) return;
        try {
          const { data: rt }: any = await api.fetchDeviceRealtime(id);
          const dev = store.devices.value.find(x => String(x.id) === String(rt?.deviceId));
          if (!dev || !Array.isArray(rt?.variables)) return;
          if (!dev.variables) dev.variables = {};
          rt.variables.forEach((v: any) => {
            const key = v?.key ?? v?.Key;
            if (key == null) return;
            dev.variables[key] = v?.value ?? v?.Value;
            // 质量分级显示：从实时端点回填变量质量（Good/Bad/Uncertain/CommunicationError/…），
            // 存入 variableMeta 供组态运行端按质量分级叠加角标。
            if (dev.variableMeta) {
              const quality = v?.quality ?? v?.Quality;
              if (dev.variableMeta[key]) {
                dev.variableMeta[key].quality = quality;
              } else if (quality) {
                dev.variableMeta[key] = { key, quality } as any;
              }
            }
          });
        } catch {
          /* 单设备实时值拉取失败静默，不影响设备列表 */
        }
      }));
    }
  } catch (err: any) {
    addLog('设备管理', `无法同步设备列表: ${extractApiError(err)}`, 'warning');
  }
};

export const createDeviceAndSync = async (deviceData: Partial<Device>): Promise<DeviceOperationResult<Device>> => {
  if (systemConfig.value.isSimulationActive) {
    const mockDevice: Device = {
      id: Date.now(),
      name: deviceData.name || '',
      key: deviceData.key || deviceData.code || '',
      code: deviceData.code || deviceData.key || '',
      areaId: deviceData.areaId || 0,
      modelId: deviceData.modelId || 0,
      type: deviceData.type || 'S7',
      ipAddress: deviceData.ipAddress,
      port: deviceData.port,
      topic: deviceData.topic,
      status: 0,
      variables: {},
      lastUpdated: new Date().toISOString(),
      cpuType: deviceData.cpuType,
      rack: deviceData.rack,
      slot: deviceData.slot,
      mqttServer: deviceData.mqttServer,
      publishTopic: deviceData.publishTopic,
      subscribeTopic: deviceData.subscribeTopic,
      payloadTemplate: deviceData.payloadTemplate
    };
    store.setDevices([...store.devices.value, mockDevice]);
    return { success: true, data: mockDevice };
  }

  try {
    const requestData = {
      name: deviceData.name?.trim(),
      key: deviceData.key?.trim(),
      areaId: deviceData.areaId,
      modelId: deviceData.modelId,
      // 协议由后端从 modelId 推导,前端不提交 type
      isEnabled: true,
      pollingInterval: 1000,
      configJson: deviceData.configJson ?? '{}',
      ipAddress: deviceData.ipAddress?.trim() || null,
      port: typeof deviceData.port === 'string' ? parseInt(deviceData.port, 10) : deviceData.port || null,
      cpuType: deviceData.cpuType?.trim() || null,
      rack: deviceData.rack !== undefined && deviceData.rack !== null ? deviceData.rack : null,
      slot: deviceData.slot !== undefined && deviceData.slot !== null ? deviceData.slot : null
    };

    const response = await api.createDeviceOnBackend(requestData);
    const createdDevice = response.data;

    await syncDevices();

    addLog('设备管理', `已在后端创建设备: ${deviceData.name}`, 'normal');
    return { success: true, data: createdDevice };
  } catch (err: any) {
    const errorResult = parseApiError(err);
    addLog('设备管理', `创建设备失败: ${errorResult.message}`, 'warning');
    return { success: false, error: errorResult };
  }
};

export const updateDeviceAndSync = async (deviceId: number, deviceData: Partial<Device>): Promise<DeviceOperationResult> => {
  if (systemConfig.value.isSimulationActive) {
    const idx = store.devices.value.findIndex(d => d.id === deviceId);
    if (idx !== -1) {
      store.devices.value[idx] = { ...store.devices.value[idx], ...deviceData };
    }
    return { success: true };
  }

  try {
    const requestData = {
      id: deviceId,
      name: deviceData.name?.trim(),
      key: deviceData.key?.trim(),
      areaId: deviceData.areaId,
      modelId: deviceData.modelId,
      // 协议由后端从 modelId 推导,前端不提交 type
      isEnabled: true,
      pollingInterval: 1000,
      configJson: deviceData.configJson ?? '{}',
      ipAddress: deviceData.ipAddress?.trim() || null,
      port: typeof deviceData.port === 'string' ? parseInt(deviceData.port, 10) : deviceData.port || null,
      cpuType: deviceData.cpuType?.trim() || null,
      rack: deviceData.rack !== undefined && deviceData.rack !== null ? deviceData.rack : null,
      slot: deviceData.slot !== undefined && deviceData.slot !== null ? deviceData.slot : null
    };

    await api.updateDeviceOnBackend(requestData);
    await syncDevices();

    addLog('设备管理', `已更新设备配置: ${deviceData.name || deviceId}`, 'normal');
    return { success: true };
  } catch (err: any) {
    const errorResult = parseApiError(err);
    addLog('设备管理', `更新设备失败 [${deviceId}]: ${errorResult.message}`, 'warning');
    return { success: false, error: errorResult };
  }
};

export const deleteDeviceAndSync = async (id: number, name: string): Promise<DeviceOperationResult> => {
  if (systemConfig.value.isSimulationActive) {
    store.setDevices(store.devices.value.filter(d => d.id !== id));
    return { success: true };
  }

  try {
    await api.deleteDeviceOnBackend(id);
    await syncDevices();

    addLog('设备管理', `已删除设备 [${name}]`, 'warning');
    return { success: true };
  } catch (err: any) {
    const errorResult = parseApiError(err);
    addLog('设备管理', `删除设备失败 [${id}]: ${errorResult.message}`, 'warning');
    return { success: false, error: errorResult };
  }
};

export const getDeviceById = async (id: number): Promise<Device | null> => {
  if (systemConfig.value.isSimulationActive) {
    return store.devices.value.find(d => d.id === id) || null;
  }

  try {
    const { data } = await api.fetchDeviceById(id);
    return data;
  } catch (err: any) {
    addLog('设备管理', `获取设备详情失败 [${id}]: ${extractApiError(err)}`, 'warning');
    return null;
  }
};
