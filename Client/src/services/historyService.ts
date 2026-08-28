import { ref, computed } from 'vue';
import { HMIComponent } from '../types';

/**
 * 阶段5-1 撤销/重做命令栈（纯前端快照，不影响后端持久化）。
 *
 * - 离散操作（add / delete / duplicate / clear）：直接以「变更前快照」入栈。
 * - 连续编辑（拖拽移动、连续属性输入）：通过 beginChange/endChange 归并为单条历史，
 *   同一组件 700ms 内的多次更新视为一段，停止编辑 700ms 后提交 before 快照。
 * - 撤销/重做仅恢复前端内存中的 components 数组（方案明确「纯前端」），
 *   不触发 persist，因此刷新后可能回到后端状态——属会话级撤销的固有语义。
 */

const MAX = 50;
const past = ref<HMIComponent[][]>([]);
const future = ref<HMIComponent[][]>([]);

// 连续编辑段（拖拽 / 连续属性输入）的 before 快照与节流
let _pending: HMIComponent[] | null = null;
let _lastId = '';
let _lastStamp = 0;
let _timer: ReturnType<typeof setTimeout> | null = null;

const clone = (comps: HMIComponent[]): HMIComponent[] =>
  comps.map((c) => ({ ...c, props: { ...(c.props || {}) } }));

function commit(before: HMIComponent[]) {
  past.value.push(before);
  if (past.value.length > MAX) past.value.shift();
  future.value = [];
}

function flushPending() {
  if (_pending) {
    commit(_pending);
    _pending = null;
  }
  if (_timer) {
    clearTimeout(_timer);
    _timer = null;
  }
}

/** 变更前调用：开始/延续一个连续编辑段（同组件、700ms 内连续触发将合并为一条） */
export function beginChange(comps: HMIComponent[], id?: string) {
  const now = Date.now();
  const cont = _pending && _lastId === (id ?? _lastId) && now - _lastStamp < 700;
  if (cont) {
    _lastStamp = now;
    return;
  }
  flushPending();
  _pending = clone(comps);
  _lastId = id ?? '';
  _lastStamp = now;
}

/** 变更后调用：停止编辑 700ms 后提交本段 before 快照入栈 */
export function endChange() {
  if (!_pending) return;
  if (_timer) clearTimeout(_timer);
  _timer = setTimeout(() => {
    flushPending();
  }, 700);
}

/** 离散操作（add/delete/duplicate/clear）：直接以变更前快照入栈 */
export function recordDiscrete(before: HMIComponent[]) {
  flushPending();
  commit(clone(before));
}

/** 撤销：返回要恢复的组件快照（当前状态作为反向快照压入 future） */
export function undo(current: HMIComponent[]): HMIComponent[] | null {
  flushPending();
  if (!past.value.length) return null;
  future.value.unshift(clone(current));
  return clone(past.value.pop()!);
}

/** 重做：返回要恢复的前向快照 */
export function redo(current: HMIComponent[]): HMIComponent[] | null {
  flushPending();
  if (!future.value.length) return null;
  past.value.push(clone(current));
  return clone(future.value.shift()!);
}

/**
 * 撤销对账：切换工程/页面时清空命令栈。
 * 命令栈是全局单栈，若不清空，会把上一页的组件快照撤销到当前页（跨页污染）。
 * 切换文档（页面）后历史即失效，故直接重置为干净状态。
 */
export function resetHistory() {
  flushPending();
  past.value = [];
  future.value = [];
  _pending = null;
  _lastId = '';
  _lastStamp = 0;
}

export const undoAvailable = computed(() => past.value.length > 0);
export const redoAvailable = computed(() => future.value.length > 0);
