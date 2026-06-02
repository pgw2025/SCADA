import * as api from '../api/areaApi';
import * as store from '../store/areaStore';
import { addLog, systemConfig } from '../store/index';
import { Area } from '../types';
import { parseApiError, ErrorResult } from '../utils/errorHandler';

export interface AreaOperationResult<T = any> {
  success: boolean;
  data?: T;
  error?: ErrorResult;
}

export const syncAreas = async () => {
  if (systemConfig.value.isSimulationActive) return;

  try {
    const { data } = await api.getAreas();
    store.setAreas(data);
    addLog('区域管理', `已从后端同步 ${data.length} 个区域`, 'normal');
  } catch (err: any) {
    addLog('区域管理', `无法同步区域列表: ${err.message}`, 'warning');
  }
};

export const createAreaAndSync = async (areaData: { name: string, description: string }): Promise<AreaOperationResult<Area>> => {
  if (systemConfig.value.isSimulationActive) {
    const mockArea: Area = {
      id: Date.now(),
      name: areaData.name,
      description: areaData.description
    };
    store.setAreas([...store.areas.value, mockArea]);
    return { success: true, data: mockArea };
  }

  try {
    const requestData = {
      Name: areaData.name.trim(),
      Description: areaData.description?.trim() || ''
    };

    const response = await api.createArea(requestData);
    const createdArea = response.data;

    await syncAreas();

    addLog('区域管理', `已在后端创建区域: ${areaData.name}`, 'normal');
    return { success: true, data: createdArea };
  } catch (err: any) {
    const errorResult = parseApiError(err);
    addLog('区域管理', `创建区域失败: ${errorResult.message}`, 'warning');
    return { success: false, error: errorResult };
  }
};

export const updateAreaAndSync = async (areaId: number, areaData: { name: string, description: string }): Promise<AreaOperationResult> => {
  if (systemConfig.value.isSimulationActive) {
    const idx = store.areas.value.findIndex(a => a.id === areaId);
    if (idx !== -1) {
      store.areas.value[idx] = { ...store.areas.value[idx], ...areaData };
    }
    return { success: true };
  }

  try {
    const requestData = {
      Id: Number(areaId) || 0,
      Name: areaData.name.trim(),
      Description: areaData.description?.trim() || ''
    };

    await api.updateArea(requestData);
    await syncAreas();

    addLog('区域管理', `已更新区域配置: ${areaData.name || areaId}`, 'normal');
    return { success: true };
  } catch (err: any) {
    const errorResult = parseApiError(err);
    addLog('区域管理', `更新区域失败 [${areaId}]: ${errorResult.message}`, 'warning');
    return { success: false, error: errorResult };
  }
};

export const deleteAreaAndSync = async (id: number, name: string): Promise<AreaOperationResult> => {
  if (systemConfig.value.isSimulationActive) {
    store.setAreas(store.areas.value.filter(a => a.id !== id));
    return { success: true };
  }

  try {
    await api.deleteArea(id);
    await syncAreas();

    addLog('区域管理', `已删除区域 [${name}]`, 'warning');
    return { success: true };
  } catch (err: any) {
    const errorResult = parseApiError(err);
    addLog('区域管理', `删除区域失败 [${id}]: ${errorResult.message}`, 'warning');
    return { success: false, error: errorResult };
  }
};

export const getAreaById = async (id: number): Promise<Area | null> => {
  if (systemConfig.value.isSimulationActive) {
    return store.areas.value.find(a => a.id === id) || null;
  }

  try {
    const { data } = await api.getAreaById(id);
    return data;
  } catch (err: any) {
    addLog('区域管理', `获取区域详情失败 [${id}]: ${err.message}`, 'warning');
    return null;
  }
};
