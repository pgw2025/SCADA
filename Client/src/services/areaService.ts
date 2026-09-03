import * as api from '../api/areaApi';
import * as store from '../store/areaStore';
import { addLog, systemConfig } from '../store/index';
import { Area, AreaTreeNode } from '../types';
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

/** 区域表单提交载荷：兼容旧字段（name/description）与阶段 1 新增树形字段。 */
export interface AreaFormData {
  name: string;
  description?: string;
  parentId?: number | null;
  code?: string | null;
  areaType?: number;
  sort?: number;
  isEnabled?: boolean;
}

/** 组装提交给后端的 PascalCase 请求体（Id 仅更新时传入）。 */
const toRequestData = (areaId: number | null, data: AreaFormData) => ({
  Id: areaId ?? 0,
  ParentId: data.parentId ?? null,
  Name: data.name.trim(),
  Code: data.code?.trim() || null,
  AreaType: data.areaType ?? 4,
  Description: data.description?.trim() || '',
  Sort: data.sort ?? 0,
  IsEnabled: data.isEnabled ?? true
});

export const createAreaAndSync = async (areaData: AreaFormData): Promise<AreaOperationResult<Area>> => {
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
    const response = await api.createArea(toRequestData(null, areaData));
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

export const updateAreaAndSync = async (areaId: number, areaData: AreaFormData): Promise<AreaOperationResult> => {
  if (systemConfig.value.isSimulationActive) {
    const idx = store.areas.value.findIndex(a => a.id === areaId);
    if (idx !== -1) {
      store.areas.value[idx] = { ...store.areas.value[idx], ...areaData };
    }
    return { success: true };
  }

  try {
    await api.updateArea(toRequestData(areaId, areaData));
    await syncAreas();

    addLog('区域管理', `已更新区域配置: ${areaData.name || areaId}`, 'normal');
    return { success: true };
  } catch (err: any) {
    const errorResult = parseApiError(err);
    addLog('区域管理', `更新区域失败 [${areaId}]: ${errorResult.message}`, 'warning');
    return { success: false, error: errorResult };
  }
};

/** 拉取区域树（阶段 1 新增），供树形筛选 / 区域管理树展示。 */
export const getAreaTree = async (): Promise<AreaTreeNode[]> => {
  if (systemConfig.value.isSimulationActive) {
    return store.areas.value.map(a => ({
      id: a.id ?? 0,
      parentId: a.parentId ?? null,
      name: a.name,
      description: a.description,
      areaType: a.areaType,
      sort: a.sort ?? 0,
      isEnabled: a.isEnabled ?? true,
      deviceCount: 0,
      children: []
    }));
  }

  try {
    const { data } = await api.getAreaTree();
    return data;
  } catch (err: any) {
    addLog('区域管理', `无法同步区域树: ${err.message}`, 'warning');
    return [];
  }
};

/** 获取指定区域（含所有子孙区域）下直接挂载的设备 ID 列表，供"含子区域"过滤设备使用。 */
export const getSubtreeDeviceIds = async (areaId: number): Promise<number[]> => {
  if (systemConfig.value.isSimulationActive) return [];

  try {
    const { data } = await api.getDeviceIdsInSubtree(areaId);
    return data;
  } catch (err: any) {
    addLog('区域管理', `获取区域子树设备失败 [${areaId}]: ${err.message}`, 'warning');
    return [];
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
