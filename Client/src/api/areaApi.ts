import { http } from './http';
import { Area } from '../types';
import { systemConfig } from '../store/configStore';

const getBaseUrl = () => systemConfig.value.backendApiUrl;

export const getAreas = () => http.get<Area[]>(`${getBaseUrl()}/api/Area`);
export const getAreaById = (id: number) => http.get<Area>(`${getBaseUrl()}/api/Area/${id}`);
export const createArea = (data: { Name: string; Description?: string }) => http.post<Area>(`${getBaseUrl()}/api/Area`, data);
export const updateArea = (data: { Id: number; Name: string; Description?: string }) => http.put<Area>(`${getBaseUrl()}/api/Area`, data);
export const deleteArea = (id: number) => http.delete(`${getBaseUrl()}/api/Area/${id}`);
