import { http } from './http';
import { MqttVariableConfig, MqttVariableConfigCreate, MqttVariableConfigUpdate } from '../types';
import { systemConfig } from '../store/configStore';

const base = () => `${systemConfig.value.backendApiUrl}/api/MqttVariableConfig`;

// GET /api/MqttVariableConfig/{serverId}/variables - 查询某服务器下已关联的所有变量
export const fetchMqttVariableConfigs = (serverId: number) =>
  http.get<MqttVariableConfig[]>(`${base()}/${serverId}/variables`);

// POST /api/MqttVariableConfig/{serverId}/variables - 为服务器关联变量
export const addMqttVariableConfig = (serverId: number, dto: MqttVariableConfigCreate) =>
  http.post<MqttVariableConfig>(`${base()}/${serverId}/variables`, dto);

// PUT /api/MqttVariableConfig/variables/{configId} - 更新映射（别名/自定义主题/启用）
export const updateMqttVariableConfig = (configId: number, dto: MqttVariableConfigUpdate) =>
  http.put<MqttVariableConfig>(`${base()}/variables/${configId}`, dto);

// DELETE /api/MqttVariableConfig/variables/{configId} - 删除映射
export const deleteMqttVariableConfig = (configId: number) =>
  http.delete(`${base()}/variables/${configId}`);