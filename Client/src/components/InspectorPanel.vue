<script setup lang="ts">
import { computed, ref } from 'vue';
import { HMIComponent, ScadaPage, HMILayer } from '../types';
import { devices } from '../store/deviceStore';
import { desktopPages, mobilePages, currentPlatform } from '../store/scadaStore';
import { getWidgetDef } from '../widgetRegistry';
import { BUILTIN_SCHEMAS } from '../propSchemas';
import { Settings, Tag, Sliders, Layout, Hash, ChevronRight, Eye, EyeOff, Lock, Unlock, Sparkles } from 'lucide-vue-next';
import ImageLibraryDialog from './ImageLibraryDialog.vue';
import PageBackgroundInspector from './inspector/PageBackgroundInspector.vue';
import TrendChartInspector from './inspector/TrendChartInspector.vue';
import MultiVarDashboardInspector from './inspector/MultiVarDashboardInspector.vue';
import RoundedBtnInspector from './inspector/RoundedBtnInspector.vue';
import NavMenuInspector from './inspector/NavMenuInspector.vue';
import PropSchemaForm from './inspector/PropSchemaForm.vue';

const props = defineProps<{
  selectedComponent: HMIComponent | null;
  currentPageId?: string;
  /** 背景选中态：非空时显示「页面属性」表单（背景 + 自适应屏幕配置） */
  backgroundPage?: ScadaPage | null;
  /** 当前页面的图层列表 */
  layers?: HMILayer[];
}>();

const emit = defineEmits<{
  (e: 'updateComponent', id: string, updates: Partial<HMIComponent>): void;
  (e: 'updatePage', updates: Partial<ScadaPage>): void;
  (e: 'collapse'): void;
}>();

const componentProps = computed(() => {
  return props.selectedComponent?.props ?? {};
});

// 当前类型注册表默认 props：回显缺省值与运行态兜底共用同一真相源
const typeDefaults = computed(() =>
  getWidgetDef(props.selectedComponent?.type ?? '')?.defaultProps() ?? {}
);

// P5：属性 Schema（DB 模板 → 内置种子兜底；legacy button 等无注册类型直接查 BUILTIN_SCHEMAS）
const schemaItems = computed(() => {
  const t = props.selectedComponent?.type ?? '';
  if (!t) return [];
  return getWidgetDef(t)?.propSchema ?? BUILTIN_SCHEMAS[t] ?? [];
});

// Prop mutator helper - emits change upwards
const updateProp = (key: string, value: any) => {
  if (!props.selectedComponent) return;
  emit('updateComponent', props.selectedComponent.id, {
    props: {
      ...componentProps.value,
      [key]: value,
    },
  });
};

// 批量 props 应用（预设风格一键切换）：单次 emit 合并提交，避免逐 key 连续 emit 的竞态
const applyProps = (patch: Record<string, any>) => {
  if (!props.selectedComponent) return;
  emit('updateComponent', props.selectedComponent.id, {
    props: { ...componentProps.value, ...patch },
  });
};

const updateComponentField = (field: keyof HMIComponent, value: any) => {
  if (!props.selectedComponent) return;
  emit('updateComponent', props.selectedComponent.id, {
    [field]: value,
  });
};

// 解析数值输入：合法（含 0）原样写入，非法（NaN/空）回退缺省值。
// 修复「threshold 填 0 被写成 90/10」类问题。
const numInput = (raw: string, fallback: number): number => {
  const n = parseFloat(raw);
  return Number.isFinite(n) ? n : fallback;
};

// 阶段3：复合绑定（设备 + 变量）两级选择
const bindingVariableOptions = computed(() => {
  const dev = devices.value.find((d) => String(d.id) === String(props.selectedComponent?.bindDeviceId));
  if (dev && dev.variables) {
    return Object.keys(dev.variables).map((k) => ({ key: k }));
  }
  // 严格模式：必须先选设备，禁止裸 key 汇总全部变量键
  return [];
});

const onBindDeviceChange = (val: string) => {
  const id = val === '' ? null : Number(val);
  updateComponentField('bindDeviceId', id);
  updateComponentField('bindVariableKey', ''); // 设备变更后清空变量
  updateComponentField('bindField', ''); // 同时清除遗留 bindField，防止运行态拿旧值下发写指令
};

const onBindVariableChange = (val: string) => {
  updateComponentField('bindVariableKey', val);
  updateComponentField('bindField', val); // 同步遗留字段，兼容旧逻辑/HMIWidget 提示
};

