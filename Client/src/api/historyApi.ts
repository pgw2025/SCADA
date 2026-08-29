import { HistoricalRecord } from '../types';
import { addLog, systemConfig } from '../store/index';
import { http } from './http';

/**
 * 后端 HistoryRecordDto → 前端 HistoricalRecord 归一化。
 * id 为空/0 时生成客户端临时 ID，避免表格 key 冲突。
 */
const mapRecord = (item: any, deviceKey: string, variableKey: string): HistoricalRecord => ({
  id: item.id !== undefined && item.id !== null && item.id !== 0
    ? String(item.id)
    : `hist-net-${Date.now()}-${Math.random().toString().slice(-4)}`,
  deviceKey: item.deviceKey || deviceKey,
  variableKey: item.variableKey || variableKey,
  variableName: item.variableName || variableKey,
  value: Number(item.value),
  timestamp: item.timestamp,
  quality: item.quality || undefined,
});

/** 单变量历史查询参数 */
export interface HistoryQueryParams {
  deviceKey: string;
  variableKey: string;
  limit?: number;
  start?: string;
  end?: string;
  aggregateWindowMs?: number;
  aggregateFn?: string;
}

/** 批量历史查询：单个待查变量（DeviceKey + VariableKey 唯一标识） */
export interface HistoryBatchVariable {
  deviceKey: string;
  variableKey: string;
}

export interface HistoryBatchRequest {
  variables: HistoryBatchVariable[];
  limit?: number;
  start?: string;
  end?: string;
  aggregateWindowMs?: number;
  aggregateFn?: string;
}

/** 批量历史查询结果项：单个变量的完整历史序列 */
export interface HistoryBatchItem {
  deviceKey: string;
  variableKey: string;
  variableName: string;
  records: HistoricalRecord[];
}

/**
 * 查询指定设备下某变量的历史记录（参数直通后端，含时间范围/聚合降采样/聚合函数）。
 * 真实模式返回后端时序；模拟模式返回空数组（由页面保留 demo 空态）。
 */
export const fetchHistoryFromBackend = async (params: HistoryQueryParams): Promise<HistoricalRecord[]> => {
  if (systemConfig.value.isSimulationActive) return [];

  try {
    addLog('历史查询', `正在向后端调取时间曲线. 变量: ${params.variableKey}, 长度: ${params.limit ?? 2000}...`, 'info');
    // deviceKey 区分不同设备的同名变量：带设备上下文时后端按 device_key 精确过滤。
    const deviceQuery = params.deviceKey ? `&deviceKey=${encodeURIComponent(params.deviceKey)}` : '';
    let url = `${systemConfig.value.backendApiUrl}/api/scada/history?variableKey=${encodeURIComponent(params.variableKey)}${deviceQuery}&limit=${params.limit ?? 2000}`;
    if (params.start) url += `&start=${encodeURIComponent(params.start)}`;
    if (params.end) url += `&end=${encodeURIComponent(params.end)}`;
    if (params.aggregateWindowMs) url += `&aggregateWindowMs=${params.aggregateWindowMs}`;
    if (params.aggregateFn) url += `&aggregateFn=${encodeURIComponent(params.aggregateFn)}`;

    const res = await http.get(url);
    const data = res.data;
    if (!Array.isArray(data)) return [];

    const converted = data.map((item: any) => mapRecord(item, params.deviceKey, params.variableKey));
    addLog('历史查询', `同步后端时序库记录成功！拉取 ${converted.length} 条数据点`, 'normal');
    return converted;
  } catch (err: any) {
    addLog('历史查询', `调取历史曲线失败: ${err.message}（请确认后端历史接口已启用、服务已启动）`, 'warning');
    throw err;
  }
};

/**
 * 批量查询多个变量的历史序列（POST /api/scada/history/batch，上限 6 个变量）。
 * 各变量独立返回序列，互不混入（后端按 DeviceKey+VariableKey 隔离）。
 */
export const fetchHistoryBatch = async (request: HistoryBatchRequest): Promise<HistoryBatchItem[]> => {
  if (systemConfig.value.isSimulationActive) return [];

  try {
    addLog('历史查询', `正在批量调取 ${request.variables.length} 个变量的历史曲线...`, 'info');
    const res = await http.post<{ items: HistoryBatchItem[] }>(
      `${systemConfig.value.backendApiUrl}/api/scada/history/batch`,
      {
        variables: request.variables,
        limit: request.limit ?? 2000,
        start: request.start,
        end: request.end,
        aggregateWindowMs: request.aggregateWindowMs,
        aggregateFn: request.aggregateFn,
      }
    );

    const items = res.data?.items ?? [];
    items.forEach(it => {
      it.records = (it.records ?? []).map((r: any) => mapRecord(r, it.deviceKey, it.variableKey));
    });
    addLog('历史查询', `批量调取成功，共 ${items.length} 个序列`, 'normal');
    return items;
  } catch (err: any) {
    addLog('历史查询', `批量调取历史曲线失败: ${err.message}`, 'warning');
    throw err;
  }
};

/** CSV 导出参数（与批量查询一致；后端导出行数上限 50000） */
export interface HistoryExportParams {
  variables: HistoryBatchVariable[];
  start?: string;
  end?: string;
  aggregateWindowMs?: number;
  aggregateFn?: string;
}

/**
 * 请求后端流式导出 CSV（真实模式；突破前端内存限制，支持万级行）。
 * 返回 Blob，由调用方触发下载。模拟模式抛出错误，由页面回退本地导出。
 */
export const exportHistoryCsv = async (params: HistoryExportParams): Promise<Blob> => {
  if (systemConfig.value.isSimulationActive) {
    throw new Error('模拟模式不支持后端导出');
  }

  const vars = params.variables.map(v => `${v.deviceKey}:${v.variableKey}`).join(',');
  let url = `${systemConfig.value.backendApiUrl}/api/scada/history/export?vars=${encodeURIComponent(vars)}&limit=50000`;
  if (params.start) url += `&start=${encodeURIComponent(params.start)}`;
  if (params.end) url += `&end=${encodeURIComponent(params.end)}`;
  if (params.aggregateWindowMs) url += `&aggregateWindowMs=${params.aggregateWindowMs}`;
  if (params.aggregateFn) url += `&aggregateFn=${encodeURIComponent(params.aggregateFn)}`;

  try {
    addLog('历史查询', '正在导出历史数据 CSV...', 'info');
    const res = await http.get(url, { responseType: 'blob' });
    addLog('历史查询', '历史数据 CSV 导出完成', 'normal');
    return res.data as Blob;
  } catch (err: any) {
    addLog('历史查询', `导出历史数据失败: ${err.message}`, 'warning');
    throw err;
  }
};
