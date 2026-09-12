/**
 * 示例 08 · 报警升级与去重（边沿触发 + 状态位）
 *
 * 场景：压力高报警触发后，先尝试自动泄压；持续 N 次仍高则升级为"停机"指令。
 *       演示如何用「状态位变量 + 计数变量」在脚本无状态的前提下实现跨执行记忆。
 * 触发类型：OnChange
 *   监听设备键 = COMP01，监听变量键 = Pressure
 *   死区 = 0.05（MPa）
 *   冷却 = 3000 ms
 * 读授权：COMP01
 * 写授权：COMP01.ReliefValve;COMP01.AlarmLevel;COMP01.AlarmCount;COMP01.Shutdown
 *
 * 假设变量：
 *   COMP01.Pressure      压力 MPa
 *   COMP01.ReliefValve   泄压阀（布尔）
 *   COMP01.AlarmLevel    报警等级 0=正常 1=预警 2=停机
 *   COMP01.AlarmCount    连续报警次数（可读写，用于跨执行计数）
 *   COMP01.Shutdown      停机指令（布尔）
 */

var DEVICE = "COMP01";
var WARN_P = 1.2;        // 预警压力
var TRIP_P = 1.5;        // 停机压力
var MAX_RETRY = 3;       // 连续报警几次后升级为停机

function num(dev, key, fb) {
  if (getQuality(dev, key) !== "Good") return fb;
  var v = read(dev, key);
  return (typeof v === "number" && isFinite(v)) ? v : fb;
}

function onChange(ev) {
  if (ev.quality !== "Good") { log("压力质量 =", ev.quality, "，忽略"); return; }

  var p = ev.value;
  if (typeof p !== "number") return;

  var prevLevel = num(DEVICE, "AlarmLevel", 0);
  var count = num(DEVICE, "AlarmCount", 0);

  log("压力变化 " + ev.previous + " → " + p + " MPa（当前等级 " + prevLevel
      + "，连续 " + count + " 次）");

  // ---------- ① 压力恢复正常：复位 ----------
  if (p < WARN_P) {
    if (prevLevel > 0) {
      log("压力恢复正常，复位报警");
      write(DEVICE, "AlarmLevel", 0);
      write(DEVICE, "AlarmCount", 0);
      write(DEVICE, "ReliefValve", false);
    }
    return;
  }

  // ---------- ② 达到停机压力：直接升级 ----------
  if (p >= TRIP_P) {
    log("★★ 压力 " + p + " ≥ 停机值 " + TRIP_P + "，执行停机");
    write(DEVICE, "AlarmLevel", 2);
    write(DEVICE, "ReliefValve", true);
    write(DEVICE, "Shutdown", true);
    return;
  }

  // ---------- ③ 预警区间：先泄压并计数，超次数则升级 ----------
  var newCount = count + 1;
  write(DEVICE, "AlarmCount", newCount);
  write(DEVICE, "AlarmLevel", 1);

  // 只在第一次进入预警时开泄压阀，避免重复写
  if (read(DEVICE, "ReliefValve") !== true) {
    var ok = write(DEVICE, "ReliefValve", true);
    log(ok ? "✔ 已开启泄压阀" : "✘ 泄压阀写入失败");
  }

  log("预警第 " + newCount + " 次");
  if (newCount >= MAX_RETRY) {
    log("★ 连续 " + newCount + " 次预警未缓解，升级为停机");
    write(DEVICE, "AlarmLevel", 2);
    write(DEVICE, "Shutdown", true);
  }
}
