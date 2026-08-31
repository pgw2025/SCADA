<script setup lang="ts">
import { ref, computed } from 'vue';
import { ComponentType } from '../types';
import { widgetList, WidgetDef } from '../widgetRegistry';
import {
  Search,
  Package,
  ChevronLeft
} from 'lucide-vue-next';

const emit = defineEmits<{
  // 签名与 ScadaTopologyView.handleAddWidget 对齐：点击落布无 x/y（传 undefined 取默认 40/60），extraProps 居第 7 位
  (e: 'addWidget', type: ComponentType, w: number, h: number, name: string, x?: number, y?: number, extraProps?: Record<string, any>): void;
  (e: 'collapse'): void;
}>();

// 阶段5-4：HTML5 拖拽投放（拖拽时写入组件类型/尺寸/名称/默认属性，由 CanvasPanel 计算落点）
const onDragStart = (e: DragEvent, widget: WidgetDef) => {
  const payload = JSON.stringify({
    type: widget.type,
    w: widget.defaultWidth,
    h: widget.defaultHeight,
    name: widget.name,
    extraProps: widget.defaultProps(),
  });
  e.dataTransfer?.setData('application/x-scada-widget', payload);
  if (e.dataTransfer) e.dataTransfer.effectAllowed = 'copy';
};

const searchTerm = ref('');
const activeTab = ref<'all' | 'equipment' | 'sensors' | 'structures' | 'headers'>('all');

// 阶段5-5：列表来自注册表，分类按注册项 category 过滤（不再硬编码类型数组）
const filteredWidgets = computed(() => {
  const term = searchTerm.value.toLowerCase();
  return widgetList.filter((w) => {
    const matchesSearch = w.name.toLowerCase().includes(term);
    if (!matchesSearch) return false;
    if (activeTab.value === 'all') return true;
    return w.category === activeTab.value;
  });
});
</script>

