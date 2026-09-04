/**
 * 示例 01 · 最小骨架与日志输出
 *
 * 场景：脚本模板的标准写法，展示哪些该写在顶层、哪些必须写在钩子里。
 * 触发类型：Manual（手动）
 * 读授权：TANK01
 * 写授权：（不需要，留空）
 *
 * 假设变量：TANK01.Temp（数值）、TANK01.PumpState（布尔）
 */

// ============ 顶层：只放常量与函数声明 ============
// ✅ 常量声明是安全的：每次执行都会重新求值，但无副作用
var MAX_TEMP = 80;
var DEVICE = "TANK01";

// ⚠️ 顶层写 log() 的输出会被清空（钩子调用前缓冲被 Clear），永远看不到
// ⚠️ 顶层写 write() 会真实下发，每次执行都写一次 —— 千万不要这么做

/**
 * 安全读取：先判质量，再判类型。
 * @returns {number|null} 可用数值；不可用返回 null
 */
function readNumber(dev, key) {
  if (getQuality(dev, key) !== "Good") return null;
  var v = read(dev, key);
  return (typeof v === "number" && isFinite(v)) ? v : null;
}

// ============ 钩子：所有逻辑都放这里 ============
function run() {
  log("=== 巡检开始 ===");

  var temp = readNumber(DEVICE, "Temp");
  if (temp === null) {
    log("温度不可用，质量 =", getQuality(DEVICE, "Temp"));
    return;
  }

  log("温度 =", temp.toFixed(2), "℃");
  log("上限 =", MAX_TEMP, "℃，是否超限 =", temp > MAX_TEMP);

  // 布尔量的读取与输出
  var pump = read(DEVICE, "PumpState");
  log("泵状态 =", pump, "（类型：" + typeof pump + "）");

  // 结构化输出：排查问题时最省事的一种日志
  log("快照 =", JSON.stringify({
    device: DEVICE,
    temp: temp,
    pump: pump,
    time: new Date().toISOString()
  }));

  log("=== 巡检结束 ===");
}
