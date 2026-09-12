/**
 * 示例 04 · 比例调节与输出限幅（简易 P 控制）
 *
 * 场景：按温度偏差比例调节阀门开度，输出必须限幅，且带死区防抖动。
 * 触发类型：Periodic，执行间隔 = 10 秒
 * 读授权：REACT01
 * 写授权：REACT01.ValveOpen
 *
 * 假设变量：REACT01.Temp（数值℃）、REACT01.ValveOpen（数值 0-100，表示开度 %）
 */

var DEVICE = "REACT01";
var SETPOINT = 65;      // 目标温度
var KP = 4.0;           // 比例增益：每偏差 1℃ 改变 4% 开度
var DEADBAND = 0.8;     // 死区：偏差小于它就不动作，防阀门抖动
var OUT_MIN = 0;        // 开度下限
var OUT_MAX = 100;      // 开度上限
var STEP_MAX = 10;      // 单次最大变化幅度：防阶跃冲击

/**
 * 限幅
 */
function clamp(v, lo, hi) {
  return Math.min(hi, Math.max(lo, v));
}

function run() {
  // ① 反馈值质量门禁
  if (getQuality(DEVICE, "Temp") !== "Good") {
    log("温度质量异常 =", getQuality(DEVICE, "Temp"), "，保持当前开度");
    return;
  }

  var pv = read(DEVICE, "Temp");          // 过程值
  if (typeof pv !== "number" || !isFinite(pv)) { log("PV 非法"); return; }

  // ② 当前输出（也要判质量，读不到就别动阀门）
  var outQ = getQuality(DEVICE, "ValveOpen");
  if (outQ !== "Good") { log("阀门反馈不可用 =", outQ); return; }
  var out = read(DEVICE, "ValveOpen");
  if (typeof out !== "number" || !isFinite(out)) { log("阀门反馈非法"); return; }

  var err = SETPOINT - pv;                // 偏差 = 设定 - 实际
  log("PV=" + pv.toFixed(2) + " SP=" + SETPOINT + " err=" + err.toFixed(2));

  // ③ 死区：小偏差不动，避免阀门频繁动作磨损
  if (Math.abs(err) <= DEADBAND) {
    log("偏差在死区内（|" + err.toFixed(2) + "| ≤ " + DEADBAND + "），保持开度 " + out);
    return;
  }

  // ④ 比例运算 + 限速 + 限幅（三重保护，缺一不可）
  var target = out + KP * err;                                  // 比例输出
  var stepped = clamp(target, out - STEP_MAX, out + STEP_MAX);  // 限速
  var final = clamp(stepped, OUT_MIN, OUT_MAX);                 // 限幅
  final = Math.round(final * 10) / 10;                          // 保留一位小数

  // ⑤ 变化太小就不写，减少不必要的下发
  if (Math.abs(final - out) < 0.5) {
    log("开度变化 " + (final - out).toFixed(2) + " 过小，跳过写入");
    return;
  }

  log("目标开度 = " + target.toFixed(1) + " → 限速 " + stepped.toFixed(1)
      + " → 限幅 " + final.toFixed(1));

  var ok = write(DEVICE, "ValveOpen", final);
  log(ok ? "✔ 阀门开度已更新为 " + final : "✘ 阀门写入失败（检查写授权/只读/上下限）");
}
