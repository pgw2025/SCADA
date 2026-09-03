/**
 * 阶段 0 基线工具：SignalR 推送观察器
 * 连接 ScadaHub，订阅设备分组，打印 ReceiveDeviceStatus / ReceiveVariableUpdate / ReceiveAlarm。
 * 用法：node signalr_observer.js [durationSec] [deviceId...]
 *   durationSec 默认 15；deviceId 默认 1（VIRTUAL 设备）。订阅设备 2 可观察 S7 断线态。
 */
const { HubConnectionBuilder, LogLevel } = require('d:/CSharp/SCADA/Client/node_modules/@microsoft/signalr');

const BASE = 'http://localhost:5555';
const duration = parseInt(process.argv[2] || '15', 10);
const deviceIds = (process.argv.slice(3).length ? process.argv.slice(3) : ['1']).map(Number);

const ts = () => new Date().toISOString().slice(11, 23);

async function login() {
  const res = await fetch(`${BASE}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: '123456' }),
  });
  const j = await res.json();
  if (!j.success) throw new Error('login failed: ' + JSON.stringify(j));
  return j.token;
}

async function main() {
  const token = await login();
  console.log(`[${ts()}] [obs] login ok, connecting to ${BASE}/hubs/scada ...`);

  const conn = new HubConnectionBuilder()
    .withUrl(`${BASE}/hubs/scada`, { accessTokenFactory: () => token })
    .configureLogging(LogLevel.Warning)
    .withAutomaticReconnect()
    .build();

  conn.on('ReceiveDeviceStatus', (deviceId, status) => {
    console.log(`[${ts()}] [obs] ReceiveDeviceStatus deviceId=${deviceId} status=${status}`);
  });
  conn.on('ReceiveVariableUpdate', (p) => {
    console.log(`[${ts()}] [obs] ReceiveVariableUpdate deviceId=${p.deviceId} variableKey=${p.variableKey} value=${JSON.stringify(p.value)} quality=${p.quality} updateTime=${p.updateTime}`);
  });
  conn.on('ReceiveAlarm', (p) => {
    console.log(`[${ts()}] [obs] ReceiveAlarm deviceId=${p.deviceId} variableKey=${p.variableKey} eventType=${p.eventType} level=${p.level} actualValue=${p.actualValue} source=${p.source} message=${p.message}`);
  });
  conn.onclose((e) => console.log(`[${ts()}] [obs] connection closed: ${e && e.message}`));

  await conn.start();
  console.log(`[${ts()}] [obs] connected. subscribing devices: ${deviceIds.join(',')}`);
  for (const id of deviceIds) {
    try { await conn.invoke('SubscribeDevice', id); console.log(`[${ts()}] [obs] subscribed device ${id}`); }
    catch (e) { console.log(`[${ts()}] [obs] subscribe device ${id} failed: ${e.message}`); }
  }

  await new Promise(r => setTimeout(r, duration * 1000));
  console.log(`[${ts()}] [obs] done, stopping.`);
  await conn.stop();
}

main().catch(e => { console.error('FATAL: ' + e.message); process.exit(1); });
