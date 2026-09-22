<script setup lang="ts">
import { ref } from 'vue';
import { AreaTreeNode } from '../../types';
import {
  Factory,
  Warehouse,
  Boxes,
  MapPin,
  ChevronRight,
  ChevronDown,
  Plus,
  Edit3,
  Trash2,
  FolderTree,
  ChevronLeft,
  Filter,
  Play,
  Pause
} from 'lucide-vue-next';

const props = defineProps<{
  nodes: AreaTreeNode[];
  selectedId: number | null;
  includeSubareas: boolean;
  totalAreas: number;
  collapsed: boolean;
}>();

const emit = defineEmits<{
  (e: 'select', id: number | null): void;
  (e: 'update:includeSubareas', val: boolean): void;
  (e: 'update:collapsed', val: boolean): void;
  (e: 'addArea', parentId?: number | null): void;
  (e: 'editArea', node: AreaTreeNode): void;
  (e: 'deleteArea', node: AreaTreeNode): void;
  (e: 'batchToggle', node: AreaTreeNode, enabled: boolean): void;
}>();

// Type meta mapping
const areaTypeMeta: Record<number, { icon: any; cls: string; label: string }> = {
  1: { icon: Factory, cls: 'text-sky-600 dark:text-sky-400', label: '工厂' },
  2: { icon: Warehouse, cls: 'text-indigo-600 dark:text-indigo-400', label: '车间' },
  3: { icon: Boxes, cls: 'text-amber-600 dark:text-amber-400', label: '产线' },
  4: { icon: MapPin, cls: 'text-emerald-600 dark:text-emerald-400', label: '区域' },
  5: { icon: Warehouse, cls: 'text-violet-600 dark:text-violet-400', label: '仓库' }
};
const metaFor = (t?: number) => areaTypeMeta[t ?? 4] ?? areaTypeMeta[4];

// Expand state
const expandedIds = ref<Set<number>>(new Set());
const toggleExpand = (id: number) => {
  if (expandedIds.value.has(id)) expandedIds.value.delete(id);
  else expandedIds.value.add(id);
};
const isExpanded = (id: number) => expandedIds.value.has(id);

// Search inside tree
const treeSearch = ref<string>('');
</script>

