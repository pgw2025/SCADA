/**
 * 示例 02 · 设备状态巡检（周期执行）
 *
 * 场景：每 60 秒巡查一组测点，统计有效点数、超温点数、离线点数，输出一行汇总。
 * 触发类型：Periodic，执行间隔 = 60 秒
 * 读授权：BOILER01
 * 写授权：（不需要）
 *
 * 假设变量：BOILER01 下的 Temp1..Temp6（数值）、Pressure（数值）
 */

var DEVICE = "BOILER01";
var TEMP_POINTS = ["Temp1", "Temp2", "Temp3", "Temp4", "Temp5", "Temp6"];
var TEMP_LIMIT = 120;      // 超温阈值
var PRESS_LIMIT = 1.6;     // 超压阈值（MPa）

/**
 * 读取一个数值点，返回 {key, value, quality, ok}
 */
function probe(dev, key) {
  var q = getQuality(dev, key);
  var v = read(dev, key);
  var ok = (q === "Good") && (typeof v === "number") && isFinite(v);
  return { key: key, value: v, quality: q, ok: ok };
}

function run() {
  var valid = 0, overTemp = 0, offline = 0;
  var sum = 0, max = null, maxKey = "";
  var details = [];

  // 逐个测点巡查
  for (var i = 0; i < TEMP_POINTS.length; i++) {
    var p = probe(DEVICE, TEMP_POINTS[i]);

    if (!p.ok) {
      offline++;
      details.push(p.key + ":" + p.quality);
      continue;
    }

    valid++;
    sum += p.value;
    if (p.value > TEMP_LIMIT) overTemp++;
    if (max === null || p.value > max) { max = p.value; maxKey = p.key; }
  }

  var avg = valid > 0 ? sum / valid : null;
  var press = probe(DEVICE, "Pressure");

  log("=== 锅炉巡检 " + new Date().toLocaleString() + " ===");
  log("有效点 " + valid + "/" + TEMP_POINTS.length
      + "，超温 " + overTemp + "，不可用 " + offline);
  if (valid > 0) {
    log("平均温度 = " + avg.toFixed(1) + "℃，最高 = " + max.toFixed(1)
        + "℃（" + maxKey + "）");
  }
  log("压力 = " + (press.ok ? press.value.toFixed(3) + " MPa" : "不可用")
      + (press.ok && press.value > PRESS_LIMIT ? " 【超压！】" : ""));
  if (offline > 0) log("异常点：", details.join(", "));

  // 整数温度输出示例：取整的三种写法
  if (max !== null) {
    log("最高温度取整：ceil=" + Math.ceil(max)
        + " floor=" + Math.floor(max)
        + " round=" + Math.round(max));
  }
}
