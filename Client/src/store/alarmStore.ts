import { ref, computed } from 'vue';
import {
  AlarmEventPayload,
  AlarmRecord,
  AlarmLevel,
  TriggerCondition,
  AlarmSource
} from '../types';
import { fetchActiveAlarms } from '../api/alarmApi';

/**
 * 报警实时状态存储（全局单例）。
 * - 由 SignalR "ReceiveAlarm" 事件实时更新（normalizeAlarmEvent 归一化枚举）；
 * - 由页面/初始化主动拉取"当前未恢复"记录校准（refreshActiveAlarms）；
 * - 未确认数量 / 当前报警数量用于导航角标与页面汇总。
 */
export const activeAlarms = ref<AlarmRecord[]>([]);
export const recentEvents = ref<AlarmRecord[]>([]);
export const alarmsLoaded = ref(false);

/** 当前未确认报警数（触发事件递增、确认/恢复后按需由页面刷新校准） */
export const unackedCount = computed(() => activeAlarms.value.filter(a => !a.acked).length);

/** 枚举数字 → 字符串映射（SignalR 默认把枚举序列化为数字） */
const LEVEL_MAP: Record<string, AlarmLevel> = { 0: 'Low', 1: 'Medium', 2: 'High', 3: 'Critical' };
const CONDITION_MAP: Record<string, TriggerCondition> = {
  0: 'GreaterThan', 1: 'GreaterOrEqual', 2: 'LessThan', 3: 'LessOrEqual', 4: 'EqualTo', 5: 'NotEqualTo'
};
const SOURCE_MAP: Record<string, AlarmSource> = { 0: 'Rule', 1: 'MinMaxLimit', 2: 'System' };

const asLevel = (v: string | number | null | undefined): AlarmLevel =>
  (v == null ? '' : String(v)) in LEVEL_MAP ? LEVEL_MAP[String(v)] : (String(v ?? '') as AlarmLevel);

const asCondition = (v: string | number | null | undefined): TriggerCondition | null =>
  ((v == null ? '' : String(v)) in CONDITION_MAP) ? CONDITION_MAP[String(v)] : (v == null ? null : String(v) as TriggerCondition);

const asSource = (v: string | number | null | undefined): AlarmSource =>
  (v == null ? '' : String(v)) in SOURCE_MAP ? SOURCE_MAP[String(v)] : (String(v ?? 'Rule') as AlarmSource);

const fmtTime = (ts?: string | null): string => {
  const d = ts ? new Date(ts) : new Date();
  // 非法时间兜底为当前时间
  const t = isNaN(d.getTime()) ? new Date() : d;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${t.getFullYear()}-${pad(t.getMonth() + 1)}-${pad(t.getDate())} ${pad(t.getHours())}:${pad(t.getMinutes())}:${pad(t.getSeconds())}`;
};

/** 是否"触发"事件（Triggered=0） */
const isTrigger = (p: AlarmEventPayload): boolean => {
  const t = String(p.eventType ?? '');
  return t === 'Triggered' || t === '0';
};

/** 归一化 SignalR 载荷为可展示的 AlarmRecord */
export const normalizeAlarmEvent = (p: AlarmEventPayload): AlarmRecord => ({
  id: -Date.now() - Math.floor(Math.random() * 100000), // 本地临时 id
  deviceId: p.deviceId,
  deviceKey: p.deviceKey ?? '',
  variableKey: p.variableKey ?? '',
  variableName: p.variableName ?? p.variableKey ?? '',
  ruleId: p.ruleId ?? null,
  ruleName: p.ruleName ?? null,
  level: asLevel(p.level),
  condition: asCondition(p.condition),
  threshold: p.threshold ?? null,
  actualValue: p.actualValue ?? null,
  message: p.message ?? '',
  source: asSource(p.source),
  triggeredAt: p.triggeredAt ?? fmtTime(p.triggeredAt),
  recoveredAt: p.triggeredAt ? (isTrigger(p) ? null : p.triggeredAt) : null,
  recoveryValue: isTrigger(p) ? null : p.actualValue ?? null,
  acked: false,
  ackedAt: null,
  ackedBy: null
});

/**
 * 处理来自 SignalR 的实时报警事件：
 * - 触发事件：插入"当前报警"顶部并计入未确认角标；
 * - 恢复事件：从"当前报警"移除对应记录。
 */
export const pushAlarmEvent = (payload: AlarmEventPayload) => {
  if (!payload || payload.deviceId == null || !payload.variableKey) return;
  const rec = normalizeAlarmEvent(payload);

  // 最近事件只保留若干个，用于 Toast / 角标滚动展示
  recentEvents.value.unshift(rec);
  if (recentEvents.value.length > 50) recentEvents.value.length = 50;

  const keyOf = (r: AlarmRecord) => `${r.deviceId}:${r.variableKey}:${r.ruleId ?? ''}`;

  if (isTrigger(payload)) {
    // 避免同键重复插入（可能存在恢复后再次触发）
    const idx = activeAlarms.value.findIndex(r => keyOf(r) === keyOf(rec));
    if (idx >= 0) activeAlarms.value.splice(idx, 1);
    activeAlarms.value.unshift(rec);
  } else {
    // 恢复事件：定位未恢复记录并打上恢复信息
    const idx = activeAlarms.value.findIndex(r =>
      keyOf(r) === keyOf(rec) && !r.recoveredAt
    );
    if (idx >= 0) {
      activeAlarms.value[idx].recoveredAt = rec.triggeredAt;
      activeAlarms.value[idx].recoveryValue = rec.actualValue;
    }
  }

  const keep = activeAlarms.value.filter(r => !r.recoveredAt);
  if (keep.length !== activeAlarms.value.length) {
    activeAlarms.value = keep;
  }
};

/** 从后端拉取当前未恢复报警记录，校准实时状态 */
export const refreshActiveAlarms = async (): Promise<void> => {
  try {
    const { data } = await fetchActiveAlarms();
    activeAlarms.value = Array.isArray(data) ? data : [];
    alarmsLoaded.value = true;
  } catch {
    // 拉取失败保持现状，SignalR 仍可实时更新
  }
};

/** 清空实时事件缓冲（页面切换等场景） */
export const clearRecentEvents = () => {
  recentEvents.value = [];
};