import { serverStatus } from '../store/systemStatusStore';
import { fetchSystemStatus } from '../api/systemApi';
import { addLog } from '../services/logService';

let resourceInterval: any = null;

export const startSystemResourceMonitoring = () => {
    if (resourceInterval) return;
    
    // 定时从后端获取真实数据
    resourceInterval = setInterval(async () => {
        try {
            const { data } = await fetchSystemStatus();
            serverStatus.value = data;
        } catch (err: any) {
            addLog('系统监控', `获取系统状态失败: ${err.message}`, 'warning');
        }
    }, 2000);
};
