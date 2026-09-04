/**
 * 示例 13 · 设备心跳看门狗（通信质量监测）
 *
 * 场景：定期扫描一批关键设备的变量质量，判定离线/通信异常并输出告警清单。
 *       可直接接入事件联动或通知中心。
 * 触发类型：Periodic，执行间隔 = 60 秒
 * 读授权：PLC_A;PLC_B;RTU01
 * 写授权：（只读脚本，不配写授权更安全）
 *
 * 假设：每台设备下都有一个 Heartbeat（心跳，数值，每周期自增）变量
 */

// 受监测设备清单：设备键 + 心跳变量键
var TARGETS = [
  { dev: "PLC_A", key: "Heartbeat", name: "1号PLC" },
  { dev: "PLC_B", key: "Heartbeat", name: "2号PLC" },
  { dev: "RTU01", key: "Heartbeat", name: "远程RTU" }
];

// 判定为异常的 quality
var BAD_QUALITIES = ["Bad", "CommunicationError", "DeviceOffline",
                     "Timeout", "NotConnected", "Unknown"];

function isBadQuality(q) {
  return BAD_QUALITIES.indexOf(q) >= 0;
}

function run() {
  var offline = [];       // 离线设备
  var initializing = [];  // 还在初始化
  var alive = [];         // 正常

  for (var i = 0; i < TARGETS.length; i++) {
    var t = TARGETS[i];
    var q = getQuality(t.dev, t.key);
    var v = read(t.dev, t.key);

    if (q === "Good") {
      alive.push(t.name + "(心跳=" + v + ")");
    } else if (q === "Initializing") {
      initializing.push(t.name);
    } else if (isBadQuality(q)) {
      offline.push(t.name + "[" + t.dev + "]:" + q);
    } else {
      // Uncertain 等中间态
      offline.push(t.name + "[" + t.dev + "]:" + q + "(数据不确定)");
    }
  }

  var ts = new Date().toLocaleString();

  if (offline.length === 0 && initializing.length === 0) {
    log("[" + ts + "] ✔ 全部 " + alive.length + " 台设备通信正常");
    return;
  }

  log("[" + ts + "] === 通信巡检告警 ===");
  log("正常 " + alive.length + " 台，异常 " + offline.length
      + " 台，初始化中 " + initializing.length + " 台");
  if (alive.length) log("  正常：" + alive.join("，"));
  if (initializing.length) log("  初始化中：" + initializing.join("，"));
  if (offline.length) log("  ✘ 异常：" + offline.join("  "));

  // 结构化输出，便于日志系统检索
  log("WATCHDOG " + JSON.stringify({
    ts: new Date().toISOString(),
    offlineCount: offline.length,
    offline: offline
  }));
}
