import { http } from './http';
import { Device } from '../types';
import { systemConfig } from '../store/configStore';

const getBaseUrl = () => systemConfig.value.backendApiUrl;

// GET /api/Device - 获取所有设备列表
export const fetchDevicesFromBackend = async () => {
  const response = await http.get<Device[]>(`${getBaseUrl()}/api/Device`);
  return response;
};

// POST /api/Device - 创建新设备
export const createDeviceOnBackend = async (deviceData: any) => {
  const response = await http.post<Device>(`${getBaseUrl()}/api/Device`, deviceData);
  return response;
};

// GET /api/Device/{id} - 获取单个设备详情
export const fetchDeviceById = async (id: number) => {
  const response = await http.get<Device>(`${getBaseUrl()}/api/Device/${id}`);
  return response;
};

// PUT /api/Device - 更新设备信息
export const updateDeviceOnBackend = async (deviceData: any) => {
  const response = await http.put<Device>(`${getBaseUrl()}/api/Device`, deviceData);
  return response;
};

// PUT /api/Device/{id}/enabled?enabled= - 启用/停用设备采集（停用即注销运行时、断开驱动）
export const setDeviceEnabled = async (id: number, enabled: boolean) => {
  const response = await http.put<Device>(`${getBaseUrl()}/api/Device/${id}/enabled`, null, { params: { enabled } });
  return response;
};

// ---- 批量启停（区域子树 或 显式设备 ID 列表）----

export interface BatchSetEnabledRequest {
  areaId?: number | null;
  includeSubAreas?: boolean;
  deviceIds?: number[] | null;
  enabled: boolean;
  skipInvalid?: boolean;
}

export interface BatchSetEnabledItem {
  deviceId: number;
  name?: string | null;
  result: string;
  reason?: string | null;
}

export interface BatchSetEnabledResult {
  operationId?: string | null;
  total: number;
  succeeded: number;
  skipped: number;
  failed: number;
  items: BatchSetEnabledItem[];
}

export interface BatchSetEnabledBlocked {
  deviceId: number;
  name?: string | null;
  reason?: string | null;
}

export interface BatchSetEnabledPrecheck {
  total: number;
  startable: number;
  blocked: BatchSetEnabledBlocked[];
}

// POST /api/Device/batch/enabled - 批量启用/停用设备采集（串行、逐台隔离、部分成功）
export const batchSetDeviceEnabled = async (req: BatchSetEnabledRequest) => {
  const response = await http.post<BatchSetEnabledResult>(`${getBaseUrl()}/api/Device/batch/enabled`, req);
  return response;
};

// POST /api/Device/batch/enabled/precheck - 批量启用前预检（只校验不执行）
export const precheckBatchSetDeviceEnabled = async (req: BatchSetEnabledRequest) => {
  const response = await http.post<BatchSetEnabledPrecheck>(`${getBaseUrl()}/api/Device/batch/enabled/precheck`, req);
  return response;
};

// DELETE /api/Device/{id} - 删除设备
export const deleteDeviceOnBackend = async (id: number) => {
  const response = await http.delete(`${getBaseUrl()}/api/Device/${id}`);
  return response;
};

// POST /api/Device/{deviceId}/variables/{variableKey}/write - 向设备运行时变量写入值（下发强制/控制命令）
export const writeDataPointMapping = async (deviceId: number, variableKey: string, value: number | boolean) => {
  const response = await http.post<any>(`${getBaseUrl()}/api/Device/${deviceId}/variables/${variableKey}/write`, { value });
  return response;
};

// GET /api/TelemetryData/{deviceId}/realtime - 获取设备运行时所有变量的当前实时值（含手动写入值）。
// 设备列表接口(DeviceDto.Variables)仅返回配置不含实时值，前端刷新/重连后需调用本接口回填。
export const fetchDeviceRealtime = async (deviceId: number) => {
  const response = await http.get<any>(`${getBaseUrl()}/api/TelemetryData/${deviceId}/realtime`);
  return response;
};
