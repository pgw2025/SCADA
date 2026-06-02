import axios from 'axios';
import { Area } from '../types';
import { systemConfig } from '../store/configStore';

const getBaseUrl = () => systemConfig.value.backendApiUrl;

export const getAreas = () => axios.get<Area[]>(`${getBaseUrl()}/api/Area`);
export const createArea = (data: { Name: string, Description: string }) => axios.post(`${getBaseUrl()}/api/Area`, data);
export const updateArea = (data: { Id: number, Name: string, Description: string }) => axios.put(`${getBaseUrl()}/api/Area`, data);
export const deleteArea = (id: string) => axios.delete(`${getBaseUrl()}/api/Area/${id}`);
