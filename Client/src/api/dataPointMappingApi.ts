import { http, extractApiError } from './http';
import { DataPointMapping } from '../types';
import { systemConfig, addLog } from '../store/index';

const BASE_URL = () => `${systemConfig.value.backendApiUrl}/api/DataPointMapping`;

// GET /api/DataPointMapping/by-device/{deviceId} - 获取某设备的全部变量实例
export const fetchDataPointMappings = async (deviceId: number): Promise<DataPointMapping[]> => {
  try {
    const response = await http.get<DataPointMapping[]>(`${BASE_URL()}/by-device/${deviceId}`);
    return response.data;
  } catch (error: any) {
    addLog('设备变量', `获取设备变量列表失败 [设备ID:${deviceId}]: ${extractApiError(error)}`, 'warning');
    throw error;
  }
};

// POST /api/DataPointMapping - 创建设备变量实例（仅需 deviceId + dataPointId + isEnabled）
export interface CreateDataPointMappingDto {
  deviceId: number;
  dataPointId: number;
  isEnabled?: boolean;
}

export const createDataPointMapping = async (dto: CreateDataPointMappingDto): Promise<DataPointMapping> => {
  try {
    const response = await http.post<DataPointMapping>(BASE_URL(), dto);
    addLog('设备变量', `已创建设备变量实例 [模板ID:${dto.dataPointId}]`, 'normal');
    return response.data;
  } catch (error: any) {
    addLog('设备变量', `创建设备变量实例失败 [模板ID:${dto.dataPointId}]: ${extractApiError(error)}`, 'warning');
    throw error;
  }
};

// PUT /api/DataPointMapping - 更新设备变量实例（全量替换语义，需传完整 DataPointMapping）
export const updateDataPointMapping = async (dV: DataPointMapping): Promise<DataPointMapping> => {
  try {
    const response = await http.put<DataPointMapping>(BASE_URL(), dV);
    addLog('设备变量', `已更新设备变量实例 [${dV.key}]`, 'normal');
    return response.data;
  } catch (error: any) {
    addLog('设备变量', `更新设备变量实例失败 [${dV.key}]: ${extractApiError(error)}`, 'warning');
    throw error;
  }
};

// DELETE /api/DataPointMapping/{id} - 删除设备变量实例
export const deleteDataPointMapping = async (id: number, label?: string): Promise<void> => {
  try {
    await http.delete(`${BASE_URL()}/${id}`);
    addLog('设备变量', `已删除设备变量实例 [${label ?? id}]`, 'warning');
  } catch (error: any) {
    addLog('设备变量', `删除设备变量实例失败 [${label ?? id}]: ${extractApiError(error)}`, 'warning');
    throw error;
  }
};