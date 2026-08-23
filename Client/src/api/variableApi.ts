import axios from 'axios';
import { ModelVariable } from '../types';
import { systemConfig, addLog } from '../store/index';

const BASE_URL = () => `${systemConfig.value.backendApiUrl}/api/ModelVariable`;

// GET /api/ModelVariable - 获取所有变量
export const fetchVariables = async (): Promise<ModelVariable[]> => {
  try {
    const response = await axios.get<ModelVariable[]>(BASE_URL());
    return response.data;
  } catch (error: any) {
    addLog('变量管理', `获取变量列表失败: ${error.message}`, 'warning');
    throw error;
  }
};

// GET /api/ModelVariable/{id} - 获取单个变量
export const fetchVariableById = async (id: number): Promise<ModelVariable> => {
  try {
    const response = await axios.get<ModelVariable>(`${BASE_URL()}/${id}`);
    return response.data;
  } catch (error: any) {
    addLog('变量管理', `获取变量详情失败 [${id}]: ${error.message}`, 'warning');
    throw error;
  }
};

// GET /api/ModelVariable/by-model/{modelId} - 获取指定模型的变量
export const fetchVariablesByModelId = async (modelId: number): Promise<ModelVariable[]> => {
  try {
    const response = await axios.get<ModelVariable[]>(`${BASE_URL()}/by-model/${modelId}`);
    return response.data;
  } catch (error: any) {
    addLog('变量管理', `获取模型变量失败 [模型ID:${modelId}]: ${error.message}`, 'warning');
    throw error;
  }
};

// POST /api/ModelVariable - 创建新变量
export const createVariable = async (variable: Omit<ModelVariable, 'id'>): Promise<ModelVariable> => {
  try {
    const response = await axios.post<ModelVariable>(BASE_URL(), variable);
    addLog('变量管理', `已创建变量: ${variable.key}`, 'normal');
    return response.data;
  } catch (error: any) {
    addLog('变量管理', `创建变量失败 [${variable.key}]: ${error.message}`, 'warning');
    throw error;
  }
};

// PUT /api/ModelVariable - 更新变量
export const updateVariable = async (variable: ModelVariable): Promise<ModelVariable> => {
  try {
    const response = await axios.put<ModelVariable>(`${BASE_URL()}/${variable.id}`, variable);
    addLog('变量管理', `已更新变量: ${variable.key}`, 'normal');
    return response.data;
  } catch (error: any) {
    addLog('变量管理', `更新变量失败 [${variable.id}]: ${error.message}`, 'warning');
    throw error;
  }
};

// DELETE /api/ModelVariable/{id} - 删除变量
export const deleteVariable = async (id: number): Promise<void> => {
  try {
    await axios.delete(`${BASE_URL()}/${id}`);
    addLog('变量管理', `已删除变量 [${id}]`, 'warning');
  } catch (error: any) {
    addLog('变量管理', `删除变量失败 [${id}]: ${error.message}`, 'warning');
    throw error;
  }
};
