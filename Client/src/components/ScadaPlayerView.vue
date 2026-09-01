<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue';
import { useRoute } from 'vue-router';
import {
  currentProject,
  desktopPages,
  mobilePages,
  selectProject,
} from '../store/scadaStore';
import { devices } from '../store/deviceStore';
import { loginUser } from '../store/userStore';
import { ROLE_OPERATOR, ROLE_ADMIN } from '../constants/roles';
import { addLog } from '../store';
import { getDeviceVariableValue, setDeviceVariableValue } from '../services/dataOrchestration';
import { subscribeDeviceTelemetry, unsubscribeDeviceTelemetry } from '../services/signalRService';
import { pushTrendPoint, clearTrendHistory, trendHistory } from '../utils/trendHistory';
import { isSamePageRef } from '../utils/pageId';
import { showToast } from '../services/toastService';
import { triggerRuntimeScript } from '../services/scriptService';
import { dispatchComponentEvent, useHmiDataEvents, HmiEventDispatchContext } from '../services/hmiEventService';
import { refreshActiveAlarms } from '../store/alarmStore';
import { HMIComponent, HmiEventType } from '../types';
import { RefreshCw } from 'lucide-vue-next';
import CanvasPanel from './CanvasPanel.vue';
import SetValueDialog from './SetValueDialog.vue';

const route = useRoute();

// 端自动检测：按访问设备判定默认端；纯播放器去掉手动切换，检测失败时自动 fallback 到另一端。
const detectMobile = (): boolean =>
  window.innerWidth < 768 || /Mobi|Android|iPhone|iPad|iPod/i.test(navigator.userAgent);

const primaryPlatform = ref<'Desktop' | 'Mobile'>(detectMobile() ? 'Mobile' : 'Desktop');

// 挂载时从路由参数懒加载具体工程（方案B：/scada-view/:projectId）
const loadingProject = ref(true);
const projectError = ref(false);

// 当前端选中画面 ID：必须在 loadFromParam 之前初始化。
const selectedRuntimePageId = ref<string>('');

const loadFromParam = async () => {
  loadingProject.value = true;
  projectError.value = false;
  selectedRuntimePageId.value = '';
  const proj = await selectProject(route.params.projectId as string);
  loadingProject.value = false;
  if (!proj) projectError.value = true;
};
watch(() => route.params.projectId, loadFromParam, { immediate: true });

// 端 fallback：当前端无画面且另一端有画面时自动切换过去，避免纯播放器无法切换而空态。
// 两端都无画面则保持当前端，落入空态分支。
const runtimePlatform = computed<'Desktop' | 'Mobile'>(() => {
  const primary = primaryPlatform.value;
  const ownPages = primary === 'Mobile' ? mobilePages.value : desktopPages.value;
  if (ownPages.length > 0) return primary;
  const alt = primary === 'Mobile' ? desktopPages.value : mobilePages.value;
  return alt.length > 0 ? (primary === 'Mobile' ? 'Desktop' : 'Mobile') : primary;
});

// 当前端可见画面列表（同端）
const runtimePages = computed(() =>
  runtimePlatform.value === 'Mobile' ? mobilePages.value : desktopPages.value
);

// 默认落地页：所在端首页（isHome）优先，否则取第一个
watch(
  runtimePages,
  (pages) => {
    if (selectedRuntimePageId.value && pages.some((p) => p.id === selectedRuntimePageId.value)) return;
    if (!pages.length) {
      selectedRuntimePageId.value = '';
      return;
    }
    const home = pages.find((p) => p.isHome) || pages[0];
    selectedRuntimePageId.value = home.id;
  },
  { immediate: true }
);

const currentPage = computed(
  () => runtimePages.value.find((p) => p.id === selectedRuntimePageId.value) || runtimePages.value[0] || null
);

const pageWidth = computed(() => currentPage.value?.width ?? (runtimePlatform.value === 'Mobile' ? 375 : 1100));
const pageHeight = computed(() => currentPage.value?.height ?? (runtimePlatform.value === 'Mobile' ? 812 : 700));

