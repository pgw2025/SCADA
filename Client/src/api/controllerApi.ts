import { http } from './http';
import { systemConfig, addLog } from '../store/index';
import {
  Controller,
  ControllerOption,
  ControllerPagedResult,
  ControllerQueryParams,
  ControllerRequest
} from '../types';

const BASE_URL = () => `${systemConfig.value.backendApiUrl}/api/controllers`;

/**
 * GET /api/controllers - 分页查询控制器（阶段 2，控制器/PLC 资产台账）。
 * 支持按协议（protocolId）与关键字（keyword）过滤；keyword 匹配编码/名称/厂商/型号。
 */
export const fetchControllers = async (query: ControllerQueryParams): Promise<ControllerPagedResult> => {
  if (systemConfig.value.isSimulationActive) return { total: 0, items: [] };
  try {
    const response = await http.get<ControllerPagedResult>(BASE_URL(), { params: query });
    return response.data || { total: 0, items: [] };
  } catch (error: any) {
    addLog('控制器管理', `获取控制器列表失败: ${error.message}`, 'warning');
    return { total: 0, items: [] };
  }
};

/**
 * GET /api/controllers/options - 控制器下拉数据源（Id+Code+Name+Protocol）。
 * 后续阶段供设备连接等下拉复用。
 */
export const fetchControllerOptions = async (): Promise<ControllerOption[]> => {
  if (systemConfig.value.isSimulationActive) return [];
  try {
    const response = await http.get<ControllerOption[]>(`${BASE_URL()}/options`);
    return response.data || [];
  } catch (error: any) {
    addLog('控制器管理', `获取控制器下拉失败: ${error.message}`, 'warning');
    return [];
  }
};

/** POST /api/controllers - 新增控制器。 */
export const createController = (dto: ControllerRequest) =>
  http.post<Controller>(BASE_URL(), dto);

/** PUT /api/controllers/{id} - 更新控制器。 */
export const updateController = (id: number, dto: ControllerRequest) =>
  http.put<Controller>(`${BASE_URL()}/${id}`, dto);

/** DELETE /api/controllers/{id} - 删除控制器。 */
export const deleteController = (id: number) =>
  http.delete(`${BASE_URL()}/${id}`);
