<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue';
import { HMIComponent } from '../types';
import HMIWidget from './HMIWidget.vue';
import {
  Maximize,
  Minimize,
  Grid,
  Trash2,
  Copy,
  Layers,
  Play,
  Edit3,
} from 'lucide-vue-next';

const props = defineProps<{
  components: HMIComponent[];
  selectedId: string | null;
  isActiveMode: boolean;
  componentValues: Record<string, number | boolean>;
}>();

const emit = defineEmits<{
  (e: 'selectComponent', id: string | null): void;
  (e: 'updateComponent', id: string, updates: Partial<HMIComponent>): void;
  (e: 'toggleMode'): void;
  (e: 'triggerToggleValue', deviceId: number | null, variableKey: string, legacyKey: string, actionType?: string, val?: any): void;
  (e: 'deleteComponent', id: string): void;
  (e: 'duplicateComponent', id: string): void;
  (e: 'clearCanvas'): void;
}>();

const canvasRef = ref<HTMLDivElement | null>(null);
const zoom = ref<number>(1);
const showGrid = ref<boolean>(true);
const snapToGrid = ref<boolean>(true);

// States to coordinate drag-and-resize of nodes
const isDragging = ref<boolean>(false);
const activeResizeHandle = ref<string | null>(null); // 'nw'|'ne'|'se'|'sw'|'e'|'s'
const dragStart = ref<{ x: number; y: number }>({ x: 0, y: 0 });
const compOriginalPos = ref<{ x: number; y: number; w: number; h: number }>({
  x: 0,
  y: 0,
  w: 0,
  h: 0,
});

const selectedComp = computed(() => {
  return props.components.find((c) => c.id === props.selectedId);
});

// Handle arrow keys for micro-adjustments in Edit Mode
const handleKeyDown = (e: KeyboardEvent) => {
  if (props.isActiveMode || !props.selectedId || !selectedComp.value) return;

  const tag = (e.target as HTMLElement).tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') {
    return; // Avoid intercepting input typings
  }

  const step = e.shiftKey ? 10 : 1;
  const snap = snapToGrid.value && !e.shiftKey ? 10 : step;

  switch (e.key) {
    case 'ArrowUp':
      e.preventDefault();
      emit('updateComponent', props.selectedId, { y: Math.max(0, selectedComp.value.y - snap) });
      break;
    case 'ArrowDown':
      e.preventDefault();
      emit('updateComponent', props.selectedId, { y: selectedComp.value.y + snap });
      break;
    case 'ArrowLeft':
      e.preventDefault();
      emit('updateComponent', props.selectedId, { x: Math.max(0, selectedComp.value.x - snap) });
      break;
    case 'ArrowRight':
      e.preventDefault();
      emit('updateComponent', props.selectedId, { x: selectedComp.value.x + snap });
      break;
    case 'Delete':
    case 'Backspace':
      e.preventDefault();
      emit('deleteComponent', props.selectedId);
      break;
    case 'd':
    case 'D':
      if (e.ctrlKey || e.metaKey) {
        e.preventDefault();
        emit('duplicateComponent', props.selectedId);
      }
      break;
  }
};

// Pointer Events callbacks
const handleDragStart = (e: MouseEvent, component: HMIComponent) => {
  if (props.isActiveMode) {
    if (component.bindVariableKey || component.bindField) {
      const devId = component.bindDeviceId ?? null;
      const varKey = component.bindVariableKey ?? component.bindField ?? '';
      const legacy = component.bindField;
      if (component.type === 'button') {
        const mode = component.props.buttonMode || 'toggle';
        if (mode === 'set-value') {
          const writeVal = component.props.clickValue ?? 1;
          emit('triggerToggleValue', devId, varKey, legacy, 'setValue', writeVal);
        } else if (mode === 'momentary') {
          // Write true on mouse press
          emit('triggerToggleValue', devId, varKey, legacy, 'momentary', true);
          
          // Fast release on window mouseup
          const onRelease = () => {
            emit('triggerToggleValue', devId, varKey, legacy, 'momentary', false);
            window.removeEventListener('mouseup', onRelease);
          };
          window.addEventListener('mouseup', onRelease);
        } else {
          // Toggle mode
          emit('triggerToggleValue', devId, varKey, legacy, 'toggle');
        }
      } else {
        // Standard toggle behavior for valves/switches
        emit('triggerToggleValue', devId, varKey, legacy, 'toggle');
      }
    }
    return;
  }

  e.stopPropagation();
  emit('selectComponent', component.id);
  isDragging.value = true;
  dragStart.value = { x: e.clientX, y: e.clientY };
  compOriginalPos.value = {
    x: component.x,
    y: component.y,
    w: component.width,
    h: component.height,
  };
};

