/**
 * 示例 05 · 类型安全的读取封装（通用工具库）
 *
 * 场景：把"读值 + 判质量 + 判类型 + 取默认"封装成工具函数，
 *      后续所有脚本都可以复用这一套，避免到处写重复的 null 判断。
 * 触发类型：任意（本例用 Manual 演示）
 * 读授权：MIXER01
 * 写授权：（不需要）
 */

var DEVICE = "MIXER01";

// ============ 工具函数集 ============

/** 读数值：不可用返回 fallback（默认 null） */
function num(dev, key, fallback) {
  if (getQuality(dev, key) !== "Good") {
    return (typeof fallback === "undefined") ? null : fallback;
  }
  var v = read(dev, key);
  if (typeof v === "number" && isFinite(v)) return v;
  // 字符串型变量里包着数字的情况（如 "25.6"）
  if (typeof v === "string" && v.trim() !== "") {
    var p = parseFloat(v);
    if (isFinite(p)) return p;
  }
  return (typeof fallback === "undefined") ? null : fallback;
}

/** 读布尔：非布尔值按 0/1 转换；不可用返回 fallback */
function bool(dev, key, fallback) {
  if (getQuality(dev, key) !== "Good") {
    return (typeof fallback === "undefined") ? null : fallback;
  }
  var v = read(dev, key);
  if (typeof v === "boolean") return v;
  if (typeof v === "number") return v !== 0;
  if (typeof v === "string") return v === "1" || v.toLowerCase() === "true";
  return (typeof fallback === "undefined") ? null : fallback;
}

/** 读字符串：不可用或非字符串返回 fallback */
function str(dev, key, fallback) {
  if (getQuality(dev, key) !== "Good") {
    return (typeof fallback === "undefined") ? null : fallback;
  }
  var v = read(dev, key);
  if (typeof v === "string") return v;
  if (typeof v === "number" || typeof v === "boolean") return String(v);
  return (typeof fallback === "undefined") ? null : fallback;
}

/** 质量是否可用 */
function good(dev, key) {
  return getQuality(dev, key) === "Good";
}

/** 写值并检查，统一日志前缀 */
function put(dev, key, value) {
  var ok = write(dev, key, value);
  log((ok ? "[OK]   " : "[FAIL] ") + dev + "." + key + " = " + value);
  return ok;
}

// ============ 用法演示 ============
function run() {
  // 读数值，给默认值：读不到就用 0，避免 null 参与运算得到 NaN
  var speed = num(DEVICE, "Speed", 0);
  var torque = num(DEVICE, "Torque", 0);
  log("转速 =", speed, "，扭矩 =", torque);

  // 读布尔，给默认值 false：设备离线时按"停止"处理更安全
  var running = bool(DEVICE, "Running", false);
  log("运行中 =", running);

  // 读字符串
  var mode = str(DEVICE, "Mode", "UNKNOWN");
  log("工作模式 =", mode);

  // 综合判断
  if (running && speed > 1500 && torque > 80) {
    log("⚠ 高速大扭矩工况，注意过载");
  }

  // 批量检查一组点的可用性
  var keys = ["Speed", "Torque", "Running", "Mode", "NotExistKey"];
  var bad = [];
  for (var i = 0; i < keys.length; i++) {
    if (!good(DEVICE, keys[i])) bad.push(keys[i] + "(" + getQuality(DEVICE, keys[i]) + ")");
  }
  log("不可用测点：" + (bad.length ? bad.join(", ") : "无"));

  // 提示：NotExistKey 与"没勾选读授权"的表现完全一致，都返回 Unknown
  // 排查时重点看列表里是不是整台设备都不通（→ 授权问题），
  // 还是只有个别点不通（→ 变量名拼错或变量已删除）
}
