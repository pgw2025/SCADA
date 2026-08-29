import { http } from './http';
import { systemConfig } from '../store/configStore';
import { ScheduledTask, ScheduledTaskRunResult } from '../types';

const base = () => `${systemConfig.value.backendApiUrl}/api/ScheduledTask`;

// ===== 定时任务 /api/ScheduledTask =====

/** 获取全部定时任务（含执行状态/下次触发时间，供列表轮询）。 */
export const fetchScheduledTasks = () =>
  http.get<ScheduledTask[]>(`${base()}`);

export const fetchScheduledTask = (id: number) =>
  http.get<ScheduledTask>(`${base()}/${id}`);

export const createScheduledTask = (dto: ScheduledTask) =>
  http.post(`${base()}`, dto);

export const updateScheduledTask = (dto: ScheduledTask) =>
  http.put(`${base()}`, dto);

export const deleteScheduledTask = (id: number) =>
  http.delete(`${base()}/${id}`);

/** 手动强制执行一次任务（绕过 Cron 计划，服务端防重入）。返回本次执行结果。 */
export const executeScheduledTask = (id: number) =>
  http.post<ScheduledTaskRunResult>(`${base()}/${id}/execute`);
