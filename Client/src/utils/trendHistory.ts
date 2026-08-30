import { reactive } from 'vue';

/** 每组件趋势缓冲区最大点数（约对应运行态 ~2 分钟 @1s 采样） */
const MAX_POINTS = 120;

/** 组件 id → 数值滚动窗口（供 trend-chart 渲染真实曲线，杜绝伪造正弦波） */
export const trendHistory: Record<string, number[]> = reactive({});

/** 值变化时推入缓冲区（由运行态 componentValues 消费方调用） */
export const pushTrendPoint = (componentId: string, value: number | boolean) => {
  const num = typeof value === 'number' ? value : value ? 1 : 0;
  const buf = trendHistory[componentId] ?? (trendHistory[componentId] = []);
  const last = buf[buf.length - 1];
  // 值未变化不推点（趋势曲线静止），避免同值刷屏
  if (last === num && buf.length > 0) return;
  buf.push(num);
  if (buf.length > MAX_POINTS) buf.shift();
};

/** 组件删除/页面切换时清理 */
export const clearTrendHistory = (componentId: string) => {
  delete trendHistory[componentId];
};