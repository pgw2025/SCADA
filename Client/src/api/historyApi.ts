import { HistoricalRecord } from '../types';
import { addLog, systemConfig } from '../store/index';
import { historicalRecords } from '../store/historyStore';
import { http } from './http';

export const fetchHistoryFromBackend = async (variableKey: string, limit: number = 80) => {
    if (systemConfig.value.isSimulationActive) return;

    try {
        addLog('历史查询', `正在向后端调取时间曲线. 变量: ${variableKey}, 长度: ${limit}...`, 'info');
        // 统一走 http 实例（自动附加 JWT），stage-3 认证收紧后原生 fetch 会因缺 Token 返回 401
        const res = await http.get(
            `${systemConfig.value.backendApiUrl}/api/scada/history?variableKey=${variableKey}&limit=${limit}`
        );
        const data = res.data;
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
        // 失败时清空该变量历史区，避免残留上次数据被误认为当前查询结果；
        // 同时向操作日志抛出明确错误，不再静默吞掉（区分“后端未实现/未连通”与“查询无数据”）。
        historicalRecords.value = historicalRecords.value.filter(r => r.variableKey !== variableKey);
        addLog('历史查询', `调取历史曲线失败: ${err.message}（请确认后端历史接口已启用、服务已启动）`, 'warning');
    }
};
