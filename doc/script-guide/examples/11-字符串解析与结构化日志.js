/**
 * 示例 11 · 字符串解析与结构化日志
 *
 * 场景：设备通过字符串型变量返回自由格式报文（如 "T=25.6C;H=48%;ST=RUN"），
 *       脚本解析出各字段并输出结构化 JSON 日志。
 * 触发类型：Periodic，执行间隔 = 20 秒
 * 读授权：ENV01
 * 写授权：（不需要）
 *
 * 假设变量：ENV01.RawPayload（字符串）、ENV01.Temp（数值）、ENV01.Humi（数值）
 */

var DEVICE = "ENV01";

/**
 * 解析 "K=V;K=V" 形式的报文为对象
 * @param {string} s 原始报文
 * @returns {Object} 键值对
 */
function parsePayload(s) {
  var result = {};
  if (typeof s !== "string" || s.length === 0) return result;

  var parts = s.split(";");
  for (var i = 0; i < parts.length; i++) {
    var seg = parts[i].trim();
    if (seg === "") continue;
    var eq = seg.indexOf("=");
    if (eq <= 0) continue;
    var k = seg.substring(0, eq).trim();
    var v = seg.substring(eq + 1).trim();
    result[k] = v;
  }
  return result;
}

/**
 * 从字符串中提取第一个数值（支持负号与小数）
 * 例："T=25.6C" → 25.6
 */
function extractNumber(s) {
  if (typeof s !== "string") return null;
  var m = s.match(/-?\d+(\.\d+)?/);
  if (!m) return null;
  var v = parseFloat(m[0]);
  return isFinite(v) ? v : null;
}

function run() {
  var raw = read(DEVICE, "RawPayload");
  var q = getQuality(DEVICE, "RawPayload");

  if (q !== "Good") {
    log("报文不可读，质量 =", q);
    return;
  }

  log("原始报文 = [" + raw + "]");

  // ---------- ① 键值报文解析 ----------
  var data = parsePayload(raw);
  log("解析结果 =", JSON.stringify(data));

  var temp = extractNumber(data["T"] || "");
  var humi = extractNumber(data["H"] || "");
  var state = data["ST"] || "UNKNOWN";

  log("温度 = " + (temp === null ? "解析失败" : temp + "℃")
      + "，湿度 = " + (humi === null ? "解析失败" : humi + "%")
      + "，状态 = " + state);

  // ---------- ② 字符串方法综合演示 ----------
  if (typeof raw === "string") {
    log("报文长度 = " + raw.length
        + "，含 'RUN' = " + (raw.indexOf("RUN") >= 0)
        + "，以 'T=' 开头 = " + raw.startsWith("T=")
        + "，大写形式 = " + raw.toUpperCase());
  }

  // ---------- ③ 与数值型变量交叉校验 ----------
  var tempVar = read(DEVICE, "Temp");        // 由驱动直接采集的温度
  if (typeof tempVar === "number" && temp !== null) {
    var diff = Math.abs(tempVar - temp);
    log("报文温度 " + temp + " vs 变量温度 " + tempVar
        + "，偏差 = " + diff.toFixed(2));
    if (diff > 2) log("⚠ 报文与变量偏差过大，请检查采集地址或报文格式");
  }

  // ---------- ④ 状态判断 ----------
  if (state === "RUN") {
    log("设备运行中");
  } else if (state === "STOP") {
    log("设备已停机");
  } else if (state === "ERR") {
    log("⚠ 设备上报错误状态");
  } else {
    log("未知状态：" + state);
  }

  // ---------- ⑤ 一次性输出结构化快照（推荐做法） ----------
  log("SNAPSHOT " + JSON.stringify({
    ts: new Date().toISOString(),
    temp: temp, humi: humi, state: state,
    raw: raw
  }));
}
