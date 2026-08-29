import { http } from './http';
import { ExposedDataInterface } from '../types';
import { systemConfig } from '../store/configStore';

const base = () => `${systemConfig.value.backendApiUrl}/api/ExposedInterface`;

// ===== 暴露接口 CRUD /api/ExposedInterface =====
export const fetchExposedInterfaces = () =>
  http.get<ExposedDataInterface[]>(base());

export const createExposedInterface = (dto: Partial<ExposedDataInterface>) =>
  http.post<ExposedDataInterface>(base(), dto);

export const updateExposedInterface = (dto: ExposedDataInterface) =>
  http.put(base(), dto);

export const deleteExposedInterface = (id: number) =>
  http.delete(`${base()}/${id}`);

// 启用/停用（停用后 /open/* 立即 404）
export const setExposedInterfaceEnabled = (id: number, enabled: boolean) =>
  http.put(`${base()}/${id}/enabled`, null, { params: { enabled } });