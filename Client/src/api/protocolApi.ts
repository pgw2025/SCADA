import { http } from './http';
import { Protocol } from '../types';
import { systemConfig, addLog } from '../store/index';

const BASE_URL = () => `${systemConfig.value.backendApiUrl}/api/Protocol`;

/**
 * GET /api/Protocol - 获取全部通信协议。
 * 协议是"数据模型如何通信"的真相源，创建数据模型时的协议下拉选择数据源来自此接口。
 */
export const fetchProtocols = async (): Promise<Protocol[]> => {
  if (systemConfig.value.isSimulationActive) return [];
  try {
    const response = await http.get<Protocol[]>(BASE_URL());
    return response.data || [];
  } catch (error: any) {
    addLog('协议管理', `获取协议列表失败: ${error.message}`, 'warning');
    return [];
  }
};