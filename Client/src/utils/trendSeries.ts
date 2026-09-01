import type { HMIComponent, HmiTrendSeries } from '../types';

/**
 * 解析趋势图的有效序列列表：
 * 1) 优先使用组件 props.trendSeries（多变量序列）；
 * 2) 否则由旧式单绑定（bindDeviceId + bindVariableKey）合成单条序列（向后兼容）；
 * 3) 无任何绑定时返回空数组。
 * 运行态数据源、HMIWidget 渲染、InspectorPanel 编辑器共用此函数，保证解析口径一致。
 */
export const getEffectiveTrendSeries = (component: HMIComponent): HmiTrendSeries[] => {
  const list = component.props?.trendSeries;
  if (Array.isArray(list) && list.length > 0) return list as HmiTrendSeries[];

  if (component.bindDeviceId != null && component.bindVariableKey) {
    return [{
      id: 'legacy',
      deviceId: component.bindDeviceId,
      variableKey: component.bindVariableKey,
      label: component.bindVariableKey,
      color: '#10b981',
      lineWidth: 2,
    }];
  }
  return [];
};

/** 从 devices store 解析某序列当前数值（与 dashboardResolvedItems 同范式） */
export const resolveSeriesValue = (
  devices: ReadonlyArray<{ id: number | string; variables?: Record<string, any> }>,
  deviceId: number | null | undefined,
  componentBindDeviceId: number | null | undefined,
  variableKey?: string,
): number => {
  if (!variableKey) return 0;
  const devId = deviceId != null ? deviceId : componentBindDeviceId;
  const dev = devId != null
    ? devices.find((d) => String(d.id) === String(devId))
    : (devices[0] as any);
  if (!dev) return 0;
  const raw = dev.variables?.[variableKey];
  if (typeof raw === 'number') return raw;
  if (typeof raw === 'boolean') return raw ? 1 : 0;
  return 0;
};
