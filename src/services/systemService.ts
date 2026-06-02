import { serverStatus } from '../store/systemStatusStore';
import { fetchSystemStatus } from '../api/systemApi';
import { addLog } from '../services/logService';

let resourceInterval: any = null;

export const startSystemResourceMonitoring = () => {
    if (resourceInterval) return;
    
    // 立即执行一次以避免等待间隔
    fetchStatus();
    
    resourceInterval = setInterval(fetchStatus, 2000);
};

export const stopSystemResourceMonitoring = () => {
    if (resourceInterval) {
        clearInterval(resourceInterval);
        resourceInterval = null;
    }
};

async function fetchStatus() {
    try {
        const { data } = await fetchSystemStatus();
        serverStatus.value = data;
    } catch (err: any) {
        // 避免在组件卸载时记录不必要的错误日志
        console.warn('获取系统状态失败:', err.message);
    }
}
