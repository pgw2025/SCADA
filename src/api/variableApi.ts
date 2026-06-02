import axios from 'axios';
import { ModelVariable } from '../types';
import { systemConfig } from '../store/index';

const BASE_URL = () => `${systemConfig.value.backendApiUrl}/api/ModelVariable`;

export const fetchVariables = () => axios.get<ModelVariable[]>(BASE_URL());
export const fetchVariableById = (id: number) => axios.get<ModelVariable>(`${BASE_URL()}/${id}`);
export const createVariable = (variable: ModelVariable) => axios.post(BASE_URL(), variable);
export const updateVariable = (variable: ModelVariable) => axios.put(BASE_URL(), variable);
export const deleteVariable = (id: number) => axios.delete(`${BASE_URL()}/${id}`);
