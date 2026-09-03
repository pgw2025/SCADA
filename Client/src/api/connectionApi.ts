import { http } from './http';
import { systemConfig, addLog } from '../store/index';
import { DeviceConnection, DeviceConnectionRequest } from '../types';

const BASE_URL = () => `${systemConfig.value.backendApiUrl}/api/device-connections`;

/**
 * GET /api/device-connections - 连接列表（阶段 3：连接/控制器管理 API）。
 * controllerId 非空时仅返回该控制器下的连接（高级模式"控制器→连接"级联下拉数据源）。
 */
export const fetchDeviceConnections = async (controllerId?: number): Promise<DeviceConnection[]> => {
  if (systemConfig.value.isSimulationActive) return [];
  try {
    const response = await http.get<DeviceConnection[]>(BASE_URL(), {
      params: controllerId ? { controllerId } : undefined
    });
    return response.data || [];
  } catch (error: any) {
    addLog('连接管理', `获取连接列表失败: ${error.message}`, 'warning');
    return [];
  }
};

/** GET /api/device-connections/{id} - 连接详情。 */
export const fetchDeviceConnectionById = async (id: number): Promise<DeviceConnection | null> => {
  if (systemConfig.value.isSimulationActive) return null;
  try {
    const response = await http.get<DeviceConnection>(`${BASE_URL()}/${id}`);
    return response.data || null;
  } catch (error: any) {
    addLog('连接管理', `获取连接详情失败: ${error.message}`, 'warning');
    return null;
  }
};

/** POST /api/device-connections - 新建独立连接（高级模式"新建独立连接"）。 */
export const createDeviceConnection = (dto: DeviceConnectionRequest) =>
  http.post<DeviceConnection>(BASE_URL(), dto);

/** PUT /api/device-connections/{id} - 更新连接（被设备引用时后端拒绝，提示走设备管理页）。 */
export const updateDeviceConnection = (id: number, dto: DeviceConnectionRequest) =>
  http.put<DeviceConnection>(`${BASE_URL()}/${id}`, dto);

/** DELETE /api/device-connections/{id} - 删除连接（被设备引用时后端拒绝）。 */
export const deleteDeviceConnection = (id: number) =>
  http.delete(`${BASE_URL()}/${id}`);
