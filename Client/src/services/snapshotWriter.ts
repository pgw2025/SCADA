/**
 * 离线快照节流写入器（doc/pwa 阶段六 · 步骤 27，D9）。
 *
 * - 「标脏不落盘」：SignalR 回调 / 设备列表加载仅调用 markXxx（内存缓冲）；
 * - 定时器（默认 10s）一拍批量 flush + visibilitychange(hidden)/pagehide 强制 flush
 *   （防移动端切后台被冻结丢数据）；
 * - 会话 epoch：flush 前校验当前登录用户未变（变了跳过写入并自清理），
 *   防止旧页面实例（多标签页）把上一账号的数据写进新账号快照；
 * - 写入失败：console.warn + 停止本会话快照（静默降级，不影响主流程）。
 */

import { appendAlarms, putDevices, touchMeta } from './snapshotDB';
import { normalizeAlarmEvent } from '../store/alarmStore';
import { devices } from '../store/deviceStore';
import { loginUser } from '../store/userStore';
import type { AlarmEventPayload } from '../types';

/** 节流间隔（ms）——计划默认 10s，常量集中定义（暂不做 UI 配置面） */
const FLUSH_INTERVAL_MS = 10_000;

let timer: ReturnType<typeof setInterval> | null = null;
let started = false;
/** 会话 epoch：start 时的登录用户；flush 时比对，变了则放弃写入 */
let sessionUserId: string | null = null;
let devicesDirty = false;
let alarmBuffer: AlarmEventPayload[] = [];
/** 写入失败后置位：本会话不再尝试落盘 */
let disabled = false;

/** 设备快照标脏（变量更新/设备状态变更/设备列表加载共用） */
export function markDevicesDirty(): void {
  if (!started || disabled) return;
  devicesDirty = true;
}

/** 报警事件标脏（缓冲 payload，flush 时统一归一化落盘） */
export function markAlarmEvent(payload: AlarmEventPayload): void {
  if (!started || disabled) return;
  if (!payload || payload.deviceId == null || !payload.variableKey) return;
  alarmBuffer.push(payload);
  if (alarmBuffer.length > 200) alarmBuffer = alarmBuffer.slice(-200);
}

/** 登录成功后启动（authApi.performLogin 调用） */
export function start(userId: string): void {
  if (typeof window === 'undefined') return;
  sessionUserId = userId || null;
  disabled = false;
  alarmBuffer = [];
  devicesDirty = false;
  if (started) return;
  started = true;
  timer = setInterval(() => void flush(), FLUSH_INTERVAL_MS);
  // 切后台强制落盘（visibilitychange 用 pagehide 兜底 iOS）
  document.addEventListener('visibilitychange', onVisibility);
  window.addEventListener('pagehide', onPageHide);
}

/** 登出/401 停止（pwaSecurity.clearUserData 主流程先停再清库） */
export function stop(): void {
  if (!started) return;
  started = false;
  if (timer) {
    clearInterval(timer);
    timer = null;
  }
  document.removeEventListener('visibilitychange', onVisibility);
  window.removeEventListener('pagehide', onPageHide);
  devicesDirty = false;
  alarmBuffer = [];
  sessionUserId = null;
}

function onVisibility(): void {
  if (document.visibilityState === 'hidden') void flush();
}

function onPageHide(): void {
  void flush();
}

/** 批量落盘：脏 store 写入 + meta 时间戳更新 */
export async function flush(): Promise<void> {
  if (!started || disabled) return;
  const uid = loginUser.value?.username ?? null;
  // 会话 epoch 校验：登录用户已变（登出/换号进行中）→ 放弃本批写入并自清理
  if (!uid || uid !== sessionUserId) {
    devicesDirty = false;
    alarmBuffer = [];
    return;
  }
  if (!devicesDirty && alarmBuffer.length === 0) return;

  try {
    if (devicesDirty) {
      devicesDirty = false;
      // 设备对象全量落盘（内嵌 variables/variableTimestamps/variableMeta，即变量最新值）
      await putDevices(uid, JSON.parse(JSON.stringify(devices.value)));
    }
    if (alarmBuffer.length > 0) {
      const records = alarmBuffer.map((p) => normalizeAlarmEvent(p));
      alarmBuffer = [];
      await appendAlarms(uid, records);
    }
    await touchMeta(uid, new Date().toISOString());
  } catch (e) {
    // 静默降级：停止本会话快照（含 quota 超限等不可恢复错误）
    console.warn('[PWA] 快照落盘失败，本会话停用快照', e);
    disabled = true;
  }
}
