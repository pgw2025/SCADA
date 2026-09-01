<script setup lang="ts">
// multi-var-dashboard 实时多变量监控看板检查器：排版/外框设置 + 多变量监控列表管理
// 从 InspectorPanel.vue 抽出（Phase 2b）；通信：component 下行 / updateProp 上行
import { computed } from 'vue';
import { HMIComponent, HmiDashboardItem } from '../../types';
import { devices } from '../../store/deviceStore';
import { LayoutDashboard, Grid, Columns, Table, Sparkles, ChevronUp, ChevronDown, Trash2, Plus } from 'lucide-vue-next';

const props = defineProps<{
  component: HMIComponent;
}>();

const emit = defineEmits<{
  (e: 'updateProp', key: string, value: any): void;
}>();

const componentProps = computed(() => props.component.props ?? {});

const updateProp = (key: string, value: any) => emit('updateProp', key, value);

// 解析数值输入：合法（含 0）原样写入，非法（NaN/空）回退缺省值
const numInput = (raw: string, fallback: number): number => {
  const n = parseFloat(raw);
  return Number.isFinite(n) ? n : fallback;
};

// ===== multi-var-dashboard 多变量监控看板配置辅助函数 =====
const dashboardItems = computed<HmiDashboardItem[]>(() => {
  const raw = componentProps.value.dashboardItems;
  return Array.isArray(raw) ? (raw as HmiDashboardItem[]) : [];
});

const commitDashboardItems = (items: HmiDashboardItem[]) => updateProp('dashboardItems', items);

const updateDashboardItem = (index: number, patch: Partial<HmiDashboardItem>) => {
  const next = dashboardItems.value.map((it, i) => (i === index ? { ...it, ...patch } : it));
  commitDashboardItems(next);
};

const addDashboardItem = () => {
  const devId = props.component.bindDeviceId ?? devices.value[0]?.id ?? null;
  const dev = devices.value.find(d => d.id === devId) || devices.value[0];
  const keys = dev ? Object.keys(dev.variables || {}) : [];
  const existingKeys = new Set(dashboardItems.value.map(it => it.variableKey));
  const unusedKey = keys.find(k => !existingKeys.has(k)) || keys[0] || 'var_1';
  const meta = dev?.variableMeta?.[unusedKey];

  const newItem: HmiDashboardItem = {
    id: `item-${Date.now()}-${dashboardItems.value.length + 1}`,
    deviceId: dev?.id ?? null,
    variableKey: unusedKey,
    label: meta?.name || unusedKey,
    unit: meta?.unit || '',
    precision: typeof dev?.variables?.[unusedKey] === 'number' ? 1 : null,
    showStatusDot: true,
    thresholdMin: null,
    thresholdMax: null,
  };
  commitDashboardItems([...dashboardItems.value, newItem]);
};

const removeDashboardItem = (index: number) => {
  commitDashboardItems(dashboardItems.value.filter((_, i) => i !== index));
};

const moveDashboardItem = (index: number, dir: -1 | 1) => {
  const to = index + dir;
  if (to < 0 || to >= dashboardItems.value.length) return;
  const next = [...dashboardItems.value];
  [next[index], next[to]] = [next[to], next[index]];
  commitDashboardItems(next);
};

// 一键从所选设备导入所有变量
const importAllVariablesFromDevice = (targetDevId?: number | null) => {
  const devId = targetDevId ?? props.component.bindDeviceId ?? devices.value[0]?.id;
  const dev = devices.value.find(d => d.id === devId);
  if (!dev || !dev.variables) return;

  const newItems: HmiDashboardItem[] = Object.keys(dev.variables).map((k, idx) => {
    const meta = dev.variableMeta?.[k];
    const isNum = typeof dev.variables[k] === 'number';
    return {
      id: `item-${dev.id}-${k}-${Date.now()}-${idx}`,
      deviceId: dev.id,
      variableKey: k,
      label: meta?.name || k,
      unit: meta?.unit || '',
      precision: isNum ? 2 : null,
      showStatusDot: true,
      thresholdMin: null,
      thresholdMax: null,
    };
  });

  commitDashboardItems(newItems);
};

