import * as api from '../api/deviceApi';
import * as store from '../store/deviceStore';
import { addLog, systemConfig } from '../store/index';
import { Device } from '../types';
import { parseApiError, ErrorResult } from '../utils/errorHandler';
import { normalizeDevices } from '../utils/deviceStatus';

export interface DeviceOperationResult<T = any> {
  success: boolean;
  data?: T;
  error?: ErrorResult;
}

export const syncDevices = async () => {
  if (systemConfig.value.isSimulationActive) return;

  try {
    const { data } = await api.fetchDevicesFromBackend();
    const normalized = normalizeDevices(data);
    store.setDevices(normalized);
    addLog('设备管理', `已从后端同步 ${normalized.length} 个设备`, 'normal');
  } catch (err: any) {
    addLog('设备管理', `无法同步设备列表: ${err.message}`, 'warning');
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
    addLog('设备管理', `获取设备详情失败 [${id}]: ${err.message}`, 'warning');
    return null;
  }
};
