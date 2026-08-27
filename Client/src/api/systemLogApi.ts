import { http } from './http';
import { systemConfig } from '../store/index';
import { SystemLogQuery } from '../types';

// GET /api/SystemLog - 分页查询系统日志（分类/级别/关键字/时间段）
export const fetchSystemLogs = (query: SystemLogQuery) =>
    http.get(`${systemConfig.value.backendApiUrl}/api/SystemLog`, { params: query });

// POST /api/SystemLog/clear - 按分类/时间段批量清理日志（仅 Admin；后端要求必须指定时间范围）
export const clearSystemLogs = (payload: { category?: string; startTime?: string | null; endTime?: string | null }) =>
    http.post(`${systemConfig.value.backendApiUrl}/api/SystemLog/clear`, payload);
