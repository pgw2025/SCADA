/**
 * 示例 12 · 指数平滑滤波（跨执行状态持久化）
 *
 * 场景：对波动剧烈的模拟量做一阶低通滤波（EMA），把平滑后的值写回另一个变量。
 *       这是「脚本无状态」最典型的应对案例——上一次的滤波结果必须存在变量里。
 *
 *   EMA(n) = α * 本次采样 + (1 - α) * EMA(n-1)
 *
 * 触发类型：Periodic，执行间隔 = 5 秒
 * 读授权：AI01
 * 写授权：AI01.LevelSmooth;AI01.EmaValid
 *
 * 假设变量：
 *   AI01.Level        原始液位（只读）
 *   AI01.LevelSmooth  平滑后液位（可读写，保存上一次 EMA 结果）
 *   AI01.EmaValid     滤波是否已初始化（布尔，可写）
 */

var DEVICE = "AI01";
var ALPHA = 0.2;           // 平滑系数，越小越平滑（0.1~0.3 常用）
var JUMP_LIMIT = 20;       // 单次跳变上限：超过则认为是干扰，不纳入滤波

function num(dev, key, fb) {
  if (getQuality(dev, key) !== "Good") return fb;
  var v = read(dev, key);
  return (typeof v === "number" && isFinite(v)) ? v : fb;
}

function run() {
  var raw = num(DEVICE, "Level", null);
  if (raw === null) {
    log("原始液位不可用，质量 =", getQuality(DEVICE, "Level"));
    return;
  }

  var prev = num(DEVICE, "LevelSmooth", null);
  var valid = read(DEVICE, "EmaValid") === true;

  // ---------- ① 首次执行：直接用原始值初始化 ----------
  if (!valid || prev === null) {
    log("首次执行，用原始值初始化 EMA =", raw);
    write(DEVICE, "LevelSmooth", raw);
    write(DEVICE, "EmaValid", true);
    return;
  }

  // ---------- ② 抗野值：跳变过大时丢弃本次采样 ----------
  if (Math.abs(raw - prev) > JUMP_LIMIT) {
    log("⚠ 检测到跳变 " + prev.toFixed(2) + " → " + raw.toFixed(2)
        + "（>" + JUMP_LIMIT + "），本次采样丢弃，保持 " + prev.toFixed(2));
    return;
  }

  // ---------- ③ EMA 计算 ----------
  var smooth = ALPHA * raw + (1 - ALPHA) * prev;
  smooth = Math.round(smooth * 100) / 100;          // 两位小数

  log("原始 " + raw.toFixed(2) + " → 平滑 " + smooth.toFixed(2)
      + "（α=" + ALPHA + "，上次 " + prev.toFixed(2) + "）");

  var ok = write(DEVICE, "LevelSmooth", smooth);
  if (!ok) log("✘ 平滑值写入失败");

  // ---------- ④ 变化率（用于趋势判断）----------
  var delta = smooth - prev;
  log("变化率 = " + delta.toFixed(3)
      + (delta > 0.5 ? " ↗上升" : delta < -0.5 ? " ↘下降" : " →平稳"));
}
