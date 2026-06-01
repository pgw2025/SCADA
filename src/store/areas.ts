import { ref } from 'vue';
import axios from 'axios';
import { Area } from '../types';
import { addLog, systemConfig } from './system';

export const areas = ref<Area[]>([]);

// GET /api/Area - 获取所有区域列表
export const fetchAreasFromBackend = async (): Promise<void> => {
  if (systemConfig.value.isSimulationActive) return;

  try {
    const response = await axios.get(`${systemConfig.value.backendApiUrl}/api/Area`);
    const data = response.data;
    
    if (Array.isArray(data)) {
      // 清空模拟数据，使用后端真实数据
      areas.value = [];
      
      data.forEach((backendArea: any) => {
        areas.value.push({
          id: backendArea.id,
          name: backendArea.name,
          description: backendArea.description || ''
        });
      });
      
      addLog('区域管理', `已从后端同步 ${data.length} 个区域`, 'normal');
    }
  } catch (err: any) {
    addLog('区域管理', `无法同步区域列表: ${err.message}`, 'warning');
  }
};

// POST /api/Area - 创建新区域
export const createAreaOnBackend = async (areaData: Omit<Area, 'id'>): Promise<Area | null> => {
  if (systemConfig.value.isSimulationActive) return null;

  try {
    // 后端API期望PascalCase字段名
    const requestData = {
      Name: areaData.name || '',
      Description: areaData.description || ''
    };
    
    const response = await axios.post(`${systemConfig.value.backendApiUrl}/api/Area`, requestData);
    const createdArea = response.data;
    
    // 转换响应格式
    areas.value.push({
      id: createdArea.Id || createdArea.id,
      name: createdArea.Name || createdArea.name,
      description: createdArea.Description || createdArea.description || ''
    });
    
    addLog('区域管理', `已在后端创建区域: ${areaData.name}`, 'normal');
    return createdArea;
  } catch (err: any) {
    addLog('区域管理', `创建区域失败: ${err.message}`, 'warning');
    return null;
  }
};

// PUT /api/Area - 更新区域信息
export const updateAreaOnBackend = async (areaId: string, areaData: Partial<Area>): Promise<boolean> => {
  if (systemConfig.value.isSimulationActive) return true;

  try {
    // 后端API期望PascalCase字段名
    const requestData = {
      Id: Number(areaId) || 0,
      Name: areaData.name,
      Description: areaData.description
    };
    
    await axios.put(`${systemConfig.value.backendApiUrl}/api/Area`, requestData);
    
    const idx = areas.value.findIndex(a => a.id === areaId);
    if (idx !== -1) {
      areas.value[idx] = { ...areas.value[idx], ...areaData };
    }
    
    addLog('区域管理', `已更新区域配置: ${areaData.name || areaId}`, 'normal');
    return true;
  } catch (err: any) {
    addLog('区域管理', `更新区域失败 [${areaId}]: ${err.message}`, 'warning');
    return false;
  }
};

// DELETE /api/Area/{id} - 删除区域
export const deleteAreaOnBackend = async (id: string): Promise<boolean> => {
  if (systemConfig.value.isSimulationActive) {
    areas.value = areas.value.filter(a => a.id !== id);
    return true;
  }

  try {
    await axios.delete(`${systemConfig.value.backendApiUrl}/api/Area/${id}`);
    
    areas.value = areas.value.filter(a => a.id !== id);
    addLog('区域管理', `已删除区域 [${id}]`, 'warning');
    return true;
  } catch (err: any) {
    addLog('区域管理', `删除区域失败 [${id}]: ${err.message}`, 'warning');
    return false;
  }
};
