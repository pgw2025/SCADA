<script setup lang="ts">
import { computed, ref } from 'vue';
import { HMIComponent, HMILayer, ComponentType } from '../types';
import {
  Layers,
  Eye,
  EyeOff,
  Lock,
  Unlock,
  Plus,
  Trash2,
  ChevronDown,
  ChevronRight,
  MoveUp,
  MoveDown,
  Edit2,
  Check,
  X,
  Copy,
  SlidersHorizontal,
  Component as ComponentIcon
} from 'lucide-vue-next';
import { getWidgetDef } from '../widgetRegistry';

const props = defineProps<{
  layers: HMILayer[];
  components: HMIComponent[];
  selectedId: string | null;
  selectedIds: string[];
  activeLayerId?: string | null;
}>();

const emit = defineEmits<{
  (e: 'updateLayers', layers: HMILayer[]): void;
  (e: 'updateComponent', id: string, updates: Partial<HMIComponent>): void;
  (e: 'updateComponents', updates: { id: string; updates: Partial<HMIComponent> }[]): void;
  (e: 'selectComponents', ids: string[]): void;
  (e: 'setActiveLayer', layerId: string): void;
  (e: 'deleteComponents', ids: string[]): void;
  (e: 'duplicateComponents', ids: string[]): void;
  (e: 'inspectComponent', id: string): void;
  (e: 'collapse'): void;
}>();

// 图层折叠状态 (layerId -> boolean: true 为展开)
const expandedLayers = ref<Record<string, boolean>>({});

// 是否正在重命名图层
const renamingLayerId = ref<string | null>(null);
const renameLayerInput = ref<string>('');

// 图层颜色预设
const COLOR_PRESETS = [
  '#3b82f6', // blue
  '#10b981', // emerald
  '#f59e0b', // amber
  '#8b5cf6', // purple
  '#ec4899', // pink
  '#06b6d4', // cyan
  '#64748b', // slate
];

// 默认图层确保存在
const effectiveLayers = computed<HMILayer[]>(() => {
  if (!props.layers || props.layers.length === 0) {
    return [
      {
        id: 'layer-default',
        name: '图层 1 (基础层)',
        visible: true,
        locked: false,
        opacity: 100,
        colorBadge: '#3b82f6',
      },
    ];
  }
  return props.layers;
});

// 计算每个图层包含的组件
const componentsByLayer = computed(() => {
  const map: Record<string, HMIComponent[]> = {};
  const layers = effectiveLayers.value;
  layers.forEach((l) => {
    map[l.id] = [];
  });

  const defaultLayerId = layers[0]?.id || 'layer-default';

  props.components.forEach((c) => {
    const targetLayerId = (c.layerId && map[c.layerId]) ? c.layerId : defaultLayerId;
    if (!map[targetLayerId]) {
      map[targetLayerId] = [];
    }
    map[targetLayerId].push(c);
  });

  // 每个图层内部按 zIndex 从大到小排列 (PS 式上层在前)
  Object.keys(map).forEach((k) => {
    map[k].sort((a, b) => (b.zIndex || 1) - (a.zIndex || 1));
  });

  return map;
});

// 检查图层是否展开
const isExpanded = (layerId: string) => {
  return expandedLayers.value[layerId] ?? true; // 默认展开
};

const toggleExpand = (layerId: string) => {
  expandedLayers.value[layerId] = !isExpanded(layerId);
};