const handleResizeStart = (e: MouseEvent, component: HMIComponent, handle: string) => {
  e.stopPropagation();
  e.preventDefault();
  activeResizeHandle.value = handle;
  dragStart.value = { x: e.clientX, y: e.clientY };
  compOriginalPos.value = {
    x: component.x,
    y: component.y,
    w: component.width,
    h: component.height,
  };
};

const handleMouseMove = (e: MouseEvent) => {
  if (!props.selectedId || !selectedComp.value) return;

  if (isDragging.value) {
    const deltaX = (e.clientX - dragStart.value.x) / zoom.value;
    const deltaY = (e.clientY - dragStart.value.y) / zoom.value;

    let nextX = compOriginalPos.value.x + deltaX;
    let nextY = compOriginalPos.value.y + deltaY;

    // Snapping inside bounds
    if (snapToGrid.value) {
      nextX = Math.round(nextX / 10) * 10;
      nextY = Math.round(nextY / 10) * 10;
    }

    nextX = Math.max(0, nextX);
    nextY = Math.max(0, nextY);

    emit('updateComponent', props.selectedId, { x: nextX, y: nextY });
  } else if (activeResizeHandle.value) {
    const deltaX = (e.clientX - dragStart.value.x) / zoom.value;
    const deltaY = (e.clientY - dragStart.value.y) / zoom.value;

    let nextW = compOriginalPos.value.w;
    let nextH = compOriginalPos.value.h;
    let nextX = compOriginalPos.value.x;
    let nextY = compOriginalPos.value.y;

    const sizeSnap = (val: number) => {
      return snapToGrid.value ? Math.round(val / 10) * 10 : val;
    };

    if (activeResizeHandle.value === 'se') {
      nextW = Math.max(20, sizeSnap(compOriginalPos.value.w + deltaX));
      nextH = Math.max(20, sizeSnap(compOriginalPos.value.h + deltaY));
    } else if (activeResizeHandle.value === 'e') {
      nextW = Math.max(20, sizeSnap(compOriginalPos.value.w + deltaX));
    } else if (activeResizeHandle.value === 's') {
      nextH = Math.max(20, sizeSnap(compOriginalPos.value.h + deltaY));
    } else if (activeResizeHandle.value === 'sw') {
      const potentialW = sizeSnap(compOriginalPos.value.w - deltaX);
      if (potentialW >= 20) {
        nextX = sizeSnap(compOriginalPos.value.x + deltaX);
        nextW = potentialW;
      }
      nextH = Math.max(20, sizeSnap(compOriginalPos.value.h + deltaY));
    } else if (activeResizeHandle.value === 'nw') {
      const potentialW = sizeSnap(compOriginalPos.value.w - deltaX);
      const potentialH = sizeSnap(compOriginalPos.value.h - deltaY);
      if (potentialW >= 20) {
        nextX = sizeSnap(compOriginalPos.value.x + deltaX);
        nextW = potentialW;
      }
      if (potentialH >= 20) {
        nextY = sizeSnap(compOriginalPos.value.y + deltaY);
        nextH = potentialH;
      }
    } else if (activeResizeHandle.value === 'ne') {
      // NE 角：宽随 deltaX 正向、高随 deltaY 反向（顶部边移动）、y 同步移动，与 sw 镜像
      const potentialH = sizeSnap(compOriginalPos.value.h - deltaY);
      if (potentialH >= 20) {
        nextY = sizeSnap(compOriginalPos.value.y + deltaY);
        nextH = potentialH;
      }
      nextW = Math.max(20, sizeSnap(compOriginalPos.value.w + deltaX));
    }

    emit('updateComponent', props.selectedId, {
      x: nextX,
      y: nextY,
      width: nextW,
      height: nextH,
    });
  }
};

