/**
 * 示例 14 · 能耗累计与定时清零（跨执行累加）
 *
 * 场景：每 5 分钟采样一次功率，按时间差累加成电量（kWh），并计算日均功率。
 *       演示「无状态脚本 + 变量持久化」实现积分运算的完整套路。
 *
 *   电量增量 ΔE = P(kW) × Δt(h)
 *
 * 触发类型：Periodic，执行间隔 = 300 秒（5 分钟）
 * 读授权：MCC01
 * 写授权：MCC01.TotalEnergy;MCC01.LastPower;MCC01.LastSampleTime;MCC01.AvgPower
 *
 * 假设变量：
 *   MCC01.Power          实时功率 kW（只读）
 *   MCC01.TotalEnergy    累计电量 kWh（可读写）
 *   MCC01.LastPower      上次采样功率 kW（可读写）
 *   MCC01.LastSampleTime 上次采样时间戳 ms（可读写）
 *   MCC01.AvgPower       区间平均功率 kW（可写）
 */

var DEVICE = "MCC01";
var SAMPLE_INTERVAL_S = 300;        // 与本脚本执行间隔保持一致
var MAX_DT_H = 2.0;                 // 两次采样的最大可信间隔（h），超出则不计（防停机后补算）
var ENERGY_MAX = 99999999;          // 累计量上限，防溢出

function num(dev, key, fb) {
  if (getQuality(dev, key) !== "Good") return fb;
  var v = read(dev, key);
  return (typeof v === "number" && isFinite(v)) ? v : fb;
}

function run() {
  var now = Date.now();

  // ---------- ① 读实时功率 ----------
  var power = num(DEVICE, "Power", null);
  if (power === null) {
    log("功率不可用（质量 = " + getQuality(DEVICE, "Power") + "），本次不累计");
    return;
  }

  // ---------- ② 读上次状态 ----------
  var total = num(DEVICE, "TotalEnergy", 0);
  var lastPower = num(DEVICE, "LastPower", null);
  var lastTime = num(DEVICE, "LastSampleTime", null);

  // ---------- ③ 计算时间差（小时）----------
  var dtH;
  if (lastTime === null || lastTime <= 0) {
    // 首次执行：用标称间隔兜底
    dtH = SAMPLE_INTERVAL_S / 3600;
    log("首次采样，采用标称间隔 " + SAMPLE_INTERVAL_S + "s");
  } else {
    dtH = (now - lastTime) / 3600000;
    log("距上次采样 " + (dtH * 60).toFixed(2) + " 分钟");

    // 间隔异常：脚本停过 / 系统重启过 → 不补算，避免虚增电量
    if (dtH <= 0) {
      log("时间戳异常（未推进），跳过本次累计");
      return;
    }
    if (dtH > MAX_DT_H) {
      log("⚠ 间隔 " + dtH.toFixed(2) + "h 超过可信上限 " + MAX_DT_H
          + "h，视为中断后恢复，本次不累计电量");
      write(DEVICE, "LastPower", power);
      write(DEVICE, "LastSampleTime", now);
      return;
    }
  }

  // ---------- ④ 梯形积分：用首尾功率平均值更准确 ----------
  var avgPower = (lastPower === null) ? power : (power + lastPower) / 2;
  var deltaE = avgPower * dtH;

  // 防负值（功率表异常）与溢出
  if (deltaE < 0) {
    log("⚠ 功率为负（" + power + " kW），跳过累计");
    deltaE = 0;
  }
  var newTotal = Math.min(ENERGY_MAX, total + deltaE);
  newTotal = Math.round(newTotal * 100) / 100;

  log("功率 " + power.toFixed(2) + " kW，区间均值 " + avgPower.toFixed(2)
      + " kW，Δt " + dtH.toFixed(4) + " h");
  log("累计电量：" + total.toFixed(2) + " → " + newTotal.toFixed(2)
      + " kWh（+" + deltaE.toFixed(3) + "）");

  // ---------- ⑤ 写回状态 ----------
  var ok1 = write(DEVICE, "TotalEnergy", newTotal);
  var ok2 = write(DEVICE, "AvgPower", Math.round(avgPower * 100) / 100);
  var ok3 = write(DEVICE, "LastPower", power);
  var ok4 = write(DEVICE, "LastSampleTime", now);

  if (!(ok1 && ok2 && ok3 && ok4)) {
    log("✘ 状态写回失败（ok1=" + ok1 + " ok2=" + ok2 + " ok3=" + ok3 + " ok4=" + ok4 + "）");
    log("  注意：LastSampleTime 未更新会导致下次间隔更大，请检查写授权");
  }

  // ---------- ⑥ 参考指标：日均功率推算 ----------
  if (dtH > 0) {
    log("等效日均功率 = " + (deltaE / dtH).toFixed(2) + " kW");
  }
}
