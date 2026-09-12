/**
 * 示例 17 · 多设备数据汇总（生成报表变量）
 *
 * 场景：从多台设备取数，汇总成一组"报表变量"，供 HMI 画面或趋势图直接绑定展示。
 *       常用于：车间总览、能耗看板、OEE 统计。
 * 触发类型：Periodic，执行间隔 = 60 秒
 * 读授权：LINE1;LINE2;LINE3
 * 写授权：SUMMARY.TotalOutput;SUMMARY.TotalPower;SUMMARY.AvgOee;SUMMARY.RunningLines;SUMMARY.UpdatedAt
 *
 * 假设：
 *   每条产线有 Output（产量）、Power（功率 kW）、Running（布尔）、Oee（OEE %）
 *   汇总设备 SUMMARY 下有若干可写变量
 */

var LINES = ["LINE1", "LINE2", "LINE3"];
var SUMMARY = "SUMMARY";

function num(dev, key, fb) {
  if (getQuality(dev, key) !== "Good") return fb;
  var v = read(dev, key);
  return (typeof v === "number" && isFinite(v)) ? v : fb;
}

function put(key, value) {
  var ok = write(SUMMARY, key, value);
  if (!ok) log("[FAIL] 汇总写入 " + SUMMARY + "." + key + " = " + value);
  return ok;
}

function run() {
  var totalOutput = 0;
  var totalPower = 0;
  var oeeSum = 0, oeeCount = 0;
  var runningCount = 0;
  var offline = [];
  var rows = [];

  for (var i = 0; i < LINES.length; i++) {
    var line = LINES[i];

    // 用"是否在线"判断：Running 这个关键变量读不到就认为该线离线
    var q = getQuality(line, "Running");
    if (q !== "Good") {
      offline.push(line + "(" + q + ")");
      rows.push({ line: line, state: "OFFLINE" });
      continue;
    }

    var running = read(line, "Running") === true;
    var output = num(line, "Output", null);
    var power = num(line, "Power", null);
    var oee = num(line, "Oee", null);

    if (running) runningCount++;
    if (output !== null) totalOutput += output;
    if (power !== null) totalPower += power;
    if (oee !== null) { oeeSum += oee; oeeCount++; }

    rows.push({
      line: line,
      state: running ? "RUN" : "STOP",
      output: output,
      power: power,
      oee: oee
    });
  }

  var avgOee = oeeCount > 0 ? oeeSum / oeeCount : 0;

  log("=== 车间汇总 " + new Date().toLocaleString() + " ===");
  log("运行产线 " + runningCount + "/" + LINES.length
      + (offline.length ? "，离线：" + offline.join(",") : ""));
  log("总产量 = " + totalOutput + "，总功率 = " + totalPower.toFixed(1) + " kW"
      + "，平均 OEE = " + avgOee.toFixed(1) + "%");

  // 逐行明细（用表格化字符串，控制台里对齐好看）
  for (var j = 0; j < rows.length; j++) {
    var r = rows[j];
    if (r.state === "OFFLINE") {
      log("  " + r.line + "  [离线]");
    } else {
      log("  " + r.line + "  " + (r.state === "RUN" ? "运行" : "停机")
          + "  产量=" + (r.output === null ? "-" : r.output)
          + "  功率=" + (r.power === null ? "-" : r.power.toFixed(1))
          + "  OEE=" + (r.oee === null ? "-" : r.oee.toFixed(1) + "%"));
    }
  }

  // ---------- 写回汇总变量 ----------
  put("TotalOutput", totalOutput);
  put("TotalPower", Math.round(totalPower * 10) / 10);
  put("AvgOee", Math.round(avgOee * 10) / 10);
  put("RunningLines", runningCount);
  put("UpdatedAt", Date.now());          // 时间戳，HMI 可显示"数据更新于 xx"

  log("汇总已写入 " + SUMMARY + ".*");
}
