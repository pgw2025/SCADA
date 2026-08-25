import { Device, DeviceType } from '../types';

/**
 * 将后端运行时状态枚举名映射为前端统一的 status 数值。
 * 后端 DeviceStatus（ScadaServer.Domain.Enums.DeviceStatus）：
 *   Online=1, Offline=0, Fault=2, ConfigUpdating=3, Connecting=4
 * 映射后复用现有所有 `dev.status === 1` / `=== 'online'` 的状态点亮与写入按钮判定逻辑。
 *
 * 兼容说明：REST 与 SignalR 推送均已改为字符串枚举名（Online 等）；
 * 这里同时保留数字分支（0~4）兜底，避免后端未同步发版时的过渡窗口期状态映射错误。
 */
export const mapRuntimeStatusToStatus = (runtimeStatus?: string | number): number => {
  // 数字分支：旧版本后端 SignalR/枚举默认序列化为数字时兜底
  if (typeof runtimeStatus === 'number') {
    switch (runtimeStatus) {
      case 1: return 1; // Online
      case 2: return 2; // Fault
      case 4: return 4; // Connecting
      case 3: return 3; // ConfigUpdating
      default: return 0; // Offline
    }
  }

  switch (runtimeStatus) {
    case 'Online': return 1;
    case 'Fault': return 2;
    case 'Connecting': return 4;
    case 'ConfigUpdating': return 3;
    case 'Offline':
    case undefined:
    default: return 0;
  }
};

/**
 * 将后端返回的原始设备数组标准化：补全 runtimeStatus 并把 status 映射为统一数值。
 * 同时把未携带 status 的设备兜底为离线，避免界面出现 undefined 状态。
 *
 * 关键归一化：后端 DeviceDto.Variables 是对象数组（DeviceVariableDto[]，仅含定义/配置，不含实时值），
 * 而前端消费模型是 `Record<变量Key, 实时值>`（如 `dev.variables[key]`）。
 * 这里把数组转成键值表并预置 null 占位，使 SignalR 实时值推送到达时即有落点
 * （`dev.variables[variableKey] !== undefined` 判断可命中），实时值/触发器/换算等消费方零改动。
 */
export const normalizeDevices = (raw: Device[]): Device[] =>
  (raw ?? []).map((d) => {
    const variableMap: Record<string, any> = {};
    if (Array.isArray(d.variables)) {
      // 后端变量数组（每个元素含 key）：预置 null 占位，等待实时值填充
      (d.variables as any[]).forEach((v: any) => {
        if (v && v.key) variableMap[v.key] = null;
      });
    } else if (d.variables && typeof d.variables === 'object') {
      // 已是键值表（如本地模拟/兼容数据）：原样保留
      Object.assign(variableMap, d.variables);
    }

    return {
      ...d,
      variables: variableMap,
      // 后端 DeviceDto 已不再返回 Type / ModelType，协议真相源在 Protocol 实体。
      // type 为派生只读，由调用方在同步数据中携带或在此兜底为 Virtual，
      // 其余派生逻辑经由 protocolKey / protocolKeyToDeviceType 完成。
      type: (d.type ?? 'Virtual') as DeviceType,
      runtimeStatus: d.runtimeStatus,
      status: mapRuntimeStatusToStatus(d.runtimeStatus)
    };
  });
