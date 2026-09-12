/**
 * 示例 03 · 液位越限联动控制（变量变化触发）
 *
 * 场景：监听液位变化，超过高限开排水阀、低于低限关排水阀并停进料泵。
 * 触发类型：OnChange
 *   监听设备键 = TANK01，监听变量键 = Level
 *   死区 = 2.0（量程 0-100 时约 2%）
 *   冷却 = 1000 ms
 * 读授权：TANK01
 * 写授权：TANK01.DrainValve;TANK01.FeedPump
 *
 * 假设变量：TANK01.Level（数值）、TANK01.DrainValve（布尔）、TANK01.FeedPump（布尔）
 */

var HIGH = 85;   // 高限：开排水
var LOW = 15;    // 低限：关排水、停进料
var DEVICE = "TANK01";

/**
 * 写入并记日志。返回是否成功。
 */
function writeChecked(key, value) {
  var ok = write(DEVICE, key, value);
  log((ok ? "✔ 写入成功 " : "✘ 写入失败 ") + DEVICE + "." + key + " = " + value);
  return ok;
}

function onChange(ev) {
  // ① 数据质量门禁：坏数据绝对不做控制决策
  if (ev.quality !== "Good") {
    log("液位质量 =", ev.quality, "，忽略本次变化");
    return;
  }

  var level = ev.value;
  if (typeof level !== "number" || !isFinite(level)) {
    log("液位值非法：", level);
    return;
  }

  log("液位变化：" + ev.previous + " → " + level);

  // ② 边沿 + 状态判断：避免每次变化都重复下发（防回声、防重复写）
  var draining = read(DEVICE, "DrainValve") === true;
  var feeding = read(DEVICE, "FeedPump") === true;

  if (level >= HIGH) {
    log("液位 " + level + " 达到高限 " + HIGH);
    if (!draining) writeChecked("DrainValve", true);   // 已经在排就别重复写
    if (feeding) writeChecked("FeedPump", false);      // 高液位停止进料
  } else if (level <= LOW) {
    log("液位 " + level + " 达到低限 " + LOW);
    if (draining) writeChecked("DrainValve", false);
    if (feeding) writeChecked("FeedPump", false);      // 低液位防抽空
  } else {
    // ③ 回到正常区间：恢复进料（这里演示"滞回区间"控制）
    if (level > 30 && level < 70 && !feeding) {
      log("液位回到正常区间，恢复进料");
      writeChecked("FeedPump", true);
    }
  }
}
