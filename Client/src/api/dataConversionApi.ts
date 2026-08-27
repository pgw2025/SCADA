import { http, extractApiError } from './http';
import { DataConversion } from '../types';
import { systemConfig, addLog } from '../store/index';

const BASE_URL = () => `${systemConfig.value.backendApiUrl}/api/DataConversion`;

// GET /api/DataConversion - 全量规则列表
export const fetchDataConversions = async (): Promise<DataConversion[]> => {
  try {
    const res = await http.get<DataConversion[]>(BASE_URL());
    return res.data;
  } catch (e: any) {
    addLog('数据转换', `获取转换规则列表失败: ${extractApiError(e)}`, 'warning');
    throw e;
  }
};

// POST /api/DataConversion - 新增（后端已回填自增 Id，返回完整实体）
export const createDataConversion = async (dto: Omit<DataConversion, 'id'>): Promise<DataConversion> => {
  try {
    const res = await http.post<DataConversion>(BASE_URL(), dto);
    return res.data;
  } catch (e: any) {
    addLog('数据转换', `创建转换规则失败: ${extractApiError(e)}`, 'warning');
    throw e;
  }
};

// PUT /api/DataConversion - 全量更新
export const updateDataConversion = async (dto: DataConversion): Promise<void> => {
  try {
    await http.put(BASE_URL(), dto);
  } catch (e: any) {
    addLog('数据转换', `更新转换规则失败 [${dto.name}]: ${extractApiError(e)}`, 'warning');
    throw e;
  }
};

// DELETE /api/DataConversion/{id}
export const deleteDataConversion = async (id: number, label?: string): Promise<void> => {
  try {
    await http.delete(`${BASE_URL()}/${id}`);
  } catch (e: any) {
    addLog('数据转换', `删除转换规则失败 [${label ?? id}]: ${extractApiError(e)}`, 'warning');
    throw e;
  }
};