/**
 * 示例 16 · 位置式 PID 控制（带积分限幅与抗饱和）
 *
 * 场景：温度/压力等需要稳态精度的回路。位置式 PID + 积分抗饱和 + 输出限幅 + 手动/自动无扰切换。
 *       依赖变量持久化积分项与上次的测量值。
 *
 *   u = Kp*e + Ki*Σe + Kd*(e - e_prev)
 *
 * 触发类型：Periodic，执行间隔 = 5 秒（务必与 SAMPLE_DT_S 一致）
 * 读授权：OVEN01
 * 写授权：OVEN01.HeatOutput;OVEN01.PidIntegral;OVEN01.PidPrevError;OVEN01.PidAuto
 *
 * 假设变量：
 *   OVEN01.Temp          炉温 ℃（只读）
 *   OVEN01.TempSP        目标温度 ℃（可读写，HMI 可设）
 *   OVEN01.HeatOutput    加热输出 0-100%（可读写）
 *   OVEN01.PidIntegral   积分累加项（可读写，内部状态）
 *   OVEN01.PidPrevError  上次偏差（可读写，内部状态）
 *   OVEN01.PidAuto       自动/手动标志（布尔，可读写；false 时脚本不输出）
 */

var DEVICE = "OVEN01";

// ---- PID 参数 ----
var KP = 2.5;             // 比例增益
var KI = 0.08;            // 积分增益（乘采样周期后累加）
var KD = 1.2;             // 微分增益
var SAMPLE_DT_S = 5;      // 采样周期（秒），必须与脚本执行间隔一致

// ---- 限幅 ----
var OUT_MIN = 0;
var OUT_MAX = 100;
var I_MAX = 150;          // 积分项限幅：抗饱和的核心
var DEADBAND = 0.5;       // 偏差死区
var RAMP_MAX = 8;         // 单次输出最大变化（%/周期）

function num(dev, key, fb) {
  if (getQuality(dev, key) !== "Good") return fb;
  var v = read(dev, key);
  return (typeof v === "number" && isFinite(v)) ? v : fb;
}

function clamp(v, lo, hi) { return Math.min(hi, Math.max(lo, v)); }

function run() {
  // ---------- ① 手动模式：不输出，但持续跟踪状态（无扰切换的基础）----------
  if (read(DEVICE, "PidAuto") !== true) {
    log("手动模式，PID 不输出（内部状态保持，切回自动时无扰动）");
    return;
  }

  // ---------- ② 读 PV / SP ----------
  if (getQuality(DEVICE, "Temp") !== "Good") {
    log("温度质量异常 =", getQuality(DEVICE, "Temp"), "，保持输出");
    return;
  }
  var pv = num(DEVICE, "Temp", null);
  var sp = num(DEVICE, "TempSP", null);
  if (pv === null || sp === null) { log("PV/SP 不可用，保持输出"); return; }

  var out = num(DEVICE, "HeatOutput", 0);
  var integral = num(DEVICE, "PidIntegral", 0);
  var prevErr = num(DEVICE, "PidPrevError", 0);

  // ---------- ③ PID 运算 ----------
  var err = sp - pv;

  // 死区内保持
  if (Math.abs(err) <= DEADBAND) {
    log("偏差 " + err.toFixed(2) + " 在死区内，保持输出 " + out.toFixed(1));
    write(DEVICE, "PidPrevError", err);
    return;
  }

  // 积分项累加 + 抗饱和（条件积分：输出已饱和且误差同向时停止积分）
  var pTerm = KP * err;
  var dTerm = KD * (err - prevErr) / SAMPLE_DT_S;
  var tentative = pTerm + KI * (integral + err * SAMPLE_DT_S) + dTerm;

  var saturated = (tentative >= OUT_MAX && err > 0) || (tentative <= OUT_MIN && err < 0);
  if (!saturated) {
    integral = integral + err * SAMPLE_DT_S;
    integral = clamp(integral, -I_MAX, I_MAX);
  } else {
    log("输出饱和，暂停积分（抗饱和生效）");
  }

  var iTerm = KI * integral;
  var target = pTerm + iTerm + dTerm;

  // ---------- ④ 限速 + 限幅 ----------
  var stepped = clamp(target, out - RAMP_MAX, out + RAMP_MAX);
  var final = clamp(stepped, OUT_MIN, OUT_MAX);
  final = Math.round(final * 10) / 10;

  log("PV=" + pv.toFixed(2) + " SP=" + sp.toFixed(1) + " err=" + err.toFixed(2));
  log("  P=" + pTerm.toFixed(2) + " I=" + iTerm.toFixed(2) + " D=" + dTerm.toFixed(3)
      + " → 目标 " + target.toFixed(2) + " → 限速 " + stepped.toFixed(2)
      + " → 输出 " + final.toFixed(1));

  // ---------- ⑤ 写回（按依赖顺序，逐个检查）----------
  var ok1 = write(DEVICE, "PidIntegral", Math.round(integral * 1000) / 1000);
  var ok2 = write(DEVICE, "PidPrevError", Math.round(err * 1000) / 1000);

  if (Math.abs(final - out) >= 0.3) {
    var ok3 = write(DEVICE, "HeatOutput", final);
    log(ok3 ? "✔ 输出已更新为 " + final : "✘ 输出写入失败");
  } else {
    log("输出变化 " + (final - out).toFixed(2) + " 过小，跳过写入");
  }

  if (!ok1 || !ok2) log("✘ PID 内部状态写回失败，下一周期控制将失真，请检查写授权");
}
