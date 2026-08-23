import { HistoricalRecord } from '../types';
import { addLog, systemConfig } from '../store/index';
import { historicalRecords } from '../store/historyStore';

export const fetchHistoryFromBackend = async (variableKey: string, limit: number = 80) => {
    if (systemConfig.value.isSimulationActive) return;

    try {
        addLog('历史查询', `正在向后端调取时间曲线. 变量: ${variableKey}, 长度: ${limit}...`, 'info');
        const res = await fetch(`${systemConfig.value.backendApiUrl}/api/scada/history?variableKey=${variableKey}&limit=${limit}`);
        if (!res.ok) {
            throw new Error(`HTTP status code ${res.status}`);
        }
        const data = await res.json();
        if (Array.isArray(data)) {
            const otherRecords = historicalRecords.value.filter(r => r.variableKey !== variableKey);

            const converted: HistoricalRecord[] = data.map((item: any) => ({
                id: item.id || `hist-net-${Date.now()}-${Math.random().toString().slice(-4)}`,
                variableKey: item.variableKey || variableKey,
                variableName: item.variableName || variableKey,
                value: Number(item.value),
                timestamp: item.timestamp
            }));

            historicalRecords.value = [...converted, ...otherRecords];
            addLog('历史查询', `同步后端时序库记录成功！拉取 ${converted.length} 条数据点`, 'normal');
        }
    } catch (err: any) {
        addLog('历史查询', `调取时序时钟出线硬阻塞: ${err.message}`, 'warning');
    }
};