// 设备级 SignalR 订阅：运行态实时值解析只消费当前页面已绑定组件的设备，
// 按绑定设备集合订阅/退订（变量更新仅推送已订阅设备分组，替代全连接广播）。
const boundDeviceIds = computed(() => {
  const ids = new Set<number>();
  (currentPage.value?.components ?? []).forEach((c: any) => {
    if (c.bindDeviceId != null) ids.add(Number(c.bindDeviceId));
    // 圆角按钮「操作变量」独立绑定的设备也纳入订阅：取反模式需读取操作变量当前值
    if (c.type === 'rounded-btn' && c.props?.opDeviceId != null) ids.add(Number(c.props.opDeviceId));
    // multi-var-dashboard 多变量看板：每个子项可独立绑定设备，需纳入订阅，
    // 否则服务端不会推送这些设备的 ReceiveVariableUpdate（看板直接读 devices store 会陈旧）。
    // 注意：HMIWidget.dashboardResolvedItems 在「子项无 deviceId 且组件无 bindDeviceId」时
    // 会回退读 devices.value[0]（首台设备）。此分支同样需把首台设备纳入订阅，
    // 否则看板仍收不到推送而陈旧（首台设备通常未被其它控件订阅）。
    if (c.type === 'multi-var-dashboard' && Array.isArray(c.props?.dashboardItems)) {
      let hasUnboundItem = false;
      for (const item of c.props.dashboardItems) {
        const devId = item?.deviceId != null ? item.deviceId : c.bindDeviceId;
        if (devId != null) ids.add(Number(devId));
        else hasUnboundItem = true;
      }
      // 回退路径：订阅首台设备，使其 ReceiveVariableUpdate 能到达客户端。
      // 此处读取 devices.value 会建立依赖，设备加载/重排后 watch 自动重新对账订阅。
      if (hasUnboundItem && devices.value.length > 0) ids.add(Number(devices.value[0].id));
    }
  });
  return ids;
});

watch(boundDeviceIds, (newIds, oldIds) => {
  oldIds?.forEach(id => { if (!newIds.has(id)) unsubscribeDeviceTelemetry(id); });
  newIds.forEach(id => { if (!oldIds?.has(id)) subscribeDeviceTelemetry(id); });
}, { immediate: true });

onUnmounted(() => {
  boundDeviceIds.value.forEach(id => unsubscribeDeviceTelemetry(id));
});

// 严格模式：运行时实时值解析（仅复合绑定 deviceId+variableKey；禁止裸 key 取值）
const warnedUnboundIds = new Set<string>();
const componentValues = computed(() => {
  const composite: Record<string, number | boolean> = {};
  devices.value.forEach((d) => {
    if (d.status === 'online' || d.status === 1) {
      Object.keys(d.variables).forEach((k) => {
        composite[`${d.id}:${k}`] = d.variables[k];
      });
    }
  });
  const result: Record<string, number | boolean> = {};
  (currentPage.value?.components ?? []).forEach((c) => {
    if (c.bindDeviceId != null && c.bindVariableKey) {
      const v = composite[`${c.bindDeviceId}:${c.bindVariableKey}`];
      if (v !== undefined) {
        result[c.id] = v;
        return;
      }
    }
    // 严格模式：未绑定设备/变量的组件禁止裸 key 取值，显示 0 并给出一次性警告
    if (!warnedUnboundIds.has(c.id)) {
      warnedUnboundIds.add(c.id);
      addLog('组态运行', `组件 [${c.id}] 未绑定设备/变量（bindDeviceId=${c.bindDeviceId}），禁止裸 key 取值，显示 0`, 'warning');
    }
    result[c.id] = 0;
  });
  return result;
});

// 趋势图真实数据源：把当前页 trend-chart 组件的实时值推入滚动缓冲
watch(componentValues, (vals) => {
  (currentPage.value?.components ?? []).forEach((c) => {
    if (c.type === 'trend-chart') pushTrendPoint(c.id, vals[c.id] ?? 0);
  });
}, { immediate: true });

// 页面切换时清理趋势缓冲，防止跨页残留
watch(() => currentPage.value?.id, () => {
  Object.keys(trendHistory).forEach(clearTrendHistory);
});

// 阶段5：控制下发权限——仅 Operator/Admin 可下发写指令
const canControlWrite = computed(() => {
  const r = loginUser.value?.role;
  return r === ROLE_OPERATOR || r === ROLE_ADMIN;
});

// 阶段2-2：质量分级显示——按组件绑定（deviceId+variableKey）回读变量质量，
// 非 Good 质量（Bad/Uncertain/CommunicationError/…）在画布组件上叠加角标，提示数据不可信。
const componentQualities = computed(() => {
  const result: Record<string, string> = {};
  const devIndex = new Map<number | string, any>();
  devices.value.forEach((d) => devIndex.set(d.id, d));
  (currentPage.value?.components ?? []).forEach((c) => {
    if (c.bindDeviceId != null && c.bindVariableKey) {
      const q = devIndex.get(c.bindDeviceId)?.variableMeta?.[c.bindVariableKey]?.quality;
      if (q && q !== 'Good') result[c.id] = String(q);
    }
  });
  return result;
});

