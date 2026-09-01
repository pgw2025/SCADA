<script setup lang="ts">
// trend-chart 趋势图检查器：多序列绑定 + 坐标轴与显示配置
// 从 InspectorPanel.vue 抽出（Phase 2a）；通信：component 下行 / updateProp 上行
import { computed, watch } from 'vue';
import { HMIComponent, HmiTrendSeries } from '../../types';
import { devices } from '../../store/deviceStore';
import { TREND_SERIES_PALETTE } from '../../widgetRegistry';
import { Sparkles, ChevronUp, ChevronDown, Trash2, Plus } from 'lucide-vue-next';

const props = defineProps<{
  component: HMIComponent;
}>();

const emit = defineEmits<{
  (e: 'updateProp', key: string, value: any): void;
}>();

const componentProps = computed(() => props.component.props ?? {});

const updateProp = (key: string, value: any) => emit('updateProp', key, value);

// ===== 趋势图多序列（trend-chart）编辑器逻辑：镜像 dashboard 子项编辑器 =====
const trendSeries = computed<HmiTrendSeries[]>(() => {
  const raw = componentProps.value.trendSeries;
  return Array.isArray(raw) ? (raw as HmiTrendSeries[]) : [];
});

const commitTrendSeries = (items: HmiTrendSeries[]) => updateProp('trendSeries', items);

const updateTrendSeries = (index: number, patch: Partial<HmiTrendSeries>) => {
  const next = trendSeries.value.map((it, i) => (i === index ? { ...it, ...patch } : it));
  commitTrendSeries(next);
};

const addTrendSeries = () => {
  const devId = props.component.bindDeviceId ?? devices.value[0]?.id ?? null;
  const dev = devices.value.find((d) => d.id === devId) || devices.value[0];
  const keys = dev ? Object.keys(dev.variables || {}) : [];
  const existingKeys = new Set(trendSeries.value.map((it) => `${it.deviceId ?? ''}:${it.variableKey}`));
  const unusedKey = keys.find((k) => !existingKeys.has(`${dev?.id ?? ''}:${k}`)) || keys[0] || 'var_1';
  const meta = dev?.variableMeta?.[unusedKey];
  const color = TREND_SERIES_PALETTE[trendSeries.value.length % TREND_SERIES_PALETTE.length];
  const newItem: HmiTrendSeries = {
    id: `series-${Date.now()}-${trendSeries.value.length + 1}`,
    deviceId: dev?.id ?? null,
    variableKey: unusedKey,
    label: meta?.name || unusedKey,
    unit: meta?.unit || '',
    color,
    lineWidth: 2,
    minValue: null,
    maxValue: null,
    precision: typeof dev?.variables?.[unusedKey] === 'number' ? 1 : null,
    thresholdMin: null,
    thresholdMax: null,
  };
  commitTrendSeries([...trendSeries.value, newItem]);
};

const removeTrendSeries = (index: number) =>
  commitTrendSeries(trendSeries.value.filter((_, i) => i !== index));

const moveTrendSeries = (index: number, dir: -1 | 1) => {
  const to = index + dir;
  if (to < 0 || to >= trendSeries.value.length) return;
  const next = [...trendSeries.value];
  [next[index], next[to]] = [next[to], next[index]];
  commitTrendSeries(next);
};

const importAllVariablesForTrend = (targetDevId?: number | null) => {
  const devId = targetDevId ?? props.component.bindDeviceId ?? devices.value[0]?.id;
  const dev = devices.value.find((d) => d.id === devId);
  if (!dev || !dev.variables) return;
  const newItems: HmiTrendSeries[] = Object.keys(dev.variables).map((k, idx) => {
    const meta = dev.variableMeta?.[k];
    const isNum = typeof dev.variables[k] === 'number';
    return {
      id: `series-${dev.id}-${k}-${Date.now()}-${idx}`,
      deviceId: dev.id,
      variableKey: k,
      label: meta?.name || k,
      unit: meta?.unit || '',
      color: TREND_SERIES_PALETTE[idx % TREND_SERIES_PALETTE.length],
      lineWidth: 2,
      minValue: null,
      maxValue: null,
      precision: isNum ? 2 : null,
      thresholdMin: null,
      thresholdMax: null,
    };
  });
  commitTrendSeries(newItems);
};

// 获取某个序列对应设备下的变量选项
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

