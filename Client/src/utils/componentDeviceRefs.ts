import type { HMIComponent } from '../types';

/**
 * 收集组件引用的全部设备 id（SignalR 订阅用）。
 * - bindDeviceId：组件主绑定；
 * - props 中所有以 "DeviceId" 结尾且值为合法数字的字段（opDeviceId、startDeviceId、spFreqDeviceId…
 *   vfd-motor-panel 全部 11 个变量级引用；未来组件新增 xxxDeviceId 字段自动覆盖，无需维护白名单）。
 */
export const collectComponentDeviceRefs = (c: HMIComponent | null | undefined): Set<number> => {
  const ids = new Set<number>();
  if (!c) return ids;
  if (c.bindDeviceId != null) ids.add(Number(c.bindDeviceId));
  for (const [key, val] of Object.entries(c.props ?? {})) {
    if (!/DeviceId$/i.test(key)) continue;
    const n = Number(val);
    if (val !== '' && val !== null && val !== undefined && !Number.isNaN(n)) ids.add(n);
  }
  return ids;
};