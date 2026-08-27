import { http } from './http';
import { systemConfig } from '../store/index';
import { AlarmRecordQuery, AlarmRule } from '../types';

const base = () => `${systemConfig.value.backendApiUrl}/api`;

// ===== 报警规则 /api/AlarmRule =====
export const fetchAlarmRules = () =>
  http.get(`${base()}/AlarmRule`);

export const createAlarmRule = (dto: AlarmRule) =>
  http.post(`${base()}/AlarmRule`, dto);

export const updateAlarmRule = (id: number, dto: AlarmRule) =>
  http.put(`${base()}/AlarmRule/${id}`, dto);

export const deleteAlarmRule = (id: number) =>
  http.delete(`${base()}/AlarmRule/${id}`);

export const toggleAlarmRule = (id: number, enabled: boolean) =>
  http.put(`${base()}/AlarmRule/${id}/toggle`, enabled);

// ===== 报警记录 /api/AlarmRecord =====
export const fetchAlarmRecords = (query: AlarmRecordQuery) =>
  http.get(`${base()}/AlarmRecord`, { params: query });

export const fetchActiveAlarms = () =>
  http.get(`${base()}/AlarmRecord/active`);

export const ackAlarmRecord = (id: number) =>
  http.put(`${base()}/AlarmRecord/${id}/ack`);