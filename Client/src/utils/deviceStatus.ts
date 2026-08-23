import { Device, DeviceType } from '../types';

/**
 * 将后端运行时状态枚举名映射为前端统一的 status 数值。
 * 后端 DeviceStatus（ScadaServer.Domain.Enums.DeviceStatus）：
 *   Online=1, Offline=0, Fault=2, ConfigUpdating=3, Connecting=4
 * 映射后复用现有所有 `dev.status === 1` / `=== 'online'` 的状态点亮与写入按钮判定逻辑。
 */
export const mapRuntimeStatusToStatus = (runtimeStatus?: string): number => {
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
 */
export const normalizeDevices = (raw: Device[]): Device[] =>
  (raw ?? []).map((d) => ({
    ...d,
    // 后端 DeviceDto 返回 ModelType(从模型推导的协议)；旧字段 Type 已删除。
    // 用它兜底填充派生只读的 type，确保所有 device.type 引用有值。
    type: (d.type ?? d.modelType ?? 'Virtual') as DeviceType,
    runtimeStatus: d.runtimeStatus,
    status: mapRuntimeStatusToStatus(d.runtimeStatus)
  }));
