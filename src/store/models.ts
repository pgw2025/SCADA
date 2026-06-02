import { ref } from 'vue';
import axios from 'axios';
import { DataModel } from '../types';
import { addLog, systemConfig } from './index';

export const dataModels = ref<DataModel[]>([]);

// POST /api/DataModel - 创建新数据模型
export const createDataModelOnBackend = async (modelData: Omit<DataModel, 'id'>): Promise<DataModel | null> => {
  if (systemConfig.value.isSimulationActive) return null;

  try {
    const response = await axios.post(`${systemConfig.value.backendApiUrl}/api/DataModel`, modelData);
    const createdModel = response.data;
    
    dataModels.value.push({
      id: createdModel.id,
      name: createdModel.name,
      description: createdModel.description || '',
      type: createdModel.type,
      variables: createdModel.variables || []
    });
    
    addLog('数据模型', `已在后端创建模型: ${modelData.name}`, 'normal');
    return createdModel;
  } catch (err: any) {
    addLog('数据模型', `创建模型失败: ${err.message}`, 'warning');
    return null;
  }
};

// GET /api/DataModel - 获取所有数据模型
export const fetchDataModelsFromBackend = async (): Promise<void> => {
  if (systemConfig.value.isSimulationActive) return;

  try {
    const response = await axios.get(`${systemConfig.value.backendApiUrl}/api/DataModel`);
    const data = response.data;
    
    if (Array.isArray(data)) {
      // 用服务器数据替换本地数据
      dataModels.value = data.map((m: any) => ({
        id: m.id,
        name: m.name,
        description: m.description || '',
        type: m.type,
        variables: m.variables || []
      }));
      
      addLog('数据模型', `已从后端同步 ${data.length} 个模型`, 'normal');
    }
  } catch (err: any) {
    addLog('数据模型', `无法同步模型列表: ${err.message}`, 'warning');
  }
};

// PUT /api/DataModel - 更新数据模型
export const updateDataModelOnBackend = async (modelId: string, modelData: Partial<DataModel>): Promise<boolean> => {
  if (systemConfig.value.isSimulationActive) return true;

  try {
    await axios.put(`${systemConfig.value.backendApiUrl}/api/DataModel/${modelId}`, modelData);
    
    const idx = dataModels.value.findIndex(m => m.id === modelId);
    if (idx !== -1) {
      dataModels.value[idx] = { ...dataModels.value[idx], ...modelData };
    }
    
    addLog('数据模型', `已更新数据模型配置: ${modelData.name || modelId}`, 'normal');
    return true;
  } catch (err: any) {
    addLog('数据模型', `更新模型失败 [${modelId}]: ${err.message}`, 'warning');
    return false;
  }
};

// DELETE /api/DataModel/{id} - 删除数据模型
export const deleteDataModelOnBackend = async (id: string): Promise<boolean> => {
  if (systemConfig.value.isSimulationActive) {
    dataModels.value = dataModels.value.filter(m => m.id !== id);
    return true;
  }

  try {
    await axios.delete(`${systemConfig.value.backendApiUrl}/api/DataModel/${id}`);
    
    dataModels.value = dataModels.value.filter(m => m.id !== id);
    addLog('数据模型', `已删除数据模型 [${id}]`, 'warning');
    return true;
  } catch (err: any) {
    addLog('数据模型', `删除模型失败 [${id}]: ${err.message}`, 'warning');
    return false;
  }
};
