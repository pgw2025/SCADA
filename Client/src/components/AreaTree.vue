<script setup lang="ts">
import { ref } from 'vue';
import { AreaTreeNode } from '../types';
import {
  Factory,
  Warehouse,
  Boxes,
  MapPin,
  ChevronRight,
  ChevronDown
} from 'lucide-vue-next';

/**
 * 区域树组件（阶段 1）：递归渲染区域层级，按 AreaType 显示图标，
 * 支持展开/折叠与点击选择（emits select）。全量树渲染（区域量级天然有限）。
 *
 * 组件通过 SFC 文件名自引用实现递归（Vue3 <script setup> 约定）。
 */

const props = defineProps<{
  nodes: AreaTreeNode[];
  /** 当前选中节点 ID（高亮） */
  selectedId?: number | null;
  /** 递归深度（内部使用，根为 0） */
  depth?: number;
}>();

const emit = defineEmits<{
  (e: 'select', node: AreaTreeNode): void;
}>();

// 按 AreaType 的图标与配色（AreaTypeEnum：Factory=1/Workshop=2/ProductionLine=3/Area=4/Warehouse=5）
const areaTypeMeta: Record<number, { icon: any; cls: string; label: string }> = {
  1: { icon: Factory, cls: 'text-sky-600 dark:text-sky-400', label: '工厂' },
  2: { icon: Warehouse, cls: 'text-indigo-600 dark:text-indigo-400', label: '车间' },
  3: { icon: Boxes, cls: 'text-amber-600 dark:text-amber-400', label: '产线' },
  4: { icon: MapPin, cls: 'text-emerald-600 dark:text-emerald-400', label: '区域' },
  5: { icon: Warehouse, cls: 'text-violet-600 dark:text-violet-400', label: '仓库' }
};

const metaFor = (node: AreaTreeNode) => areaTypeMeta[node.areaType ?? 4] ?? areaTypeMeta[4];

// 展开状态（按节点 ID 记忆）
const expanded = ref<Set<number>>(new Set());
const isExpanded = (id: number) => expanded.value.has(id);
const toggleExpand = (node: AreaTreeNode) => {
  if (expanded.value.has(node.id)) {
    expanded.value.delete(node.id);
  } else {
    expanded.value.add(node.id);
  }
};

const handleSelect = (node: AreaTreeNode) => emit('select', node);
</script>

<template>
  <ul class="select-none">
    <li v-for="node in nodes" :key="node.id">
      <div
        class="flex items-center gap-1.5 rounded-md px-2 py-1.5 cursor-pointer text-left transition-all"
        :class="[
          selectedId === node.id
            ? 'bg-[#1890ff]/10 ring-1 ring-[#1890ff]/30'
            : 'hover:bg-slate-100 dark:hover:bg-slate-800/60',
          node.isEnabled === false ? 'opacity-50' : ''
        ]"
        :style="{ paddingLeft: `${(depth ?? 0) * 16 + 8}px` }"
        @click="handleSelect(node)"
      >
        <!-- 展开/折叠箭头 -->
        <button
          class="w-4 h-4 shrink-0 flex items-center justify-center rounded text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 transition-all cursor-pointer"
          @click.stop="toggleExpand(node)"
        >
          <ChevronDown
            v-if="isExpanded(node.id)"
            class="w-3.5 h-3.5"
          />
          <ChevronRight
            v-else-if="node.children.length > 0"
            class="w-3.5 h-3.5"
          />
          <span v-else class="w-3.5 h-3.5 block" />
        </button>

        <!-- 区域类型图标 -->
        <component
          :is="metaFor(node).icon"
          class="w-4 h-4 shrink-0"
          :class="metaFor(node).cls"
        />

        <!-- 名称 -->
        <span class="flex-1 min-w-0 truncate text-xs font-bold text-slate-700 dark:text-slate-200">
          {{ node.name }}
        </span>

        <!-- 类型标签 -->
        <span class="shrink-0 text-[9px] font-bold uppercase tracking-wider text-slate-400 dark:text-slate-500 hidden sm:inline">
          {{ metaFor(node).label }}
        </span>

        <!-- 设备数 -->
        <span
          class="shrink-0 text-[10px] font-bold px-1.5 py-0.5 rounded-full"
          :class="node.deviceCount > 0
            ? 'bg-sky-50 dark:bg-sky-950/60 text-[#1890ff] dark:text-sky-400'
            : 'bg-slate-100 dark:bg-slate-800 text-slate-400 dark:text-slate-500'"
        >
          {{ node.deviceCount }} 台
        </span>
      </div>

      <!-- 子节点（递归） -->
      <AreaTree
        v-if="isExpanded(node.id)"
        :nodes="node.children"
        :selected-id="selectedId"
        :depth="(depth ?? 0) + 1"
        @select="emit('select', $event)"
      />
    </li>
  </ul>
</template>
