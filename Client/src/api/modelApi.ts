import axios from 'axios';
import { DataModel } from '../types';
import { dataModels } from '../store/modelStore';
import { addLog, systemConfig } from '../store/index';

// POST /api/DataModel - 创建新数据模型
export const createDataModelOnBackend = async (modelData: Omit<DataModel, 'id'>): Promise<DataModel | null> => {
  if (systemConfig.value.isSimulationActive) return null;

  try {
    const response = await axios.post(`${systemConfig.value.backendApiUrl}/api/DataModel`, modelData);
    const createdModel = response.data;
    
    dataModels.value.push({
      id: String(createdModel.id),
      name: createdModel.name,
      description: createdModel.description || '',
      // 协议真相源在 DataModel.Type（后端返回）
      type: (createdModel.type as DataModel['type']) ?? (modelData.type || 'Virtual'),
      variables: createdModel.variables || []
    });
    
    addLog('数据模型', `已在后端创建模型: ${modelData.name}`, 'normal');
    return dataModels.value[dataModels.value.length - 1];
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
      dataModels.value = data.map((m: any) => ({
        id: String(m.id),
        name: m.name,
        description: m.description || '',
        // 协议真相源在 DataModel.Type（后端返回真实协议）
        type: (m.type as DataModel['type']) || 'Virtual',
        // 协议绑定（对应后端 DataModelDto.ProtocolId），更新模型时须原样回传
        protocolId: m.protocolId ?? undefined,
        protocolKey: m.protocolKey ?? undefined,
        protocolName: m.protocolName ?? undefined,
        variables: m.variables?.map((v: any) => ({
          id: v.id,
          modelId: v.modelId,
          key: v.key,
          name: v.name,
          type: v.type?.toLowerCase() === 'digital' ? 'digital' : 'analog',
          dataType: v.dataType || 'INT',
          unit: v.unit,
          min: v.min,
          max: v.max,
          address: v.address,
          description: v.description,
          isStored: v.isStored || false,
          storeMode: (v.storeMode === 'Cycle' ? 'Cycle' : 'Change') as 'Change' | 'Cycle',
          updateMode: (v.updateMode === 'subscription' ? 'subscription' : 'polling') as 'polling' | 'subscription',
          pollingIntervalMs: v.pollingIntervalMs || 1000,
          bitOffset: v.bitOffset,
          scaleSlope: v.scaleSlope || 1.0,
          scaleOffset: v.scaleOffset || 0.0,
          deadBand: v.deadBand,
          isReadOnly: v.isReadOnly || true,
          extensionData: v.extensionData
        })) || []
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
    // 后端 PUT /api/DataModel/{id} 为全量替换语义：未传 protocolId 会解绑协议。
    // 这里在合并提交体时保留既有模型的协议绑定，避免误解绑。
    const existing = dataModels.value.find(m => m.id === modelId);
    const payload: Record<string, any> = {
      ...modelData,
      protocolId: modelData.protocolId ?? existing?.protocolId ?? null
    };

    await axios.put(`${systemConfig.value.backendApiUrl}/api/DataModel/${modelId}`, payload);
    
    const idx = dataModels.value.findIndex(m => m.id === modelId);
    if (idx !== -1) {
      dataModels.value[idx] = { 
        ...dataModels.value[idx], 
        ...modelData,
        id: String(dataModels.value[idx].id)
      };
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
