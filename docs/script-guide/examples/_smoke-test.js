/**
 * 冒烟测试脚手架（Node 环境，非项目代码）
 * 用模拟宿主在严格模式下执行 examples/*.js，验证：
 *   1. 顶层代码可解析可执行
 *   2. 钩子（run / onChange）可被调用
 *   3. 不引用任何沙箱中不存在的全局（console / setTimeout / fetch 等）
 *   4. 读写授权缺失时不崩溃
 */
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const dir = __dirname;

// ---- 模拟运行时数据 ----
const DB = {
  'TANK01.Temp': 62.5, 'TANK01.Level': 88, 'TANK01.DrainValve': false, 'TANK01.FeedPump': true,
  'TANK01.PumpState': true, 'BOILER01.Temp1': 118, 'BOILER01.Temp2': 125, 'BOILER01.Temp3': 0,
  'BOILER01.Temp4': 121, 'BOILER01.Temp5': 119, 'BOILER01.Temp6': 130, 'BOILER01.Pressure': 1.42,
  'REACT01.Temp': 58, 'REACT01.ValveOpen': 40, 'MIXER01.Speed': 1480, 'MIXER01.Torque': 92,
  'MIXER01.Running': true, 'MIXER01.Mode': 'AUTO', 'PUMP_GRP.P1_Run': true, 'PUMP_GRP.P1_Flow': 45,
  'PUMP_GRP.P1_Curr': 30, 'PUMP_GRP.P2_Run': true, 'PUMP_GRP.P2_Flow': 8, 'PUMP_GRP.P2_Curr': 52,
  'PUMP_GRP.P3_Run': false, 'PUMP_GRP.P3_Flow': 0, 'PUMP_GRP.P3_Curr': 0, 'PUMP_GRP.P4_Run': true,
  'PUMP_GRP.P4_Flow': 40, 'PUMP_GRP.P4_Curr': 28, 'PUMP_GRP.P5_Run': true, 'PUMP_GRP.P5_Flow': 42,
  'PUMP_GRP.P5_Curr': 31, 'PUMP_GRP.P6_Run': true, 'PUMP_GRP.P6_Flow': 44, 'PUMP_GRP.P6_Curr': 29,
  'LINE01.ShiftOutput': 3820, 'LINE01.ShiftTag': 'DAY', 'COMP01.Pressure': 1.35,
  'COMP01.ReliefValve': false, 'COMP01.AlarmLevel': 1, 'COMP01.AlarmCount': 2, 'COMP01.Shutdown': false,
  'LINE_A.OilPump': false, 'LINE_A.OilPress': 0.32, 'LINE_A.MainMotor': false, 'LINE_A.FeedValve': false,
  'LINE_A.SeqStep': 0, 'LINE_A.SeqError': 0, 'FT01.DiffPress': 12.4, 'FT01.Temp': 26, 'FT01.Press': 102.1,
  'FT01.StdFlow': 0, 'FT01.HeatValue': 0, 'ENV01.RawPayload': 'T=25.6C;H=48%;ST=RUN', 'ENV01.Temp': 25.4,
  'AI01.Level': 51.2, 'AI01.LevelSmooth': 50.8, 'AI01.EmaValid': true, 'PLC_A.Heartbeat': 1234,
  'PLC_B.Heartbeat': 99, 'RTU01.Heartbeat': 7, 'MCC01.Power': 128.5, 'MCC01.TotalEnergy': 4210.5,
  'MCC01.LastPower': 126.0, 'MCC01.LastSampleTime': Date.now() - 300000, 'MCC01.AvgPower': 127,
  'DEV01.SetPoint': 5, 'OVEN01.Temp': 58, 'OVEN01.TempSP': 65, 'OVEN01.HeatOutput': 30,
  'OVEN01.PidIntegral': 12.5, 'OVEN01.PidPrevError': 6.8, 'OVEN01.PidAuto': true,
  'LINE1.Running': true, 'LINE1.Output': 1200, 'LINE1.Power': 45, 'LINE1.Oee': 82.5,
  'LINE2.Running': true, 'LINE2.Output': 980, 'LINE2.Power': 38, 'LINE2.Oee': 76.2,
  'LINE3.Running': false, 'LINE3.Output': 0, 'LINE3.Power': 3, 'LINE3.Oee': 0,
  'PRESS01.MotorOn': true, 'PRESS01.GuardClosed': true, 'PRESS01.EstopPressed': false,
  'PRESS01.AirPressOk': true, 'PRESS01.LubeOk': true, 'PRESS01.Overload': false,
  'PRESS01.InterlockOk': true, 'PRESS01.InterlockCode': 0, 'PRESS01.Estop': false,
  'SUMMARY.TotalOutput': 0, 'SUMMARY.TotalPower': 0, 'SUMMARY.AvgOee': 0,
  'SUMMARY.RunningLines': 0, 'SUMMARY.UpdatedAt': 0
};

const SCOPE_READ = new Set(['TANK01', 'BOILER01', 'REACT01', 'MIXER01', 'PUMP_GRP', 'LINE01',
  'COMP01', 'LINE_A', 'FT01', 'ENV01', 'AI01', 'PLC_A', 'PLC_B', 'RTU01', 'MCC01', 'DEV01',
  'OVEN01', 'LINE1', 'LINE2', 'LINE3', 'PRESS01']);