// 获取某个监控项对应设备下的变量选项
const getItemVariableOptions = (itemDevId?: number | null) => {
  const devId = itemDevId != null ? itemDevId : (props.component.bindDeviceId ?? devices.value[0]?.id);
  const dev = devices.value.find(d => d.id === devId) || devices.value[0];
  if (!dev || !dev.variables) return [];
  return Object.keys(dev.variables).map(k => ({
    key: k,
    name: dev.variableMeta?.[k]?.name || k,
    unit: dev.variableMeta?.[k]?.unit || '',
    type: typeof dev.variables[k] === 'number' ? 'analog' : 'digital'
  }));
};
</script>

<template>
  <!-- REAL-TIME MULTI-VARIABLE DASHBOARD CONTROLS (实时多变量监控看板专属配置) -->
  <div class="space-y-4">
    <!-- 模块一：看板排版与外框设置 -->
    <div
      class="space-y-3 text-xs border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/40 dark:bg-sky-950/20">
      <div class="flex items-center justify-between">
        <p
          class="font-bold text-sky-600 dark:text-sky-400 text-[11px] uppercase tracking-wider flex items-center gap-1.5">
          <LayoutDashboard class="w-3.5 h-3.5" />
          看板布局与边框设置
        </p>
        <span
          class="text-[9px] font-mono bg-sky-100 dark:bg-sky-900/60 text-sky-700 dark:text-sky-300 px-1.5 py-0.5 rounded">Dashboard</span>
      </div>

      <!-- 看板标题设置 -->
      <div class="space-y-2 pb-2 border-b border-sky-100 dark:border-sky-900/40">
        <div class="flex items-center justify-between">
          <label class="flex items-center gap-2 select-none cursor-pointer">
            <input type="checkbox" id="dashShowTitle" :checked="componentProps.showDashboardTitle !== false"
              @change="updateProp('showDashboardTitle', ($event.target as HTMLInputElement).checked)"
              class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
            <span class="text-xs font-semibold text-gray-700 dark:text-slate-300">显示看板标题栏</span>
          </label>
        </div>

        <div v-if="componentProps.showDashboardTitle !== false" class="space-y-1.5">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">标题名称</label>
            <input type="text" :value="componentProps.dashboardTitle ?? '实时参数监控看板'"
              @input="updateProp('dashboardTitle', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] text-xs mt-0.5"
              placeholder="看板标题" />
          </div>
        </div>
      </div>

      <!-- 排版布局模式 (Layout Mode) -->
      <div>
        <label class="text-[10px] font-semibold text-gray-700 dark:text-slate-300">排版模式 (Layout)</label>
        <div class="grid grid-cols-3 gap-1.5 mt-1">
          <button type="button" @click="updateProp('dashboardLayout', 'grid')"
            class="flex flex-col items-center gap-1 p-2 rounded border text-center transition-all cursor-pointer"
            :class="(!componentProps.dashboardLayout || componentProps.dashboardLayout === 'grid')
              ? 'bg-[#1890ff]/10 border-[#1890ff] text-[#1890ff] font-bold dark:bg-sky-950/60 dark:border-sky-500'
              : 'bg-white dark:bg-slate-900 border-gray-200 dark:border-slate-800 text-gray-600 dark:text-slate-400'">
            <Grid class="w-4 h-4" />
            <span class="text-[10px]">卡片网格</span>
          </button>
          <button type="button" @click="updateProp('dashboardLayout', 'table')"
            class="flex flex-col items-center gap-1 p-2 rounded border text-center transition-all cursor-pointer"
            :class="componentProps.dashboardLayout === 'table'
              ? 'bg-[#1890ff]/10 border-[#1890ff] text-[#1890ff] font-bold dark:bg-sky-950/60 dark:border-sky-500'
              : 'bg-white dark:bg-slate-900 border-gray-200 dark:border-slate-800 text-gray-600 dark:text-slate-400'">
            <Table class="w-4 h-4" />
            <span class="text-[10px]">列表表格</span>
          </button>
          <button type="button" @click="updateProp('dashboardLayout', 'compact')"
            class="flex flex-col items-center gap-1 p-2 rounded border text-center transition-all cursor-pointer"
            :class="componentProps.dashboardLayout === 'compact'
              ? 'bg-[#1890ff]/10 border-[#1890ff] text-[#1890ff] font-bold dark:bg-sky-950/60 dark:border-sky-500'
              : 'bg-white dark:bg-slate-900 border-gray-200 dark:border-slate-800 text-gray-600 dark:text-slate-400'">
            <Columns class="w-4 h-4" />
            <span class="text-[10px]">紧凑微标</span>
          </button>
        </div>
      </div>

      <!-- 列数与间距设置 (仅卡片网格模式) -->
      <div v-if="!componentProps.dashboardLayout || componentProps.dashboardLayout === 'grid'"
        class="grid grid-cols-2 gap-2">
        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">排版列数 (Columns)</label>
          <select :value="componentProps.dashboardColumns ?? 2"
            @change="updateProp('dashboardColumns', numInput(($event.target as HTMLSelectElement).value, 2))"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs text-[#262626] dark:text-white mt-0.5">
            <option :value="1">1 列 (单列垂直)</option>
            <option :value="2">2 列 (双列卡片)</option>
            <option :value="3">3 列 (三列排版)</option>
            <option :value="4">4 列 (四列密集)</option>
            <option :value="6">6 列 (六列大屏)</option>
            <option :value="0">Auto (自适应流式)</option>
          </select>
        </div>
        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">间距大小 (Gap)</label>
          <select :value="componentProps.dashboardGap ?? 8"
            @change="updateProp('dashboardGap', numInput(($event.target as HTMLSelectElement).value, 8))"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs text-[#262626] dark:text-white mt-0.5">
            <option :value="4">4 px (紧凑)</option>
            <option :value="8">8 px (标准)</option>
            <option :value="12">12 px (舒适)</option>
            <option :value="16">16 px (宽松)</option>
            <option :value="20">20 px (超宽)</option>
          </select>
        </div>
      </div>

      <!-- 表格模式斑马纹 -->
      <div v-if="componentProps.dashboardLayout === 'table'" class="flex items-center gap-2">
        <input type="checkbox" id="dashZebra" :checked="componentProps.dashboardZebra === true"
          @change="updateProp('dashboardZebra', ($event.target as HTMLInputElement).checked)"
          class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
        <label for="dashZebra" class="text-xs text-gray-700 dark:text-slate-300 select-none cursor-pointer">
          启用表格行隔行斑马纹
        </label>
      </div>

      <!-- 看板外框边框 (Border) -->
      <div class="space-y-2 pt-2 border-t border-sky-100 dark:border-sky-900/40">
        <div class="flex items-center justify-between">
          <label class="flex items-center gap-2 select-none cursor-pointer">
            <input type="checkbox" id="dashShowBorder" :checked="componentProps.showBorder !== false"
              @change="updateProp('showBorder', ($event.target as HTMLInputElement).checked)"
              class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
            <span class="text-xs font-semibold text-gray-700 dark:text-slate-300">显示看板外框边框</span>
          </label>
        </div>

        <div v-if="componentProps.showBorder !== false"
          class="space-y-2 pl-4 border-l-2 border-sky-200 dark:border-sky-800">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">外框边框颜色</label>
            <div class="flex items-center gap-1.5 mt-0.5">
              <input type="color" :value="componentProps.borderColor || '#cbd5e1'"
                @input="updateProp('borderColor', ($event.target as HTMLInputElement).value)"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="componentProps.borderColor || '#cbd5e1'"
                @input="updateProp('borderColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1.5 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
            <div class="flex items-center gap-1 mt-1.5">
              <button
                v-for="bc in ['#cbd5e1', '#94a3b8', '#475569', '#1890ff', '#38bdf8', '#10b981', '#f59e0b', '#ef4444', '#1e293b']"
                :key="bc" type="button" @click="updateProp('borderColor', bc)"
                class="w-4 h-4 rounded-full border border-black/20 dark:border-white/20 cursor-pointer transition-transform hover:scale-125"
                :style="{ backgroundColor: bc }" :title="bc" />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-2">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">边框粗细</label>
              <select :value="componentProps.borderWidth ?? 1.5"
                @change="updateProp('borderWidth', numInput(($event.target as HTMLSelectElement).value, 1.5))"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs text-[#262626] dark:text-white mt-0.5">
                <option :value="1">1 px (细)</option>
                <option :value="1.5">1.5 px (标准)</option>
                <option :value="2">2 px (中等)</option>
                <option :value="3">3 px (粗)</option>
                <option :value="4">4 px (加粗)</option>
              </select>
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">边框线条</label>
              <select :value="componentProps.borderStyle || 'solid'"
                @change="updateProp('borderStyle', ($event.target as HTMLSelectElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs text-[#262626] dark:text-white mt-0.5">
                <option value="solid">实线 (Solid)</option>
                <option value="dashed">虚线 (Dashed)</option>
                <option value="dotted">点线 (Dotted)</option>
              </select>
            </div>
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">外框圆角弧度</label>
            <div class="flex items-center gap-2 mt-0.5">
              <input type="range" min="0" max="24" step="2" :value="componentProps.borderRadius ?? 8"
                @input="updateProp('borderRadius', numInput(($event.target as HTMLInputElement).value, 8))"
                class="flex-1 accent-[#1890ff]" />
              <span class="text-[10px] font-mono text-gray-600 dark:text-slate-300 w-8 text-right">{{
                componentProps.borderRadius ?? 8 }}px</span>
            </div>
          </div>
        </div>
      </div>

      <!-- 看板背景底色 -->
      <div class="space-y-2 pt-2 border-t border-sky-100 dark:border-sky-900/40">
        <div class="flex items-center justify-between">
          <label class="flex items-center gap-2 select-none cursor-pointer">
            <input type="checkbox" id="dashShowBg" :checked="componentProps.showBackground !== false"
              @change="updateProp('showBackground', ($event.target as HTMLInputElement).checked)"
              class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
            <span class="text-xs font-semibold text-gray-700 dark:text-slate-300">显示看板背景底色</span>
          </label>
        </div>

        <div v-if="componentProps.showBackground !== false"
          class="space-y-2 pl-4 border-l-2 border-sky-200 dark:border-sky-800">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">背景颜色</label>
            <div class="flex items-center gap-1.5 mt-0.5">
              <input type="color" :value="componentProps.bgColor || '#ffffff'"
                @input="updateProp('bgColor', ($event.target as HTMLInputElement).value)"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="componentProps.bgColor || '#ffffff'"
                @input="updateProp('bgColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1.5 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
            <div class="flex items-center gap-1 mt-1.5">
              <button v-for="bg in ['#ffffff', '#f8fafc', '#f1f5f9', '#e2e8f0', '#0f172a', '#1e293b', '#030712']"
                :key="bg" type="button" @click="updateProp('bgColor', bg)"
                class="w-4 h-4 rounded-full border border-black/20 dark:border-white/20 cursor-pointer transition-transform hover:scale-125"
                :style="{ backgroundColor: bg }" :title="bg" />
            </div>
          </div>
        </div>
      </div>

      <!-- 子项卡片样式设置 (字号/子项边框/底色) -->
      <div class="space-y-2 pt-2 border-t border-sky-100 dark:border-sky-900/40">
        <p class="font-bold text-gray-700 dark:text-slate-300 text-[10px]">子项与文字样式</p>

        <div class="grid grid-cols-2 gap-2">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">数值字号 (px)</label>
            <input type="number" min="12" max="32" :value="componentProps.dashboardValueFontSize ?? 16"
              @input="updateProp('dashboardValueFontSize', numInput(($event.target as HTMLInputElement).value, 16))"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none text-xs mt-0.5" />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">标签字号 (px)</label>
            <input type="number" min="9" max="18" :value="componentProps.dashboardLabelFontSize ?? 11"
              @input="updateProp('dashboardLabelFontSize', numInput(($event.target as HTMLInputElement).value, 11))"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none text-xs mt-0.5" />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-2">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">子卡片底色</label>
            <div class="flex items-center gap-1 mt-0.5">
              <input type="color" :value="componentProps.dashboardItemBgColor || '#f8fafc'"
                @input="updateProp('dashboardItemBgColor', ($event.target as HTMLInputElement).value)"
                class="w-6 h-6 bg-transparent border-0 cursor-pointer rounded" />
              <input type="text" :value="componentProps.dashboardItemBgColor || '#f8fafc'"
                @input="updateProp('dashboardItemBgColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
            </div>
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">子卡片边框色</label>
            <div class="flex items-center gap-1 mt-0.5">
              <input type="color" :value="componentProps.dashboardItemBorderColor || '#e2e8f0'"
                @input="updateProp('dashboardItemBorderColor', ($event.target as HTMLInputElement).value)"
                class="w-6 h-6 bg-transparent border-0 cursor-pointer rounded" />
              <input type="text" :value="componentProps.dashboardItemBorderColor || '#e2e8f0'"
                @input="updateProp('dashboardItemBorderColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 模块二：多变量监控列表管理 (dashboardItems) -->
    <div
      class="space-y-3 text-xs border border-emerald-200/80 dark:border-emerald-900/60 p-3 rounded-lg bg-emerald-50/40 dark:bg-emerald-950/20">
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-1.5">
          <p class="font-bold text-emerald-600 dark:text-emerald-400 text-[11px] uppercase tracking-wider">
            多变量监控点位列表
          </p>
          <span
            class="text-[9px] font-mono px-1.5 py-0.5 rounded-full bg-emerald-100 dark:bg-emerald-900/60 text-emerald-700 dark:text-emerald-300">
            {{ dashboardItems.length }} 项
          </span>
        </div>

        <!-- 一键导入按钮 -->
        <button type="button" @click="importAllVariablesFromDevice()"
          class="flex items-center gap-1 px-2 py-1 rounded bg-emerald-600 hover:bg-emerald-500 text-white text-[10px] font-medium transition-all shadow-sm cursor-pointer"
          title="一键将当前设备的所有变量导入到看板中">
          <Sparkles class="w-3 h-3" />
          <span>导入设备全部变量</span>
        </button>
      </div>

      <!-- 空列表提示 -->
      <div v-if="dashboardItems.length === 0"
        class="p-4 rounded border border-dashed border-slate-300 dark:border-slate-700 bg-white/60 dark:bg-slate-900/60 text-center space-y-2">
        <p class="text-xs text-slate-500 dark:text-slate-400">暂未添加任何变量点位</p>
        <div class="flex items-center justify-center gap-2">
          <button type="button" @click="addDashboardItem"
            class="px-3 py-1 rounded bg-[#1890ff] text-white text-xs font-medium hover:bg-[#40a9ff] transition-colors cursor-pointer">
            + 添加单项变量
          </button>
          <button type="button" @click="importAllVariablesFromDevice()"
            class="px-3 py-1 rounded bg-emerald-600 text-white text-xs font-medium hover:bg-emerald-500 transition-colors cursor-pointer">
            一键导入全部
          </button>
        </div>
      </div>

      <!-- 变量条目列表 -->
      <div v-else class="space-y-2.5 max-h-[480px] overflow-y-auto pr-0.5">
        <div v-for="(item, idx) in dashboardItems" :key="item.id || idx"
          class="p-2.5 rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 shadow-sm space-y-2 transition-all hover:border-sky-300 dark:hover:border-sky-800">
          <!-- 头部：序号 + 移动排序 + 删除 -->
          <div class="flex items-center justify-between pb-1 border-b border-slate-100 dark:border-slate-800">
            <div class="flex items-center gap-1.5">
              <span
                class="w-4 h-4 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 text-[10px] font-mono font-bold flex items-center justify-center">
                {{ idx + 1 }}
              </span>
              <span class="font-bold text-slate-800 dark:text-slate-200 text-xs truncate max-w-[120px]">
                {{ item.label || item.variableKey }}
              </span>
            </div>

            <div class="flex items-center gap-1">
              <button type="button" @click="moveDashboardItem(idx, -1)" :disabled="idx === 0"
                class="p-1 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer"
                title="上移">
                <ChevronUp class="w-3.5 h-3.5" />
              </button>
              <button type="button" @click="moveDashboardItem(idx, 1)"
                :disabled="idx === dashboardItems.length - 1"
                class="p-1 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer"
                title="下移">
                <ChevronDown class="w-3.5 h-3.5" />
              </button>
              <button type="button" @click="removeDashboardItem(idx)"
                class="p-1 rounded text-rose-500 hover:bg-rose-50 dark:hover:bg-rose-950/40 transition-colors cursor-pointer"
                title="删除此变量项">
                <Trash2 class="w-3.5 h-3.5" />
              </button>
            </div>
          </div>

          <!-- 变量绑定设置 (设备 + 变量) -->
          <div class="grid grid-cols-2 gap-1.5">
            <div>
              <label class="text-[9px] text-slate-400">所属设备</label>
              <select :value="item.deviceId ?? component.bindDeviceId ?? devices[0]?.id ?? ''"
                @change="updateDashboardItem(idx, { deviceId: ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null })"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]">
                <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
              </select>
            </div>
            <div>
              <label class="text-[9px] text-slate-400">绑定变量</label>
              <select :value="item.variableKey" @change="updateDashboardItem(idx, {
                variableKey: ($event.target as HTMLSelectElement).value,
                label: item.label || ($event.target as HTMLSelectElement).value
              })"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]">
                <option v-for="v in getItemVariableOptions(item.deviceId)" :key="v.key" :value="v.key">
                  {{ v.name }} ({{ v.key }})
                </option>
              </select>
            </div>
          </div>

          <!-- 自定义名称与单位 -->
          <div class="grid grid-cols-2 gap-1.5">
            <div>
              <label class="text-[9px] text-slate-400">自定义显示名称</label>
              <input type="text" :value="item.label ?? ''"
                @input="updateDashboardItem(idx, { label: ($event.target as HTMLInputElement).value })"
                placeholder="自动显示变量名"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-white focus:outline-none focus:border-[#1890ff]" />
            </div>
            <div>
              <label class="text-[9px] text-slate-400">单位 (Unit)</label>
              <input type="text" :value="item.unit ?? ''"
                @input="updateDashboardItem(idx, { unit: ($event.target as HTMLInputElement).value })"
                placeholder="例如 ℃, MPa, A"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-white focus:outline-none focus:border-[#1890ff]" />
            </div>
          </div>

          <!-- 小数位与指示灯开关 -->
          <div class="grid grid-cols-2 gap-1.5 pt-0.5">
            <div>
              <label class="text-[9px] text-slate-400">小数位数</label>
              <select :value="item.precision ?? ''"
                @change="updateDashboardItem(idx, { precision: ($event.target as HTMLSelectElement).value === '' ? null : Number(($event.target as HTMLSelectElement).value) })"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none">
                <option value="">自动</option>
                <option :value="0">0 位 (整数)</option>
                <option :value="1">1 位小数</option>
                <option :value="2">2 位小数</option>
                <option :value="3">3 位小数</option>
                <option :value="4">4 位小数</option>
              </select>
            </div>
            <div class="flex items-center gap-1.5 pt-4">
              <input type="checkbox" :id="`dot-${idx}`" :checked="item.showStatusDot !== false"
                @change="updateDashboardItem(idx, { showStatusDot: ($event.target as HTMLInputElement).checked })"
                class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
              <label :for="`dot-${idx}`"
                class="text-[10px] text-slate-700 dark:text-slate-300 cursor-pointer select-none">
                显示状态指示圆点
              </label>
            </div>
          </div>

          <!-- 阈值报警设置 (可选) -->
          <div
            class="grid grid-cols-2 gap-1.5 pt-1 border-t border-dashed border-slate-100 dark:border-slate-800">
            <div>
              <label class="text-[9px] text-amber-600 dark:text-amber-400">低限预警值 (≤ 变黄)</label>
              <input type="number" :value="item.thresholdMin ?? ''"
                @input="updateDashboardItem(idx, { thresholdMin: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
                placeholder="默认不设"
                class="w-full bg-amber-50/40 dark:bg-amber-950/20 border border-amber-200 dark:border-amber-900 rounded px-1.5 py-0.5 text-[10px] text-amber-800 dark:text-amber-300 focus:outline-none" />
            </div>
            <div>
              <label class="text-[9px] text-rose-600 dark:text-rose-400">高限报警值 (≥ 变红)</label>
              <input type="number" :value="item.thresholdMax ?? ''"
                @input="updateDashboardItem(idx, { thresholdMax: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
                placeholder="默认不设"
                class="w-full bg-rose-50/40 dark:bg-rose-950/20 border border-rose-200 dark:border-rose-900 rounded px-1.5 py-0.5 text-[10px] text-rose-800 dark:text-rose-300 focus:outline-none" />
            </div>
          </div>
        </div>
      </div>

      <!-- 底部新增按钮 -->
      <button type="button" @click="addDashboardItem"
        class="w-full py-1.5 rounded border border-dashed border-emerald-400 dark:border-emerald-700 bg-white/70 dark:bg-slate-900/70 hover:bg-emerald-50 dark:hover:bg-emerald-950/40 text-emerald-700 dark:text-emerald-300 text-xs font-semibold flex items-center justify-center gap-1.5 transition-colors cursor-pointer">
        <Plus class="w-3.5 h-3.5" />
        <span>添加监控变量项</span>
      </button>
    </div>
  </div>
</template>
