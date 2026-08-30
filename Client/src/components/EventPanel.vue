<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import {
  HMIComponent,
  HmiEventAction,
  HmiEventActionKind,
  HmiEventConfig,
  HmiEventConditionOp,
  HmiEventType,
  HmiEventWriteMode,
} from '../types';
import { devices } from '../store/deviceStore';
import { desktopPages, mobilePages, currentPlatform } from '../store/scadaStore';
import { systemScripts } from '../store/configStore';
import { loadSystemScripts } from '../services/scriptService';
import { loginUser } from '../store/userStore';
import { ROLE_ADMIN } from '../constants/roles';
import {
  Zap,
  ChevronRight,
  Plus,
  Trash2,
  MousePointerClick,
  ArrowDownToLine,
  ArrowUpFromLine,
  Gauge,
  BellRing,
  CornerUpRight,
  FileCode2,
  Settings2,
  PencilLine,
  ChevronUp,
  ChevronDown,
} from 'lucide-vue-next';

const props = defineProps<{
  selectedComponent: HMIComponent | null;
  /** 当前页面全部组件（setProp 动作选择目标用） */
  pageComponents?: HMIComponent[];
  currentPageId?: string;
}>();

const emit = defineEmits<{
  (e: 'updateComponent', id: string, updates: Partial<HMIComponent>): void;
  (e: 'collapse'): void;
}>();

// ===== 事件类型元数据 =====
const EVENT_TYPE_META: {
  type: HmiEventType;
  label: string;
  desc: string;
  needsBinding: boolean;
  icon: any;
}[] = [
  { type: 'click', label: '点击时', desc: '运行态单击组件触发（松开时执行）', needsBinding: false, icon: MousePointerClick },
  { type: 'press', label: '按下时', desc: '按下即触发（点动写 1 场景）', needsBinding: false, icon: ArrowDownToLine },
  { type: 'release', label: '松开时', desc: '松开触发（点动写 0 场景）', needsBinding: false, icon: ArrowUpFromLine },
  { type: 'valueChange', label: '值变化时', desc: '绑定变量值变化且满足条件触发', needsBinding: true, icon: Gauge },
  { type: 'alarm', label: '报警时', desc: '绑定变量进入报警状态触发（恢复后可再触发）', needsBinding: true, icon: BellRing },
];

// 数据类事件需组件已绑定 设备+变量
const hasBinding = computed(
  () => props.selectedComponent?.bindDeviceId != null && !!props.selectedComponent?.bindVariableKey
);

const events = computed<HmiEventConfig[]>(() => props.selectedComponent?.props?.events ?? []);

const getEvent = (type: HmiEventType): HmiEventConfig | undefined =>
  events.value.find((e) => e && e.type === type);

const getActionCount = (type: HmiEventType): number => {
  const e = getEvent(type);
  if (!e || e.enabled === false) return 0;
  return (e.actions ?? []).filter((a) => a && a.enabled !== false).length;
};

// 当前编辑的事件类型
const activeEventType = ref<HmiEventType>('click');
const activeEvent = computed<HmiEventConfig | undefined>(() => getEvent(activeEventType.value));

// ===== 编辑操作：整体替换 events 数组提交（走既有防抖落库链路） =====
const commit = (next: HmiEventConfig[]) => {
  if (!props.selectedComponent) return;
  emit('updateComponent', props.selectedComponent.id, {
    props: { ...props.selectedComponent.props, events: next },
  });
};

/** 取事件配置（不存在则按需创建后返回新数组中的引用） */
const upsertEvent = (type: HmiEventType): { list: HmiEventConfig[]; evt: HmiEventConfig } => {
  const list = [...events.value];
  let evt = list.find((e) => e && e.type === type);
  if (!evt) {
    evt = { type, enabled: true, condition: null, actions: [] };
    list.push(evt);
  }
  return { list, evt };
};

const setEventEnabled = (type: HmiEventType, enabled: boolean) => {
  const { list, evt } = upsertEvent(type);
  evt.enabled = enabled;
  commit(list);
};

const setCondition = (type: HmiEventType, patch: Partial<HmiEventConfig['condition']>) => {
  const { list, evt } = upsertEvent(type);
  evt.condition = { op: evt.condition?.op ?? '>=', operand: evt.condition?.operand ?? 0, ...patch };
  commit(list);
};

