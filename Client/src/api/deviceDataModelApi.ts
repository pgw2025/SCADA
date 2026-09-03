import { http, extractApiError } from './http';
import { systemConfig, addLog } from '../store/index';
import { DeviceModelBinding } from '../types';

/** RESTful 子资源基址：/api/devices/{deviceId}/data-models（阶段 5 绑定管理）。 */
const dataModelBindingUrl = (deviceId: number) =>
  `${systemConfig.value.backendApiUrl}/api/devices/${deviceId}/data-models`;

/**
 * GET /api/devices/{deviceId}/data-models
 * 查询某设备全部数据模型绑定（含模型摘要 Code/Name/Version 与模型变量数）。
 * 返回数组按 IsPrimary 降序（主模型行在前）。
 */
export const fetchDeviceDataModelBindings = async (deviceId: number): Promise<DeviceModelBinding[]> => {
  if (systemConfig.value.isSimulationActive) return [];
  try {
    const response = await http.get<DeviceModelBinding[]>(dataModelBindingUrl(deviceId));
    return response.data || [];
  } catch (error: any) {
    addLog('设备数据模型', `获取模型绑定列表失败 [设备ID:${deviceId}]: ${extractApiError(error)}`, 'warning');
    return [];
  }
};

/** 绑定请求体（与后端 DeviceDataModelRequest / BindDeviceDataModelDto 对齐）。 */
export interface DeviceDataModelBindDto {
  /** 目标数据模型 ID（必填）。 */
  dataModelId: number;
  /** 是否同时设为主模型（默认 false；设为主时后端事务内降级旧主并同步 Device.ModelId）。 */
  isPrimary?: boolean;
}

/**
 * POST /api/devices/{deviceId}/data-models
 * 绑定一个数据模型到设备；isPrimary=true 时同时设为主模型。
 * 成功返回刷新后的绑定列表。
 */
export const bindDeviceDataModel = async (deviceId: number, dto: DeviceDataModelBindDto): Promise<DeviceModelBinding[]> => {
  if (systemConfig.value.isSimulationActive) return [];
  const response = await http.post<DeviceModelBinding[]>(dataModelBindingUrl(deviceId), dto);
  return response.data || [];
};

/**
 * PUT /api/devices/{deviceId}/data-models/primary
 * 切换主模型（目标必须是已绑定模型；后端事务内降级旧主并同步 Device.ModelId + 热重载运行时）。
 * 成功返回刷新后的绑定列表。
 */
export const setPrimaryDeviceDataModel = async (deviceId: number, dataModelId: number): Promise<DeviceModelBinding[]> => {
  if (systemConfig.value.isSimulationActive) return [];
  const response = await http.put<DeviceModelBinding[]>(`${dataModelBindingUrl(deviceId)}/primary`, { dataModelId });
  return response.data || [];
};

/**
 * DELETE /api/devices/{deviceId}/data-models/{dataModelId}
 * 解绑模型（主模型不可解绑；该模型下存在设备变量实例引用时后端拒绝）。
 * 成功返回刷新后的绑定列表。
 */
export const unbindDeviceDataModel = async (deviceId: number, dataModelId: number): Promise<DeviceModelBinding[]> => {
  if (systemConfig.value.isSimulationActive) return [];
  const response = await http.delete<DeviceModelBinding[]>(`${dataModelBindingUrl(deviceId)}/${dataModelId}`);
  return response.data || [];
};
