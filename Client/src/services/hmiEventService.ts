import { watch, type Ref } from 'vue';
import {
  HMIComponent,
  HmiEventAction,
  HmiEventConfig,
  HmiEventCondition,
  HmiEventConditionOp,
  HmiEventType,
  HmiEventWriteMode,
} from '../types';
import { activeAlarms } from '../store/alarmStore';

/**
 * 组态组件事件系统 —— 运行态分发器（纯函数，无 UI 依赖）。
 *
 * 设计约定：
 * - 事件配置存于 HmiComponent.props.events，随 PropsJson 落库，后端零改动；
 * - 运行时优先消费 events 配置；组件未配置事件时由调用方回退既有 buttonMode 逻辑（共存策略）；
 * - writeVar / runScript 动作沿用既有权限边界（canControlWrite + 后端 [Authorize(Roles)] 兜底）；
 * - setProp 动作仅改运行态渲染数据，不落库（如「点击按钮弹出报警面板」）。
 */

/** 分发上下文：由宿主视图（播放器/编辑器预览）提供具体执行能力 */
export interface HmiEventDispatchContext {
  /** 是否有写指令权限（Operator/Admin）；writeVar / runScript 动作前置拦截 */
  canControlWrite: boolean;
  /** 写变量回调（复用宿主既有写指令链，含只读/未绑定拦截） */
  writeVariable: (deviceId: number | null, variableKey: string, writeMode: HmiEventWriteMode, value?: number) => void;
  /** 页面跳转回调（同端切换） */
  navigateToPage: (pageId: string) => void;
  /** 触发系统脚本回调 */
  runScript: (scriptId: number) => void;
  /** 运行态组件补丁（不落库）：componentId + 顶层字段/props 补丁 */
  applyRuntimePatch: (componentId: string, patch: { visible?: boolean; label?: string; props?: Record<string, any> }) => void;
  /** 动作被拦截/跳过时的提示回调（可选） */
  onBlocked?: (message: string) => void;
}

/** 读取组件事件配置（容错：旧组件无 events 字段返回空数组） */
export const getComponentEvents = (component: HMIComponent | null | undefined): HmiEventConfig[] =>
  (component?.props?.events as HmiEventConfig[] | undefined) ?? [];

/** 判断组件是否配置了指定事件（存在且启用且含可用动作） */
export const hasComponentEvent = (component: HMIComponent | null | undefined, eventType: HmiEventType): boolean =>
  getComponentEvents(component).some(
    (e) => e && e.type === eventType && e.enabled !== false && (e.actions ?? []).some((a) => a && a.enabled !== false)
  );

/** 值变化条件求值 */
export const evaluateCondition = (
  value: number | boolean | undefined,
  op: HmiEventConditionOp,
  operand: number
): boolean => {
  if (typeof value === 'boolean') return evaluateCondition(value ? 1 : 0, op, operand);
  if (typeof value !== 'number' || !Number.isFinite(value)) return false;
  switch (op) {
    case '>': return value > operand;
    case '<': return value < operand;
    case '=': return value === operand;
    case '>=': return value >= operand;
    case '<=': return value <= operand;
    case '!=': return value !== operand;
    default: return false;
  }
};

/** 执行单个动作（不校验事件启用状态，由调用方保证） */
const runAction = (
  action: HmiEventAction,
  component: HMIComponent,
  ctx: HmiEventDispatchContext
) => {
  const p = action.params ?? {};
  switch (action.kind) {
    case 'writeVar': {
      // 写权限拦截：与既有 buttonMode 行为一致，非授权角色仅提示
      if (!ctx.canControlWrite) {
        ctx.onBlocked?.('当前角色无写指令权限，事件动作已拦截');
        return;
      }
      const devId = p.deviceId != null ? p.deviceId : component.bindDeviceId ?? null;
      const varKey = p.variableKey || component.bindVariableKey || '';
      const mode = p.writeMode ?? 'toggle';
      ctx.writeVariable(devId, varKey, mode, p.value);
      return;
    }
    case 'navigate': {
      if (p.targetPageId) ctx.navigateToPage(p.targetPageId);
      return;
    }
    case 'runScript': {
      if (!ctx.canControlWrite) {
        ctx.onBlocked?.('当前角色无脚本执行权限，事件动作已拦截');
        return;
      }
      const scriptId = Number(p.scriptId);
      if (scriptId) ctx.runScript(scriptId);
      return;
    }
    case 'setProp': {
      // 目标组件（空=自身）；补丁仅改运行态渲染数据
      const targetId = p.targetComponentId || component.id;
      if (p.patch) ctx.applyRuntimePatch(targetId, p.patch);
      return;
    }
    default:
      return;
  }
};