const clearCondition = (type: HmiEventType) => {
  const evt = getEvent(type);
  if (!evt) return;
  commit(events.value.map((e) => (e === evt ? { ...e, condition: null } : e)));
};

// ===== 动作编辑 =====
const genId = () => `act-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;

const addAction = (type: HmiEventType, kind: HmiEventActionKind) => {
  const { list, evt } = upsertEvent(type);
  const action: HmiEventAction = {
    id: genId(),
    kind,
    enabled: true,
    params: kind === 'writeVar'
      ? { writeMode: 'setBit', deviceId: null, variableKey: '' }
      : kind === 'navigate'
        ? { targetPageId: '' }
        : kind === 'runScript'
          ? { scriptId: undefined }
          : { targetComponentId: '', patch: {} },
  };
  evt.actions = [...(evt.actions ?? []), action];
  commit(list);
};

const updateAction = (type: HmiEventType, actionId: string, patch: Partial<HmiEventAction>) => {
  const evt = getEvent(type);
  if (!evt) return;
  commit(
    events.value.map((e) =>
      e === evt
        ? {
          ...e,
          actions: (e.actions ?? []).map((a) =>
            a.id === actionId ? { ...a, ...patch, params: { ...a.params, ...(patch.params ?? {}) } } : a
          ),
        }
        : e
    )
  );
};

const updateActionParams = (type: HmiEventType, actionId: string, paramPatch: Record<string, any>) => {
  const evt = getEvent(type);
  if (!evt) return;
  commit(
    events.value.map((e) =>
      e === evt
        ? {
          ...e,
          actions: (e.actions ?? []).map((a) =>
            a.id === actionId ? { ...a, params: { ...a.params, ...paramPatch } } : a
          ),
        }
        : e
    )
  );
};

const removeAction = (type: HmiEventType, actionId: string) => {
  const evt = getEvent(type);
  if (!evt) return;
  const nextActions = (evt.actions ?? []).filter((a) => a.id !== actionId);
  // 事件动作清空后保留配置骨架（enabled 状态保留），便于继续添加
  commit(events.value.map((e) => (e === evt ? { ...e, actions: nextActions } : e)));
};

const moveAction = (type: HmiEventType, actionId: string, dir: -1 | 1) => {
  const evt = getEvent(type);
  if (!evt) return;
  const actions = [...(evt.actions ?? [])];
  const idx = actions.findIndex((a) => a.id === actionId);
  const target = idx + dir;
  if (idx < 0 || target < 0 || target >= actions.length) return;
  [actions[idx], actions[target]] = [actions[target], actions[idx]];
  commit(events.value.map((e) => (e === evt ? { ...e, actions } : e)));
};

// ===== 动作元数据 =====
const ACTION_KIND_META: Record<HmiEventActionKind, { label: string; icon: any }> = {
  writeVar: { label: '写变量', icon: PencilLine },
  navigate: { label: '页面跳转', icon: CornerUpRight },
  runScript: { label: '运行系统脚本', icon: FileCode2 },
  setProp: { label: '组件控制', icon: Settings2 },
};

// ===== writeVar：设备/变量候选（严格模式：先选设备再列变量） =====
const writeVarDeviceOptions = computed(() =>
  devices.value.map((d) => ({ id: d.id, name: d.name }))
);

const writeVarVariableOptions = (action: HmiEventAction) => {
  const devId = action.params.deviceId != null
    ? action.params.deviceId
    : props.selectedComponent?.bindDeviceId;
  const dev = devices.value.find((d) => String(d.id) === String(devId));
  return dev && dev.variables ? Object.keys(dev.variables) : [];
};

// ===== navigate：目标画面候选（同端，排除当前页） =====
const navTargetOptions = computed(() => {
  const list = currentPlatform.value === 'Mobile' ? mobilePages.value : desktopPages.value;
  return list.filter((p) => p.id !== props.currentPageId).map((p) => ({ id: p.id, name: p.name }));
});

// ===== runScript：脚本列表（管理员懒加载，避免非管理员 403 噪音） =====
const scriptListRequested = ref(false);
watch(
  () =>
    (events.value ?? []).some((e) =>
      (e.actions ?? []).some((a) => a.kind === 'runScript' && a.enabled !== false)
    ),
  (need) => {
    if (need && !scriptListRequested.value && loginUser.value?.role === ROLE_ADMIN) {
      scriptListRequested.value = true;
      loadSystemScripts().catch(() => { scriptListRequested.value = false; });
    }
  },
  { immediate: true }
);

// ===== setProp：目标组件候选（自身 + 同页面其他组件） =====
const setPropTargetOptions = computed(() => [
  { id: '', name: '自身（本组件）' },
  ...(props.pageComponents ?? [])
    .filter((c) => c.id !== props.selectedComponent?.id)
    .map((c) => ({ id: c.id, name: `${c.name || c.type} (${c.id.slice(-6)})` })),
]);

// setProp 补丁的可视化字段读写辅助
const getPatchVisible = (action: HmiEventAction): '' | 'true' | 'false' => {
  if (action.params.patch?.visible === true) return 'true';
  if (action.params.patch?.visible === false) return 'false';
  return '';
};
const setPatchVisible = (type: HmiEventType, actionId: string, val: string) => {
  if (val === '') {
    updateActionParams(type, actionId, { patch: {} });
    return;
  }
  updateActionParams(type, actionId, {
    patch: { visible: val === 'true' },
  });
};
</script>

<template>
  <!-- 空态：未选中任何组件 -->
  <div v-if="!selectedComponent"
    class="h-full bg-[#fafafa] dark:bg-slate-950 p-6 text-gray-400 dark:text-slate-500 text-xs flex flex-col justify-between items-center text-center transition-colors relative">
    <div class="w-full flex justify-end">
      <button @click="emit('collapse')"
        class="p-1 rounded text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
        title="收起事件面板">
        <ChevronRight class="w-4 h-4" />
      </button>
    </div>
    <div class="flex flex-col items-center justify-center my-auto">
      <Zap class="w-8 h-8 text-[#1890ff] dark:text-sky-400 mb-2 opacity-60" />
      <p class="font-semibold text-gray-700 dark:text-slate-300">事件面板</p>
      <p class="text-[10px] text-gray-400 dark:text-slate-500 mt-2.5 max-w-[200px] leading-relaxed">
        请在画布上选择元件以配置事件。<br />事件 = 触发条件 + 动作链（写变量/跳转/脚本/组件控制）。
      </p>
    </div>
    <div class="h-4"></div>
  </div>

  <div v-else class="h-full flex flex-col bg-white dark:bg-slate-900 text-[#262626] dark:text-slate-100 overflow-y-auto transition-colors">
    <!-- Title -->
    <div
      class="p-4 border-b border-[#f0f0f0] dark:border-slate-800 bg-[#fafafa] dark:bg-slate-950 flex items-center justify-between">
      <div class="flex items-center gap-2 min-w-0">
        <Zap class="w-4 h-4 text-[#1890ff] dark:text-sky-400 shrink-0" />
        <h3 class="text-xs font-bold text-[#141414] dark:text-slate-100 uppercase tracking-wider truncate">
          事件配置
        </h3>
        <span class="text-[10px] text-gray-400 dark:text-slate-500 font-mono truncate">
          {{ selectedComponent.name || selectedComponent.type }}
        </span>
      </div>
      <button @click="emit('collapse')"
        class="p-1 rounded text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
        title="收起事件面板">
        <ChevronRight class="w-4 h-4" />
      </button>
    </div>

    <div class="p-4 space-y-4 text-left">
      <!-- 事件类型选择 -->
      <section class="space-y-2.5">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <MousePointerClick class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          触发事件
        </div>
        <div class="grid grid-cols-2 gap-1.5">
          <button v-for="meta in EVENT_TYPE_META" :key="meta.type" @click="activeEventType = meta.type"
            :disabled="meta.needsBinding && !hasBinding"
            class="flex items-center gap-1.5 px-2.5 py-1.5 rounded-md border text-xs transition-colors cursor-pointer disabled:cursor-not-allowed disabled:opacity-40"
            :class="activeEventType === meta.type
              ? 'border-[#1890ff] dark:border-sky-500 bg-[#e6f7ff] dark:bg-sky-950/40 text-[#1890ff] dark:text-sky-400'
              : 'border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] dark:hover:border-sky-500 text-slate-600 dark:text-slate-300'"
            :title="meta.needsBinding && !hasBinding ? '需先在「属性配置」中绑定设备与变量' : meta.desc">
            <component :is="meta.icon" class="w-3.5 h-3.5 shrink-0" />
            <span class="flex-1 text-left">{{ meta.label }}</span>
            <span v-if="getActionCount(meta.type) > 0"
              class="px-1.5 py-0.2 text-[10px] font-bold rounded-full bg-[#1890ff] dark:bg-sky-500 text-white">
              {{ getActionCount(meta.type) }}
            </span>
          </button>
        </div>
        <p v-if="!hasBinding" class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug">
          「值变化 / 报警」为数据类事件，需先在属性配置中绑定设备与变量。
        </p>
      </section>

      <div class="border-t border-[#f0f0f0] dark:border-slate-800" />

      <!-- 当前事件编辑 -->
      <section class="space-y-3">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
            <component :is="EVENT_TYPE_META.find(m => m.type === activeEventType)?.icon" class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
            {{ EVENT_TYPE_META.find(m => m.type === activeEventType)?.label }}
            <span class="text-[9px] font-normal text-gray-400 dark:text-slate-500 truncate">
              {{ EVENT_TYPE_META.find(m => m.type === activeEventType)?.desc }}
            </span>
          </div>
          <!-- 事件启用开关 -->
          <label class="flex items-center gap-1.5 text-[10px] text-gray-500 dark:text-slate-400 cursor-pointer select-none">
            <input type="checkbox" :checked="activeEvent ? activeEvent.enabled !== false : false"
              @change="setEventEnabled(activeEventType, ($event.target as HTMLInputElement).checked)"
              class="accent-[#1890ff] cursor-pointer" />
            启用
          </label>
        </div>

        <!-- 值变化条件 -->
        <div v-if="activeEventType === 'valueChange' && activeEvent" class="space-y-1.5">
          <label class="text-[10px] text-gray-500 dark:text-slate-400">触发条件（不设置 = 任何变化都触发）</label>
          <div class="flex items-center gap-1.5">
            <select :value="activeEvent.condition?.op ?? ''"
              @change="setCondition(activeEventType, { op: ($event.target as HTMLSelectElement).value as HmiEventConditionOp })"
              class="w-16 bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-xs focus:outline-none focus:border-[#1890ff]">
              <option value="">不限</option>
              <option value=">">&gt;</option>
              <option value="<">&lt;</option>
              <option value="=">=</option>
              <option value=">=">&ge;</option>
              <option value="<=">&le;</option>
              <option value="!=">&ne;</option>
            </select>
            <input v-if="activeEvent.condition" type="number" step="any"
              :value="activeEvent.condition.operand"
              @change="setCondition(activeEventType, { operand: parseFloat(($event.target as HTMLInputElement).value) || 0 })"
              class="flex-1 min-w-0 bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-xs font-mono focus:outline-none focus:border-[#1890ff]" />
            <button v-if="activeEvent.condition" @click="clearCondition(activeEventType)"
              class="px-1.5 py-1 text-[10px] text-gray-400 hover:text-red-500 cursor-pointer">清除</button>
          </div>
        </div>

        <!-- 动作链 -->
        <div class="space-y-2">
          <label class="text-[10px] text-gray-500 dark:text-slate-400">
            动作链（按顺序执行）
          </label>

          <!-- 空态 -->
          <div v-if="!activeEvent || !(activeEvent.actions ?? []).length"
            class="border border-dashed border-[#d9d9d9] dark:border-slate-700 rounded-md py-4 text-center text-[10px] text-gray-400 dark:text-slate-500">
            该事件尚未配置动作
          </div>

          <!-- 动作卡片 -->
          <div v-for="(action, idx) in activeEvent?.actions ?? []" :key="action.id"
            class="border border-[#f0f0f0] dark:border-slate-700 rounded-md bg-[#fafafa] dark:bg-slate-950/60 space-y-2 p-2.5">
            <!-- 动作头 -->
            <div class="flex items-center gap-1.5">
              <span class="w-5 h-5 rounded-full bg-[#e6f7ff] dark:bg-sky-950/60 text-[#1890ff] dark:text-sky-400 text-[10px] font-bold flex items-center justify-center shrink-0">
                {{ idx + 1 }}
              </span>
              <select :value="action.kind"
                @change="updateAction(activeEventType, action.id, { kind: ($event.target as HTMLSelectElement).value as HmiEventActionKind })"
                class="flex-1 min-w-0 bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-xs focus:outline-none focus:border-[#1890ff]">
                <option v-for="(meta, kind) in ACTION_KIND_META" :key="kind" :value="kind">{{ meta.label }}</option>
              </select>
              <label class="flex items-center cursor-pointer shrink-0" title="启用/禁用该动作">
                <input type="checkbox" :checked="action.enabled !== false"
                  @change="updateAction(activeEventType, action.id, { enabled: ($event.target as HTMLInputElement).checked })"
                  class="accent-[#1890ff] cursor-pointer" />
              </label>
              <button @click="moveAction(activeEventType, action.id, -1)" :disabled="idx === 0"
                class="p-0.5 text-slate-400 hover:text-[#1890ff] disabled:opacity-30 cursor-pointer" title="上移">
                <ChevronUp class="w-3.5 h-3.5" />
              </button>
              <button @click="moveAction(activeEventType, action.id, 1)" :disabled="idx === (activeEvent?.actions?.length ?? 0) - 1"
                class="p-0.5 text-slate-400 hover:text-[#1890ff] disabled:opacity-30 cursor-pointer" title="下移">
                <ChevronDown class="w-3.5 h-3.5" />
              </button>
              <button @click="removeAction(activeEventType, action.id)"
                class="p-0.5 text-slate-400 hover:text-red-500 cursor-pointer" title="删除动作">
                <Trash2 class="w-3.5 h-3.5" />
              </button>
            </div>

            <!-- writeVar 参数 -->
            <template v-if="action.kind === 'writeVar'">
              <div class="grid grid-cols-2 gap-1.5">
                <div>
                  <label class="text-[9px] text-gray-400 dark:text-slate-500">目标设备（空=主绑定）</label>
                  <select :value="action.params.deviceId ?? ''"
                    @change="updateActionParams(activeEventType, action.id, { deviceId: ($event.target as HTMLSelectElement).value === '' ? null : Number(($event.target as HTMLSelectElement).value), variableKey: '' })"
                    class="w-full bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-[10px] focus:outline-none focus:border-[#1890ff]">
                    <option value="">主绑定设备</option>
                    <option v-for="d in writeVarDeviceOptions" :key="d.id" :value="d.id">{{ d.name }}</option>
                  </select>
                </div>
                <div>
                  <label class="text-[9px] text-gray-400 dark:text-slate-500">变量键（空=主绑定）</label>
                  <select :value="action.params.variableKey ?? ''"
                    @change="updateActionParams(activeEventType, action.id, { variableKey: ($event.target as HTMLSelectElement).value })"
                    class="w-full bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-[10px] font-mono focus:outline-none focus:border-[#1890ff]">
                    <option value="">主绑定变量</option>
                    <option v-for="k in writeVarVariableOptions(action)" :key="k" :value="k">{{ k }}</option>
                  </select>
                </div>
              </div>
              <div class="grid grid-cols-2 gap-1.5">
                <div>
                  <label class="text-[9px] text-gray-400 dark:text-slate-500">写入模式</label>
                  <select :value="action.params.writeMode ?? 'toggle'"
                    @change="updateActionParams(activeEventType, action.id, { writeMode: ($event.target as HTMLSelectElement).value as HmiEventWriteMode })"
                    class="w-full bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-[10px] focus:outline-none focus:border-[#1890ff]">
                    <option value="setBit">置位（写 1）</option>
                    <option value="resetBit">复位（写 0）</option>
                    <option value="toggle">取反</option>
                    <option value="setValue">设值</option>
                    <option value="momentary">点动（按下写1/松开写0）</option>
                  </select>
                </div>
                <div v-if="action.params.writeMode === 'setValue'">
                  <label class="text-[9px] text-gray-400 dark:text-slate-500">写入值</label>
                  <input type="number" step="any" :value="action.params.value ?? 1"
                    @change="updateActionParams(activeEventType, action.id, { value: parseFloat(($event.target as HTMLInputElement).value) || 0 })"
                    class="w-full bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-[10px] font-mono focus:outline-none focus:border-[#1890ff]" />
                </div>
              </div>
            </template>

            <!-- navigate 参数 -->
            <template v-else-if="action.kind === 'navigate'">
              <div>
                <label class="text-[9px] text-gray-400 dark:text-slate-500">目标画面（仅同端）</label>
                <select :value="action.params.targetPageId ?? ''"
                  @change="updateActionParams(activeEventType, action.id, { targetPageId: ($event.target as HTMLSelectElement).value })"
                  class="w-full bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-[10px] focus:outline-none focus:border-[#1890ff]">
                  <option value="">请选择画面…</option>
                  <option v-for="p in navTargetOptions" :key="p.id" :value="p.id">{{ p.name }}</option>
                </select>
              </div>
            </template>

            <!-- runScript 参数 -->
            <template v-else-if="action.kind === 'runScript'">
              <div>
                <label class="text-[9px] text-gray-400 dark:text-slate-500">系统脚本</label>
                <select :value="action.params.scriptId ?? ''"
                  @change="updateActionParams(activeEventType, action.id, { scriptId: Number(($event.target as HTMLSelectElement).value) || undefined })"
                  class="w-full bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-[10px] focus:outline-none focus:border-[#1890ff]">
                  <option value="">请选择脚本…</option>
                  <option v-for="s in systemScripts" :key="s.id" :value="s.id">#{{ s.id }} {{ s.name }}</option>
                </select>
                <p v-if="loginUser?.role !== ROLE_ADMIN" class="text-[9px] text-gray-400 dark:text-slate-500 mt-1 leading-snug">
                  脚本列表仅管理员可加载；运行态 Operator/Admin 可触发执行。
                </p>
              </div>
            </template>

            <!-- setProp 参数 -->
            <template v-else-if="action.kind === 'setProp'">
              <div>
                <label class="text-[9px] text-gray-400 dark:text-slate-500">目标组件</label>
                <select :value="action.params.targetComponentId ?? ''"
                  @change="updateActionParams(activeEventType, action.id, { targetComponentId: ($event.target as HTMLSelectElement).value })"
                  class="w-full bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-[10px] focus:outline-none focus:border-[#1890ff]">
                  <option v-for="t in setPropTargetOptions" :key="t.id" :value="t.id">{{ t.name }}</option>
                </select>
              </div>
              <div class="grid grid-cols-2 gap-1.5">
                <div>
                  <label class="text-[9px] text-gray-400 dark:text-slate-500">显示/隐藏</label>
                  <select :value="getPatchVisible(action)"
                    @change="setPatchVisible(activeEventType, action.id, ($event.target as HTMLSelectElement).value)"
                    class="w-full bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-[10px] focus:outline-none focus:border-[#1890ff]">
                    <option value="">不修改</option>
                    <option value="true">显示</option>
                    <option value="false">隐藏</option>
                  </select>
                </div>
                <div>
                  <label class="text-[9px] text-gray-400 dark:text-slate-500">文本（Label）</label>
                  <input type="text" :value="action.params.patch?.label ?? ''" placeholder="留空=不修改"
                    @change="updateActionParams(activeEventType, action.id, { patch: { ...(action.params.patch ?? {}), label: ($event.target as HTMLInputElement).value || undefined } })"
                    class="w-full bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-[10px] focus:outline-none focus:border-[#1890ff]" />
                </div>
              </div>
              <p class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug">
                组件控制仅运行态生效（不落库），典型用法：点击/报警时显示或隐藏报警面板。
              </p>
            </template>
          </div>

          <!-- 添加动作 -->
          <div class="flex flex-wrap gap-1.5 pt-0.5">
            <button v-for="(meta, kind) in ACTION_KIND_META" :key="kind"
              @click="addAction(activeEventType, kind as HmiEventActionKind)"
              class="flex items-center gap-1 px-2 py-1 rounded border border-dashed border-[#1890ff] dark:border-sky-500 text-[#1890ff] dark:text-sky-400 dark:bg-sky-950/30 hover:bg-[#e6f7ff] dark:hover:bg-sky-950/60 text-[10px] transition-colors cursor-pointer">
              <Plus class="w-3 h-3" />
              {{ meta.label }}
            </button>
          </div>
        </div>
      </section>

      <div class="border-t border-[#f0f0f0] dark:border-slate-800" />

      <!-- 说明 -->
      <section class="space-y-1.5">
        <p class="text-[9px] text-gray-400 dark:text-slate-500 leading-relaxed">
          运行态优先执行事件配置；未配置事件的按钮/开关仍走原「操作模式」逻辑（新旧共存）。<br />
          写变量与脚本动作需 Operator/Admin 权限；「按下/松开」适合点动（按下写1、松开写0）。
        </p>
      </section>
    </div>
  </div>
</template>
