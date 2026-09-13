import { reactive } from 'vue';

/** 单条趋势采样点：t=采集时间戳(ms)，v=数值 */
export interface TrendSample { t: number; v: number; }

/** 每序列趋势缓冲区最大点数（约对应运行态 ~2 分钟 @1s 采样） */
const MAX_POINTS = 120;

/** 值未变化时的最小补点间隔（ms）：恒定信号也按此间隔推点，推进 X 轴相对时间 */
const MIN_SAMPLE_GAP_MS = 1000;

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
  const t = timestamp ?? Date.now();
  // 值未变化：仅当距上一点超过最小采样间隔才补点（推进相对时间轴），否则跳过避免同值刷屏。
  // 修复：此前「值不变一律不推」导致恒定信号（恒温/开关稳态）曲线停滞、时间冻结。
  if (last === num && buf.length > 0) {
    const lastT = buf[buf.length - 1].t;
    if (t - lastT < MIN_SAMPLE_GAP_MS) return;
  }
  buf.push({ t, v: num });
  if (buf.length > MAX_POINTS) buf.shift();
};

/** 组件删除/页面切换时清理该组件下所有序列缓冲 */
export const clearTrendHistory = (componentId: string) => {
  delete trendHistory[componentId];
};

/**
 * 历史回填：将后端历史序列按时间升序预填充到某序列缓冲头部（阶段5 P2-8）。
 * 回填点早于当前实时点，直接整体替换缓冲（回填调用发生在挂载期，缓冲尚未有实时点）。
 * value 为 null/NaN 的点跳过（趋势图无法绘制）。
 */
export const prependTrendHistory = (
  componentId: string,
  seriesId: string,
  samples: TrendSample[],
) => {
  const valid = samples.filter(s => Number.isFinite(s.v));
  if (valid.length === 0) return;
  const comp = trendHistory[componentId] ?? (trendHistory[componentId] = {});
  const existing = comp[seriesId] ?? [];
  // 合并历史点 + 已有实时点，按时间升序去重（同时间戳保留后写入者 = 实时点），
  // 避免回填异步晚到时「整体替换」顶掉已推入的实时点（竞态）。
  const merged = new Map<number, TrendSample>();
  valid.forEach(s => merged.set(s.t, s));     // 先放历史点
  existing.forEach(s => merged.set(s.t, s));  // 后放实时点（覆盖同时间戳）
  const sorted = [...merged.values()].sort((a, b) => a.t - b.t);
  comp[seriesId] = sorted.slice(-MAX_POINTS);
};

