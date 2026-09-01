import { reactive } from 'vue';

/** 每序列趋势缓冲区最大点数（约对应运行态 ~2 分钟 @1s 采样） */
const MAX_POINTS = 120;

/**
 * 趋势缓冲：组件 id → (序列 id → 数值滚动窗口)
 * 每个 trend-chart 可含多条序列（trendSeries），各自独立缓冲、独立 buffer key。
 */
export const trendHistory: Record<string, Record<string, number[]>> = reactive({});

/** 取某组件下全部序列缓冲（无则返回空对象），供 HMIWidget 渲染多序列 */
export const getSeriesMap = (componentId: string): Record<string, number[]> =>
  trendHistory[componentId] ?? {};

/** 值变化时推入某序列缓冲区（由运行态数据源调用） */
export const pushTrendPoint = (componentId: string, seriesId: string, value: number | boolean) => {
  const num = typeof value === 'number' ? value : value ? 1 : 0;
  const comp = trendHistory[componentId] ?? (trendHistory[componentId] = {});
  const buf = comp[seriesId] ?? (comp[seriesId] = []);
  const last = buf[buf.length - 1];
  // 值未变化不推点（趋势曲线静止），避免同值刷屏
  if (last === num && buf.length > 0) return;
  buf.push(num);
  if (buf.length > MAX_POINTS) buf.shift();
};

/** 组件删除/页面切换时清理该组件下所有序列缓冲 */
export const clearTrendHistory = (componentId: string) => {
  delete trendHistory[componentId];
};
