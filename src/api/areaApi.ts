import axios from 'axios';
import { Area } from '../types';
import { systemConfig } from '../store/configStore';

const getBaseUrl = () => systemConfig.value.backendApiUrl;

export const getAreas = () => axios.get<Area[]>(`${getBaseUrl()}/api/Area`);
export const getAreaById = (id: number) => axios.get<Area>(`${getBaseUrl()}/api/Area/${id}`);
export const createArea = (data: { Name: string; Description?: string }) => axios.post<Area>(`${getBaseUrl()}/api/Area`, data);
export const updateArea = (data: { Id: number; Name: string; Description?: string }) => axios.put<Area>(`${getBaseUrl()}/api/Area`, data);
export const deleteArea = (id: number) => axios.delete(`${getBaseUrl()}/api/Area/${id}`);
