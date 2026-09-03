import { http } from './http';
import { Area, AreaTreeNode } from '../types';
import { systemConfig } from '../store/configStore';

const getBaseUrl = () => systemConfig.value.backendApiUrl;

/** 区域创建/更新请求体（与后端 AreaDto 字段一致，PascalCase，由 ASP.NET Core 模型绑定）。 */
export interface AreaRequest {
  Id?: number;
  ParentId?: number | null;
  Name: string;
  Code?: string | null;
  AreaType?: number;
  Description?: string;
  Sort?: number;
  IsEnabled?: boolean;
}

export const getAreas = () => http.get<Area[]>(`${getBaseUrl()}/api/Area`);
/** 获取区域树（含各节点直接挂载设备数 DeviceCount 与子节点 Children）。 */
export const getAreaTree = () => http.get<AreaTreeNode[]>(`${getBaseUrl()}/api/Area/tree`);
/** 获取指定区域（含所有子孙区域）下直接挂载的设备 ID 列表，供"含子区域"过滤设备使用。 */
export const getDeviceIdsInSubtree = (id: number) => http.get<number[]>(`${getBaseUrl()}/api/Area/${id}/device-ids`);
export const getAreaById = (id: number) => http.get<Area>(`${getBaseUrl()}/api/Area/${id}`);
export const createArea = (data: AreaRequest) => http.post<Area>(`${getBaseUrl()}/api/Area`, data);
export const updateArea = (data: AreaRequest) => http.put<Area>(`${getBaseUrl()}/api/Area`, data);
export const deleteArea = (id: number) => http.delete(`${getBaseUrl()}/api/Area/${id}`);
