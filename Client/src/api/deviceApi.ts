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

// DELETE /api/Device/{id} - 删除设备
export const deleteDeviceOnBackend = async (id: number) => {
  const response = await http.delete(`${getBaseUrl()}/api/Device/${id}`);
  return response;
};
