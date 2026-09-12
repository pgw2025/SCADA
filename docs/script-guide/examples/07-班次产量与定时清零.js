/**
 * 示例 07 · 班次产量统计与定时清零（Cron + 状态持久化）
 *
 * 场景：每班（8:30 / 20:30）把当班产量归档并清零计数器。
 *       演示「脚本无状态」这一铁律：计数器必须存在变量里，不能存 JS 变量。
 * 触发类型：Schedule，Cron = "0 30 8,20 * * ?"（Asia/Shanghai）
 * 读授权：LINE01
 * 写授权：LINE01.ShiftOutput;LINE01.LastShiftOutput;LINE01.ShiftTag
 *
 * 假设变量：
 *   LINE01.ShiftOutput       当班产量（累计，可读写）
 *   LINE01.LastShiftOutput   上一班产量（归档用，可写）
 *   LINE01.ShiftTag          班次标识（"DAY" / "NIGHT"）
 */

var DEVICE = "LINE01";
var DAY_START_HOUR = 8;
var NIGHT_START_HOUR = 20;

/**
 * 安全读取数值
 */
function num(dev, key, fallback) {
  if (getQuality(dev, key) !== "Good") return fallback;
  var v = read(dev, key);
  return (typeof v === "number" && isFinite(v)) ? v : fallback;
}

function run() {
  var now = new Date();
  var hour = now.getHours();

  // ① 判断即将开始的班次（按服务器小时判定）
  var nextShift = (hour >= DAY_START_HOUR && hour < NIGHT_START_HOUR) ? "DAY" : "NIGHT";

  // ② 读出本班产量（这是上次清零后由其它脚本/流程累加进来的）
  var output = num(DEVICE, "ShiftOutput", 0);
  var prevTag = read(DEVICE, "ShiftTag");

  log("=== 班次切换 " + now.toLocaleString() + " ===");
  log("上一班 = " + (prevTag || "未知") + "，产量 = " + output);
  log("即将开始 = " + nextShift);

  // ③ 归档：把本班产量写入"上一班产量"
  if (output > 0) {
    var okArchive = write(DEVICE, "LastShiftOutput", output);
    log(okArchive ? "✔ 已归档：" + output : "✘ 归档失败（检查写授权）");
  } else {
    log("本班产量为 0，跳过归档");
  }

  // ④ 清零当班计数器 + 打上班次标签
  //    注意：写操作有先后依赖，必须逐个检查返回值
  var okReset = write(DEVICE, "ShiftOutput", 0);
  if (!okReset) {
    log("✘ 清零失败！下一班会重复累计，请人工处理");
    return;                       // 清零失败就不再打标签，保留现场
  }
  log("✔ 当班产量已清零");

  var okTag = write(DEVICE, "ShiftTag", nextShift);
  log(okTag ? "✔ 班次标签已更新为 " + nextShift : "✘ 班次标签写入失败");

  // ⑤ 产出率参考（假设额定 8 小时产 4000 件）
  var rate = output / 4000 * 100;
  log("本班达成率 = " + rate.toFixed(1) + "%"
      + (rate < 80 ? " 【偏低，需关注】" : ""));

  // ⚠️ 关键提醒：
  // 如果把 var count = 0 写在顶层，每次执行都会重置为 0 —— 脚本是无状态的。
  // 一切需要跨执行保留的数据，都必须通过 write() 存进变量（并授予写授权）。
}