<template>
  <aside
    class="relative flex flex-col bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 transition-all duration-300 select-none shrink-0"
    :class="collapsed ? 'w-14' : 'w-72 lg:w-80'"
  >
    <!-- Collapsed Toggle Button -->
    <button
      type="button"
      @click="emit('update:collapsed', !collapsed)"
      class="absolute -right-3.5 top-5 z-20 w-7 h-7 rounded-full bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-xs flex items-center justify-center text-slate-500 hover:text-slate-800 dark:hover:text-slate-200 cursor-pointer transition-transform hover:scale-105"
      :title="collapsed ? '展开区域拓扑树' : '收起区域拓扑树'"
    >
      <ChevronRight v-if="collapsed" class="w-4 h-4" />
      <ChevronLeft v-else class="w-4 h-4" />
    </button>

    <!-- Header: when collapsed -->
    <div v-if="collapsed" class="p-3 flex flex-col items-center gap-4 border-b border-slate-100 dark:border-slate-800">
      <div class="w-8 h-8 rounded-lg bg-[#1890ff]/10 text-[#1890ff] flex items-center justify-center font-bold" title="工艺区域拓扑">
        <FolderTree class="w-4 h-4" />
      </div>
      <button
        type="button"
        @click="emit('select', null)"
        class="w-8 h-8 rounded-lg flex items-center justify-center cursor-pointer transition-colors"
        :class="selectedId === null ? 'bg-slate-900 text-white dark:bg-sky-600' : 'text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800'"
        title="全部区域"
      >
        <Filter class="w-4 h-4" />
      </button>
    </div>

    <!-- Header: when expanded -->
    <div v-else class="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
      <div class="flex items-center gap-2">
        <FolderTree class="w-4 h-4 text-[#1890ff]" />
        <span class="text-xs font-bold text-slate-900 dark:text-white uppercase tracking-wider">工艺区域拓扑</span>
        <span class="text-[10px] font-mono px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 font-bold">
          {{ totalAreas }}
        </span>
      </div>
      <button
        type="button"
        @click="emit('addArea', null)"
        class="inline-flex items-center gap-1 text-[11px] font-bold text-[#1890ff] hover:text-sky-600 dark:text-sky-400 cursor-pointer px-2 py-1 rounded hover:bg-sky-50 dark:hover:bg-sky-950/50 transition-colors"
        title="添加根级区域"
      >
        <Plus class="w-3.5 h-3.5" />
        <span>添加</span>
      </button>
    </div>

    <!-- Controls when expanded: Subareas toggle -->
    <div v-if="!collapsed" class="px-3.5 py-2.5 bg-slate-50/70 dark:bg-slate-950/40 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between text-xs">
      <label class="flex items-center gap-1.5 text-slate-600 dark:text-slate-300 font-medium cursor-pointer text-[11px]">
        <input
          type="checkbox"
          :checked="includeSubareas"
          @change="emit('update:includeSubareas', ($event.target as HTMLInputElement).checked)"
          class="rounded text-[#1890ff] focus:ring-0"
        />
        <span>包含子区域设备</span>
      </label>
      <button
        v-if="selectedId !== null"
        type="button"
        @click="emit('select', null)"
        class="text-[11px] text-[#1890ff] hover:underline cursor-pointer font-medium"
      >
        重置筛选
      </button>
    </div>

    <!-- Tree Content List -->
    <div v-if="!collapsed" class="flex-1 overflow-y-auto p-2 space-y-0.5 text-xs text-left">
      <!-- Root: All Areas Item -->
      <div
        @click="emit('select', null)"
        class="group flex items-center justify-between px-2.5 py-2 rounded-lg cursor-pointer transition-all mb-1"
        :class="selectedId === null
          ? 'bg-[#1890ff]/10 text-[#1890ff] dark:text-sky-400 font-bold ring-1 ring-[#1890ff]/30'
          : 'hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-700 dark:text-slate-300'"
      >
        <div class="flex items-center gap-2 truncate">
          <Filter class="w-4 h-4 shrink-0 text-slate-400 group-hover:text-slate-600" />
          <span class="truncate">全部区域</span>
        </div>
      </div>

      <!-- Recursive Tree Items Template -->
      <template v-for="node in nodes" :key="node.id">
        <!-- Tree Item Helper Component Inline -->
        <div
          @click="emit('select', node.id)"
          class="group flex items-center justify-between px-2 py-1.5 rounded-lg cursor-pointer transition-all"
          :class="[
            selectedId === node.id
              ? 'bg-[#1890ff]/10 text-[#1890ff] dark:text-sky-400 font-bold ring-1 ring-[#1890ff]/30'
              : 'hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-700 dark:text-slate-300',
            node.isEnabled === false ? 'opacity-50' : ''
          ]"
        >
          <div class="flex items-center gap-1.5 min-w-0 flex-1">
            <!-- Expand toggle arrow -->
            <button
              v-if="node.children && node.children.length > 0"
              type="button"
              @click.stop="toggleExpand(node.id)"
              class="w-4 h-4 shrink-0 flex items-center justify-center text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 cursor-pointer"
            >
              <ChevronDown v-if="isExpanded(node.id)" class="w-3.5 h-3.5" />
              <ChevronRight v-else class="w-3.5 h-3.5" />
            </button>
            <span v-else class="w-4 h-4 shrink-0" />

            <!-- Type Icon -->
            <component :is="metaFor(node.areaType).icon" class="w-3.5 h-3.5 shrink-0" :class="metaFor(node.areaType).cls" />

            <!-- Name -->
            <span class="truncate text-[12px] font-medium">{{ node.name }}</span>
          </div>

          <!-- Device count and hover actions -->
          <div class="flex items-center gap-1 shrink-0 ml-1">
            <!-- Actions visible on hover -->
            <div class="hidden group-hover:flex items-center gap-0.5 mr-0.5">
              <button
                type="button"
                @click.stop="emit('batchToggle', node, true)"
                class="w-5 h-5 rounded hover:bg-emerald-100 dark:hover:bg-emerald-950 text-emerald-600 flex items-center justify-center cursor-pointer"
                title="批量启用该区域采集"
              >
                <Play class="w-3 h-3" />
              </button>
              <button
                type="button"
                @click.stop="emit('batchToggle', node, false)"
                class="w-5 h-5 rounded hover:bg-amber-100 dark:hover:bg-amber-950 text-amber-600 flex items-center justify-center cursor-pointer"
                title="批量停用该区域采集"
              >
                <Pause class="w-3 h-3" />
              </button>
              <button
                type="button"
                @click.stop="emit('addArea', node.id)"
                class="w-5 h-5 rounded hover:bg-sky-100 dark:hover:bg-sky-950 text-[#1890ff] flex items-center justify-center cursor-pointer"
                title="添加子区域"
              >
                <Plus class="w-3 h-3" />
              </button>
              <button
                type="button"
                @click.stop="emit('editArea', node)"
                class="w-5 h-5 rounded hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-600 dark:text-slate-300 flex items-center justify-center cursor-pointer"
                title="编辑区域"
              >
                <Edit3 class="w-3 h-3" />
              </button>
              <button
                type="button"
                @click.stop="emit('deleteArea', node)"
                class="w-5 h-5 rounded hover:bg-rose-100 dark:hover:bg-rose-950 text-rose-500 flex items-center justify-center cursor-pointer"
                title="删除区域"
              >
                <Trash2 class="w-3 h-3" />
              </button>
            </div>

            <!-- Device count badge -->
            <span
              class="text-[10px] font-mono px-1.5 py-0.5 rounded-full font-semibold"
              :class="node.deviceCount > 0 ? 'bg-sky-50 dark:bg-sky-950/60 text-[#1890ff] dark:text-sky-400' : 'text-slate-400 bg-slate-100 dark:bg-slate-800'"
            >
              {{ node.deviceCount }}
            </span>
          </div>
        </div>

        <!-- Level 2 Children -->
        <div v-if="isExpanded(node.id) && node.children && node.children.length > 0" class="pl-4 space-y-0.5 border-l border-slate-100 dark:border-slate-800 ml-3">
          <div
            v-for="sub in node.children"
            :key="sub.id"
            @click="emit('select', sub.id)"
            class="group flex items-center justify-between px-2 py-1.5 rounded-lg cursor-pointer transition-all"
            :class="[
              selectedId === sub.id
                ? 'bg-[#1890ff]/10 text-[#1890ff] dark:text-sky-400 font-bold ring-1 ring-[#1890ff]/30'
                : 'hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-700 dark:text-slate-300',
              sub.isEnabled === false ? 'opacity-50' : ''
            ]"
          >
            <div class="flex items-center gap-1.5 min-w-0 flex-1">
              <component :is="metaFor(sub.areaType).icon" class="w-3.5 h-3.5 shrink-0" :class="metaFor(sub.areaType).cls" />
              <span class="truncate text-[12px] font-medium">{{ sub.name }}</span>
            </div>
            <div class="flex items-center gap-1 shrink-0 ml-1">
              <div class="hidden group-hover:flex items-center gap-0.5 mr-0.5">
                <button
                  type="button"
                  @click.stop="emit('batchToggle', sub, true)"
                  class="w-5 h-5 rounded hover:bg-emerald-100 dark:hover:bg-emerald-950 text-emerald-600 flex items-center justify-center cursor-pointer"
                  title="批量启用该区域采集"
                >
                  <Play class="w-3 h-3" />
                </button>
                <button
                  type="button"
                  @click.stop="emit('batchToggle', sub, false)"
                  class="w-5 h-5 rounded hover:bg-amber-100 dark:hover:bg-amber-950 text-amber-600 flex items-center justify-center cursor-pointer"
                  title="批量停用该区域采集"
                >
                  <Pause class="w-3 h-3" />
                </button>
                <button
                  type="button"
                  @click.stop="emit('addArea', sub.id)"
                  class="w-5 h-5 rounded hover:bg-sky-100 dark:hover:bg-sky-950 text-[#1890ff] flex items-center justify-center cursor-pointer"
                  title="添加子区域"
                >
                  <Plus class="w-3 h-3" />
                </button>
                <button
                  type="button"
                  @click.stop="emit('editArea', sub)"
                  class="w-5 h-5 rounded hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-600 dark:text-slate-300 flex items-center justify-center cursor-pointer"
                  title="编辑区域"
                >
                  <Edit3 class="w-3 h-3" />
                </button>
                <button
                  type="button"
                  @click.stop="emit('deleteArea', sub)"
                  class="w-5 h-5 rounded hover:bg-rose-100 dark:hover:bg-rose-950 text-rose-500 flex items-center justify-center cursor-pointer"
                  title="删除区域"
                >
                  <Trash2 class="w-3 h-3" />
                </button>
              </div>
              <span
                class="text-[10px] font-mono px-1.5 py-0.5 rounded-full font-semibold"
                :class="sub.deviceCount > 0 ? 'bg-sky-50 dark:bg-sky-950/60 text-[#1890ff] dark:text-sky-400' : 'text-slate-400 bg-slate-100 dark:bg-slate-800'"
              >
                {{ sub.deviceCount }}
              </span>
            </div>
          </div>
        </div>
      </template>

      <div v-if="nodes.length === 0" class="py-8 text-center text-slate-400 text-xs">
        暂无工艺区域
      </div>
    </div>
  </aside>
</template>
