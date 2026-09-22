<script setup lang="ts">
import { AreaTreeNode } from '../../types';
import {
  X,
  Plus,
  Filter,
  Factory,
  Warehouse,
  Boxes,
  MapPin,
  Edit3,
  Trash2,
  Play,
  Pause
} from 'lucide-vue-next';

const props = defineProps<{
  open: boolean;
  nodes: AreaTreeNode[];
  selectedId: number | null;
  includeSubareas: boolean;
}>();

const emit = defineEmits<{
  (e: 'close'): void;
  (e: 'select', id: number | null): void;
  (e: 'update:includeSubareas', val: boolean): void;
  (e: 'addArea', parentId?: number | null): void;
  (e: 'editArea', node: AreaTreeNode): void;
  (e: 'deleteArea', node: AreaTreeNode): void;
  (e: 'batchToggle', node: AreaTreeNode, enabled: boolean): void;
}>();

const areaTypeMeta: Record<number, { icon: any; cls: string; label: string }> = {
  1: { icon: Factory, cls: 'text-sky-600 dark:text-sky-400', label: '工厂' },
  2: { icon: Warehouse, cls: 'text-indigo-600 dark:text-indigo-400', label: '车间' },
  3: { icon: Boxes, cls: 'text-amber-600 dark:text-amber-400', label: '产线' },
  4: { icon: MapPin, cls: 'text-emerald-600 dark:text-emerald-400', label: '区域' },
  5: { icon: Warehouse, cls: 'text-violet-600 dark:text-violet-400', label: '仓库' }
};
const metaFor = (t?: number) => areaTypeMeta[t ?? 4] ?? areaTypeMeta[4];

// Flatten tree for clean mobile list
const flattenNodes = (list: AreaTreeNode[], depth = 0, out: { node: AreaTreeNode; depth: number }[] = []): { node: AreaTreeNode; depth: number }[] => {
  list.forEach(n => {
    out.push({ node: n, depth });
    if (n.children?.length) flattenNodes(n.children, depth + 1, out);
  });
  return out;
};
</script>

<template>
  <div v-if="open" class="fixed inset-0 z-50 overflow-hidden select-none">
    <!-- Backdrop -->
    <div class="absolute inset-0 bg-slate-900/50 backdrop-blur-xs transition-opacity" @click="emit('close')" />

    <!-- Bottom Sheet container -->
    <div class="fixed inset-x-0 bottom-0 z-50 max-h-[85vh] bg-white dark:bg-slate-900 rounded-t-2xl shadow-2xl flex flex-col overflow-hidden text-left border-t border-slate-200 dark:border-slate-800">
      <!-- Drag handle indicator -->
      <div class="w-12 h-1 bg-slate-300 dark:bg-slate-700 rounded-full mx-auto mt-2.5 mb-1" />

      <!-- Header -->
      <div class="px-4 py-3 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
        <div class="flex items-center gap-2">
          <span class="font-bold text-sm text-slate-900 dark:text-white">选择工艺区域</span>
          <button
            type="button"
            @click="emit('addArea', null)"
            class="text-[11px] text-[#1890ff] dark:text-sky-400 font-bold inline-flex items-center gap-0.5 px-2 py-0.5 rounded bg-sky-50 dark:bg-sky-950/60"
          >
            <Plus class="w-3 h-3" />
            <span>新建区域</span>
          </button>
        </div>
        <button
          type="button"
          @click="emit('close')"
          class="p-1.5 rounded-lg text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
        >
          <X class="w-5 h-5" />
        </button>
      </div>

      <!-- Subareas Switch -->
      <div class="px-4 py-2.5 bg-slate-50 dark:bg-slate-950/40 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between text-xs">
        <label class="flex items-center gap-2 text-slate-600 dark:text-slate-300 font-medium">
          <input
            type="checkbox"
            :checked="includeSubareas"
            @change="emit('update:includeSubareas', ($event.target as HTMLInputElement).checked)"
            class="rounded text-[#1890ff] focus:ring-0 w-4 h-4"
          />
          <span>包含下属子区域设备</span>
        </label>
      </div>

      <!-- Area items list -->
      <div class="flex-1 overflow-y-auto p-3 space-y-1 text-xs">
        <!-- All areas option -->
        <div
          @click="emit('select', null); emit('close')"
          class="flex items-center justify-between p-3 rounded-xl cursor-pointer transition-colors"
          :class="selectedId === null
            ? 'bg-[#1890ff]/10 text-[#1890ff] dark:text-sky-400 font-bold border border-[#1890ff]/30'
            : 'bg-slate-50 dark:bg-slate-800/60 text-slate-700 dark:text-slate-200'"
        >
          <div class="flex items-center gap-2">
            <Filter class="w-4 h-4 text-slate-400" />
            <span class="text-sm font-medium">全部区域</span>
          </div>
        </div>

        <!-- Flattened nodes with indentation -->
        <div
          v-for="{ node, depth } in flattenNodes(nodes)"
          :key="node.id"
          class="flex items-center justify-between p-2.5 rounded-xl cursor-pointer transition-colors"
          :class="[
            selectedId === node.id
              ? 'bg-[#1890ff]/10 text-[#1890ff] dark:text-sky-400 font-bold border border-[#1890ff]/30'
              : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-700 dark:text-slate-200',
            node.isEnabled === false ? 'opacity-50' : ''
          ]"
          :style="{ paddingLeft: `${depth * 16 + 12}px` }"
          @click="emit('select', node.id); emit('close')"
        >
          <div class="flex items-center gap-2 min-w-0 flex-1">
            <component :is="metaFor(node.areaType).icon" class="w-4 h-4 shrink-0" :class="metaFor(node.areaType).cls" />
            <span class="truncate text-xs font-medium">{{ node.name }}</span>
            <span class="text-[9px] px-1 py-0.2 rounded bg-slate-100 dark:bg-slate-800 text-slate-400 shrink-0">
              {{ metaFor(node.areaType).label }}
            </span>
          </div>

          <div class="flex items-center gap-1 shrink-0 ml-2" @click.stop>
            <span class="text-[10px] font-mono font-bold px-1.5 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-500 mr-1">
              {{ node.deviceCount }} 台
            </span>
            <button
              type="button"
              @click.stop="emit('batchToggle', node, true); emit('close')"
              class="p-1 rounded text-emerald-500 hover:text-emerald-600"
              title="批量启用该区域采集"
            >
              <Play class="w-3.5 h-3.5" />
            </button>
            <button
              type="button"
              @click.stop="emit('batchToggle', node, false); emit('close')"
              class="p-1 rounded text-amber-500 hover:text-amber-600"
              title="批量停用该区域采集"
            >
              <Pause class="w-3.5 h-3.5" />
            </button>
            <button
              type="button"
              @click.stop="emit('editArea', node)"
              class="p-1 rounded text-slate-400 hover:text-[#1890ff]"
            >
              <Edit3 class="w-3.5 h-3.5" />
            </button>
            <button
              type="button"
              @click.stop="emit('deleteArea', node)"
              class="p-1 rounded text-slate-400 hover:text-rose-500"
            >
              <Trash2 class="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