// D4-A：选中趋势图且尚无序列、但有旧式单绑定时，一次性迁移为第 1 条序列（向后兼容）
watch(
  () => props.component,
  (c) => {
    if (c?.type !== 'trend-chart') return;
    const existing = Array.isArray(c.props?.trendSeries) ? c.props.trendSeries : [];
    if (existing.length === 0 && c.bindDeviceId != null && c.bindVariableKey) {
      commitTrendSeries([{
        id: 'legacy',
        deviceId: c.bindDeviceId,
        variableKey: c.bindVariableKey,
        label: c.bindVariableKey,
        color: TREND_SERIES_PALETTE[0],
        lineWidth: 2,
        minValue: null,
        maxValue: null,
        precision: null,
        thresholdMin: null,
        thresholdMax: null,
      }]);
    }
  },
  { immediate: true }
);
</script>

<template>
  <!-- 趋势图多序列绑定（trend-chart）：支持多变量 + 逐线颜色/粗细自定义 -->
  <div
    class="space-y-3 text-xs border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/40 dark:bg-sky-950/20">
    <div class="flex items-center justify-between">
      <div class="flex items-center gap-1.5">
        <p class="font-bold text-sky-600 dark:text-sky-400 text-[11px] uppercase tracking-wider">趋势序列 (多变量)</p>
        <span
          class="text-[9px] font-mono px-1.5 py-0.5 rounded-full bg-sky-100 dark:bg-sky-900/60 text-sky-700 dark:text-sky-300">
          {{ trendSeries.length }} 条
        </span>
      </div>
      <button type="button" @click="importAllVariablesForTrend()"
        class="flex items-center gap-1 px-2 py-1 rounded bg-sky-600 hover:bg-sky-500 text-white text-[10px] font-medium transition-all shadow-sm cursor-pointer"
        title="一键将当前设备的所有变量导入为趋势序列">
        <Sparkles class="w-3 h-3" />
        <span>导入设备全部变量</span>
      </button>
    </div>

    <!-- 空列表提示 -->
    <div v-if="trendSeries.length === 0"
      class="p-4 rounded border border-dashed border-slate-300 dark:border-slate-700 bg-white/60 dark:bg-slate-900/60 text-center space-y-2">
      <p class="text-xs text-slate-500 dark:text-slate-400">暂未添加任何趋势序列</p>
      <div class="flex items-center justify-center gap-2">
        <button type="button" @click="addTrendSeries"
          class="px-3 py-1 rounded bg-[#1890ff] text-white text-xs font-medium hover:bg-[#40a9ff] transition-colors cursor-pointer">
          + 添加序列
        </button>
        <button type="button" @click="importAllVariablesForTrend()"
          class="px-3 py-1 rounded bg-sky-600 text-white text-xs font-medium hover:bg-sky-500 transition-colors cursor-pointer">
          一键导入全部
        </button>
      </div>
      <p v-if="component.bindDeviceId == null || !component.bindVariableKey"
        class="text-[9px] text-amber-600 dark:text-amber-400">提示：也可先在上方「变量绑定」区绑定一个变量，打开此组件时会自动升级为第 1 条序列。</p>
    </div>

    <!-- 序列条目列表 -->
    <div v-else class="space-y-2.5 max-h-[480px] overflow-y-auto pr-0.5">
      <div v-for="(s, idx) in trendSeries" :key="s.id || idx"
        class="p-2.5 rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 shadow-sm space-y-2 transition-all hover:border-sky-300 dark:hover:border-sky-800">
        <!-- 头部：序号 + 移动排序 + 删除 -->
        <div class="flex items-center justify-between pb-1 border-b border-slate-100 dark:border-slate-800">
          <div class="flex items-center gap-1.5">
            <span class="w-4 h-4 rounded-full flex items-center justify-center text-[10px] font-mono font-bold text-white"
              :style="{ background: s.color }">{{ idx + 1 }}</span>
            <span class="font-bold text-slate-800 dark:text-slate-200 text-xs truncate max-w-[120px]">
              {{ s.label || s.variableKey }}
            </span>
          </div>
          <div class="flex items-center gap-1">
            <button type="button" @click="moveTrendSeries(idx, -1)" :disabled="idx === 0"
              class="p-1 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer"
              title="上移">
              <ChevronUp class="w-3.5 h-3.5" />
            </button>
            <button type="button" @click="moveTrendSeries(idx, 1)" :disabled="idx === trendSeries.length - 1"
              class="p-1 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer"
              title="下移">
              <ChevronDown class="w-3.5 h-3.5" />
            </button>
            <button type="button" @click="removeTrendSeries(idx)"
              class="p-1 rounded text-rose-500 hover:bg-rose-50 dark:hover:bg-rose-950/40 transition-colors cursor-pointer" title="删除此序列">
              <Trash2 class="w-3.5 h-3.5" />
            </button>
          </div>
        </div>

        <!-- 变量绑定设置 (设备 + 变量) -->
        <div class="grid grid-cols-2 gap-1.5">
          <div>
            <label class="text-[9px] text-slate-400">所属设备</label>
            <select :value="s.deviceId ?? component.bindDeviceId ?? devices[0]?.id ?? ''"
              @change="updateTrendSeries(idx, { deviceId: ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null })"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]">
              <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
            </select>
          </div>
          <div>
            <label class="text-[9px] text-slate-400">绑定变量</label>
            <select :value="s.variableKey"
              @change="updateTrendSeries(idx, { variableKey: ($event.target as HTMLSelectElement).value, label: s.label || ($event.target as HTMLSelectElement).value })"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]">
              <option v-for="v in getItemVariableOptions(s.deviceId)" :key="v.key" :value="v.key">
                {{ v.name }} ({{ v.key }})
              </option>
            </select>
          </div>
        </div>

        <!-- 图例名称 + 单位 -->
        <div class="grid grid-cols-2 gap-1.5">
          <div>
            <label class="text-[9px] text-slate-400">图例名称</label>
            <input type="text" :value="s.label ?? ''"
              @input="updateTrendSeries(idx, { label: ($event.target as HTMLInputElement).value })"
              placeholder="自动显示变量名"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-white focus:outline-none focus:border-[#1890ff]" />
          </div>
          <div>
            <label class="text-[9px] text-slate-400">单位 (Unit)</label>
            <input type="text" :value="s.unit ?? ''"
              @input="updateTrendSeries(idx, { unit: ($event.target as HTMLInputElement).value })"
              placeholder="例如 ℃, MPa"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-white focus:outline-none focus:border-[#1890ff]" />
          </div>
        </div>

        <!-- 颜色 + 线宽 -->
        <div class="grid grid-cols-2 gap-1.5 items-end">
          <div>
            <label class="text-[9px] text-slate-400">线条颜色</label>
            <div class="flex items-center gap-1.5">
              <input type="color" :value="s.color"
                @input="updateTrendSeries(idx, { color: ($event.target as HTMLInputElement).value })"
                class="w-8 h-7 p-0 border border-slate-200 dark:border-slate-700 rounded bg-transparent cursor-pointer" />
              <span class="text-[10px] font-mono text-slate-500">{{ s.color }}</span>
            </div>
          </div>
          <div>
            <label class="text-[9px] text-slate-400">线条粗细 (px): {{ s.lineWidth }}</label>
            <input type="range" min="1" max="8" step="0.5" :value="s.lineWidth"
              @input="updateTrendSeries(idx, { lineWidth: Number(($event.target as HTMLInputElement).value) })"
              class="w-full accent-[#1890ff] dark:accent-sky-500" />
          </div>
        </div>

        <!-- 序列级量程/阈值（可选；空则按全局自适应） -->
        <div class="grid grid-cols-2 gap-1.5 pt-1 border-t border-dashed border-slate-100 dark:border-slate-800">
          <div>
            <label class="text-[9px] text-slate-400">量程下限 (空=全局)</label>
            <input type="number" :value="s.minValue ?? ''"
              @input="updateTrendSeries(idx, { minValue: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
              placeholder="全局自适应"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px] text-slate-800 dark:text-slate-200 focus:outline-none" />
          </div>
          <div>
            <label class="text-[9px] text-slate-400">量程上限 (空=全局)</label>
            <input type="number" :value="s.maxValue ?? ''"
              @input="updateTrendSeries(idx, { maxValue: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
              placeholder="全局自适应"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px] text-slate-800 dark:text-slate-200 focus:outline-none" />
          </div>
          <div>
            <label class="text-[9px] text-amber-600 dark:text-amber-400">低限预警 (≤ 变黄)</label>
            <input type="number" :value="s.thresholdMin ?? ''"
              @input="updateTrendSeries(idx, { thresholdMin: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
              placeholder="默认不设"
              class="w-full bg-amber-50/40 dark:bg-amber-950/20 border border-amber-200 dark:border-amber-900 rounded px-1.5 py-0.5 text-[10px] text-amber-800 dark:text-amber-300 focus:outline-none" />
          </div>
          <div>
            <label class="text-[9px] text-rose-600 dark:text-rose-400">高限报警 (≥ 变红)</label>
            <input type="number" :value="s.thresholdMax ?? ''"
              @input="updateTrendSeries(idx, { thresholdMax: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
              placeholder="默认不设"
              class="w-full bg-rose-50/40 dark:bg-rose-950/20 border border-rose-200 dark:border-rose-900 rounded px-1.5 py-0.5 text-[10px] text-rose-800 dark:text-rose-300 focus:outline-none" />
          </div>
        </div>
      </div>
    </div>

    <!-- 坐标轴与显示配置 -->
    <div class="pt-2 mt-2 border-t border-slate-200 dark:border-slate-800 space-y-2">
      <p class="font-bold text-sky-600 dark:text-sky-400 text-[10px] uppercase tracking-wider">坐标轴与显示</p>

      <!-- 坐标模式 -->
      <div class="flex items-center gap-2">
        <label class="text-[9px] text-slate-400 w-14 shrink-0">坐标模式</label>
        <select :value="componentProps.trendAxisMode ?? 'absolute'"
          @change="updateProp('trendAxisMode', ($event.target as HTMLSelectElement).value)"
          class="flex-1 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]">
          <option value="absolute">绝对坐标（工程量值）</option>
          <option value="relative">相对坐标（0–100%）</option>
        </select>
      </div>

      <!-- 手动范围 -->
      <div class="grid grid-cols-2 gap-1.5">
        <div>
          <label class="text-[9px] text-slate-400">Y 轴下限（空=自动）</label>
          <input type="number" :value="componentProps.trendAxisMin ?? ''"
            @input="updateProp('trendAxisMin', ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value))"
            placeholder="自动"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px] text-slate-800 dark:text-slate-200 focus:outline-none" />
        </div>
        <div>
          <label class="text-[9px] text-slate-400">Y 轴上限（空=自动）</label>
          <input type="number" :value="componentProps.trendAxisMax ?? ''"
            @input="updateProp('trendAxisMax', ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value))"
            placeholder="自动"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px] text-slate-800 dark:text-slate-200 focus:outline-none" />
        </div>
      </div>

      <!-- 开关：网格 / 刻度 / 点位值 -->
      <div class="grid grid-cols-3 gap-1.5">
        <label class="flex items-center gap-1 text-[9px] text-slate-500 cursor-pointer">
          <input type="checkbox" :checked="componentProps.trendShowGrid !== false"
            @change="updateProp('trendShowGrid', ($event.target as HTMLInputElement).checked)" class="accent-[#1890ff]" /> 网格
        </label>
        <label class="flex items-center gap-1 text-[9px] text-slate-500 cursor-pointer">
          <input type="checkbox" :checked="componentProps.trendShowAxisLabels !== false"
            @change="updateProp('trendShowAxisLabels', ($event.target as HTMLInputElement).checked)" class="accent-[#1890ff]" /> 刻度
        </label>
        <label class="flex items-center gap-1 text-[9px] text-slate-500 cursor-pointer">
          <input type="checkbox" :checked="componentProps.trendShowPointValues === true"
            @change="updateProp('trendShowPointValues', ($event.target as HTMLInputElement).checked)" class="accent-[#1890ff]" /> 点位值
        </label>
      </div>

      <!-- 字号 -->
      <div class="grid grid-cols-2 gap-1.5">
        <div>
          <label class="text-[9px] text-slate-400">刻度字号: {{ componentProps.trendAxisLabelFontSize ?? 8 }}px</label>
          <input type="range" min="6" max="16" step="1" :value="componentProps.trendAxisLabelFontSize ?? 8"
            @input="updateProp('trendAxisLabelFontSize', Number(($event.target as HTMLInputElement).value))" class="w-full accent-[#1890ff]" />
        </div>
        <div>
          <label class="text-[9px] text-slate-400">点位值字号: {{ componentProps.trendPointValueFontSize ?? 8 }}px</label>
          <input type="range" min="6" max="16" step="1" :value="componentProps.trendPointValueFontSize ?? 8"
            @input="updateProp('trendPointValueFontSize', Number(($event.target as HTMLInputElement).value))" class="w-full accent-[#1890ff]" />
        </div>
      </div>

      <!-- 点位值颜色 -->
      <div class="flex items-center gap-1.5">
        <label class="text-[9px] text-slate-400 w-14 shrink-0">点位值色</label>
        <select :value="componentProps.trendPointValueColor ?? 'auto'"
          @change="updateProp('trendPointValueColor', ($event.target as HTMLSelectElement).value)"
          class="flex-1 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]">
          <option value="auto">跟随序列色</option>
          <option value="#e2e8f0">浅灰 #e2e8f0</option>
          <option value="#f8fafc">白色 #f8fafc</option>
          <option value="#facc15">黄 #facc15</option>
          <option value="#f87171">红 #f87171</option>
        </select>
      </div>
    </div>

    <!-- 底部新增按钮 -->
    <button type="button" @click="addTrendSeries"
      class="w-full py-1.5 rounded border border-dashed border-sky-400 dark:border-sky-700 bg-white/70 dark:bg-slate-900/70 hover:bg-sky-50 dark:hover:bg-sky-950/40 text-sky-700 dark:text-sky-300 text-xs font-semibold flex items-center justify-center gap-1.5 transition-colors cursor-pointer">
      <Plus class="w-3.5 h-3.5" />
      <span>添加趋势序列</span>
    </button>
  </div>
</template>
