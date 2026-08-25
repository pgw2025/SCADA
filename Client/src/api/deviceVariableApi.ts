import { http } from './http';
import { DeviceVariable } from '../types';
import { systemConfig, addLog } from '../store/index';

const BASE_URL = () => `${systemConfig.value.backendApiUrl}/api/DeviceVariable`;

// GET /api/DeviceVariable/by-device/{deviceId} - 获取某设备的全部变量实例
export const fetchDeviceVariables = async (deviceId: number): Promise<DeviceVariable[]> => {
  try {
    const response = await http.get<DeviceVariable[]>(`${BASE_URL()}/by-device/${deviceId}`);
    return response.data;
  } catch (error: any) {
    addLog('设备变量', `获取设备变量列表失败 [设备ID:${deviceId}]: ${error.message}`, 'warning');
    throw error;
  }
};

// POST /api/DeviceVariable - 创建设备变量实例（仅需 deviceId + modelVariableId + isEnabled）
export interface CreateDeviceVariableDto {
  deviceId: number;
  modelVariableId: number;
  isEnabled?: boolean;
}

export const createDeviceVariable = async (dto: CreateDeviceVariableDto): Promise<DeviceVariable> => {
  try {
    const response = await http.post<DeviceVariable>(BASE_URL(), dto);
    addLog('设备变量', `已创建设备变量实例 [模板ID:${dto.modelVariableId}]`, 'normal');
    return response.data;
  } catch (error: any) {
    addLog('设备变量', `创建设备变量实例失败 [模板ID:${dto.modelVariableId}]: ${error.message}`, 'warning');
    throw error;
  }
};

// PUT /api/DeviceVariable - 更新设备变量实例（全量替换语义，需传完整 DeviceVariable）
export const updateDeviceVariable = async (dV: DeviceVariable): Promise<DeviceVariable> => {
  try {
    const response = await http.put<DeviceVariable>(BASE_URL(), dV);
    addLog('设备变量', `已更新设备变量实例 [${dV.key}]`, 'normal');
    return response.data;
  } catch (error: any) {
    addLog('设备变量', `更新设备变量实例失败 [${dV.key}]: ${error.message}`, 'warning');
    throw error;
  }
};

// DELETE /api/DeviceVariable/{id} - 删除设备变量实例
export const deleteDeviceVariable = async (id: number, label?: string): Promise<void> => {
  try {
    await http.delete(`${BASE_URL()}/${id}`);
    addLog('设备变量', `已删除设备变量实例 [${label ?? id}]`, 'warning');
  } catch (error: any) {
    addLog('设备变量', `删除设备变量实例失败 [${label ?? id}]: ${error.message}`, 'warning');
    throw error;
  }
};