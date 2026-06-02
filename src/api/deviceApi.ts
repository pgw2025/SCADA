import axios from 'axios';
import { Device } from '../types';
import { devices } from '../store/deviceStore';
import { addLog, systemConfig } from '../store/system';

// GET /api/Device - 获取所有设备列表
export const fetchDevicesFromBackend = async (): Promise<void> => {
  if (systemConfig.value.isSimulationActive) return;

  try {
    const response = await axios.get(`${systemConfig.value.backendApiUrl}/api/Device`);
    const data = response.data;

    if (Array.isArray(data)) {
      // 清空模拟数据，使用后端真实数据
      devices.value = [];

      data.forEach((backendDev: any) => {
        devices.value.push({
          id: String(backendDev.id) || `dev-${Date.now()}`,
          name: backendDev.name || '',
          code: backendDev.code || 'UNKNOWN',
          areaId: String(backendDev.areaId) || 'area-1',
          modelId: String(backendDev.modelId) || 'model-wastewater',
          type: backendDev.type || 'OPCUA',
          ipAddress: backendDev.ipAddress || '',
          port: backendDev.port?.toString() || '',
          topic: backendDev.topic || '',
          status: backendDev.status === 1 || backendDev.status === 'online' ? 'online' : 'offline',
          variables: backendDev.variables || {},
          lastUpdated: backendDev.lastUpdated || '刚刚',
          cpuType: backendDev.cpuType || '',
          rack: backendDev.rack || 0,
          slot: backendDev.slot || 0,
          mqttServer: backendDev.mqttServer || '',
          publishTopic: backendDev.publishTopic || '',
          payloadTemplate: backendDev.payloadTemplate || ''
        });
      });

      addLog('设备管理', `已从后端同步 ${data.length} 台设备`, 'normal');
    }
  } catch (err: any) {
    addLog('REST 轮询', `无法同步设备列表: ${err.message}`, 'warning');
  }
};

// POST /api/Device - 创建新设备
export const createDeviceOnBackend = async (deviceData: Omit<Device, 'id' | 'lastUpdated' | 'variables'>): Promise<Device | null> => {
  if (systemConfig.value.isSimulationActive) return null;

  try {
    const requestData = {
      name: deviceData.name || '',
      code: deviceData.code || '',
      areaId: Number(deviceData.areaId) || 1,
      modelId: Number(deviceData.modelId) || 1,
      type: deviceData.type || 'OPCUA',
      ipAddress: deviceData.ipAddress || '',
      port: deviceData.port ? Number(deviceData.port) : 0,
      topic: deviceData.topic || '',
      status: 0,
      cpuType: deviceData.cpuType || '',
      rack: deviceData.rack || 0,
      slot: deviceData.slot || 0
    };

    const response = await axios.post(`${systemConfig.value.backendApiUrl}/api/Device`, requestData);
    const createdDevice = response.data;

    // 将新设备添加到本地状态
    devices.value.push({
      ...createdDevice,
      variables: createdDevice.variables || {},
      lastUpdated: '刚刚'
    });

    addLog('设备管理', `已在后端创建设备: ${deviceData.name}`, 'normal');
    return createdDevice;
  } catch (err: any) {
    addLog('设备管理', `创建设备失败: ${err.message}`, 'warning');
    return null;
  }
};

// GET /api/Device/{id} - 获取单个设备详情
export const fetchDeviceById = async (id: string): Promise<Device | null> => {
  if (systemConfig.value.isSimulationActive) {
    return devices.value.find(d => d.id === id) || null;
  }

  try {
    const response = await axios.get(`${systemConfig.value.backendApiUrl}/api/Device/${id}`);
    return response.data;
  } catch (err: any) {
    addLog('设备管理', `获取设备详情失败 [${id}]: ${err.message}`, 'warning');
    return null;
  }
};

// PUT /api/Device - 更新设备信息
export const updateDeviceOnBackend = async (deviceId: string, deviceData: Partial<Device>): Promise<boolean> => {
  if (systemConfig.value.isSimulationActive) return true;

  try {
    const requestData = {
      name: deviceData.name || '',
      code: deviceData.code || '',
      areaId: deviceData.areaId ? Number(deviceData.areaId) : undefined,
      modelId: deviceData.modelId ? Number(deviceData.modelId) : undefined,
      type: deviceData.type || 'OPCUA',
      ipAddress: deviceData.ipAddress || '',
      port: deviceData.port ? Number(deviceData.port) : 0,
      topic: deviceData.topic || '',
      status: 0,
      cpuType: deviceData.cpuType || '',
      rack: deviceData.rack || 0,
      slot: deviceData.slot || 0
    };

    await axios.put(`${systemConfig.value.backendApiUrl}/api/Device/${deviceId}`, requestData);

    // 更新本地状态
    const idx = devices.value.findIndex(d => d.id === deviceId);
    if (idx !== -1) {
      devices.value[idx] = { ...devices.value[idx], ...deviceData, lastUpdated: '刚刚' };
    }

    addLog('设备管理', `已更新设备配置: ${deviceData.name || deviceId}`, 'normal');
    return true;
  } catch (err: any) {
    addLog('设备管理', `更新设备失败 [${deviceId}]: ${err.message}`, 'warning');
    return false;
  }
};

// DELETE /api/Device/{id} - 删除设备
export const deleteDeviceOnBackend = async (id: string): Promise<boolean> => {
  if (systemConfig.value.isSimulationActive) {
    devices.value = devices.value.filter(d => d.id !== id);
    return true;
  }

  try {
    await axios.delete(`${systemConfig.value.backendApiUrl}/api/Device/${id}`);

    // 从本地状态移除
    devices.value = devices.value.filter(d => d.id !== id);

    addLog('设备管理', `已删除设备 [${id}]`, 'warning');
    return true;
  } catch (err: any) {
    addLog('设备管理', `删除设备失败 [${id}]: ${err.message}`, 'warning');
    return false;
  }
};

// POST /api/Device/{id}/update-config - 更新设备配置
export const updateDeviceConfig = async (deviceId: string, configJson: string): Promise<boolean> => {
  if (systemConfig.value.isSimulationActive) return true;

  try {
    await axios.post(`${systemConfig.value.backendApiUrl}/api/Device/${deviceId}/update-config`, configJson);
    addLog('设备管理', `已更新设备配置 [${deviceId}]`, 'normal');
    return true;
  } catch (err: any) {
    addLog('设备管理', `更新设备配置失败 [${deviceId}]: ${err.message}`, 'warning');
    return false;
  }
};

// POST /api/Device/{id}/control - 发送控制命令
export const sendDeviceControl = async (deviceId: string, command: string): Promise<boolean> => {
  if (systemConfig.value.isSimulationActive) return true;

  try {
    await axios.post(`${systemConfig.value.backendApiUrl}/api/Device/${deviceId}/control`, command);
    addLog('设备管理', `已向设备发送控制命令 [${deviceId}]`, 'normal');
    return true;
  } catch (err: any) {
    addLog('设备管理', `发送控制命令失败 [${deviceId}]: ${err.message}`, 'warning');
    return false;
  }
};
