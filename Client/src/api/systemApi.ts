import { http } from './http';
import { systemConfig } from '../store/index';

// GET /api/System/status - 获取实时系统资源状态
export const fetchSystemStatus = () => 
    http.get(`${systemConfig.value.backendApiUrl}/api/System/status`);