// 新建图层
const handleAddLayer = () => {
  const current = [...effectiveLayers.value];
  const nextNum = current.length + 1;
  const newColor = COLOR_PRESETS[(nextNum - 1) % COLOR_PRESETS.length];
  const newLayer: HMILayer = {
    id: `layer-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
    name: `图层 ${nextNum}`,
    visible: true,
    locked: false,
    opacity: 100,
    colorBadge: newColor,
  };
  // 插入到最顶层 (PS 习惯：新建图层位于最上方)
  const updated = [newLayer, ...current];
  emit('updateLayers', updated);
  emit('setActiveLayer', newLayer.id);
  expandedLayers.value[newLayer.id] = true;
};

// 删除图层
const handleDeleteLayer = (layerId: string) => {
  const current = effectiveLayers.value;
  if (current.length <= 1) {
    alert('至少需要保留一个图层！');
    return;
  }
  const remaining = current.filter((l) => l.id !== layerId);
  const fallbackLayerId = remaining[0].id;

  // 将被删图层中的组件迁移至保留的第一个图层
  const compsToMigrate = props.components.filter((c) => (c.layerId || current[0]?.id) === layerId);
  if (compsToMigrate.length > 0) {
    const updates = compsToMigrate.map((c) => ({
      id: c.id,
      updates: { layerId: fallbackLayerId },
    }));
    emit('updateComponents', updates);
  }

  emit('updateLayers', remaining);
};

// 切换图层显隐
const toggleLayerVisibility = (layer: HMILayer) => {
  const nextVis = !layer.visible;
  const updated = effectiveLayers.value.map((l) =>
    l.id === layer.id ? { ...l, visible: nextVis } : l
  );
  emit('updateLayers', updated);
};

// 切换图层锁定
const toggleLayerLock = (layer: HMILayer) => {
  const nextLock = !layer.locked;
  const updated = effectiveLayers.value.map((l) =>
    l.id === layer.id ? { ...l, locked: nextLock } : l
  );
  emit('updateLayers', updated);
};

// 切换单个组件显隐
const toggleComponentVisibility = (comp: HMIComponent) => {
  const nextVis = comp.visible !== false ? false : true;
  emit('updateComponent', comp.id, { visible: nextVis });
};

// 切换单个组件锁定
const toggleComponentLock = (comp: HMIComponent) => {
  const nextLock = comp.locked === true ? false : true;
  emit('updateComponent', comp.id, { locked: nextLock });
};

// 图层排序：上移 / 下移
const moveLayer = (index: number, direction: 'up' | 'down') => {
  const list = [...effectiveLayers.value];
  const targetIndex = direction === 'up' ? index - 1 : index + 1;
  if (targetIndex < 0 || targetIndex >= list.length) return;

  const temp = list[index];
  list[index] = list[targetIndex];
  list[targetIndex] = temp;

  emit('updateLayers', list);
};

// 开始重命名图层
const startRenameLayer = (layer: HMILayer) => {
  renamingLayerId.value = layer.id;
  renameLayerInput.value = layer.name;
};

const saveRenameLayer = (layerId: string) => {
  if (!renameLayerInput.value.trim()) {
    renamingLayerId.value = null;
    return;
  }
  const updated = effectiveLayers.value.map((l) =>
    l.id === layerId ? { ...l, name: renameLayerInput.value.trim() } : l
  );
  emit('updateLayers', updated);
  renamingLayerId.value = null;
};

// 选择组件
const selectComp = (comp: HMIComponent, event: MouseEvent) => {
  if (event.ctrlKey || event.metaKey) {
    const isAlready = props.selectedIds.includes(comp.id);
    const next = isAlready
      ? props.selectedIds.filter((id) => id !== comp.id)
      : [...props.selectedIds, comp.id];
    emit('selectComponents', next);
  } else {
    emit('selectComponents', [comp.id]);
  }
};

// 将选中的组件移动到指定图层
const moveSelectedToLayer = (targetLayerId: string) => {
  if (props.selectedIds.length === 0) return;
  const updates = props.selectedIds.map((id) => ({
    id,
    updates: { layerId: targetLayerId },
  }));
  emit('updateComponents', updates);
};

// 获取组件的友好类型名与图标
const getComponentTitle = (comp: HMIComponent) => {
  if (comp.name) return comp.name;
  if (comp.label) return comp.label;
  const def = getWidgetDef(comp.type);
  return def ? def.name : comp.type;
};

// 改变图层透明度
const updateLayerOpacity = (layer: HMILayer, val: number) => {
  const updated = effectiveLayers.value.map((l) =>
    l.id === layer.id ? { ...l, opacity: val } : l
  );
  emit('updateLayers', updated);
};
</script>

<template>
  <div class="h-full flex flex-col bg-white dark:bg-slate-900 text-slate-800 dark:text-slate-100 select-none text-xs">
    <!-- Panel Action Subheader (Add Layer & Summary) -->
    <div
      class="px-3 py-2 border-b border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-950/70 flex items-center justify-between shrink-0">
      <div class="flex items-center gap-1.5 text-slate-700 dark:text-slate-200 font-semibold text-xs">
        <Layers class="w-3.5 h-3.5 text-indigo-600 dark:text-indigo-400" />
        <span>PS 图层结构</span>
        <span class="text-[10px] text-slate-400 dark:text-slate-500 font-normal">({{ effectiveLayers.length }} 个图层)</span>
      </div>
      <div class="flex items-center gap-1">
        <button @click="handleAddLayer"
          class="px-2 py-1 rounded bg-[#1890ff] hover:bg-sky-600 active:scale-95 text-white font-medium text-xs flex items-center gap-1 shadow-2xs transition-all cursor-pointer"
          title="新建图层">
          <Plus class="w-3.5 h-3.5" />
          <span>新建图层</span>
        </button>
      </div>
    </div>

    <!-- Quick Tool for Selected Component: Move to Layer -->
    <div v-if="selectedIds.length > 0"
      class="px-3 py-2 bg-sky-50/70 dark:bg-sky-950/40 border-b border-sky-100 dark:border-sky-900/50 flex items-center justify-between gap-2 shrink-0">
      <span class="text-[11px] text-sky-800 dark:text-sky-300 font-medium truncate">
        已选 {{ selectedIds.length }} 个元件：
      </span>
      <div class="flex items-center gap-1">
        <select @change="moveSelectedToLayer(($event.target as HTMLSelectElement).value)"
          class="bg-white dark:bg-slate-900 border border-sky-300 dark:border-sky-800 text-sky-900 dark:text-sky-200 text-[11px] rounded px-1.5 py-0.5 outline-none cursor-pointer">
          <option value="" disabled selected>移至图层...</option>
          <option v-for="layer in effectiveLayers" :key="layer.id" :value="layer.id">
            {{ layer.name }}
          </option>
        </select>
      </div>
    </div>

    <!-- Layers List Body (PS style layer tree) -->
    <div class="flex-1 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800/80 custom-scrollbar">
      <div v-for="(layer, index) in effectiveLayers" :key="layer.id"
        class="group/layer flex flex-col transition-colors border-l-4"
        :style="{ borderLeftColor: layer.colorBadge || '#3b82f6' }"
        :class="activeLayerId === layer.id ? 'bg-slate-50/80 dark:bg-slate-800/40' : ''">

        <!-- Layer Header Row -->
        <div
          class="flex items-center justify-between px-2.5 py-2 hover:bg-slate-100/70 dark:hover:bg-slate-800/60 cursor-pointer gap-1.5"
          @click="emit('setActiveLayer', layer.id)">

          <!-- Left expand caret & layer title -->
          <div class="flex items-center gap-1.5 min-w-0 flex-1">
            <button @click.stop="toggleExpand(layer.id)"
              class="text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 p-0.5 rounded cursor-pointer">
              <ChevronDown v-if="isExpanded(layer.id)" class="w-3.5 h-3.5" />
              <ChevronRight v-else class="w-3.5 h-3.5" />
            </button>

            <!-- Rename Mode or Label -->
            <div v-if="renamingLayerId === layer.id" class="flex items-center gap-1 flex-1" @click.stop>
              <input v-model="renameLayerInput" type="text"
                class="w-full bg-white dark:bg-slate-900 border border-[#1890ff] rounded px-1 py-0.5 text-xs text-slate-800 dark:text-slate-100 outline-none"
                @keyup.enter="saveRenameLayer(layer.id)" />
              <button @click="saveRenameLayer(layer.id)"
                class="text-emerald-600 dark:text-emerald-400 hover:text-emerald-700">
                <Check class="w-3.5 h-3.5" />
              </button>
            </div>
            <div v-else class="flex items-center gap-1.5 min-w-0 flex-1" @dblclick="startRenameLayer(layer)">
              <span class="font-semibold text-xs truncate" :class="[
                layer.visible ? 'text-slate-800 dark:text-slate-100' : 'text-slate-400 dark:text-slate-500 line-through opacity-70',
                layer.locked ? 'italic' : ''
              ]">
                {{ layer.name }}
              </span>
              <span class="text-[10px] text-slate-400 dark:text-slate-500 shrink-0 font-mono">
                ({{ (componentsByLayer[layer.id] || []).length }})
              </span>
            </div>
          </div>

          <!-- Layer Action Icons: Visibility, Lock, Reorder, Delete -->
          <div class="flex items-center gap-1 shrink-0">
            <!-- Eye Toggle (Visible / Hidden) -->
            <button @click.stop="toggleLayerVisibility(layer)" class="p-1 rounded transition-colors cursor-pointer"
              :class="layer.visible
                ? 'text-slate-600 dark:text-slate-300 hover:bg-slate-200 dark:hover:bg-slate-700'
                : 'text-amber-500 bg-amber-50 dark:bg-amber-950/50 hover:bg-amber-100 dark:hover:bg-amber-900/60'"
              :title="layer.visible ? '隐藏此图层' : '显示此图层'">
              <Eye v-if="layer.visible" class="w-3.5 h-3.5" />
              <EyeOff v-else class="w-3.5 h-3.5" />
            </button>

            <!-- Lock Toggle (Locked / Unlocked) -->
            <button @click.stop="toggleLayerLock(layer)" class="p-1 rounded transition-colors cursor-pointer" :class="layer.locked
              ? 'text-rose-500 bg-rose-50 dark:bg-rose-950/50 hover:bg-rose-100 dark:hover:bg-rose-900/60'
              : 'text-slate-400 hover:text-slate-600 dark:hover:text-slate-300 hover:bg-slate-200 dark:hover:bg-slate-700'"
              :title="layer.locked ? '解锁此图层' : '锁定此图层（禁止选中/拖拽）'">
              <Lock v-if="layer.locked" class="w-3.5 h-3.5" />
              <Unlock v-else class="w-3.5 h-3.5" />
            </button>

            <!-- Order Buttons: Up / Down -->
            <div class="hidden group-hover/layer:flex items-center gap-0.5">
              <button :disabled="index === 0" @click.stop="moveLayer(index, 'up')"
                class="p-0.5 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 disabled:opacity-20 cursor-pointer"
                title="图层上移">
                <MoveUp class="w-3 h-3" />
              </button>
              <button :disabled="index === effectiveLayers.length - 1" @click.stop="moveLayer(index, 'down')"
                class="p-0.5 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 disabled:opacity-20 cursor-pointer"
                title="图层下移">
                <MoveDown class="w-3 h-3" />
              </button>
              <button @click.stop="startRenameLayer(layer)"
                class="p-0.5 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 cursor-pointer"
                title="重命名图层">
                <Edit2 class="w-3 h-3" />
              </button>
              <button v-if="effectiveLayers.length > 1" @click.stop="handleDeleteLayer(layer.id)"
                class="p-0.5 rounded text-rose-400 hover:text-rose-600 cursor-pointer" title="删除图层">
                <Trash2 class="w-3 h-3" />
              </button>
            </div>
          </div>
        </div>

        <!-- Layer Opacity & Settings Sub-bar (if expanded) -->
        <div v-if="isExpanded(layer.id)"
          class="px-6 py-1 bg-slate-50/50 dark:bg-slate-950/40 flex items-center justify-between border-t border-slate-100 dark:border-slate-800/40 text-[10px] text-slate-400">
          <span>不透明度:</span>
          <div class="flex items-center gap-1.5">
            <input type="range" min="10" max="100" step="5" :value="layer.opacity ?? 100"
              @input="updateLayerOpacity(layer, Number(($event.target as HTMLInputElement).value))"
              class="w-16 h-1 bg-slate-200 dark:bg-slate-700 rounded-lg appearance-none cursor-pointer accent-[#1890ff]" />
            <span class="font-mono w-7 text-right">{{ layer.opacity ?? 100 }}%</span>
          </div>
        </div>

        <!-- Layer's Components Sub-list -->
        <div v-if="isExpanded(layer.id)" class="pl-4 pr-1 py-1 space-y-0.5 bg-slate-50/20 dark:bg-slate-900/30">
          <!-- Empty State in Layer -->
          <div v-if="!componentsByLayer[layer.id] || componentsByLayer[layer.id].length === 0"
            class="py-2 text-center text-[10px] text-slate-400 italic">
            此图层暂次元件，可将画布元件移入
          </div>

          <!-- Components in this layer -->
          <div v-for="comp in componentsByLayer[layer.id]" :key="comp.id"
            @click.stop="selectComp(comp, $event)"
            @dblclick.stop="emit('selectComponents', [comp.id]); emit('inspectComponent', comp.id)"
            class="group/comp flex items-center justify-between px-2 py-1 rounded text-[11px] cursor-pointer transition-all border"
            :class="[
              selectedIds.includes(comp.id)
                ? 'bg-sky-100/80 dark:bg-sky-950/70 border-sky-300 dark:border-sky-700 text-sky-900 dark:text-sky-200 font-semibold'
                : 'border-transparent hover:bg-slate-100 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-300',
              (comp.visible === false || !layer.visible) ? 'opacity-40 line-through' : '',
              (comp.locked || layer.locked) ? 'italic' : ''
            ]"
            title="单击选中，双击快速进入属性配置">

            <!-- Left: Component Type / Name -->
            <div class="flex items-center gap-1.5 min-w-0 flex-1">
              <ComponentIcon class="w-3 h-3 text-[#1890ff] dark:text-sky-400 shrink-0" />
              <span class="truncate">{{ getComponentTitle(comp) }}</span>
              <span class="text-[9px] font-mono text-slate-400 dark:text-slate-500 shrink-0">
                #{{ comp.id.slice(-4) }}
              </span>
            </div>

            <!-- Right: Component Specific Visible / Lock Switches & Quick Inspect -->
            <div class="flex items-center gap-1 shrink-0 opacity-0 group-hover/comp:opacity-100 focus-within:opacity-100 transition-opacity">
              <!-- Quick Inspect Property Button -->
              <button @click.stop="emit('selectComponents', [comp.id]); emit('inspectComponent', comp.id)"
                class="p-0.5 rounded text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 cursor-pointer"
                title="配置此元件属性">
                <SlidersHorizontal class="w-3 h-3" />
              </button>

              <!-- Single Component Visible Toggle -->
              <button @click.stop="toggleComponentVisibility(comp)"
                class="p-0.5 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 cursor-pointer"
                :title="comp.visible !== false ? '隐藏此元件' : '显示此元件'">
                <Eye v-if="comp.visible !== false" class="w-3 h-3" />
                <EyeOff v-else class="w-3 h-3 text-amber-500" />
              </button>

              <!-- Single Component Lock Toggle -->
              <button @click.stop="toggleComponentLock(comp)"
                class="p-0.5 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 cursor-pointer"
                :title="comp.locked ? '解锁此元件' : '锁定此元件'">
                <Lock v-if="comp.locked" class="w-3 h-3 text-rose-500" />
                <Unlock v-else class="w-3 h-3" />
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Panel Footer Stats -->
    <div
      class="p-2.5 border-t border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-950 flex items-center justify-between text-[11px] text-slate-500 dark:text-slate-400 shrink-0 font-mono">
      <span>图层数: {{ effectiveLayers.length }}</span>
      <span>总元件: {{ components.length }}</span>
    </div>
  </div>
</template>
