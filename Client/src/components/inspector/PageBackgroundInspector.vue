<script setup lang="ts">
// 页面属性检查器：点击画布背景后显示（背景设置 + 自适应屏幕设置）
// 从 InspectorPanel.vue 抽出（Phase 1）；通信：props 下行 / emit 上行，与父组件 updatePage 链路同构
import { computed, ref, watch } from 'vue';
import { ScadaPage, PageBackground, PageBackgroundType } from '../../types';
import { Palette, Layout, ChevronRight, Expand } from 'lucide-vue-next';
import ImageLibraryDialog from '../ImageLibraryDialog.vue';

const props = defineProps<{
  backgroundPage: ScadaPage;
}>();

const emit = defineEmits<{
  (e: 'updatePage', updates: Partial<ScadaPage>): void;
  (e: 'collapse'): void;
}>();

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

// 背景选图（updateBackground 整体提交，父级落库）
const showBgImagePicker = ref(false);
const onPickBackgroundImage = (img: { url: string }) => {
  showBgImagePicker.value = false;
  updateBackground({ imageUrl: img.url });
};

// 5 套主题风格适配的画布背景快速配色预设
const THEME_CANVAS_PRESETS = [
  {
    id: 'pure-white',
    name: '极简亮白',
    category: '☀️ 浅色大方',
    isLight: true,
    color: '#ffffff',
    borderColor: '#e2e8f0',
    gradient: { start: '#ffffff', end: '#f1f5f9', angle: 180 },
    textColor: '#0f172a',
    accentColor: '#2563eb',
  },
  {
    id: 'titanium-light',
    name: '工业钛灰',
    category: '☀️ 浅色大方',
    isLight: true,
    color: '#f1f5f9',
    borderColor: '#cbd5e1',
    gradient: { start: '#f8fafc', end: '#e2e8f0', angle: 180 },
    textColor: '#1e293b',
    accentColor: '#0284c7',
  },
  {
    id: 'slate-dark',
    name: '经典石板深灰',
    category: '🌙 深色稳健',
    isLight: false,
    color: '#0f172a',
    borderColor: '#334155',
    gradient: { start: '#1e293b', end: '#0f172a', angle: 180 },
    textColor: '#f8fafc',
    accentColor: '#38bdf8',
  },
  {
    id: 'navy-midnight',
    name: '深海商务暗蓝',
    category: '🌙 深色稳健',
    isLight: false,
    color: '#061426',
    borderColor: '#1e293b',
    gradient: { start: '#0b172a', end: '#061426', angle: 180 },
    textColor: '#ffffff',
    accentColor: '#38bdf8',
  },
  {
    id: 'translucent-frost',
    name: '悬浮通透暗调',
    category: '🌿 轻量通透',
    isLight: false,
    color: '#111c2e',
    borderColor: 'rgba(255,255,255,0.2)',
    gradient: { start: '#1e293b', end: '#0a0f1d', angle: 180 },
    textColor: '#ffffff',
    accentColor: '#38bdf8',
  },
];

// 一键应用 5 套主题对应的画布背景
const applyThemePreset = (preset: typeof THEME_CANVAS_PRESETS[0]) => {
  if (pageBackground.value.type === 'gradient') {
    updateBackground({
      gradientStart: preset.gradient.start,
      gradientEnd: preset.gradient.end,
      gradientAngle: preset.gradient.angle,
    });
  } else {
    updateBackground({
      type: 'color',
      color: preset.color,
    });
  }
};
</script>