/**
 * 统一事件分发入口。
 * @param component 触发事件的组件
 * @param eventType 事件类型
 * @param ctx 分发上下文
 * @param payload 附加值（valueChange 携带当前值用于条件判断）
 * @returns 是否执行了至少一个动作（false=未配置/未启用/条件不满足，调用方据此回退旧逻辑）
 */
export const dispatchComponentEvent = (
  component: HMIComponent | null | undefined,
  eventType: HmiEventType,
  ctx: HmiEventDispatchContext,
  payload?: { value?: number | boolean }
): boolean => {
  if (!component) return false;
  const events = getComponentEvents(component).filter((e) => e && e.type === eventType && e.enabled !== false);
  if (!events.length) return false;

  let executed = false;
  for (const evt of events) {
    // valueChange 条件过滤：配置了条件且当前值不满足 → 跳过该事件
    const cond: HmiEventCondition | null | undefined = evt.condition;
    if (eventType === 'valueChange' && cond) {
      if (!evaluateCondition(payload?.value, cond.op, cond.operand)) continue;
    }
    (evt.actions ?? []).forEach((action) => {
      if (!action || action.enabled === false) return;
      runAction(action, component, ctx);
      executed = true;
    });
  }
  return executed;
};

// ===== 数据类事件（valueChange / alarm）监听 composable =====
// 供运行端（ScadaPlayerView）与编辑器预览（ScadaTopologyView isActiveMode）共用。

export interface UseHmiDataEventsOptions {
  /** 当前页面组件（响应式） */
  components: Ref<HMIComponent[]> | (() => HMIComponent[]);
  /** 组件实时值（compId → value），与画布渲染同源 */
  componentValues: Ref<Record<string, number | boolean>>;
  /** 事件分发上下文（可为 getter，使 canControlWrite 等保持响应） */
  ctx: HmiEventDispatchContext | (() => HmiEventDispatchContext);
  /** 是否启用（如仅预览/运行态启用；编辑态禁用） */
  enabled?: Ref<boolean> | (() => boolean);
}

/**
 * 监听组件值变化与报警状态，匹配并分发 valueChange / alarm 事件。
 * - valueChange：值发生实际变化（首帧赋值不触发）且满足条件时执行动作链；
 * - alarm：绑定变量进入 activeAlarms（未恢复）时触发一次，报警恢复后允许再次触发。
 */
export const useHmiDataEvents = (options: UseHmiDataEventsOptions) => {
  const resolve = <T,>(r: Ref<T> | (() => T)): T =>
    typeof r === 'function' ? (r as () => T)() : r.value;
  const resolveCtx = (): HmiEventDispatchContext =>
    typeof options.ctx === 'function' ? options.ctx() : options.ctx;
  const isEnabled = () => (options.enabled ? resolve(options.enabled) : true);

  // ---- valueChange ----
  const prevValues = new Map<string, number | boolean>();
  watch(
    () => resolve(options.componentValues),
    (vals) => {
      if (!isEnabled() || !vals) return;
      const comps = resolve(options.components) ?? [];
      comps.forEach((c) => {
        if (!hasComponentEvent(c, 'valueChange')) return;
        const v = vals[c.id];
        if (v === undefined) return;
        const prev = prevValues.get(c.id);
        prevValues.set(c.id, v);
        // 首次见到该值（prev undefined）不触发，避免订阅建立瞬间误触发
        if (prev === undefined || v === prev) return;
        dispatchComponentEvent(c, 'valueChange', resolveCtx(), { value: v });
      });
    },
    { immediate: true }
  );

  // ---- alarm ----
  // 已触发记录键：`${compId}:${deviceId}:${variableKey}`，报警恢复后移除以便再次触发
  const firedAlarmKeys = new Set<string>();
  watch(
    activeAlarms,
    (alarms) => {
      if (!isEnabled() || !alarms) return;
      const activeKeys = new Set(
        alarms.filter((a) => !a.recoveredAt).map((a) => `${a.deviceId}:${a.variableKey}`)
      );
      const comps = resolve(options.components) ?? [];
      comps.forEach((c) => {
        if (!hasComponentEvent(c, 'alarm')) return;
        if (c.bindDeviceId == null || !c.bindVariableKey) return;
        const key = `${c.id}:${c.bindDeviceId}:${c.bindVariableKey}`;
        const matching = activeKeys.has(`${c.bindDeviceId}:${c.bindVariableKey}`);
        if (matching && !firedAlarmKeys.has(key)) {
          firedAlarmKeys.add(key);
          dispatchComponentEvent(c, 'alarm', resolveCtx());
        } else if (!matching && firedAlarmKeys.has(key)) {
          firedAlarmKeys.delete(key);
        }
      });
    },
    { immediate: true, deep: true }
  );
};
