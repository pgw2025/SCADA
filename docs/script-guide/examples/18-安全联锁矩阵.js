/**
 * 示例 18 · 安全联锁矩阵（多条件互锁 + 急停）
 *
 * 场景：设备启动前必须满足一组安全条件（互锁），任一不满足则禁止启动；
 *       运行中任一条件失效则立即安全停机。这是工业控制里最常见的模式。
 * 触发类型：Periodic，执行间隔 = 3 秒（安全相关，间隔要短）
 * 读授权：PRESS01
 * 写授权：PRESS01.MotorOn;PRESS01.InterlockOk;PRESS01.InterlockCode;PRESS01.Estop
 *
 * 假设变量（布尔为主）：
 *   PRESS01.MotorOn        主电机（可写）
 *   PRESS01.GuardClosed    安全门关闭（只读）
 *   PRESS01.EstopPressed   急停按下（只读，true=按下）
 *   PRESS01.AirPressOk     气压正常（只读）
 *   PRESS01.LubeOk         润滑正常（只读）
 *   PRESS01.Overload       过载（只读）
 *   PRESS01.InterlockOk    互锁综合状态（可写，HMI 显示用）
 *   PRESS01.InterlockCode  互锁失败码（可写，0=正常，位掩码）
 *   PRESS01.Estop          停机输出（可写）
 */

var DEVICE = "PRESS01";

// 互锁条件表：位值 → { 变量, 期望值, 说明 }
// 用位掩码表达多个条件，一个数值就能在 HMI 上看出是哪一条不满足
var INTERLOCKS = [
  { bit: 1,   key: "GuardClosed",  expect: true,  desc: "安全门未关闭" },
  { bit: 2,   key: "EstopPressed", expect: false, desc: "急停被按下" },
  { bit: 4,   key: "AirPressOk",   expect: true,  desc: "气压不足" },
  { bit: 8,   key: "LubeOk",       expect: true,  desc: "润滑异常" },
  { bit: 16,  key: "Overload",     expect: false, desc: "电机过载" }
];

function good(dev, key) { return getQuality(dev, key) === "Good"; }

function run() {
  var failMask = 0;       // 失败位掩码
  var reasons = [];       // 失败原因文本
  var unknown = [];       // 读不到的条件（视为不安全）

  // ---------- ① 逐条检查互锁 ----------
  for (var i = 0; i < INTERLOCKS.length; i++) {
    var il = INTERLOCKS[i];

    if (!good(DEVICE, il.key)) {
      // 安全原则：读不到 = 不安全
      unknown.push(il.key);
      failMask += il.bit;
      reasons.push(il.desc + "(数据不可用)");
      continue;
    }

    var v = read(DEVICE, il.key);
    if (v !== il.expect) {
      failMask += il.bit;
      reasons.push(il.desc);
    }
  }

  var allOk = (failMask === 0);
  var motorOn = read(DEVICE, "MotorOn") === true;

  // ---------- ② 写回互锁状态（供 HMI 显示）----------
  write(DEVICE, "InterlockOk", allOk);
  if (failMask !== num(DEVICE, "InterlockCode", -1)) {
    write(DEVICE, "InterlockCode", failMask);
  }

  // ---------- ③ 运行中条件失效 → 立即安全停机 ----------
  if (motorOn && !allOk) {
    log("★★ 运行中互锁失效（code=" + failMask + "）：" + reasons.join("、"));
    var ok = write(DEVICE, "MotorOn", false);
    var ok2 = write(DEVICE, "Estop", true);
    log(ok ? "✔ 已停止主电机" : "✘ 停止主电机失败！");
    log(ok2 ? "✔ 已置停机输出" : "✘ 停机输出写入失败！");
    return;
  }

  // ---------- ④ 允许启动 / 保持 ----------
  if (allOk) {
    if (unknown.length === 0) {
      log("互锁全部满足" + (motorOn ? "（运行中）" : "（待启动）"));
    }
  } else {
    log("互锁未满足，禁止启动（code=" + failMask + "）：" + reasons.join("、"));
    if (motorOn === false) {
      // 停机状态下把停机输出清掉，允许下次启动
      write(DEVICE, "Estop", false);
    }
  }
}

/** 读数值工具（本例仅用于 InterlockCode 比较） */
function num(dev, key, fb) {
  if (getQuality(dev, key) !== "Good") return fb;
  var v = read(dev, key);
  return (typeof v === "number" && isFinite(v)) ? v : fb;
}
