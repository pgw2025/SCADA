<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { HMIComponent, ScadaPage, PageBackground, PageBackgroundType, HMILayer } from '../types';
import { devices } from '../store/deviceStore';
import { desktopPages, mobilePages, currentPlatform } from '../store/scadaStore';
import { getWidgetDef } from '../widgetRegistry';
import { Settings, Tag, Sliders, Layout, Hash, ChevronRight, Palette, Expand, Layers, Eye, EyeOff, Lock, Unlock } from 'lucide-vue-next';
import ImageLibraryDialog from './ImageLibraryDialog.vue';

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

// 自定义画布尺寸：范围 200~10000，失焦/回车提交；非法值回退当前页面值
const CANVAS_SIZE_MIN = 200;
const CANVAS_SIZE_MAX = 10000;
const canvasW = ref<number>(props.backgroundPage?.width ?? 1100);
const canvasH = ref<number>(props.backgroundPage?.height ?? 700);
watch(
  () => [props.backgroundPage?.width, props.backgroundPage?.height] as const,
  ([w, h]) => {
    canvasW.value = w ?? 1100;
    canvasH.value = h ?? 700;
  }
);
const applyCanvasSize = () => {
  const page = props.backgroundPage;
  if (!page) return;
  const clamp = (v: number, fallback: number) => {
    const n = Math.round(Number(v));
    if (!Number.isFinite(n)) return fallback;
    return Math.min(CANVAS_SIZE_MAX, Math.max(CANVAS_SIZE_MIN, n));
  };
  const w = clamp(canvasW.value, page.width ?? 1100);
  const h = clamp(canvasH.value, page.height ?? 700);
  canvasW.value = w;
  canvasH.value = h;
  if (w !== page.width || h !== page.height) {
    emit('updatePage', { width: w, height: h });
  }
};

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
const navTargetOptions = computed(() => {
  const list = currentPlatform.value === 'Mobile' ? mobilePages.value : desktopPages.value;
  return list
    // 排除「当前页面」本身：页面 id 与组件 id 不可比，须用父级传入的 currentPageId
    .filter(p => p.id !== props.currentPageId)
    .map(p => ({ id: p.id, name: p.name }));
});

// ===== 页面属性（背景 + 自适应屏幕）=====
// 未配置背景时的默认值（纯色白底）；每次编辑整体提交，父级负责落库
const pageBackground = computed<PageBackground>(() =>
  props.backgroundPage?.background ?? { type: 'color', color: '#ffffff' });

const updateBackground = (patch: Partial<PageBackground>) => {
  emit('updatePage', { background: { ...pageBackground.value, ...patch } });
};

const onBackgroundTypeChange = (val: string) => {
  const type = val as PageBackgroundType;
  // 切换类型时保留各类型已有参数，仅补默认值，避免来回切换丢失已填内容
  const cur = pageBackground.value;
  const patch: Partial<PageBackground> = { type };
  if (type === 'color' && !cur.color) patch.color = '#ffffff';
  if (type === 'gradient') {
    if (!cur.gradientStart) patch.gradientStart = '#e0f2fe';
    if (!cur.gradientEnd) patch.gradientEnd = '#1e3a8a';
    if (typeof cur.gradientAngle !== 'number') patch.gradientAngle = 180;
  }
  if (type === 'image') {
    if (!cur.imageFit) patch.imageFit = 'fill';
  }
  updateBackground(patch);
};

const onAdaptModeChange = (val: string) => {
  emit('updatePage', { adaptMode: val === 'FitScaleUp' || val === 'Stretch' ? val : null });
};

// ===== 图片图元 / 背景图：图库选图 =====
// 图元换图（updateProp 走既有防抖落库链路）
const showImagePicker = ref(false);
const onPickComponentImage = (img: { url: string }) => {
  showImagePicker.value = false;
  updateProp('imageUrl', img.url);
};

