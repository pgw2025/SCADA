import { http } from './http';
import { MqttServer, MqttServerStatus } from '../types';
import { systemConfig } from '../store/configStore';

const base = () => `${systemConfig.value.backendApiUrl}/api/MqttServer`;

// ===== 服务器 CRUD /api/MqttServer =====
export const fetchMqttServers = () =>
  http.get<MqttServer[]>(base());

export const fetchMqttServerById = (id: number) =>
  http.get<MqttServer>(`${base()}/${id}`);

export const createMqttServer = (dto: Partial<MqttServer>) =>
  http.post<MqttServer>(base(), dto);

export const updateMqttServer = (dto: MqttServer) =>
  http.put(base(), dto);

export const deleteMqttServer = (id: number) =>
  http.delete(`${base()}/${id}`);

// 启用/停用（停用即断开连接且不再发布）
export const setMqttServerEnabled = (id: number, enabled: boolean) =>
  http.put<MqttServer>(`${base()}/${id}/enabled`, null, { params: { enabled } });

// ===== 连接状态与测试 =====
export const fetchMqttServerStatuses = () =>
  http.get<MqttServerStatus[]>(`${base()}/statuses`);

export interface MqttTestConnectionPayload {
  brokerUrl: string;
  port: number;
  clientId?: string;
  username?: string;
  password?: string;
}

export const testMqttServerConnection = (dto: MqttTestConnectionPayload) =>
  http.post<{ success: boolean; errorMessage: string }>(`${base()}/test`, dto);