<template>
  <div
    class="h-full flex flex-col bg-white dark:bg-slate-900 border-r border-[#d9d9d9] dark:border-slate-800 text-[#262626] dark:text-slate-100 transition-colors">
    <!-- Search Header -->
    <div class="p-4 border-b border-[#f0f0f0] dark:border-slate-800 bg-[#fafafa] dark:bg-slate-950">
      <div class="flex items-center justify-between mb-2.5">
        <h3
          class="text-xs font-bold text-[#141414] dark:text-slate-100 uppercase tracking-wider flex items-center gap-2">
          <Package class="w-4 h-4 text-[#1890ff] dark:text-sky-400" />
          工业器件图库
        </h3>
        <button @click="emit('collapse')"
          class="p-1 rounded text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
          title="收起器件图库">
          <ChevronLeft class="w-4 h-4" />
        </button>
      </div>
      <div class="relative">
        <input type="text" placeholder="搜索工业器件..." v-model="searchTerm"
          class="w-full bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded py-1.5 pl-8 pr-3 text-xs text-slate-800 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 focus:ring-1 focus:ring-[#1890ff]" />
        <Search class="w-3.5 h-3.5 text-gray-400 dark:text-slate-500 absolute left-2.5 top-2.5" />
      </div>
    </div>

    <!-- Tabs -->
    <div
      class="flex text-center border-b border-[#f0f0f0] dark:border-slate-800 px-1 text-[11px] bg-[#fafafa] dark:bg-slate-950">
      <button @click="activeTab = 'all'" :class="[
        'flex-1 py-2 font-medium transition-all cursor-pointer',
        activeTab === 'all'
          ? 'text-[#1890ff] dark:text-sky-400 border-b-2 border-[#1890ff] dark:border-sky-400 bg-white dark:bg-slate-900 font-bold'
          : 'text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/40 dark:hover:bg-slate-800/40'
      ]">
        全部
      </button>
      <button @click="activeTab = 'equipment'" :class="[
        'flex-1 py-2 font-medium transition-all cursor-pointer',
        activeTab === 'equipment'
          ? 'text-[#1890ff] dark:text-sky-400 border-b-2 border-[#1890ff] dark:border-sky-400 bg-white dark:bg-slate-900 font-bold'
          : 'text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/40 dark:hover:bg-slate-800/40'
      ]">
        设备
      </button>
      <button @click="activeTab = 'sensors'" :class="[
        'flex-1 py-2 font-medium transition-all cursor-pointer',
        activeTab === 'sensors'
          ? 'text-[#1890ff] dark:text-sky-400 border-b-2 border-[#1890ff] dark:border-sky-400 bg-white dark:bg-slate-900 font-bold'
          : 'text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/40 dark:hover:bg-slate-800/40'
      ]">
        仪表
      </button>
      <button @click="activeTab = 'structures'" :class="[
        'flex-1 py-2 font-medium transition-all cursor-pointer',
        activeTab === 'structures'
          ? 'text-[#1890ff] dark:text-sky-400 border-b-2 border-[#1890ff] dark:border-sky-400 bg-white dark:bg-slate-900 font-bold'
          : 'text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/40 dark:hover:bg-slate-800/40'
      ]">
        结构
      </button>
      <button @click="activeTab = 'headers'" :class="[
        'flex-1 py-2 font-medium transition-all cursor-pointer',
        activeTab === 'headers'
          ? 'text-[#1890ff] dark:text-sky-400 border-b-2 border-[#1890ff] dark:border-sky-400 bg-white dark:bg-slate-900 font-bold'
          : 'text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/40 dark:hover:bg-slate-800/40'
      ]">
        标题背景
      </button>
    </div>

    <!-- Grid List -->
    <div class="flex-1 overflow-y-auto p-3 space-y-2 bg-white dark:bg-slate-900">
      <div v-if="filteredWidgets.length === 0" class="text-center py-6 text-gray-400 dark:text-slate-500 text-xs">
        未找到相关组态器件
      </div>
      <div v-else v-for="widget in filteredWidgets" :key="widget.name" draggable="true"
        @dragstart="onDragStart($event, widget)"
        @click="emit('addWidget', widget.type, widget.defaultWidth, widget.defaultHeight, widget.name, undefined, undefined, widget.defaultProps())"
        class="group flex gap-3 p-2.5 bg-[#fafafa] dark:bg-slate-950/60 hover:bg-white dark:hover:bg-slate-800 border border-[#f0f0f0] dark:border-slate-800 hover:border-[#1890ff] dark:hover:border-sky-500 hover:shadow-sm rounded cursor-grab active:cursor-grabbing transition-all duration-200">
        <div
          class="w-10 h-10 rounded bg-white dark:bg-slate-900 border border-[#f0f0f0] dark:border-slate-800 flex items-center justify-center group-hover:scale-105 transition-all shadow-sm">
          <!-- Render icons -->
          <component v-if="widget.iconKind === 'lucide'" :is="widget.icon" class="w-5 h-5" :class="widget.iconColor" />
          <div v-else-if="widget.icon === 'div-h'" class="w-7 h-2 bg-slate-600 dark:bg-slate-400 rounded-full" />
          <div v-else-if="widget.icon === 'div-v'" class="w-2 h-7 bg-slate-600 dark:bg-slate-400 rounded-full" />
          <div v-else-if="widget.icon === 'div-led'"
            class="w-4 h-4 rounded-full bg-emerald-500 ring-2 ring-emerald-300 dark:ring-emerald-600 animate-pulse" />
        </div>
        <div class="flex-1 min-w-0 text-left">
          <div class="flex justify-between items-start">
            <h4
              class="text-xs font-semibold text-gray-800 dark:text-slate-200 group-hover:text-[#1890ff] dark:group-hover:text-sky-400 transition-colors">
              {{ widget.name }}
            </h4>
            <span class="text-[9px] text-gray-400 dark:text-slate-500 font-mono">
              {{ widget.defaultWidth }}x{{ widget.defaultHeight }}
            </span>
          </div>
          <p class="text-[10px] text-gray-400 dark:text-slate-400 mt-0.5 truncate leading-relaxed">
            {{ widget.description }}
          </p>
        </div>
      </div>
    </div>

    <!-- Instructions Footer -->
    <div
      class="p-3 bg-[#fafafa] dark:bg-slate-950 border-t border-[#f0f0f0] dark:border-slate-800 text-[10px] text-gray-400 dark:text-slate-500 text-center select-none leading-relaxed">
      💡 点击左侧器件即可放置在中央画布，可自由在画布上拖拽、双击配置。
    </div>
  </div>
</template>
