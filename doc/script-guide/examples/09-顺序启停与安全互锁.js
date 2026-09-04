/**
 * 示例 09 · 顺序启停与安全互锁（Manual / 按钮触发）
 *
 * 场景：HMI 按钮触发的"一键启动"序列：先开润滑油泵 → 确认油压建立 → 启动主电机 → 开进料阀。
 *       任一步失败立即中止并回滚已执行的步骤。
 * 触发类型：Manual（由 HMI 按钮 / 事件联动 / 计划任务调用）
 * 读授权：LINE_A
 * 写授权：LINE_A.OilPump;LINE_A.MainMotor;LINE_A.FeedValve;LINE_A.SeqStep;LINE_A.SeqError
 *
 * 假设变量：
 *   LINE_A.OilPump     润滑油泵（布尔，可写）
 *   LINE_A.OilPress    油压（数值，只读）
 *   LINE_A.MainMotor   主电机（布尔，可写）
 *   LINE_A.FeedValve   进料阀（布尔，可写）
 *   LINE_A.SeqStep     序列步骤（数值，可写，用于 HMI 显示进度）
 *   LINE_A.SeqError    错误码（数值，可写，0=无错误）
 */

var DEVICE = "LINE_A";
var OIL_PRESS_MIN = 0.25;    // 允许启动主电机的最低油压 MPa

function num(dev, key, fb) {
  if (getQuality(dev, key) !== "Good") return fb;
  var v = read(dev, key);
  return (typeof v === "number" && isFinite(v)) ? v : fb;
}

/** 写值 + 日志，失败抛异常由外层统一处理 */
function step(stepNo, key, value, desc) {
  log("步骤 " + stepNo + "：" + desc);
  var ok = write(DEVICE, key, value);
  if (!ok) {
    throw new Error("步骤 " + stepNo + " 失败：" + desc
        + "（" + DEVICE + "." + key + " = " + value + "）");
  }
  write(DEVICE, "SeqStep", stepNo);       // 进度写回，HMI 可显示
  log("  ✔ 完成");
}

/**
 * 中止并回滚：按相反顺序关闭已开启的设备
 */
function abort(stepNo, errMsg, opened) {
  log("✘ 序列在第 " + stepNo + " 步中止：" + errMsg);
  log("执行回滚…");

  // 从后往前关
  for (var i = opened.length - 1; i >= 0; i--) {
    var item = opened[i];
    var ok = write(DEVICE, item.key, false);
    log("  回滚 " + item.key + " → " + (ok ? "已关闭" : "关闭失败！"));
  }

  write(DEVICE, "SeqError", stepNo);      // 错误码 = 失败步骤号
  write(DEVICE, "SeqStep", 0);
  log("回滚结束，错误码 = " + stepNo);
}

function run() {
  var opened = [];        // 记录已成功开启的设备，供回滚用

  try {
    // ---------- 前置检查 ----------
    if (getQuality(DEVICE, "OilPress") !== "Good") {
      throw new Error("油压数据不可用，禁止启动");
    }
    if (read(DEVICE, "MainMotor") === true) {
      log("主电机已在运行，无需启动");
      return;
    }

    write(DEVICE, "SeqError", 0);
    log("=== 一键启动序列开始 ===");

    // ---------- 步骤 1：开润滑油泵 ----------
    step(1, "OilPump", true, "启动润滑油泵");
    opened.push({ key: "OilPump" });

    // ---------- 步骤 2：等待油压建立 ----------
    // 注意：脚本里不能用 setTimeout 等待！只能在"本次执行内"做即时判断，
    // 或者把等待逻辑交给周期脚本（本例用即时判断 + 状态位，由下次触发继续）
    var press = num(DEVICE, "OilPress", 0);
    log("步骤 2：检查油压 = " + press + " MPa（要求 ≥ " + OIL_PRESS_MIN + "）");

    if (press < OIL_PRESS_MIN) {
      // 油压未建立：保留油泵运行，把流程停在步骤 2，等下次触发继续
      log("油压未建立，保持油泵运行，等待下次触发继续（SeqStep=2）");
      write(DEVICE, "SeqStep", 2);
      return;                                  // 这不是失败，不回滚
    }
    log("  ✔ 油压已建立");

    // ---------- 步骤 3：启动主电机 ----------
    step(3, "MainMotor", true, "启动主电机");
    opened.push({ key: "MainMotor" });

    // ---------- 步骤 4：开进料阀 ----------
    step(4, "FeedValve", true, "打开进料阀");
    opened.push({ key: "FeedValve" });

    write(DEVICE, "SeqStep", 99);              // 99 = 完成
    log("=== 启动序列完成 ===");

  } catch (e) {
    // 捕获后自行回滚：本次执行仍记为 Success（不会累积熔断计数）
    abort(opened.length + 1, e.message, opened);
  }
}