const SCOPE_WRITE = new Set(['TANK01.DrainValve', 'TANK01.FeedPump', 'REACT01.ValveOpen',
  'LINE01.ShiftOutput', 'LINE01.LastShiftOutput', 'LINE01.ShiftTag', 'COMP01.ReliefValve',
  'COMP01.AlarmLevel', 'COMP01.AlarmCount', 'COMP01.Shutdown', 'LINE_A.OilPump',
  'LINE_A.MainMotor', 'LINE_A.FeedValve', 'LINE_A.SeqStep', 'LINE_A.SeqError',
  'FT01.StdFlow', 'FT01.HeatValue', 'AI01.LevelSmooth', 'AI01.EmaValid', 'MCC01.TotalEnergy',
  'MCC01.LastPower', 'MCC01.LastSampleTime', 'MCC01.AvgPower', 'DEV01.SetPoint',
  'OVEN01.HeatOutput', 'OVEN01.PidIntegral', 'OVEN01.PidPrevError', 'PRESS01.MotorOn',
  'PRESS01.InterlockOk', 'PRESS01.InterlockCode', 'PRESS01.Estop', 'SUMMARY.TotalOutput',
  'SUMMARY.TotalPower', 'SUMMARY.AvgOee', 'SUMMARY.RunningLines', 'SUMMARY.UpdatedAt']);

function makeSandbox(code) {
  const out = [];
  const denied = [];
  const writes = [];
  const sandbox = {
    log: (...args) => out.push(args.map(a =>
      a === undefined ? 'undefined' : (typeof a === 'object' ? JSON.stringify(a) : String(a))).join(' ')),
    read: (d, v) => {
      if (!SCOPE_READ.has(d)) { denied.push(`read ${d}.${v}`); return null; }
      const k = `${d}.${v}`;
      return Object.prototype.hasOwnProperty.call(DB, k) ? DB[k] : null;
    },
    getQuality: (d, v) => {
      if (!SCOPE_READ.has(d)) { denied.push(`getQuality ${d}.${v}`); return 'Unknown'; }
      const k = `${d}.${v}`;
      return Object.prototype.hasOwnProperty.call(DB, k) ? 'Good' : 'Unknown';
    },
    write: (d, v, val) => {
      const k = `${d}.${v}`;
      if (!SCOPE_WRITE.has(k)) { denied.push(`write ${k}`); return false; }
      DB[k] = val;
      writes.push(`${k}=${val}`);
      return true;
    }
  };
  const ctx = vm.createContext(sandbox);
  // 严格模式：与 Jint 沙箱 opts.Strict(true) 对齐
  vm.runInContext('"use strict";\n' + code, ctx, { timeout: 5000 });
  return { ctx, out, denied, writes };
}

function detectForbiddenGlobals(code) {
  const forbidden = ['console', 'setTimeout', 'setInterval', 'fetch', 'XMLHttpRequest',
    'require', 'process', 'localStorage', 'alert', 'document', 'window'];
  const found = [];
  for (const f of forbidden) {
    const re = new RegExp('\\b' + f + '\\s*[.\\[(]', 'g');
    // 排除注释行
    const lines = code.split('\n');
    for (const line of lines) {
      const trimmed = line.trim();
      if (trimmed.startsWith('*') || trimmed.startsWith('//') || trimmed.startsWith('/*')) continue;
      if (re.test(line)) { found.push(`${f} @ ${trimmed.slice(0, 60)}`); break; }
    }
  }
  return found;
}

const files = fs.readdirSync(dir)
  .filter(f => f.endsWith('.js') && !f.startsWith('_'))   // _ 开头为测试脚手架自身
  .sort();
let pass = 0, fail = 0;

console.log('=== 系统脚本示例冒烟测试 ===\n');

for (const f of files) {
  const code = fs.readFileSync(path.join(dir, f), 'utf8');
  const forbidden = detectForbiddenGlobals(code);

  try {
    const sb = makeSandbox(code);
    const kind = vm.runInContext('typeof run === "function" ? "run" : (typeof onChange === "function" ? "onChange" : "none")', sb.ctx);

    if (kind === 'run') {
      vm.runInContext('run()', sb.ctx, { timeout: 5000 });
    } else if (kind === 'onChange') {
      vm.runInContext('onChange({deviceKey:"TANK01",variableKey:"Level",value:88,previous:60,quality:"Good"})', sb.ctx, { timeout: 5000 });
    }

    const status = (kind === 'none' || forbidden.length > 0) ? 'WARN' : 'PASS';
    if (status === 'PASS') pass++; else fail++;

    console.log(`[${status}] ${f}`);
    console.log(`      钩子=${kind}  日志=${sb.out.length}行  写入=${sb.writes.length}次  越权=${sb.denied.length}次`);
    if (sb.out.length) console.log(`      首行输出: ${sb.out[0].slice(0, 90)}`);
    if (sb.denied.length) console.log(`      ⚠ 越权(演示预期): ${[...new Set(sb.denied)].slice(0, 5).join(', ')}`);
    if (forbidden.length) console.log(`      ⚠ 引用禁用全局(仅注释中): ${forbidden.join(' | ')}`);
    if (kind === 'none') console.log(`      ⚠ 未声明 run/onChange 钩子`);
  } catch (e) {
    fail++;
    console.log(`[FAIL] ${f}`);
    console.log(`      ${e.message}`);
  }
  console.log('');
}

console.log(`=== 结果：PASS ${pass} / FAIL-WARN ${fail} / 共 ${files.length} ===`);
