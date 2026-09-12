/**
 * 示例 15 · 反模式对照（错误写法 vs 正确写法）
 *
 * 场景：把最常见的 10 个坑集中演示。
 * ⚠️ 本脚本中的错误写法仅作对照说明，请勿直接上线。
 *
 * 触发类型：Manual
 * 读授权：DEV01
 * 写授权：DEV01.SetPoint
 */

var DEVICE = "DEV01";

// ============ ❌ 错误 1：用 console.log ============
// console.log("hello");
// 后果：ReferenceError → 本次执行 Error → 连续 3 次熔断。

// ============ ✅ 正确 1：用 log() ============
// log("hello");


// ============ ❌ 错误 2：顶层写 write()，每次执行都真实下发 ============
// write(DEVICE, "SetPoint", 0);      // 危险！包括"试运行"之外每次执行都会写


// ============ ❌ 错误 3：顶层写 log()，输出被清空 ============
// log("脚本已加载");                  // 这行永远看不到


// ============ ❌ 错误 4：JS 变量跨执行累加 ============
// var counter = 0;
// function runBad() { counter++; log(counter); }   // 永远是 1

// ============ ✅ 正确 4：状态存进变量 ============
// counter 存在 DEV01.Counter 变量里，用 read/write 读写。


// ============ ❌ 错误 5：不判质量直接算 ============
function bad() {
  var v = read(DEVICE, "SetPoint");
  return v * 1.5;              // v 可能是 null → NaN，或者设备离线时的旧值
}

// ============ ✅ 正确 5：先判质量与类型 ============
function goodCalc() {
  if (getQuality(DEVICE, "SetPoint") !== "Good") return null;
  var v = read(DEVICE, "SetPoint");
  if (typeof v !== "number" || !isFinite(v)) return null;
  return v * 1.5;
}


// ============ ❌ 错误 6：用 Promise / async（静默失效）============
// Promise.resolve().then(function () { log("不会出现"); });
// async function runAsync() { await 1; log("不会出现"); }


// ============ ❌ 错误 7：无限递归 ============
// function f(n) { return f(n + 1); }      // 递归深度 >100 直接抛错


// ============ ❌ 错误 8：用 setTimeout 做延时 ============
// setTimeout(function () { log("不会执行"); }, 1000);   // 沙箱里没有 setTimeout
// ✅ 正确 8：延时需求用「周期触发 + 状态变量」实现，或拆成两个脚本


// ============ ❌ 错误 9：不检查 write 返回值 ============
function badWrite() {
  write(DEVICE, "SetPoint", 50);
  log("写入完成");            // 写没写成功根本不知道
}

// ============ ✅ 正确 9：检查返回值 ============
function goodWrite() {
  var ok = write(DEVICE, "SetPoint", 50);
  log(ok ? "✔ 写入成功" : "✘ 写入失败，查输出中的 [DENIED] / [WRITE-FAIL]");
  return ok;
}


// ============ ❌ 错误 10：把未限幅的计算值直接下发 ============
function badClamp() {
  var v = read(DEVICE, "SetPoint");
  write(DEVICE, "SetPoint", v * 10);       // 可能超出变量 Max，被服务端拒绝
}

// ============ ✅ 正确 10：先限幅 ============
function goodClamp() {
  var v = read(DEVICE, "SetPoint");
  if (typeof v !== "number") return false;
  var target = Math.min(100, Math.max(0, v * 10));     // 与变量 Min/Max 对齐
  return write(DEVICE, "SetPoint", target);
}


// ============ 演示：正确用法的组合 ============
function run() {
  log("=== 反模式对照演示 ===");

  log("1) 错误写法的计算结果 =", bad());          // 可能是 NaN 或基于坏值的结果
  log("1) 正确写法的计算结果 =", goodCalc());

  log("2) 未限幅写入："); badWrite();
  log("3) 检查返回值写入："); goodWrite();
  log("4) 限幅写入："); goodClamp();

  log("--- 提示 ---");
  log("本脚本列出了 10 类典型错误。上线前请对照 README 的「三条铁律」检查：");
  log("  ① 没有授权就不会有数据，且不报错（看 [DENIED]）");
  log("  ② 脚本里没有 console，只有 log()");
  log("  ③ 每次执行新建沙箱，脚本无状态，状态必须存变量");
}