// 阶段3：导航目标候选（仅限当前端画面，排除自身）。编辑器内按 currentPlatform 过滤，
// 保证「跨端跳转不允许」由设计约束（目标下拉不含异端画面）。
// value 用稳定引用：已落库存 `srv-{serverId}`（跨会话可比）；未落库页面暂存本地 id，
// 由跳转侧 normalizePageRef 兜底比较。
const navTargetOptions = computed(() => {
  const list = currentPlatform.value === 'Mobile' ? mobilePages.value : desktopPages.value;
  return list
    // 排除「当前页面」本身：页面 id 与组件 id 不可比，须用父级传入的 currentPageId
    .filter(p => p.id !== props.currentPageId)
    .map(p => ({ id: p.serverId ? `srv-${p.serverId}` : p.id, name: p.name }));
});

// ===== 图片图元：图库选图 =====
// 图元换图（updateProp 走既有防抖落库链路）
const showImagePicker = ref(false);
const onPickComponentImage = (img: { url: string }) => {
  showImagePicker.value = false;
  updateProp('imageUrl', img.url);
};
</script>

<template>
  <!-- 空态：未选中任何元件/背景 -->
  <div v-if="!selectedComponent && !backgroundPage"
    class="h-full bg-[#fafafa] dark:bg-slate-950 p-6 text-gray-400 dark:text-slate-500 text-xs flex flex-col justify-between items-center text-center transition-colors relative">
    <div class="w-full flex justify-end">
      <button @click="emit('collapse')"
        class="p-1 rounded text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
        title="收起属性面板">
        <ChevronRight class="w-4 h-4" />
      </button>
    </div>
    <div class="flex flex-col items-center justify-center my-auto">
      <!-- Spinning Cog -->
      <Settings class="w-8 h-8 text-[#1890ff] dark:text-sky-400 mb-2 animate-spin-slow opacity-60" />
      <p class="font-semibold text-gray-700 dark:text-slate-300">属性面板</p>
      <p class="text-[10px] text-gray-400 dark:text-slate-500 mt-2.5 max-w-[200px] leading-relaxed">
        请在画布上选择元件以配置属性。<br />点击画布空白背景可配置页面属性。
      </p>
    </div>
    <div class="h-4"></div>
  </div>

  <!-- 页面属性：点击画布背景后显示（背景设置 + 自适应屏幕设置）——已抽取为子组件 -->
  <PageBackgroundInspector v-else-if="backgroundPage" :background-page="backgroundPage"
    @update-page="(updates) => emit('updatePage', updates)" @collapse="emit('collapse')" />

  <div v-else
    class="h-full flex flex-col bg-white dark:bg-slate-900 text-[#262626] dark:text-slate-100 overflow-y-auto transition-colors">
    <!-- Title -->
    <div
      class="p-4 border-b border-[#f0f0f0] dark:border-slate-800 bg-[#fafafa] dark:bg-slate-950 flex items-center justify-between">
      <div class="flex items-center gap-2">
        <Layout class="w-4 h-4 text-[#1890ff] dark:text-sky-400" />
        <h3 class="text-xs font-bold text-[#141414] dark:text-slate-100 uppercase tracking-wider">
          属性配置
        </h3>
      </div>
      <button @click="emit('collapse')"
        class="p-1 rounded text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
        title="收起属性面板">
        <ChevronRight class="w-4 h-4" />
      </button>
    </div>

    <div class="p-4 space-y-4 text-left">
      <!-- Core Layout section -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <Sliders class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          布局属性
        </div>

        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400 font-mono">元件标识 (ID)</label>
            <input type="text" disabled :value="selectedComponent.id"
              class="w-full bg-[#fafafa] dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 mt-0.5 text-gray-400 dark:text-slate-500 font-mono text-[10px] cursor-not-allowed" />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">元件名称</label>
            <input type="text" :value="selectedComponent.name"
              @input="updateComponentField('name', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none" />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400 font-mono">X 轴坐标 (px)</label>
            <input type="number" :value="selectedComponent.x"
              @input="updateComponentField('x', parseInt(($event.target as HTMLInputElement).value) || 0)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white font-mono focus:outline-none" />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400 font-mono">Y 轴坐标 (px)</label>
            <input type="number" :value="selectedComponent.y"
              @input="updateComponentField('y', parseInt(($event.target as HTMLInputElement).value) || 0)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white font-mono focus:outline-none" />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400 font-mono">宽度 (Width)</label>
            <input type="number" :value="selectedComponent.width"
              @input="updateComponentField('width', parseInt(($event.target as HTMLInputElement).value) || 20)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white font-mono focus:outline-none" />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400 font-mono">高度 (Height)</label>
            <input type="number" :value="selectedComponent.height"
              @input="updateComponentField('height', parseInt(($event.target as HTMLInputElement).value) || 20)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white font-mono focus:outline-none" />
          </div>
        </div>

        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">图层顺序 (Z-Index)</label>
          <input type="number" :value="selectedComponent.zIndex ?? 1"
            @input="updateComponentField('zIndex', parseInt(($event.target as HTMLInputElement).value) || 1)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none" />
        </div>

        <!-- PS-style Layer Assignment & Component State -->
        <div v-if="layers && layers.length > 0">
          <label class="text-[10px] text-gray-500 dark:text-slate-400">所属图层 (PS 图层管理)</label>
          <select :value="selectedComponent.layerId || (layers[0]?.id ?? 'layer-default')"
            @change="updateComponentField('layerId', ($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option v-for="l in layers" :key="l.id" :value="l.id">
              {{ l.name }} {{ l.locked ? '🔒' : '' }} {{ l.visible === false ? '👁️(隐)' : '' }}
            </option>
          </select>
        </div>

        <div class="grid grid-cols-2 gap-2 text-xs pt-1">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">元件可见性</label>
            <button type="button"
              @click="updateComponentField('visible', selectedComponent.visible === false ? true : false)"
              class="w-full flex items-center justify-center gap-1.5 py-1.5 px-2 rounded border text-xs font-medium transition-colors mt-0.5 cursor-pointer"
              :class="selectedComponent.visible !== false
                ? 'bg-slate-100 dark:bg-slate-800 border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200'
                : 'bg-amber-50 dark:bg-amber-950/40 border-amber-300 dark:border-amber-800 text-amber-700 dark:text-amber-400'">
              <Eye v-if="selectedComponent.visible !== false" class="w-3.5 h-3.5 text-[#1890ff]" />
              <EyeOff v-else class="w-3.5 h-3.5 text-amber-500" />
              <span>{{ selectedComponent.visible !== false ? '显示 (正常)' : '隐藏 (画布隐藏)' }}</span>
            </button>
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">元件锁定状态</label>
            <button type="button"
              @click="updateComponentField('locked', selectedComponent.locked === true ? false : true)"
              class="w-full flex items-center justify-center gap-1.5 py-1.5 px-2 rounded border text-xs font-medium transition-colors mt-0.5 cursor-pointer"
              :class="selectedComponent.locked === true
                ? 'bg-rose-50 dark:bg-rose-950/40 border-rose-300 dark:border-rose-800 text-rose-700 dark:text-rose-400'
                : 'bg-slate-100 dark:bg-slate-800 border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200'">
              <Lock v-if="selectedComponent.locked === true" class="w-3.5 h-3.5 text-rose-500" />
              <Unlock v-else class="w-3.5 h-3.5 text-slate-400" />
              <span>{{ selectedComponent.locked === true ? '已锁定 (禁止拖拽)' : '未锁定 (可编辑)' }}</span>
            </button>
          </div>
        </div>
      </section>

      <div class="border-t border-[#f0f0f0] dark:border-slate-800 my-4" />

      <!-- PLC Register binding selector -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <Tag class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          数据绑定
        </div>

        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">
            {{ selectedComponent?.type === 'multi-var-dashboard' ? '默认绑定设备（预设设备）' : '绑定设备' }}
          </label>
          <select :value="selectedComponent?.bindDeviceId ?? ''"
            @change="onBindDeviceChange(($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option value="">-- 未绑定设备（禁止裸 key）--</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }} ({{ d.key }})</option>
          </select>
          <p v-if="selectedComponent?.bindDeviceId == null && selectedComponent?.type !== 'multi-var-dashboard'"
            class="text-[10px] text-amber-600 dark:text-amber-400 mt-1 leading-relaxed">
            未绑定设备：运行态将无法定位变量值，且禁止裸 key 写入。请先选择设备。
          </p>
        </div>
        <div v-if="selectedComponent?.type !== 'multi-var-dashboard'">
          <label class="text-[10px] text-gray-500 dark:text-slate-400">绑定变量</label>
          <select
            :value="(selectedComponent?.bindDeviceId != null ? selectedComponent?.bindVariableKey : selectedComponent?.bindField) ?? ''"
            @change="onBindVariableChange(($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option value="">-- 无绑定 --</option>
            <option v-for="v in bindingVariableOptions" :key="v.key" :value="v.key">{{ v.key }}</option>
          </select>
        </div>
        <div v-else
          class="text-[10px] text-sky-600 dark:text-sky-400 bg-sky-50 dark:bg-sky-950/40 border border-sky-200 dark:border-sky-900 rounded p-2 flex items-start gap-1.5">
          <Sparkles class="w-3.5 h-3.5 shrink-0 mt-0.5" />
          <span>多变量看板支持绑定任意多个变量点位，请在下方「多变量监控列表」中管理和配置具体点位。</span>
        </div>
      </section>

      <div class="border-t border-[#f0f0f0] dark:border-slate-800 my-4" />

      <!-- Widget specifics customization -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <Hash class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          组件属性
        </div>

        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">标签</label>
          <textarea rows="2" :value="selectedComponent.label"
            @input="updateComponentField('label', ($event.target as HTMLTextAreaElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs" />
        </div>

        <!-- P5：通用属性由 PropSchema 驱动渲染（颜色/量程/阈值/状态文案/外观显隐/字体等，按类型 schema 过滤） -->
        <PropSchemaForm
          v-if="schemaItems.length > 0"
          :schema="schemaItems"
          :props="componentProps"
          :defaults="typeDefaults"
          @update-prop="(key, value) => updateProp(key, value)"
        />

        <!-- 趋势图多序列绑定（trend-chart）：支持多变量 + 逐线颜色/粗细自定义——已抽取为子组件 -->
        <TrendChartInspector v-if="selectedComponent.type === 'trend-chart'" :component="selectedComponent"
          @update-prop="(key, value) => updateProp(key, value)" />

        <!-- var-display 数据变量显示专属配置：小数位 / 可设定 / 写入范围 / 二次确认 -->
        <div v-if="selectedComponent.type === 'var-display'"
          class="space-y-2.5 text-xs border border-gray-100 dark:border-slate-800 p-2.5 rounded bg-gray-50/50 dark:bg-slate-950/60">
          <p class="font-bold text-[#1890ff] dark:text-sky-400 text-[10px] uppercase tracking-wider">变量显示配置</p>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">小数位数 (0~4)</label>
            <select :value="componentProps.decimals ?? 2"
              @change="updateProp('decimals', numInput(($event.target as HTMLSelectElement).value, 2))"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option v-for="n in [0, 1, 2, 3, 4]" :key="n" :value="n">{{ n }} 位</option>
            </select>
            <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-0.5 leading-snug">
              显示与设值弹窗输入同步约束；写入前按位数四舍五入。
            </p>
          </div>

          <div class="flex items-center gap-2">
            <input type="checkbox" id="settableDef" :checked="componentProps.settable === true"
              @change="updateProp('settable', ($event.target as HTMLInputElement).checked)"
              class="accent-[#1890ff] dark:accent-sky-500" />
            <label for="settableDef" class="text-[11px] text-gray-700 dark:text-slate-300 cursor-pointer">
              可设定（运行态点击弹出数字键盘写值）
            </label>
          </div>
          <p v-if="componentProps.settable !== true"
            class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug -mt-1.5">
            未开启时组件仅作显示；写值仍需绑定设备/变量且有 Operator/Admin 权限。
          </p>

          <template v-if="componentProps.settable === true">
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-[10px] text-gray-500 dark:text-slate-400">写入下限（空=不限）</label>
                <input type="number" :value="componentProps.writeMin ?? ''"
                  @input="updateProp('writeMin', ($event.target as HTMLInputElement).value === '' ? null : numInput(($event.target as HTMLInputElement).value, 0))"
                  class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500"
                  placeholder="不限制" />
              </div>
              <div>
                <label class="text-[10px] text-gray-500 dark:text-slate-400">写入上限（空=不限）</label>
                <input type="number" :value="componentProps.writeMax ?? ''"
                  @input="updateProp('writeMax', ($event.target as HTMLInputElement).value === '' ? null : numInput(($event.target as HTMLInputElement).value, 0))"
                  class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500"
                  placeholder="不限制" />
              </div>
            </div>
            <div class="flex items-center gap-2">
              <input type="checkbox" id="confirmReqDef" :checked="componentProps.confirmRequired === true"
                @change="updateProp('confirmRequired', ($event.target as HTMLInputElement).checked)"
                class="accent-[#1890ff] dark:accent-sky-500" />
              <label for="confirmReqDef" class="text-[11px] text-gray-700 dark:text-slate-300 cursor-pointer">
                写入前二次确认（高危变量防误写）
              </label>
            </div>
          </template>
        </div>

        <!-- INDUSTRIAL BUTTON SPECIFIC CONTROLS -->
        <div v-if="selectedComponent.type === 'button'"
          class="space-y-2 text-xs border border-gray-100 dark:border-slate-800 p-2 rounded bg-gray-50/50 dark:bg-slate-950/60">
          <p class="font-bold text-[#1890ff] dark:text-sky-400 text-[10px] uppercase tracking-wider mb-1">按钮功能配置</p>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">操作类型 (Action Mode)</label>
            <select :value="componentProps.buttonMode || 'toggle'"
              @change="updateProp('buttonMode', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option value="toggle">主锁/自锁 (Toggle - 单击取反)</option>
              <option value="momentary">按1送0 / 点动 (Momentary - 按下1松开0)</option>
              <option value="set-bit">置位 (SetBit - 写入1)</option>
              <option value="reset-bit">复位 (ResetBit - 写入0)</option>
              <option value="set-value">恒定设值 (SetValue - 写入固定值)</option>
              <option value="navigate">画面跳转 (Navigate - 跳转到同端其它画面)</option>
            </select>
          </div>

          <div v-if="componentProps.buttonMode === 'set-value'">
            <label class="text-[10px] text-gray-500 dark:text-slate-400">点击写入的数值</label>
            <input type="number" :value="componentProps.clickValue ?? 1"
              @input="updateProp('clickValue', numInput(($event.target as HTMLInputElement).value, 1))"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs" />
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">按钮文本说明 (Static Label)</label>
            <input type="text" :value="componentProps.buttonText ?? ''"
              @input="updateProp('buttonText', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
              placeholder="默认取本级Label" />
          </div>

          <!-- 阶段3：导航模式 → 选择同端目标画面 -->
          <div v-if="componentProps.buttonMode === 'navigate'">
            <label class="text-[10px] text-gray-500 dark:text-slate-400">跳转目标画面（仅同端）</label>
            <select :value="componentProps.targetPageId ?? ''"
              @change="updateProp('targetPageId', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option value="">-- 请选择目标画面 --</option>
              <option v-for="opt in navTargetOptions" :key="opt.id" :value="opt.id">{{ opt.name }}</option>
            </select>
            <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-1 leading-snug">
              运行时点击该按钮将跳转到所选画面；跨端跳转不允许，下拉仅列出「{{ currentPlatform === 'Mobile' ? '移动端' : '桌面端' }}」画面。
            </p>
          </div>
        </div>

        <!-- NAV-MENU SPECIFIC CONTROLS（导航菜单专属配置：3~5 项，图标/文字/跳转目标）——已抽取为子组件 -->
        <NavMenuInspector v-if="selectedComponent.type === 'nav-menu'" :component="selectedComponent"
          :nav-target-options="navTargetOptions" @update-prop="(key, value) => updateProp(key, value)" />

        <!-- IMAGE WIDGET SPECIFIC CONTROLS（图片图元专属配置） -->
        <div v-if="selectedComponent.type === 'image'"
          class="space-y-2 text-xs border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/40 dark:bg-sky-950/20">
          <p class="font-bold text-sky-600 dark:text-sky-400 text-[10px] uppercase tracking-wider">图片配置</p>

          <!-- 预览 -->
          <div
            class="h-28 rounded border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-950 flex items-center justify-center overflow-hidden">
            <img v-if="(componentProps.imageUrl || '').trim()" :src="componentProps.imageUrl" alt=""
              class="max-h-full max-w-full object-contain" draggable="false" />
            <span v-else class="text-[10px] text-slate-400 dark:text-slate-500">未设置图片</span>
          </div>

          <button type="button" @click="showImagePicker = true"
            class="w-full py-1.5 rounded bg-[#1890ff] hover:bg-[#40a9ff] text-white text-xs font-medium transition-colors cursor-pointer">
            更换图片（从图库选择/上传）
          </button>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">填充方式</label>
            <select :value="componentProps.imageFit || 'fill'"
              @change="updateProp('imageFit', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option value="fill">拉伸填满（可能变形）</option>
              <option value="contain">等比完整显示（可能留白）</option>
              <option value="cover">等比铺满裁切（可能裁边）</option>
              <option value="tile">平铺（按原始尺寸重复）</option>
            </select>
          </div>

          <!-- 图元换图库（组件内嵌实例，与图元添加/背景选图互不影响） -->
          <ImageLibraryDialog v-model="showImagePicker" @select="onPickComponentImage" />
        </div>

        <!-- 大屏标题背景图元专属配置（title-header）三套风格 -->
        <div v-if="selectedComponent.type === 'title-header'"
          class="space-y-3 text-xs border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/40 dark:bg-sky-950/20">
          <div class="flex items-center justify-between">
            <p class="font-bold text-sky-600 dark:text-sky-400 text-[11px] uppercase tracking-wider">大屏标题背景设置
            </p>
            <span
              class="text-[9px] font-mono bg-sky-100 dark:bg-sky-900/60 text-sky-700 dark:text-sky-300 px-1.5 py-0.5 rounded">Title
              Header</span>
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">风格主题 (Style Preset)</label>
            <select :value="componentProps.headerStyle || 'navy-midnight'"
              @change="updateProp('headerStyle', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <optgroup label="☀️ 浅色大方系列">
                <option value="pure-white">极简亮白 (Pure Crisp White · 浅色)</option>
                <option value="titanium-light">工业钛灰 (Titanium Light · 浅色)</option>
              </optgroup>
              <optgroup label="🌙 深色稳健系列">
                <option value="slate-dark">经典石板深灰 (Classic Slate · 深色)</option>
                <option value="navy-midnight">深海商务暗蓝 (Navy Midnight · 深色)</option>
              </optgroup>
              <optgroup label="🌿 轻量通透系列">
                <option value="translucent-frost">悬浮通透胶囊 (Adaptive Frost · 通透)</option>
              </optgroup>
              <optgroup label="⚙️ 经典特色预设">
                <option value="eco-green">生态翡翠绿 (Eco Green)</option>
                <option value="carbon-orange">机能碳纤橙 (Carbon Orange)</option>
                <option value="tech-blue">科技蓝 (Tech Blue)</option>
              </optgroup>
            </select>
          </div>

          <div class="grid grid-cols-2 gap-2">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">主标题 (Title)</label>
              <input type="text" :value="componentProps.headerTitle ?? ''"
                @input="updateProp('headerTitle', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
                placeholder="大屏主标题" />
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">副标题 (Subtitle)</label>
              <input type="text" :value="componentProps.headerSubtitle ?? ''"
                @input="updateProp('headerSubtitle', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
                placeholder="英文/副标题（可留空）" />
            </div>
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">角标 / Logo 文字</label>
            <input type="text" :value="componentProps.headerLogoText ?? ''"
              @input="updateProp('headerLogoText', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
              placeholder="SCADA">
          </div>

          <div class="grid grid-cols-2 gap-2 pt-1">
            <label class="flex items-center gap-2 text-gray-700 dark:text-slate-300 select-none cursor-pointer">
              <input type="checkbox" id="headerClock" :checked="componentProps.headerShowClock !== false"
                @change="updateProp('headerShowClock', ($event.target as HTMLInputElement).checked)"
                class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] dark:text-sky-500 focus:ring-0" />
              显示动态时钟
            </label>
            <label class="flex items-center gap-2 text-gray-700 dark:text-slate-300 select-none cursor-pointer">
              <input type="checkbox" id="headerStatus" :checked="componentProps.headerShowStatus !== false"
                @change="updateProp('headerShowStatus', ($event.target as HTMLInputElement).checked)"
                class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] dark:text-sky-500 focus:ring-0" />
              显示运行状态
            </label>
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">状态文案 (Status Text)</label>
            <input type="text" :value="componentProps.headerStatusText ?? ''"
              @input="updateProp('headerStatusText', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
              placeholder="系统运行正常" />
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">发光 / 主辅高亮色</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="componentProps.headerGlowColor || '#38bdf8'"
                @input="updateProp('headerGlowColor', ($event.target as HTMLInputElement).value)"
                class="w-6 h-6 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="componentProps.headerGlowColor || '#38bdf8'"
                @input="updateProp('headerGlowColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-2 pt-1 border-t border-sky-100 dark:border-sky-900/40">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">字体大小 (px)</label>
              <input type="number" :value="componentProps.fontSize ?? 22"
                @input="updateProp('fontSize', numInput(($event.target as HTMLInputElement).value, 22))"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white mt-0.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 text-xs" />
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">字重</label>
              <select :value="componentProps.bold ? 'bold' : 'normal'"
                @change="updateProp('bold', ($event.target as HTMLSelectElement).value === 'bold')"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white mt-0.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 text-xs">
                <option value="bold">加粗 (Bold)</option>
                <option value="normal">常规 (Normal)</option>
              </select>
            </div>
          </div>
        </div>

        <!-- INDUSTRIAL ROUNDED BUTTON SPECIFIC CONTROLS (圆角按钮专属配置)——已抽取为子组件 -->
        <RoundedBtnInspector v-if="selectedComponent.type === 'rounded-btn'" :component="selectedComponent"
          :nav-target-options="navTargetOptions" @update-prop="(key, value) => updateProp(key, value)"
          @apply-props="(patch) => applyProps(patch)" />

        <!-- TIME CLOCK WIDGET FORMATS -->
        <div v-if="selectedComponent.type === 'sys-time'"
          class="space-y-2 text-xs border border-gray-100 dark:border-slate-800 p-2 rounded bg-gray-50/50 dark:bg-slate-950/60">
          <p class="font-bold text-emerald-600 dark:text-emerald-400 text-[10px] uppercase tracking-wider mb-1">系统时间显示设置
          </p>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">排版格式 (DateTime Format)</label>
            <select :value="componentProps.timeFormat || 'HH:mm:ss'"
              @change="updateProp('timeFormat', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option value="HH:mm:ss">时分秒 (HH:mm:ss)</option>
              <option value="YYYY-MM-DD HH:mm:ss">年月日 时分秒</option>
              <option value="YYYY-MM-DD">仅显示日期 (YYYY-MM-DD)</option>
            </select>
          </div>
        </div>

        <!-- REAL-TIME MULTI-VARIABLE DASHBOARD CONTROLS (实时多变量监控看板专属配置)——已抽取为子组件 -->
        <MultiVarDashboardInspector v-if="selectedComponent.type === 'multi-var-dashboard'"
          :component="selectedComponent" @update-prop="(key, value) => updateProp(key, value)" />

        <!-- Custom fonts controls for Text boxes -->
        <div v-if="['text', 'button', 'rounded-btn'].includes(selectedComponent.type)" class="space-y-2 text-xs">
          <div class="grid grid-cols-2 gap-2">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">对齐方式</label>
              <select :value="componentProps.align || 'center'"
                @change="updateProp('align', ($event.target as HTMLSelectElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1 text-gray-800 dark:text-white mt-0.5 focus:outline-none">
                <option value="left">靠左对齐</option>
                <option value="center">居中对齐</option>
                <option value="right">靠右对齐</option>
              </select>
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">字体大小 (px)</label>
              <input type="number" :value="componentProps.fontSize ?? 12"
                @input="updateProp('fontSize', numInput(($event.target as HTMLInputElement).value, 12))"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1 text-[#262626] dark:text-white mt-0.5 focus:outline-none" />
            </div>
          </div>

          <div class="flex items-center gap-2 mt-2">
            <input type="checkbox" id="fontBoldDef" :checked="componentProps.bold || false"
              @change="updateProp('bold', ($event.target as HTMLInputElement).checked)"
              class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
            <label htmlFor="fontBoldDef" class="text-xs text-gray-700 dark:text-slate-300 select-none cursor-pointer">
              加粗字体 (Font Bold)
            </label>
          </div>

          <!-- 阶段：showValue — 组件内显示变量值（隐藏顶部浮签标签），复活死属性（#6） -->
          <div class="flex items-center gap-2 mt-2" v-if="selectedComponent.type !== 'text'">
            <input type="checkbox" id="showValueDef" :checked="componentProps.showValue || false"
              @change="updateProp('showValue', ($event.target as HTMLInputElement).checked)"
              class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
            <label htmlFor="showValueDef" class="text-xs text-gray-700 dark:text-slate-300 select-none cursor-pointer">
              组件内显示变量值（隐藏顶部浮签）
            </label>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>
