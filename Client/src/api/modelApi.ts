import { http, extractApiError } from './http';
import { DataModel, AccessMode } from '../types';
import { dataModels } from '../store/modelStore';
import { addLog, systemConfig } from '../store/index';

/** 后端 AccessMode 三值白名单（防御旧后端/脏数据返回未知串）。 */
const isAccessMode = (m: unknown): m is AccessMode =>
  m === 'Read' || m === 'Write' || m === 'ReadWrite';

// POST /api/DataModel - 创建新数据模型
export const createDataModelOnBackend = async (modelData: Omit<DataModel, 'id'>): Promise<DataModel | null> => {
  if (systemConfig.value.isSimulationActive) return null;

  try {
    const response = await http.post(`${systemConfig.value.backendApiUrl}/api/DataModel`, modelData);
    const createdModel = response.data;

    dataModels.value.push({
      id: String(createdModel.id),
      name: createdModel.name,
      code: createdModel.code ?? modelData.code ?? '',
      version: createdModel.version ?? modelData.version ?? '1.0',
      isPublished: createdModel.isPublished ?? modelData.isPublished ?? true,
      description: createdModel.description || '',
      // 协议真相源在 Protocol 实体：创建成功后回填 protocolId / protocolKey / protocolName
      protocolId: createdModel.protocolId ?? modelData.protocolId,
      protocolKey: createdModel.protocolKey ?? modelData.protocolKey,
      protocolName: createdModel.protocolName ?? modelData.protocolName,
      variables: createdModel.variables || []
    });

    addLog('数据模型', `已在后端创建模型: ${modelData.name}`, 'normal');
    return dataModels.value[dataModels.value.length - 1];
  } catch (err: any) {
    addLog('数据模型', `创建模型失败: ${extractApiError(err)}`, 'warning');
    return null;
  }
};

// GET /api/DataModel - 获取所有数据模型
export const fetchDataModelsFromBackend = async (): Promise<void> => {
  if (systemConfig.value.isSimulationActive) return;

  try {
    const response = await http.get(`${systemConfig.value.backendApiUrl}/api/DataModel`);
    const data = response.data;
    
    if (Array.isArray(data)) {
      dataModels.value = data.map((m: any) => ({
        id: String(m.id),
        name: m.name,
        code: m.code ?? '',
        version: m.version ?? '1.0',
        isPublished: m.isPublished ?? true,
        description: m.description || '',
        // 协议真相源在 Protocol 实体（对应后端 DataModelDto.ProtocolId / ProtocolKey / ProtocolName），
        // 更新模型时须原样回传 protocolId
        protocolId: m.protocolId ?? 0,
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
          description: v.description,
          isStored: v.isStored || false,
          storeMode: v.storeMode && v.storeMode !== 'None' ? v.storeMode : (v.isStored ? 'Change' : 'None'),
          storeIntervalMs: v.storeIntervalMs ?? 300000,
          // 后端 UpdateMode 枚举以 JsonStringEnumConverter 输出 PascalCase（Subscription/Polling），
          // 统一 toLowerCase 后比较，避免大小写漂移导致订阅模式变量回传后恒变 polling。
          updateMode: (String(v.updateMode ?? '').toLowerCase() === 'subscription' ? 'subscription' : 'polling') as 'polling' | 'subscription',
          scaleExpression: v.scaleExpression ?? '',
          deadBand: v.deadBand,
          // 仅当后端缺省（null/undefined）时回退到安全默认 true；
          // 后端明确返回 false（可写）时须保留 false，否则会被 || true 误判为只读导致写入按钮恒隐藏。
          // @deprecated isReadOnly：阶段 4 起读写以 accessMode 为权威（== accessMode==='Read'）。
          isReadOnly: v.isReadOnly ?? true,
          // 阶段 4 定义字段：accessMode 权威；旧后端（无 accessMode）按 isReadOnly 推导兼容
          accessMode: isAccessMode(v.accessMode)
            ? v.accessMode
            : (v.isReadOnly === false ? 'ReadWrite' : 'Read'),
          isRequired: v.isRequired ?? false,
          sort: v.sort ?? 0,
          isEnabled: v.isEnabled ?? true,
          extensionData: v.extensionData
        })) || []
      }));
      
      addLog('数据模型', `已从后端同步 ${data.length} 个模型`, 'normal');
    }
  } catch (err: any) {
    addLog('数据模型', `无法同步模型列表: ${extractApiError(err)}`, 'warning');
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

    await http.put(`${systemConfig.value.backendApiUrl}/api/DataModel/${modelId}`, payload);
    
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
    addLog('数据模型', `更新模型失败 [${modelId}]: ${extractApiError(err)}`, 'warning');
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
    await http.delete(`${systemConfig.value.backendApiUrl}/api/DataModel/${id}`);
    
    dataModels.value = dataModels.value.filter(m => m.id !== id);
    addLog('数据模型', `已删除数据模型 [${id}]`, 'warning');
    return true;
  } catch (err: any) {
    addLog('数据模型', `删除模型失败 [${id}]: ${extractApiError(err)}`, 'warning');
    return false;
  }
};
