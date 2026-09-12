/**
 * 示例 06 · 批量设备轮询与统计（数组 / Set / reduce）
 *
 * 场景：轮询一组泵组设备，统计运行台数、累计流量、找出异常设备。
 * 触发类型：Periodic，执行间隔 = 30 秒
 * 读授权：PUMP_GRP
 * 写授权：（不需要）
 *
 * 假设：单台设备 PUMP_GRP 下有多台泵的变量
 *   P1_Run..P6_Run（布尔）、P1_Flow..P6_Flow（数值 m³/h）、P1_Curr..P6_Curr（数值 A）
 */

var DEVICE = "PUMP_GRP";
var PUMPS = ["P1", "P2", "P3", "P4", "P5", "P6"];
var CURR_HIGH = 45;      // 电流上限（A）
var FLOW_MIN = 10;       // 运行泵的最小流量（m³/h），低于它判定为"空转/堵塞"

function run() {
  var running = [];      // 运行中的泵编号
  var abnormal = [];     // 异常泵描述
  var totalFlow = 0;
  var totalCurr = 0;
  var offline = 0;

  for (var i = 0; i < PUMPS.length; i++) {
    var p = PUMPS[i];

    var isRun = read(DEVICE, p + "_Run");
    var flow = read(DEVICE, p + "_Flow");
    var curr = read(DEVICE, p + "_Curr");
    var qRun = getQuality(DEVICE, p + "_Run");

    // 状态都读不到 → 计入离线
    if (qRun !== "Good") {
      offline++;
      abnormal.push(p + ":通信异常(" + qRun + ")");
      continue;
    }

    if (isRun === true) {
      running.push(p);

      if (typeof flow === "number") totalFlow += flow;
      if (typeof curr === "number") totalCurr += curr;

      // 运行中的泵做健康判断
      if (typeof curr === "number" && curr > CURR_HIGH) {
        abnormal.push(p + ":过流(" + curr.toFixed(1) + "A)");
      }
      if (typeof flow === "number" && flow < FLOW_MIN) {
        abnormal.push(p + ":流量偏低(" + flow.toFixed(1) + ")");
      }
    }
  }

  var avgCurr = running.length > 0 ? totalCurr / running.length : 0;

  log("=== 泵组轮询 ===");
  log("运行 " + running.length + "/" + PUMPS.length + " 台"
      + (running.length ? "：" + running.join(",") : ""));
  log("总流量 = " + totalFlow.toFixed(1) + " m³/h"
      + "，平均电流 = " + avgCurr.toFixed(1) + " A");
  log("通信异常 " + offline + " 台");
  log("异常项：" + (abnormal.length ? abnormal.join(" | ") : "无"));

  // Set 去重示例：收集本次出现的所有异常类型
  var types = new Set();
  for (var j = 0; j < abnormal.length; j++) {
    var desc = abnormal[j];
    var idx = desc.indexOf(":");
    if (idx > 0) types.add(desc.substring(idx + 1).replace(/\(.*\)/, ""));
  }
  log("异常类型（去重后）：" + Array.from(types).join("、"));

  // 简单负载率评估：运行台数 / 总台数
  var loadRate = Math.round(running.length / PUMPS.length * 100);
  log("负载率 = " + loadRate + "%"
      + (loadRate >= 80 ? " 【高负载】" : loadRate === 0 ? " 【全部停机】" : ""));
}
