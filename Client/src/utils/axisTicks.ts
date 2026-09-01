/**
 * 坐标轴刻度计算工具：niceTicks 生成「漂亮」刻度（类 R 的 pretty()），
 * toRelative 相对坐标映射，relTimeLabel 相对时间标签。供 trend-chart 渲染使用。
 */

/** 生成 count+1 个「漂亮」刻度值（含端点的 nice 范围） */
export function niceTicks(min: number, max: number, count = 4): number[] {
  if (!Number.isFinite(min) || !Number.isFinite(max) || max <= min) return [];
  const range = niceNum(max - min, false);
  const step = niceNum(range / Math.max(1, count), true);
  const niceMin = Math.floor(min / step) * step;
  const niceMax = Math.ceil(max / step) * step;
  const ticks: number[] = [];
  // 用极小值容差避免浮点末位漏掉 niceMax
  for (let v = niceMin; v <= niceMax + step * 1e-6; v += step) {
    ticks.push(Number(v.toFixed(10)));
  }
  return ticks;
}

/** 取整到 1/2/5 × 10^n（round=true 时向下取整到友好步长） */
function niceNum(range: number, round: boolean): number {
  const exp = Math.floor(Math.log10(range));
  const frac = range / Math.pow(10, exp);
  let nf: number;
  if (round) {
    if (frac < 1.5) nf = 1;
    else if (frac < 3) nf = 2;
    else if (frac < 7) nf = 5;
    else nf = 10;
  } else {
    if (frac <= 1) nf = 1;
    else if (frac <= 2) nf = 2;
    else if (frac <= 5) nf = 5;
    else nf = 10;
  }
  return nf * Math.pow(10, exp);
}

/** 相对坐标映射：把值按 [lo, hi] 归一化为 0-100（越界夹紧） */
export function toRelative(v: number, lo: number, hi: number): number {
  if (hi <= lo) return 0;
  return Math.max(0, Math.min(100, ((v - lo) / (hi - lo)) * 100));
}

/** 相对时间标签：相对 nowMs 的毫秒时间戳 → "-30s" / "-2m3s" / "now" */
export function relTimeLabel(t: number, nowMs: number): string {
  const sec = Math.round((nowMs - t) / 1000);
  if (sec <= 0) return 'now';
  if (sec < 60) return `-${sec}s`;
  const min = Math.floor(sec / 60);
  const rem = sec % 60;
  return rem ? `-${min}m${rem}s` : `-${min}m`;
}

/** 刻度数值格式化：整数去小数，否则保留 1 位 */
export function fmtTick(v: number): string {
  if (!Number.isFinite(v)) return '';
  return Number.isInteger(v) ? String(v) : v.toFixed(1);
}