// 阶段3：导航按钮跳转（同端切换，跨端不允许）
const handleNavigate = (pageId: string) => {
  // 归一化兜底比较：目标可能存的是 srv-{serverId}（新配置）或本地 page-{时间戳}（历史遗留），
  // 页面列表 id 在重新加载后为 srv-N，直接字符串相等会失配导致静默不跳转。
  const target = runtimePages.value.find(p => isSamePageRef(pageId, p.id));
  if (!target) return;
  selectedRuntimePageId.value = target.id;
  addLog('组态运行', `跳转到画面: [${target.name}]`, 'normal');
};

// 阶段4/6-2：控件写指令（含只读拦截，与编辑器一致）
const handleTriggerToggleValue = (
  deviceId: number | null,
  variableKey: string,
  legacyKey: string,
  actionType?: string,
  val?: any
) => {
  const key = variableKey || legacyKey;
  if (!key) return;

  // 严格模式：控件未绑定设备 → 禁止裸 key 写指令
  if (deviceId == null) {
    showToast('该控件未绑定设备，禁止写入（请到编辑器补全绑定）', 'warning');
    addLog('组态运行', `写指令被拒绝：组件未绑定设备 (key=${key})`, 'warning');
    return;
  }

  const dev = devices.value.find((d) => String(d.id) === String(deviceId));
  const meta = dev?.variableMeta?.[key];
  const isReadOnly = meta?.effectiveIsReadOnly ?? false;
  if (isReadOnly) {
    showToast(`变量 [${key}] 为只读，禁止写入`, 'warning');
    return;
  }

  const current = getDeviceVariableValue(deviceId, key);
  let targetVal: any;
  if (actionType === 'setValue' && val !== undefined) {
    targetVal = val;
  } else if (actionType === 'setBit') {
    targetVal = typeof current === 'boolean' ? true : 1;
  } else if (actionType === 'resetBit') {
    targetVal = typeof current === 'boolean' ? false : 0;
  } else if (actionType === 'momentary' && val !== undefined) {
    targetVal = typeof current === 'boolean' ? val : val ? 1 : 0;
  } else {
    if (typeof current === 'boolean') targetVal = !current;
    else if (typeof current === 'number') targetVal = current === 0 ? 1 : 0;
    else targetVal = val ?? 1;
  }
  setDeviceVariableValue(deviceId, key, targetVal);
};

// ===== var-display 设值弹窗 =====
// 点击可设定的数值显示组件 → 记录目标组件弹数字键盘；确认后走既有 setValue 写管道（含只读/权限拦截）。
// 写入冷却 1.2s：防触摸屏抖动/连点重复下发。
const setValueTarget = ref<HMIComponent | null>(null);
const setValueCurrentValue = computed<number | boolean | undefined>(() => {
  const c = setValueTarget.value;
  if (!c || c.bindDeviceId == null || !c.bindVariableKey) return undefined;
  return getDeviceVariableValue(c.bindDeviceId, c.bindVariableKey);
});
let lastSetValueAt = 0;
const SET_VALUE_COOLDOWN_MS = 1200;

const handleRequestSetValue = (component: HMIComponent) => {
  if (component.bindDeviceId == null || !(component.bindVariableKey || component.bindField)) {
    showToast('该组件未绑定设备/变量，无法设定', 'warning');
    return;
  }
  setValueTarget.value = component;
};

const handleSetValueConfirm = (value: number | boolean) => {
  const c = setValueTarget.value;
  if (!c) return;
  const now = Date.now();
  if (now - lastSetValueAt < SET_VALUE_COOLDOWN_MS) {
    showToast('写入冷却中，请稍候', 'warning');
    return;
  }
  lastSetValueAt = now;
  const varKey = c.bindVariableKey || c.bindField || '';
  setValueTarget.value = null;
  addLog('组态运行', `设值弹窗写入: 设备${c.bindDeviceId}.${varKey} → ${typeof value === 'boolean' ? (value ? '开' : '关') : value}`, 'normal');
  handleTriggerToggleValue(c.bindDeviceId ?? null, varKey, c.bindField, 'setValue', value);
};

