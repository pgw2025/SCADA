/**
 * 示例 10 · 工程量换算与 Math 工具集
 *
 * 场景：常见的工业换算公式集合（开方流量、温度补偿、三点标定、百分比换算等）。
 *       展示 Math 在系统脚本中的典型用法。
 *
 * ⚠️ 重要：如果只是"单个采集值的固定公式变换"，应该用变量的
 *    「工程换算表达式」功能（跑在采集线程内，零延迟），不要用脚本。
 *    脚本只适合需要"多变量参与计算"或"带业务逻辑"的换算。
 *
 * 触发类型：Periodic，执行间隔 = 15 秒
 * 读授权：FT01
 * 写授权：FT01.StdFlow;FT01.HeatValue
 *
 * 假设变量：FT01.DiffPress（差压 kPa）、FT01.Temp（℃）、FT01.Press（kPa 绝压）
 */

var DEVICE = "FT01";

// 设计工况（用于温压补偿）
var DESIGN_T = 20;        // 设计温度 ℃
var DESIGN_P = 101.325;   // 设计绝压 kPa
var T0 = 273.15;          // 绝对零度偏移

function num(dev, key, fb) {
  if (getQuality(dev, key) !== "Good") return fb;
  var v = read(dev, key);
  return (typeof v === "number" && isFinite(v)) ? v : fb;
}

/**
 * 差压式流量：Q = K * sqrt(ΔP)
 */
function flowFromDP(dp, k) {
  if (dp <= 0) return 0;                    // 负差压（倒流/噪声）按 0 处理
  return k * Math.sqrt(dp);
}

/**
 * 温压补偿（理想气体）：Q_std = Q * sqrt((P*T_d)/(P_d*T))
 */
function compensate(flow, tempC, pressAbs) {
  var t = tempC + T0;
  var td = DESIGN_T + T0;
  if (t <= 0 || pressAbs <= 0) return flow;
  return flow * Math.sqrt((pressAbs * td) / (DESIGN_P * t));
}

/**
 * 量程百分比换算：把原始值映射到 0-100%
 */
function toPercent(v, rangeLo, rangeHi) {
  if (rangeHi === rangeLo) return 0;
  var p = (v - rangeLo) / (rangeHi - rangeLo) * 100;
  return Math.min(100, Math.max(0, p));
}

/**
 * 保留 n 位小数
 */
function round(v, n) {
  var f = Math.pow(10, n);
  return Math.round(v * f) / f;
}

function run() {
  var dp = num(DEVICE, "DiffPress", null);
  var t = num(DEVICE, "Temp", null);
  var p = num(DEVICE, "Press", null);

  if (dp === null) { log("差压不可用"); return; }

  // ① 开方流量
  var flow = flowFromDP(dp, 12.5);
  log("差压 " + dp.toFixed(3) + " kPa → 工况流量 " + round(flow, 2) + " m³/h");

  // ② 温压补偿（缺任一项就不补偿，避免用默认值算出错误结果）
  if (t !== null && p !== null) {
    var stdFlow = compensate(flow, t, p);
    log("温度 " + t.toFixed(1) + "℃ / 绝压 " + p.toFixed(2)
        + " kPa → 标况流量 " + round(stdFlow, 2) + " Nm³/h");
    write(DEVICE, "StdFlow", round(stdFlow, 2));
  } else {
    log("温度/压力缺失，跳过温压补偿");
  }

  // ③ 百分比与对数示例
  log("流量占量程（0-500）= " + round(toPercent(flow, 0, 500), 1) + "%");
  if (flow > 0) {
    log("量程比（可调比）= " + round(flow / (12.5 * Math.sqrt(0.5)), 2));
  }

  // ④ 三角/幂/对数小集合（按需取用）
  log("Math 速查：PI=" + Math.PI.toFixed(4)
      + " e=" + Math.E.toFixed(4)
      + " log10(100)=" + Math.log10(100)
      + " pow(2,10)=" + Math.pow(2, 10)
      + " hypot(3,4)=" + Math.hypot(3, 4)
      + " sign(-5)=" + Math.sign(-5));

  // ⑤ 限幅后下发
  var heat = round(flow * 35.2, 1);                 // 假设热值系数
  heat = Math.min(99999, Math.max(0, heat));
  var ok = write(DEVICE, "HeatValue", heat);
  log(ok ? "✔ 热值已写入 " + heat + " MJ/h" : "✘ 热值写入失败");
}