<template>
  <div
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
        <div class="flex items-center justify-between text-xs font-semibold text-gray-700 dark:text-slate-300">
          <div class="flex items-center gap-1.5">
            <Palette class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
            背景设置
          </div>
          <span class="text-[10px] text-gray-400 dark:text-slate-500 font-normal">支持5大主题一键适配</span>
        </div>

        <!-- 5 套主题风格快速套用卡片 -->
        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400 flex items-center justify-between">
            <span>主题风格预设 (5大体系)</span>
            <span class="text-[9px] text-[#1890ff] dark:text-sky-400">点击即应用</span>
          </label>
          <div class="grid grid-cols-1 gap-1.5 mt-1.5">
            <div v-for="t in THEME_CANVAS_PRESETS" :key="t.id" @click="applyThemePreset(t)"
              class="flex items-center justify-between px-2.5 py-1.5 rounded-lg border text-xs cursor-pointer transition-all hover:scale-[1.01] active:scale-[0.99] shadow-xs select-none"
              :class="((pageBackground.type === 'color' && pageBackground.color === t.color) || (pageBackground.type === 'gradient' && pageBackground.gradientStart === t.gradient.start && pageBackground.gradientEnd === t.gradient.end)) ? 'ring-2 ring-[#1890ff] dark:ring-sky-400 border-transparent' : 'border-gray-200 dark:border-slate-700 hover:border-[#1890ff] dark:hover:border-sky-500'"
              :style="{
                background: t.isLight ? t.color : t.color,
                color: t.textColor,
                border: `1px solid ${t.borderColor}`
              }">
              <div class="flex items-center gap-2 min-w-0">
                <span class="w-3 h-3 rounded-full shrink-0 border border-black/20"
                  :style="{ background: t.accentColor }" />
                <div class="truncate">
                  <span class="font-bold text-[11px]">{{ t.name }}</span>
                  <span class="text-[9px] opacity-60 ml-1.5">{{ t.category }}</span>
                </div>
              </div>
              <!-- 纯色/渐变微缩色块 -->
              <div class="flex items-center gap-1 shrink-0">
                <span class="text-[9px] font-mono opacity-70">{{ t.color }}</span>
                <div class="w-5 h-3.5 rounded border border-black/20" :style="{
                  backgroundImage: `linear-gradient(135deg, ${t.gradient.start}, ${t.gradient.end})`
                }" :title="`渐变: ${t.gradient.start} ➔ ${t.gradient.end}`" />
              </div>
            </div>
          </div>
        </div>

        <div class="border-t border-dashed border-gray-200 dark:border-slate-800 my-2" />

        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">背景类型</label>
          <select :value="pageBackground.type"
            @change="onBackgroundTypeChange(($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option value="color">纯色背景 (Solid Color)</option>
            <option value="gradient">渐变背景 (Linear Gradient)</option>
            <option value="image">图片背景 (Image URL)</option>
          </select>
        </div>

        <!-- 纯色 -->
        <template v-if="pageBackground.type === 'color'">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">自定义颜色</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="pageBackground.color || '#ffffff'"
                @input="updateBackground({ color: ($event.target as HTMLInputElement).value })"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="pageBackground.color || '#ffffff'"
                @input="updateBackground({ color: ($event.target as HTMLInputElement).value })"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>

          <!-- 5套主题适配颜色快速选择 -->
          <div class="space-y-2">
            <div>
              <div class="flex items-center justify-between text-[10px] text-gray-500 dark:text-slate-400">
                <span>☀️ 浅色主题底色 (Light Presets)</span>
              </div>
              <div class="grid grid-cols-4 gap-1.5 mt-1">
                <button v-for="item in [
                  { color: '#ffffff', name: '极简纯白' },
                  { color: '#f8fafc', name: '冷光亮白' },
                  { color: '#f1f5f9', name: '工业钛灰' },
                  { color: '#e2e8f0', name: '金属中灰' }
                ]" :key="item.color" @click="updateBackground({ color: item.color })"
                  class="h-7 rounded-md border flex flex-col items-center justify-center cursor-pointer transition-all hover:scale-105 active:scale-95 shadow-2xs"
                  :class="pageBackground.color === item.color ? 'ring-2 ring-[#1890ff] dark:ring-sky-400 border-transparent' : 'border-gray-300 dark:border-slate-700'"
                  :style="{ backgroundColor: item.color }" :title="`${item.name} (${item.color})`">
                  <span class="text-[8px] font-mono font-medium text-slate-800 leading-none">{{ item.name }}</span>
                </button>
              </div>
            </div>

            <div>
              <div class="flex items-center justify-between text-[10px] text-gray-500 dark:text-slate-400">
                <span>🌙 深色/通透主题底色 (Dark & Frost Presets)</span>
              </div>
              <div class="grid grid-cols-4 gap-1.5 mt-1">
                <button v-for="item in [
                  { color: '#0f172a', name: '石板深灰' },
                  { color: '#1e293b', name: '石板中黑' },
                  { color: '#061426', name: '深海暗蓝' },
                  { color: '#111c2e', name: '通透暗调' }
                ]" :key="item.color" @click="updateBackground({ color: item.color })"
                  class="h-7 rounded-md border flex flex-col items-center justify-center cursor-pointer transition-all hover:scale-105 active:scale-95 shadow-2xs"
                  :class="pageBackground.color === item.color ? 'ring-2 ring-[#1890ff] dark:ring-sky-400 border-transparent' : 'border-gray-400 dark:border-slate-700'"
                  :style="{ backgroundColor: item.color }" :title="`${item.name} (${item.color})`">
                  <span class="text-[8px] font-mono font-medium text-slate-200 leading-none">{{ item.name }}</span>
                </button>
              </div>
            </div>

            <div>
              <div class="flex items-center justify-between text-[10px] text-gray-500 dark:text-slate-400">
                <span>🎨 经典工业辅助色</span>
              </div>
              <div class="grid grid-cols-8 gap-1.5 mt-1">
                <button
                  v-for="c in ['#ffffff', '#f5f5f5', '#e0f2fe', '#dcfce7', '#fef9c3', '#111827', '#1e3a8a', '#073a26']"
                  :key="c" @click="updateBackground({ color: c })" :style="{ backgroundColor: c }"
                  class="h-5 rounded border border-[#d9d9d9] dark:border-slate-700 cursor-pointer hover:ring-2 hover:ring-[#1890ff] transition-all hover:scale-110"
                  :class="pageBackground.color === c ? 'ring-2 ring-[#1890ff]' : ''" :title="c" />
              </div>
            </div>
          </div>
        </template>

        <!-- 渐变 -->
        <template v-else-if="pageBackground.type === 'gradient'">
          <!-- 5 套主题渐变快速选择 -->
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">主题渐变快速选择</label>
            <div class="grid grid-cols-1 gap-1.5 mt-1">
              <button v-for="t in THEME_CANVAS_PRESETS" :key="t.id"
                @click="updateBackground({ gradientStart: t.gradient.start, gradientEnd: t.gradient.end, gradientAngle: t.gradient.angle })"
                class="flex items-center justify-between px-2 py-1.5 rounded-lg border text-xs cursor-pointer transition-all hover:scale-[1.01] active:scale-[0.99]"
                :class="(pageBackground.gradientStart === t.gradient.start && pageBackground.gradientEnd === t.gradient.end) ? 'ring-2 ring-[#1890ff] dark:ring-sky-400 border-transparent' : 'border-gray-200 dark:border-slate-700 hover:border-[#1890ff] dark:hover:border-sky-500'"
                :style="{
                  background: `linear-gradient(90deg, ${t.gradient.start}, ${t.gradient.end})`,
                  color: t.textColor,
                }">
                <span class="font-bold text-[10px] drop-shadow-xs">{{ t.name }}渐变</span>
                <span class="text-[9px] font-mono opacity-80">{{ t.gradient.start }} ➔ {{ t.gradient.end }}</span>
              </button>
            </div>
          </div>

          <div class="border-t border-dashed border-gray-200 dark:border-slate-800 my-1" />

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
          <div class="h-8 rounded border border-[#d9d9d9] dark:border-slate-700 shadow-inner" :style="{
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
</template>