const handleMouseUp = () => {
  isDragging.value = false;
  activeResizeHandle.value = null;
};

// Component alignment helpers
const alignComponents = (direction: 'top' | 'left' | 'layer-up' | 'layer-down') => {
  if (!props.selectedId || !selectedComp.value) return;
  if (direction === 'top') {
    emit('updateComponent', props.selectedId, { y: 10 });
  } else if (direction === 'left') {
    emit('updateComponent', props.selectedId, { x: 10 });
  } else if (direction === 'layer-up') {
    emit('updateComponent', props.selectedId, { zIndex: (selectedComp.value.zIndex || 1) + 1 });
  } else if (direction === 'layer-down') {
    emit('updateComponent', props.selectedId, { zIndex: Math.max(1, (selectedComp.value.zIndex || 1) - 1) });
  }
};

onMounted(() => {
  window.addEventListener('keydown', handleKeyDown);
  window.addEventListener('mouseup', handleMouseUp);
});

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeyDown);
  window.removeEventListener('mouseup', handleMouseUp);
});
</script>

<template>
  <div class="flex-1 flex flex-col bg-[#eaeaea] text-[#262626] overflow-hidden relative select-none" @mouseup="handleMouseUp">
    <!-- Top Toolbar controls -->
    <div class="h-12 border-b border-[#d9d9d9] bg-[#fafafa] px-4 flex items-center justify-between z-10 gap-2 flex-wrap shadow-sm">
      <!-- Run/Edit Mode toggle -->
      <div class="flex items-center gap-1 bg-white p-0.5 rounded border border-[#d9d9d9]">
        <button
          @click="emit('toggleMode'); emit('selectComponent', null)"
          :class="[
            'flex items-center gap-1 px-3 py-1 text-xs font-semibold rounded transition-all cursor-pointer',
            !isActiveMode
              ? 'bg-[#001529] text-white shadow-sm'
              : 'text-gray-500 hover:text-gray-800'
          ]"
        >
          <Edit3 class="w-3.5 h-3.5" />
          设计模式
        </button>
        <button
          @click="emit('toggleMode'); emit('selectComponent', null)"
          :class="[
            'flex items-center gap-1 px-3 py-1 text-xs font-semibold rounded transition-all cursor-pointer',
            isActiveMode
              ? 'bg-[#1890ff] text-white shadow-sm'
              : 'text-gray-500 hover:text-gray-800'
          ]"
        >
          <Play class="w-3.5 h-3.5 animate-pulse" />
          运行模式
        </button>
      </div>

      <!-- Zoom & Grid settings -->
      <div class="flex items-center gap-3">
        <div class="flex items-center gap-1.5 bg-white border border-[#d9d9d9] rounded p-0.5">
          <button
            @click="zoom = Math.max(0.5, zoom - 0.1)"
            class="p-1 hover:bg-gray-150 rounded text-gray-500 hover:text-gray-800 cursor-pointer"
            title="缩小"
          >
            <Minimize class="w-3.5 h-3.5" />
          </button>
          <span class="text-[10px] font-mono font-bold w-12 text-center text-gray-600">
            {{ Math.round(zoom * 100) }}%
          </span>
          <button
            @click="zoom = Math.min(1.5, zoom + 0.1)"
            class="p-1 hover:bg-gray-150 rounded text-gray-500 hover:text-gray-800 cursor-pointer"
            title="放大"
          >
            <Maximize class="w-3.5 h-3.5" />
          </button>
        </div>

        <div class="h-5 w-[1px] bg-gray-300 hidden md:block" />

        <!-- Grid align state options -->
        <div class="hidden md:flex items-center gap-1.5">
          <button
            @click="showGrid = !showGrid"
            :class="[
              'p-1.5 rounded border transition-colors cursor-pointer',
              showGrid
                ? 'bg-white border-[#1890ff] text-[#1890ff]'
                : 'bg-[#fafafa] border-[#d9d9d9] text-gray-400'
            ]"
            title="显示辅助网格"
          >
            <Grid class="w-3.5 h-3.5" />
          </button>

          <button
            @click="snapToGrid = !snapToGrid"
            :class="[
              'text-[10px] h-7 font-semibold px-2 rounded border transition-colors cursor-pointer',
              snapToGrid
                ? 'bg-white border-[#1890ff] text-[#1890ff]'
                : 'bg-[#fafafa] border-[#d9d9d9] text-gray-400'
            ]"
            title="吸附网格 (10px)"
          >
            网格吸附
          </button>
        </div>

        <div class="h-5 w-[1px] bg-gray-300" />

        <!-- Actions shortcuts on selection -->
        <div v-if="selectedId && selectedComp && !isActiveMode" class="flex items-center gap-1">
          <button
            @click="alignComponents('layer-up')"
            class="p-1.5 hover:bg-gray-100 rounded border border-[#d9d9d9] text-gray-500 hover:text-[#1890ff] cursor-pointer"
            title="置于顶层"
          >
            <Layers class="w-3.5 h-3.5 text-orange-500" />
          </button>
          <button
            @click="emit('duplicateComponent', selectedId)"
            class="p-1.5 hover:bg-gray-100 rounded border border-[#d9d9d9] text-gray-500 hover:text-[#1890ff] cursor-pointer"
            title="复制 (Ctrl+D)"
          >
            <Copy class="w-3.5 h-3.5 text-cyan-600" />
          </button>
          <button
            @click="emit('deleteComponent', selectedId)"
            class="p-1.5 hover:bg-gray-100 rounded border border-[#d9d9d9] text-gray-500 hover:text-red-500 cursor-pointer"
            title="删除"
          >
            <Trash2 class="w-3.5 h-3.5 text-red-500" />
          </button>
        </div>

        <button
          @click="emit('clearCanvas')"
          class="text-xs border border-red-200 hover:bg-red-50 text-red-500 font-bold px-2.5 py-1 rounded transition-all cursor-pointer"
        >
          清空画布
        </button>
      </div>
    </div>

    <!-- Editor Inner Stage workspace -->
    <div
      class="flex-1 overflow-auto p-8 relative flex items-start justify-start custom-scrollbar bg-[#f0f2f5]"
      @click="emit('selectComponent', null)"
      @mousemove="handleMouseMove"
    >
      <!-- Canvas bounding card container -->
      <div
        ref="canvasRef"
        class="bg-white border border-[#d9d9d9] rounded h-[700px] w-[1100px] shadow-lg relative transition-shadow duration-150"
        :style="{
          transform: `scale(${zoom})`,
          transformOrigin: 'top left',
          backgroundImage: showGrid
            ? 'radial-gradient(#d9d9d9 1px, transparent 1px)'
            : 'none',
          backgroundSize: '10px 10px',
          boxShadow: isActiveMode
            ? '0 0 30px rgba(24, 144, 255, 0.08)'
            : '0 0 20px rgba(0, 0, 0, 0.05)',
        }"
      >
        <!-- Running light watermarks or status tag -->
        <div class="absolute top-3 right-4 font-mono text-[9px] pointer-events-none select-none flex items-center gap-1.5 bg-white/90 px-2.5 py-1 rounded border border-[#d9d9d9] shadow-sm">
          <span
            :class="[
              'w-1.5 h-1.5 rounded-full',
              isActiveMode ? 'bg-amber-500 animate-pulse' : 'bg-[#1890ff]'
            ]"
          />
          {{ isActiveMode ? 'HMI 实时监控' : 'HMI 设计中心' }}
        </div>

        <div class="absolute bottom-3 left-4 font-mono text-[9px] text-gray-400 pointer-events-none select-none">
          画布尺寸: 1100 × 700 像素
        </div>

        <!-- Render individual canvas components -->
        <div
          v-for="component in components"
          :key="component.id"
          @mousedown="handleDragStart($event, component)"
          @click.stop
          :class="[
            'absolute rounded transition-shadow',
            isActiveMode ? 'cursor-pointer hover:brightness-105' : 'cursor-grab active:cursor-grabbing',
            component.id === selectedId && !isActiveMode ? 'ring-2 ring-offset-2 ring-offset-white z-50 shadow' : ''
          ]"
          :style="{
            left: `${component.x}px`,
            top: `${component.y}px`,
            width: `${component.width}px`,
            height: `${component.height}px`,
            zIndex: component.zIndex || 1,
            '--tw-ring-color': '#1890ff'
          }"
        >
          <!-- Visual rendering logic box -->
          <HMIWidget
            :component="component"
            :value="componentValues[component.id] ?? 0"
            :isActiveMode="isActiveMode"
          />

          <!-- Editable labels in component container -->
          <div
            v-if="!component.props.showValue && component.type !== 'text' && component.type !== 'led' && component.type !== 'gauge-level' && component.type !== 'gauge-dial' && component.type !== 'digital-val'"
            class="absolute -top-5 left-1/2 -translate-x-1/2 whitespace-nowrap text-[9px] bg-white/95 border border-[#d9d9d9] text-gray-600 font-mono px-1.5 py-0.5 rounded shadow-sm truncate max-w-full pointer-events-none"
          >
            {{ component.label }}
          </div>

          <!-- Edit overlay elements like resize pointers -->
          <template v-if="component.id === selectedId && !isActiveMode">
            <!-- NW Handle -->
            <div
              class="absolute -top-1.5 -left-1.5 w-3 h-3 bg-white border-2 border-[#1890ff] rounded-full cursor-nwse-resize z-50 shadow"
              @mousedown="handleResizeStart($event, component, 'nw')"
            />
            <!-- SW Handle -->
            <div
              class="absolute -bottom-1.5 -left-1.5 w-3 h-3 bg-white border-2 border-[#1890ff] rounded-full cursor-nesw-resize z-50 shadow"
              @mousedown="handleResizeStart($event, component, 'sw')"
            />
            <!-- NE Handle -->
            <div
              class="absolute -top-1.5 -right-1.5 w-3 h-3 bg-white border-2 border-[#1890ff] rounded-full cursor-nesw-resize z-50 shadow"
              @mousedown="handleResizeStart($event, component, 'ne')"
            />
            <!-- SE Handle (Primary Resize trigger) -->
            <div
              class="absolute -bottom-1.5 -right-1.5 w-3.5 h-3.5 bg-[#1890ff] border border-white rounded-full cursor-nwse-resize z-50 shadow"
              @mousedown="handleResizeStart($event, component, 'se')"
            />
            <!-- East handle -->
            <div
              class="absolute top-1/2 -right-1.5 -translate-y-1/2 w-2.5 h-2.5 bg-white border border-[#1890ff] rounded-full cursor-e-resize z-50 shadow"
              @mousedown="handleResizeStart($event, component, 'e')"
            />
            <!-- South handle -->
            <div
              class="absolute -bottom-1.5 left-1/2 -translate-x-1/2 w-2.5 h-2.5 bg-white border border-[#1890ff] rounded-full cursor-s-resize z-50 shadow"
              @mousedown="handleResizeStart($event, component, 's')"
            />
          </template>
        </div>
      </div>
    </div>
  </div>
</template>
