import { reactive } from 'vue';

/** 单条趋势采样点：t=采集时间戳(ms)，v=数值 */
export interface TrendSample { t: number; v: number; }

/** 每序列趋势缓冲区最大点数（约对应运行态 ~2 分钟 @1s 采样） */
const MAX_POINTS = 120;

/**
 * 趋势缓冲：组件 id → (序列 id → 采样点滚动窗口)
 * 每个 trend-chart 可含多条序列（trendSeries），各自独立缓冲、独立 buffer key。
 * 存 {t,v} 以支持 X 轴相对时间刻度。
 */
export const trendHistory: Record<string, Record<string, TrendSample[]>> = reactive({});

/** 取某组件下全部序列缓冲（无则返回空对象），供 HMIWidget 渲染多序列 */
export const getSeriesMap = (componentId: string): Record<string, TrendSample[]> =>
  trendHistory[componentId] ?? {};

/** 值变化时推入某序列缓冲区（由运行态数据源调用；timestamp 缺省取当前时间） */
export const pushTrendPoint = (
  componentId: string,
  seriesId: string,
  value: number | boolean,
  timestamp?: number,
) => {
  const num = typeof value === 'number' ? value : value ? 1 : 0;
  const comp = trendHistory[componentId] ?? (trendHistory[componentId] = {});
  const buf = comp[seriesId] ?? (comp[seriesId] = []);
  const last = buf.length ? buf[buf.length - 1].v : undefined;
  // 值未变化不推点（趋势曲线静止），避免同值刷屏
  if (last === num && buf.length > 0) return;
  buf.push({ t: timestamp ?? Date.now(), v: num });
  if (buf.length > MAX_POINTS) buf.shift();
};

/** 组件删除/页面切换时清理该组件下所有序列缓冲 */
export const clearTrendHistory = (componentId: string) => {
  delete trendHistory[componentId];
};