// 圆角按钮 run-script 模式：点击触发服务端系统脚本（/api/ScriptRuntime，Operator/Admin）
const handleTriggerRunScript = async (scriptId: number) => {
  if (!scriptId) return;
  try {
    await triggerRuntimeScript(scriptId);
    showToast(`脚本 #${scriptId} 已触发执行`, 'success');
    addLog('组态运行', `按钮触发脚本 #${scriptId} 执行`, 'normal');
  } catch {
    // 失败提示由 http 拦截器统一弹出（403/404/脚本熔断等）
  }
};

// ===== 事件系统：分发上下文（写变量/跳转/脚本复用既有处理器；setProp 走运行态补丁不落库） =====
const eventCtx = computed<HmiEventDispatchContext>(() => ({
  canControlWrite: canControlWrite.value,
  writeVariable: (deviceId, variableKey, writeMode, value) => {
    // writeMode 与既有 actionType 一词同义（toggle/setBit/resetBit/setValue/momentary）
    handleTriggerToggleValue(deviceId, variableKey, '', writeMode, value);
  },
  navigateToPage: handleNavigate,
  runScript: (scriptId) => { handleTriggerRunScript(scriptId); },
  applyRuntimePatch: (componentId, patch) => {
    const comps = currentPage.value?.components ?? [];
    const target = comps.find((c) => c.id === componentId);
    if (!target) return;
    // 仅改运行态渲染数据（store 内组件对象，响应式生效），不落库
    if (patch.visible !== undefined) target.visible = patch.visible;
    if (patch.label !== undefined) target.label = patch.label;
    if (patch.props) target.props = { ...target.props, ...patch.props };
  },
  onBlocked: (msg) => showToast(msg, 'warning'),
}));

// 事件系统：交互类事件（click/press/release）由 CanvasPanel 上抛分发
const handleComponentEvent = (component: HMIComponent, eventType: string) => {
  dispatchComponentEvent(component, eventType as HmiEventType, eventCtx.value);
};

// 事件系统：数据类事件（valueChange/alarm）监听（值变化条件过滤、报警触发/恢复去重由 composable 处理）
useHmiDataEvents({
  components: () => currentPage.value?.components ?? [],
  componentValues,
  ctx: () => eventCtx.value,
});

// 报警事件校准：进入播放器时拉取一次当前未恢复报警（SignalR 实时增量由全局连接推送）
onMounted(() => {
  refreshActiveAlarms().catch(() => { });
});
</script>

<template>
  <div class="h-screen w-screen bg-slate-200 dark:bg-[#0b1220] overflow-hidden select-none">
    <!-- 加载中 -->
    <div v-if="loadingProject"
      class="h-full flex items-center justify-center text-slate-500 dark:text-slate-400">
      <div class="text-center">
        <RefreshCw class="w-8 h-8 mx-auto mb-2 animate-spin opacity-40" />
        <p class="text-sm">正在加载工程组态…</p>
      </div>
    </div>

    <!-- 空态 / 工程加载失败 / 当前无可用画面 -->
    <div v-else-if="projectError || !currentPage"
      class="h-full flex items-center justify-center text-center text-slate-500 dark:text-slate-400">
      <div>
        <p class="text-sm">{{ projectError ? '工程加载失败或不存在' : '当前工程暂无可用画面' }}</p>
        <p class="text-[11px] mt-1">请在组态设计中为该端新增画面并发布。</p>
      </div>
    </div>

    <!-- 组态画布：直接铺满视口（运行态自动缩放适配，无卡片/手机壳装饰） -->
    <div v-else class="h-full w-full">
      <CanvasPanel class="h-full w-full" :components="currentPage.components" :selectedId="null" :selectedIds="[]"
        :isActiveMode="true" :component-values="componentValues" :component-qualities="componentQualities"
        :canvas-width="pageWidth" :canvas-height="pageHeight" :can-control-write="canControlWrite" :readonly="true"
        :background="currentPage.background" :adapt-mode="currentPage.adaptMode"
        :layers="currentPage.layers" :current-page-id="currentPage.id"
        @triggerToggleValue="handleTriggerToggleValue" @navigateToPage="handleNavigate"
        @triggerRunScript="handleTriggerRunScript" @component-event="handleComponentEvent"
        @request-set-value="handleRequestSetValue" />
    </div>

    <!-- var-display 设值弹窗：确认后走 handleTriggerToggleValue('setValue') 写管道 -->
    <SetValueDialog v-if="setValueTarget" :component="setValueTarget" :current="setValueCurrentValue"
      @close="setValueTarget = null" @confirm="handleSetValueConfirm" />
  </div>
</template>