// 背景选图（updateBackground 整体提交，父级落库）
const showBgImagePicker = ref(false);
const onPickBackgroundImage = (img: { url: string }) => {
  showBgImagePicker.value = false;
  updateBackground({ imageUrl: img.url });
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

  <!-- 页面属性：点击画布背景后显示（背景设置 + 自适应屏幕设置） -->
  <div v-else-if="backgroundPage"
    class="h-full flex flex-col bg-white dark:bg-slate-900 text-[#262626] dark:text-slate-100 overflow-y-auto transition-colors">
    <!-- Title -->
    <div
      class="p-4 border-b border-[#f0f0f0] dark:border-slate-800 bg-[#fafafa] dark:bg-slate-950 flex items-center justify-between">
      <div class="flex items-center gap-2">
        <Palette class="w-4 h-4 text-[#1890ff] dark:text-sky-400" />
        <h3 class="text-xs font-bold text-[#141414] dark:text-slate-100 uppercase tracking-wider">
          页面属性
        </h3>
      </div>
      <button @click="emit('collapse')"
        class="p-1 rounded text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
        title="收起属性面板">
        <ChevronRight class="w-4 h-4" />
      </button>
    </div>

    <div class="p-4 space-y-4 text-left">
      <!-- 页面基本信息（只读） -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <Layout class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          基本信息
        </div>
        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">画面名称</label>
            <input type="text" disabled :value="backgroundPage.name"
              class="w-full bg-[#fafafa] dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 mt-0.5 text-gray-400 dark:text-slate-500 cursor-not-allowed" />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">画布尺寸 (px)</label>
            <div class="flex items-center gap-1 mt-0.5">
              <input type="number" min="200" max="10000" step="10" v-model.number="canvasW" @change="applyCanvasSize"
                @keyup.enter="applyCanvasSize"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2 py-1.5 font-mono text-[10px] text-[#262626] dark:text-white focus:outline-none"
                title="画布宽度（200~10000）" />
              <span class="text-gray-400 text-[10px] shrink-0">×</span>
              <input type="number" min="200" max="10000" step="10" v-model.number="canvasH" @change="applyCanvasSize"
                @keyup.enter="applyCanvasSize"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2 py-1.5 font-mono text-[10px] text-[#262626] dark:text-white focus:outline-none"
                title="画布高度（200~10000）" />
            </div>
          </div>
        </div>
      </section>

      <div class="border-t border-[#f0f0f0] dark:border-slate-800 my-4" />

      <!-- 背景设置 -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <Palette class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          背景设置
        </div>

        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">背景类型</label>
          <select :value="pageBackground.type"
            @change="onBackgroundTypeChange(($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option value="color">纯色背景</option>
            <option value="gradient">渐变背景</option>
            <option value="image">图片背景 (URL)</option>
          </select>
        </div>

        <!-- 纯色 -->
        <template v-if="pageBackground.type === 'color'">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">背景颜色</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="pageBackground.color || '#ffffff'"
                @input="updateBackground({ color: ($event.target as HTMLInputElement).value })"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="pageBackground.color || '#ffffff'"
                @input="updateBackground({ color: ($event.target as HTMLInputElement).value })"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">快速选择</label>
            <div class="grid grid-cols-8 gap-1.5 mt-1">
              <button
                v-for="c in ['#ffffff', '#f5f5f5', '#e0f2fe', '#dcfce7', '#fef9c3', '#111827', '#0f172a', '#1e3a8a']"
                :key="c" @click="updateBackground({ color: c })" :style="{ backgroundColor: c }"
                class="h-6 rounded border border-[#d9d9d9] dark:border-slate-700 cursor-pointer hover:ring-2 hover:ring-[#1890ff] transition-shadow"
                :title="c" />
            </div>
          </div>
        </template>

        <!-- 渐变 -->
        <template v-else-if="pageBackground.type === 'gradient'">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">起始色 (Start)</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="pageBackground.gradientStart || '#e0f2fe'"
                @input="updateBackground({ gradientStart: ($event.target as HTMLInputElement).value })"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="pageBackground.gradientStart || '#e0f2fe'"
                @input="updateBackground({ gradientStart: ($event.target as HTMLInputElement).value })"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">终止色 (End)</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="pageBackground.gradientEnd || '#1e3a8a'"
                @input="updateBackground({ gradientEnd: ($event.target as HTMLInputElement).value })"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="pageBackground.gradientEnd || '#1e3a8a'"
                @input="updateBackground({ gradientEnd: ($event.target as HTMLInputElement).value })"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">
              渐变角度 ({{ pageBackground.gradientAngle ?? 180 }}°)
            </label>
            <input type="range" min="0" max="360" step="5" :value="pageBackground.gradientAngle ?? 180"
              @input="updateBackground({ gradientAngle: parseInt(($event.target as HTMLInputElement).value) || 0 })"
              class="w-full mt-1 accent-[#1890ff]" />
          </div>
          <!-- 实时预览 -->
          <div class="h-8 rounded border border-[#d9d9d9] dark:border-slate-700" :style="{
            backgroundImage: `linear-gradient(${pageBackground.gradientAngle ?? 180}deg, ${pageBackground.gradientStart || '#e0f2fe'}, ${pageBackground.gradientEnd || '#1e3a8a'})`
          }" />
        </template>

        <!-- 图片 -->
        <template v-else>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">图片 URL</label>
            <div class="flex items-center gap-1.5 mt-0.5">
              <input type="text" :value="pageBackground.imageUrl ?? ''"
                @input="updateBackground({ imageUrl: ($event.target as HTMLInputElement).value })"
                class="flex-1 min-w-0 bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 text-[#262626] dark:text-white text-xs focus:outline-none"
                placeholder="https://example.com/bg.png 或 /api/HmiImage/..." />
              <button type="button" @click="showBgImagePicker = true"
                class="shrink-0 px-2 py-1.5 rounded border border-[#1890ff] text-[#1890ff] dark:text-sky-400 dark:border-sky-500 hover:bg-[#e6f7ff] dark:hover:bg-sky-950/40 text-[10px] whitespace-nowrap transition-colors cursor-pointer">
                从图库选择
              </button>
            </div>
            <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-1 leading-snug">
              可从图库选择/上传，或填写可访问的外部图片地址。
            </p>
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">填充方式</label>
            <select :value="pageBackground.imageFit || 'fill'"
              @change="updateBackground({ imageFit: ($event.target as HTMLSelectElement).value as PageBackground['imageFit'] })"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
              <option value="fill">拉伸铺满（可能变形）</option>
              <option value="contain">等比完整显示（可能留白）</option>
              <option value="cover">等比铺满裁切（可能裁边）</option>
              <option value="tile">平铺（按原始尺寸重复）</option>
            </select>
          </div>

          <!-- 背景选图库（内嵌实例，选择后写入 imageUrl） -->
          <ImageLibraryDialog v-model="showBgImagePicker" @select="onPickBackgroundImage" />
        </template>
      </section>

      <div class="border-t border-[#f0f0f0] dark:border-slate-800 my-4" />

      <!-- 自适应屏幕设置 -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <Expand class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          自适应屏幕
        </div>

        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">运行端适配模式</label>
          <select :value="backgroundPage.adaptMode ?? ''"
            @change="onAdaptModeChange(($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option value="">默认（等比缩小，不放大）</option>
            <option value="FitScaleUp">等比缩放（允许放大填满）</option>
            <option value="Stretch">拉伸填满（非等比，可能变形）</option>
          </select>
          <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-1.5 leading-relaxed">
            仅作用于运行端全屏查看：画面按所选模式缩放适配视口。<br />
            设计端画布不受影响，可随时用工具栏缩放查看。
          </p>
        </div>
      </section>
    </div>
  </div>

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
          <label class="text-[10px] text-gray-500 dark:text-slate-400">绑定设备</label>
          <select :value="selectedComponent?.bindDeviceId ?? ''"
            @change="onBindDeviceChange(($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option value="">-- 未绑定设备（禁止裸 key）--</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }} ({{ d.key }})</option>
          </select>
          <p v-if="selectedComponent?.bindDeviceId == null"
            class="text-[10px] text-amber-600 dark:text-amber-400 mt-1 leading-relaxed">
            未绑定设备：运行态将无法定位变量值，且禁止裸 key 写入。请先选择设备。
          </p>
        </div>
        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">绑定变量</label>
          <select
            :value="(selectedComponent?.bindDeviceId != null ? selectedComponent?.bindVariableKey : selectedComponent?.bindField) ?? ''"
            @change="onBindVariableChange(($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option value="">-- 无绑定 --</option>
            <option v-for="v in bindingVariableOptions" :key="v.key" :value="v.key">{{ v.key }}</option>
          </select>
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

        <!-- States color picks -->
        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">运行激活光效</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="componentProps.activeColor || '#1890ff'"
                @input="updateProp('activeColor', ($event.target as HTMLInputElement).value)"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="componentProps.activeColor || '#1890ff'"
                @input="updateProp('activeColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">空闲正常底色</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="componentProps.inactiveColor || '#8c8c8c'"
                @input="updateProp('inactiveColor', ($event.target as HTMLInputElement).value)"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="componentProps.inactiveColor || '#8c8c8c'"
                @input="updateProp('inactiveColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>
        </div>

        <!-- 阶段5-6：状态文本解耦——阀/数显/开关等有状态控件的开/关文案可配置，去除硬编码英/中文 -->
        <div v-if="['valve', 'digital-val', 'switch', 'led'].includes(selectedComponent.type)"
          class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">开启状态文本</label>
            <input type="text" :value="componentProps.onText ?? '开启'"
              @input="updateProp('onText', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
              placeholder="默认: 开启" />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">关闭状态文本</label>
            <input type="text" :value="componentProps.offText ?? '关闭'"
              @input="updateProp('offText', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
              placeholder="默认: 关闭" />
          </div>
        </div>

        <!-- Medium Fluid Filler style -->
        <div v-if="['tank', 'boiler', 'conveyor'].includes(selectedComponent.type)">
          <label class="text-[10px] text-gray-500 dark:text-slate-400">填充介质颜色 (Medium)</label>
          <div class="flex items-center gap-1.5 mt-1">
            <input type="color" :value="componentProps.fillColor || '#1890ff'"
              @input="updateProp('fillColor', ($event.target as HTMLInputElement).value)"
              class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
            <input type="text" :value="componentProps.fillColor || '#1890ff'"
              @input="updateProp('fillColor', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 focus:outline-none text-gray-600 dark:text-slate-300" />
          </div>
        </div>

        <!-- 量程设置：量程上限/下限/单位（百分比类与仪表类归一化基准） -->
        <div
          v-if="['gauge-dial', 'gauge-level', 'digital-val', 'tank', 'boiler', 'trend-chart', 'pump', 'motor'].includes(selectedComponent.type)"
          class="space-y-2">
          <div class="grid grid-cols-3 gap-2 text-xs">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">量程下限 (Min)</label>
              <input type="number" :value="componentProps.minValue ?? typeDefaults.minValue ?? 0"
                @input="updateProp('minValue', numInput(($event.target as HTMLInputElement).value, 0))"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none" />
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">量程上限 (Max)</label>
              <input type="number" :value="componentProps.maxValue ?? typeDefaults.maxValue ?? 100"
                @input="updateProp('maxValue', numInput(($event.target as HTMLInputElement).value, typeDefaults.maxValue ?? 100))"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none" />
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">单位 (Unit)</label>
              <input type="text" :value="componentProps.unit ?? ''"
                @input="updateProp('unit', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none"
                placeholder="e.g. L/s, MPa, ℃" />
            </div>
          </div>
        </div>

        <!-- 高/低限报警阈值：覆盖所有消费 isHighAlert/alertColor 的器件 -->
        <div
          v-if="['gauge-dial', 'gauge-level', 'digital-val', 'boiler', 'pump', 'motor', 'trend-chart', 'led'].includes(selectedComponent.type)"
          class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-red-500 dark:text-red-400">红色高限报警值</label>
            <input type="number" :value="componentProps.thresholdMax ?? typeDefaults.thresholdMax ?? 90"
              @input="updateProp('thresholdMax', numInput(($event.target as HTMLInputElement).value, typeDefaults.thresholdMax ?? 90))"
              class="w-full bg-white dark:bg-slate-950 border border-red-300 dark:border-red-800 rounded px-2.5 py-1 text-red-600 dark:text-red-400 focus:outline-none focus:border-red-500" />
          </div>
          <div>
            <label class="text-[10px] text-amber-600 dark:text-amber-400">黄色低限预警值</label>
            <input type="number" :value="componentProps.thresholdMin ?? typeDefaults.thresholdMin ?? 10"
              @input="updateProp('thresholdMin', numInput(($event.target as HTMLInputElement).value, typeDefaults.thresholdMin ?? 10))"
              class="w-full bg-white dark:bg-slate-950 border border-amber-300 dark:border-amber-800 rounded px-2.5 py-1 text-amber-700 dark:text-amber-300 focus:outline-none focus:border-amber-500" />
          </div>
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

        <!-- INDUSTRIAL ROUNDED BUTTON SPECIFIC CONTROLS (圆角按钮专属配置) -->
        <div v-if="selectedComponent.type === 'rounded-btn'"
          class="space-y-3 text-xs border border-emerald-200/80 dark:border-emerald-900/60 p-3 rounded-lg bg-emerald-50/40 dark:bg-emerald-950/20">
          <div class="flex items-center justify-between">
            <p class="font-bold text-emerald-600 dark:text-emerald-400 text-[11px] uppercase tracking-wider">圆角按钮与多状态配置
            </p>
            <span
              class="text-[9px] font-mono bg-emerald-100 dark:bg-emerald-900/60 text-emerald-700 dark:text-emerald-300 px-1.5 py-0.5 rounded">Rounded
              Button</span>
          </div>

          <!-- 控制模式选择 -->
          <div>
            <label class="text-[10px] font-semibold text-gray-700 dark:text-slate-300">控制动作模式 (Action Mode)</label>
            <select :value="componentProps.buttonMode || 'toggle'"
              @change="updateProp('buttonMode', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 focus:outline-none focus:border-emerald-500 mt-1 text-xs text-[#262626] dark:text-white font-medium">
              <option value="toggle">取反 (Toggle - 0变1，1变0)</option>
              <option value="set-bit">置位 (SetBit - 强制写入1 / True)</option>
              <option value="reset-bit">复位 (ResetBit - 强制写入0 / False)</option>
              <option value="momentary">按1送0 (Momentary - 按下写1松开写0)</option>
              <option value="set-value">恒定设值 (SetValue - 写入指定数值)</option>
              <option value="navigate">画面跳转 (Navigate - 跳转同端画面)</option>
            </select>
          </div>

          <div v-if="componentProps.buttonMode === 'set-value'">
            <label class="text-[10px] text-gray-500 dark:text-slate-400">设值写入数值</label>
            <input type="number" :value="componentProps.clickValue ?? 1"
              @input="updateProp('clickValue', numInput(($event.target as HTMLInputElement).value, 1))"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-emerald-500 mt-0.5 text-xs" />
          </div>

          <div v-if="componentProps.buttonMode === 'navigate'">
            <label class="text-[10px] text-gray-500 dark:text-slate-400">跳转目标画面（仅同端）</label>
            <select :value="componentProps.targetPageId ?? ''"
              @change="updateProp('targetPageId', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-emerald-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option value="">-- 请选择目标画面 --</option>
              <option v-for="opt in navTargetOptions" :key="opt.id" :value="opt.id">{{ opt.name }}</option>
            </select>
          </div>

          <!-- 圆角与边框精细调节 -->
          <div class="grid grid-cols-2 gap-2 text-xs pt-1 border-t border-emerald-100 dark:border-emerald-900/40">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">圆角弧度 (Radius: {{ componentProps.borderRadius
                ?? 10 }}px)</label>
              <input type="range" min="0" max="40" step="1" :value="componentProps.borderRadius ?? 10"
                @input="updateProp('borderRadius', parseInt(($event.target as HTMLInputElement).value) || 0)"
                class="w-full mt-1 accent-emerald-600 dark:accent-emerald-400" />
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">边框粗细 (Border: {{ componentProps.borderWidth
                ?? 1 }}px)</label>
              <input type="range" min="0" max="6" step="1" :value="componentProps.borderWidth ?? 1"
                @input="updateProp('borderWidth', parseInt(($event.target as HTMLInputElement).value) || 0)"
                class="w-full mt-1 accent-emerald-600 dark:accent-emerald-400" />
            </div>
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">边框轮廓颜色</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="componentProps.strokeColor || '#38bdf8'"
                @input="updateProp('strokeColor', ($event.target as HTMLInputElement).value)"
                class="w-6 h-6 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="componentProps.strokeColor || '#38bdf8'"
                @input="updateProp('strokeColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>

          <!-- 双态/多态配置：状态0 (OFF/停止) 与 状态1 (ON/运行) -->
          <div class="space-y-2 pt-2 border-t border-emerald-100 dark:border-emerald-900/40">
            <p class="font-bold text-gray-700 dark:text-slate-300 text-[10px]">基础状态样式定义 (状态 0 / 1)</p>

            <!-- 状态0 (值=0/false) -->
            <div
              class="bg-white dark:bg-slate-900 p-2 rounded border border-slate-200 dark:border-slate-800 space-y-1.5">
              <div class="flex items-center justify-between">
                <span class="text-[10px] font-bold text-slate-500 dark:text-slate-400">● 状态 0 (关/停止/0)</span>
              </div>
              <div class="grid grid-cols-3 gap-1.5">
                <div class="col-span-1">
                  <label class="text-[9px] text-slate-400">显示文本</label>
                  <input type="text" :value="componentProps.state0Text ?? 'OFF 停止'"
                    @input="updateProp('state0Text', ($event.target as HTMLInputElement).value)"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[11px] text-slate-800 dark:text-white" />
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">背景色</label>
                  <div class="flex items-center gap-1 mt-0.5">
                    <input type="color" :value="componentProps.state0BgColor || '#1e293b'"
                      @input="updateProp('state0BgColor', ($event.target as HTMLInputElement).value)"
                      class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
                    <input type="text" :value="componentProps.state0BgColor || '#1e293b'"
                      @input="updateProp('state0BgColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
                  </div>
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">文字颜色</label>
                  <div class="flex items-center gap-1 mt-0.5">
                    <input type="color" :value="componentProps.state0TextColor || '#94a3b8'"
                      @input="updateProp('state0TextColor', ($event.target as HTMLInputElement).value)"
                      class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
                    <input type="text" :value="componentProps.state0TextColor || '#94a3b8'"
                      @input="updateProp('state0TextColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
                  </div>
                </div>
              </div>
            </div>

            <!-- 状态1 (值=1/true) -->
            <div
              class="bg-white dark:bg-slate-900 p-2 rounded border border-slate-200 dark:border-slate-800 space-y-1.5">
              <div class="flex items-center justify-between">
                <span class="text-[10px] font-bold text-emerald-600 dark:text-emerald-400">● 状态 1 (开/运行/1)</span>
              </div>
              <div class="grid grid-cols-3 gap-1.5">
                <div class="col-span-1">
                  <label class="text-[9px] text-slate-400">显示文本</label>
                  <input type="text" :value="componentProps.state1Text ?? 'ON 运行'"
                    @input="updateProp('state1Text', ($event.target as HTMLInputElement).value)"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[11px] text-slate-800 dark:text-white" />
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">背景色</label>
                  <div class="flex items-center gap-1 mt-0.5">
                    <input type="color" :value="componentProps.state1BgColor || '#0284c7'"
                      @input="updateProp('state1BgColor', ($event.target as HTMLInputElement).value)"
                      class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
                    <input type="text" :value="componentProps.state1BgColor || '#0284c7'"
                      @input="updateProp('state1BgColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
                  </div>
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">文字颜色</label>
                  <div class="flex items-center gap-1 mt-0.5">
                    <input type="color" :value="componentProps.state1TextColor || '#ffffff'"
                      @input="updateProp('state1TextColor', ($event.target as HTMLInputElement).value)"
                      class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
                    <input type="text" :value="componentProps.state1TextColor || '#ffffff'"
                      @input="updateProp('state1TextColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
                  </div>
                </div>
              </div>
            </div>

            <!-- 高级多状态自定义 (Custom States) -->
            <div class="space-y-1 pt-1">
              <label class="text-[10px] text-gray-500 dark:text-slate-400 flex justify-between">
                <span>高级自定义多状态规则 (值:文本:背景色:字色)</span>
              </label>
              <textarea rows="2" :value="componentProps.customStates ?? ''"
                @input="updateProp('customStates', ($event.target as HTMLTextAreaElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 mt-0.5 focus:outline-none focus:border-emerald-500 text-[10px] font-mono text-gray-700 dark:text-slate-300 leading-relaxed"
                placeholder="0:停止:#334155:#94a3b8;1:运行:#0284c7:#ffffff;2:报警:#dc2626:#ffffff" />
              <p class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug">
                支持任意数值状态映射，例如 <code
                  class="bg-slate-100 dark:bg-slate-800 px-1 rounded">0:停止:#1e293b:#94a3b8;1:运行:#10b981:#ffffff;2:过载:#f59e0b:#ffffff;3:紧急故障:#ef4444:#ffffff</code>
              </p>
            </div>
          </div>
        </div>

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
