import * as api from '../api/areaApi';
import * as store from '../store/areaStore';
import { addLog, systemConfig } from '../store/index';

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

export const createAreaAndSync = async (areaData: { name: string, description: string }) => {
  if (systemConfig.value.isSimulationActive) return null;

  try {
    const requestData = {
      Name: areaData.name || '',
      Description: areaData.description || ''
    };
    
    const response = await api.createArea(requestData);
    const createdArea = response.data;
    
    // Refresh list
    await syncAreas();
    
    addLog('区域管理', `已在后端创建区域: ${areaData.name}`, 'normal');
    return createdArea;
  } catch (err: any) {
    addLog('区域管理', `创建区域失败: ${err.message}`, 'warning');
    return null;
  }
};

export const updateAreaAndSync = async (areaId: string, areaData: { name: string, description: string }) => {
  if (systemConfig.value.isSimulationActive) return true;

  try {
    const requestData = {
      Id: Number(areaId) || 0,
      Name: areaData.name,
      Description: areaData.description
    };
    
    await api.updateArea(requestData);
    await syncAreas();
    
    addLog('区域管理', `已更新区域配置: ${areaData.name || areaId}`, 'normal');
    return true;
  } catch (err: any) {
    addLog('区域管理', `更新区域失败 [${areaId}]: ${err.message}`, 'warning');
    return false;
  }
};

export const deleteAreaAndSync = async (id: string, name: string) => {
  if (systemConfig.value.isSimulationActive) {
    store.setAreas(store.areas.value.filter(a => a.id !== id));
    return true;
  }

  try {
    await api.deleteArea(id);
    await syncAreas();
    
    addLog('区域管理', `已删除区域 [${name}]`, 'warning');
    return true;
  } catch (err: any) {
    addLog('区域管理', `删除区域失败 [${id}]: ${err.message}`, 'warning');
    return false;
  }
};
