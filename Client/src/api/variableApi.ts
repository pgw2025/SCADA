import { http } from './http';
import { DataPoint, VariableImportPreview, VariableImportResult, ConflictStrategy } from '../types';
import { systemConfig, addLog } from '../store/index';

const BASE_URL = () => `${systemConfig.value.backendApiUrl}/api/DataPoint`;

// GET /api/DataPoint - 获取所有变量
export const fetchVariables = async (): Promise<DataPoint[]> => {
  try {
    const response = await http.get<DataPoint[]>(BASE_URL());
    return response.data;
  } catch (error: any) {
    addLog('变量管理', `获取变量列表失败: ${error.message}`, 'warning');
    throw error;
  }
};

// GET /api/DataPoint/{id} - 获取单个变量
export const fetchVariableById = async (id: number): Promise<DataPoint> => {
  try {
    const response = await http.get<DataPoint>(`${BASE_URL()}/${id}`);
    return response.data;
  } catch (error: any) {
    addLog('变量管理', `获取变量详情失败 [${id}]: ${error.message}`, 'warning');
    throw error;
  }
};

// GET /api/DataPoint/by-model/{modelId} - 获取指定模型的变量
export const fetchVariablesByModelId = async (modelId: number): Promise<DataPoint[]> => {
  try {
    const response = await http.get<DataPoint[]>(`${BASE_URL()}/by-model/${modelId}`);
    return response.data;
  } catch (error: any) {
    addLog('变量管理', `获取模型变量失败 [模型ID:${modelId}]: ${error.message}`, 'warning');
    throw error;
  }
};

// POST /api/DataPoint - 创建新变量
export const createVariable = async (variable: Omit<DataPoint, 'id'>): Promise<DataPoint> => {
  try {
    const response = await http.post<DataPoint>(BASE_URL(), variable);
    addLog('变量管理', `已创建变量: ${variable.key}`, 'normal');
    return response.data;
  } catch (error: any) {
    addLog('变量管理', `创建变量失败 [${variable.key}]: ${error.message}`, 'warning');
    throw error;
  }
};

// PUT /api/DataPoint - 更新变量
export const updateVariable = async (variable: DataPoint): Promise<DataPoint> => {
  try {
    const response = await http.put<DataPoint>(`${BASE_URL()}/${variable.id}`, variable);
    addLog('变量管理', `已更新变量: ${variable.key}`, 'normal');
    return response.data;
  } catch (error: any) {
    addLog('变量管理', `更新变量失败 [${variable.id}]: ${error.message}`, 'warning');
    throw error;
  }
};

// DELETE /api/DataPoint/{id} - 删除变量
export const deleteVariable = async (id: number): Promise<void> => {
  try {
    await http.delete(`${BASE_URL()}/${id}`);
    addLog('变量管理', `已删除变量 [${id}]`, 'warning');
  } catch (error: any) {
    addLog('变量管理', `删除变量失败 [${id}]: ${error.message}`, 'warning');
    throw error;
  }
};

// POST /api/DataPoint/import/preview - 解析并预览导入文件（不入库）
export const previewVariableImport = async (modelId: number, file: File): Promise<VariableImportPreview> => {
  const form = new FormData();
  form.append('modelId', String(modelId));
  form.append('file', file);
  const response = await http.post<VariableImportPreview>(`${BASE_URL()}/import/preview`, form);
  return response.data;
};

// POST /api/DataPoint/import - 确认导入（按冲突策略批量写入）
export const submitVariableImport = async (
  modelId: number,
  file: File,
  strategy: ConflictStrategy = 'Skip'
): Promise<VariableImportResult> => {
  const form = new FormData();
  form.append('modelId', String(modelId));
  form.append('file', file);
  form.append('conflictStrategy', strategy);
  const response = await http.post<VariableImportResult>(`${BASE_URL()}/import`, form);
  return response.data;
};

// GET /api/DataPoint/by-model/{modelId}/export - 导出模型变量（xlsx/csv，blob 由调用方落地下载）
export const exportVariables = async (modelId: number, format: 'xlsx' | 'csv' = 'xlsx'): Promise<Blob> => {
  const response = await http.get<Blob>(`${BASE_URL()}/by-model/${modelId}/export`, {
    params: { format },
    responseType: 'blob',
  });
  return response.data;
};
