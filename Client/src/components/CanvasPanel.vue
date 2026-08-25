<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
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
  selectedIds: string[];
  isActiveMode: boolean;
  componentValues: Record<string, number | boolean>;
  canvasWidth: number;
  canvasHeight: number;
  canControlWrite?: boolean;
}>();

const emit = defineEmits<{
  (e: 'selectComponents', ids: string[]): void;
  (e: 'updateComponent', id: string, updates: Partial<HMIComponent>): void;
  (e: 'updateComponents', updates: { id: string; updates: Partial<HMIComponent> }[]): void;
  (e: 'toggleMode'): void;
  (e: 'triggerToggleValue', deviceId: number | null, variableKey: string, legacyKey: string, actionType?: string, val?: any): void;
  (e: 'deleteComponents', ids: string[]): void;
  (e: 'duplicateComponents', ids: string[]): void;
  (e: 'clearCanvas'): void;
  (e: 'updateCanvasSize', w: number, h: number): void;
  (e: 'addComponentAt', type: string, w: number, h: number, name: string, x: number, y: number): void;
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

// 阶段5-2：多选批量拖动——记录所有选中项的拖拽前坐标
const dragSnapshot = ref<{ id: string; x: number; y: number }[]>([]);

// 阶段5-2：空白处拖拽框选（橡皮筋）
const isBoxSelecting = ref<boolean>(false);
const boxRect = ref<{ x: number; y: number; w: number; h: number }>({ x: 0, y: 0, w: 0, h: 0 });

// 屏幕坐标 → 画布坐标（按 zoom 反算）
const toCanvasCoords = (clientX: number, clientY: number) => {
  const rect = canvasRef.value?.getBoundingClientRect();
  if (!rect) return { x: 0, y: 0 };
  return { x: (clientX - rect.left) / zoom.value, y: (clientY - rect.top) / zoom.value };
};

// Handle arrow keys for micro-adjustments in Edit Mode
const handleKeyDown = (e: KeyboardEvent) => {
  if (props.isActiveMode || props.selectedIds.length === 0) return;

  const tag = (e.target as HTMLElement).tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') {
    return; // Avoid intercepting input typings
  }

  const step = e.shiftKey ? 10 : 1;
  const snap = snapToGrid.value && !e.shiftKey ? 10 : step;
  const selSet = new Set(props.selectedIds);
  const selComps = props.components.filter((c) => selSet.has(c.id));

  switch (e.key) {
    case 'ArrowUp':
      e.preventDefault();
      emit('updateComponents', selComps.map((c) => ({ id: c.id, updates: { y: Math.max(0, c.y - snap) } })));
      break;
    case 'ArrowDown':
      e.preventDefault();
      emit('updateComponents', selComps.map((c) => ({ id: c.id, updates: { y: c.y + snap } })));
      break;
    case 'ArrowLeft':
      e.preventDefault();
      emit('updateComponents', selComps.map((c) => ({ id: c.id, updates: { x: Math.max(0, c.x - snap) } })));
      break;
    case 'ArrowRight':
      e.preventDefault();
      emit('updateComponents', selComps.map((c) => ({ id: c.id, updates: { x: c.x + snap } })));
      break;
    case 'Delete':
    case 'Backspace':
      e.preventDefault();
      emit('deleteComponents', [...props.selectedIds]);
      break;
    case 'd':
    case 'D':
      if (e.ctrlKey || e.metaKey) {
        e.preventDefault();
        emit('duplicateComponents', [...props.selectedIds]);
      }
      break;
  }
};

// Pointer Events callbacks
const handleDragStart = (e: MouseEvent, component: HMIComponent) => {
  if (props.isActiveMode) {
    // 阶段6-2：非授权角色（非 Operator/Admin）在运行模式禁止下发写指令，
    // 控件仅作只读展示；后端 [Authorize(Roles)] 仍兜底返回 403。
    if (props.canControlWrite === false) return;
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

  // 阶段5-2：多选逻辑
  const isMulti = e.ctrlKey || e.shiftKey;
  let selSet: string[];
  if (isMulti) {
    // 切换该组件在选中集合中的状态
    const exists = props.selectedIds.includes(component.id);
    selSet = exists
      ? props.selectedIds.filter((id) => id !== component.id)
      : [...props.selectedIds, component.id];
    emit('selectComponents', selSet);
  } else {
    if (props.selectedIds.includes(component.id) && props.selectedIds.length > 0) {
      // 已属多选集 → 整体拖动，保持原集合
      selSet = props.selectedIds;
    } else {
      // 单选该组件
      selSet = [component.id];
      emit('selectComponents', selSet);
    }
  }

  // 集合为空（如 Ctrl 取消最后一个选中）则不进入拖动
  if (selSet.length === 0) return;

  isDragging.value = true;
  dragStart.value = { x: e.clientX, y: e.clientY };
  // 记录所有选中项拖拽前坐标（用于整体平移 + 网格吸附）
  const selSetObj = new Set(selSet);
  dragSnapshot.value = props.components
    .filter((c) => selSetObj.has(c.id))
    .map((c) => ({ id: c.id, x: c.x, y: c.y }));
};

const handleResizeStart = (e: MouseEvent, component: HMIComponent, handle: string) => {
  // 阶段5-2：缩放手柄仅对单选组件生效
  if (props.selectedIds.length !== 1 || component.id !== props.selectedId) return;
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
  // 阶段5-2：框选（空白区域拖拽橡皮筋）
  if (isBoxSelecting.value) {
    const cur = toCanvasCoords(e.clientX, e.clientY);
    const x = Math.min(boxRect.value.x, cur.x);
    const y = Math.min(boxRect.value.y, cur.y);
    const w = Math.abs(cur.x - boxRect.value.x);
    const h = Math.abs(cur.y - boxRect.value.y);
    boxRect.value = { x, y, w, h };
    return;
  }

  // 无拖拽 / 无缩放 / 无框选 → 无需处理（批量拖动时 selectedId 为空但 isDragging 为真，需放行）
  if (!isDragging.value && !activeResizeHandle.value) return;

  if (isDragging.value) {
    if (dragSnapshot.value.length === 0) return;
    // 阶段5-2：批量拖动——以首个选中项为基准计算吸附后的位移，整体平移
    const deltaX = (e.clientX - dragStart.value.x) / zoom.value;
    const deltaY = (e.clientY - dragStart.value.y) / zoom.value;

    const base = dragSnapshot.value[0];
    let nextX = base.x + deltaX;
    let nextY = base.y + deltaY;

    if (snapToGrid.value && !e.shiftKey) {
      nextX = Math.round(nextX / 10) * 10;
      nextY = Math.round(nextY / 10) * 10;
    }
    nextX = Math.max(0, nextX);
    nextY = Math.max(0, nextY);

    const adx = nextX - base.x;
    const ady = nextY - base.y;

    const updates = dragSnapshot.value.map((s) => ({
      id: s.id,
      updates: { x: Math.max(0, s.x + adx), y: Math.max(0, s.y + ady) },
    }));
    emit('updateComponents', updates);
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

// 阶段5-2：空白区域按下 → 起手框选
const handleStageMouseDown = (e: MouseEvent) => {
  if (props.isActiveMode || e.button !== 0) return;
  const start = toCanvasCoords(e.clientX, e.clientY);
  isBoxSelecting.value = true;
  boxRect.value = { x: start.x, y: start.y, w: 0, h: 0 };
};

// 阶段5-2：框选结束 → 命中相交组件；极小框视为单击空白 → 取消选择
const handleStageMouseUp = () => {
  if (!isBoxSelecting.value) return;
  isBoxSelecting.value = false;
  const r = boxRect.value;
  const area = r.w * r.h;
  if (area < 16) {
    emit('selectComponents', []);
    boxRect.value = { x: 0, y: 0, w: 0, h: 0 };
    return;
  }
  const hits = props.components
    .filter((c) => {
      const cx1 = c.x;
      const cy1 = c.y;
      const cx2 = c.x + c.width;
      const cy2 = c.y + c.height;
      return cx1 < r.x + r.w && cx2 > r.x && cy1 < r.y + r.h && cy2 > r.y;
    })
    .map((c) => c.id);
  emit('selectComponents', hits);
  boxRect.value = { x: 0, y: 0, w: 0, h: 0 };
};

// 阶段5-2：组件对齐（相对画布边缘/中心；多选≥2 时相对选区包围盒；含等距分布）
const alignComponents = (direction: string) => {
  const sel = props.components.filter((c) => props.selectedIds.includes(c.id));
  if (!sel.length) return;

  const snapVal = (v: number) => Math.max(0, Math.round(v));

  // 等距分布：跨选区包围盒均匀铺开
  if (direction === 'distribute-h' || direction === 'distribute-v') {
    const sorted = [...sel].sort((a, b) =>
      direction === 'distribute-h'
        ? a.x + a.width / 2 - (b.x + b.width / 2)
        : a.y + a.height / 2 - (b.y + b.height / 2)
    );
    const first = sorted[0];
    const last = sorted[sorted.length - 1];
    if (sorted.length < 3) return;
    if (direction === 'distribute-h') {
      const span = last.x + last.width - first.x;
      const sumW = sorted.reduce((s, c) => s + c.width, 0);
      const gap = (span - sumW) / (sorted.length - 1);
      let cur = first.x;
      const finalUpdates = sorted.map((c, i) => {
        const u = i === 0 || i === sorted.length - 1 ? {} : { x: snapVal(cur) };
        cur += c.width + gap;
        return { id: c.id, updates: u };
      });
      emit('updateComponents', finalUpdates);
    } else {
      const span = last.y + last.height - first.y;
      const sumH = sorted.reduce((s, c) => s + c.height, 0);
      const gap = (span - sumH) / (sorted.length - 1);
      let cur = first.y;
      const finalUpdates = sorted.map((c, i) => {
        const u = i === 0 || i === sorted.length - 1 ? {} : { y: snapVal(cur) };
        cur += c.height + gap;
        return { id: c.id, updates: u };
      });
      emit('updateComponents', finalUpdates);
    }
    return;
  }

  // 单选：相对画布边缘/中心
  if (sel.length === 1) {
    const c = sel[0];
    let upd: Partial<HMIComponent> = {};
    switch (direction) {
      case 'left': upd = { x: 0 }; break;
      case 'right': upd = { x: Math.max(0, props.canvasWidth - c.width) }; break;
      case 'top': upd = { y: 0 }; break;
      case 'bottom': upd = { y: Math.max(0, props.canvasHeight - c.height) }; break;
      case 'h-center': upd = { x: Math.round((props.canvasWidth - c.width) / 2) }; break;
      case 'v-center': upd = { y: Math.round((props.canvasHeight - c.height) / 2) }; break;
      case 'layer-up': upd = { zIndex: (c.zIndex || 1) + 1 }; break;
      case 'layer-down': upd = { zIndex: Math.max(1, (c.zIndex || 1) - 1) }; break;
    }
    if (Object.keys(upd).length) {
      emit('updateComponent', c.id, upd);
    }
    return;
  }

  // 多选：相对选区包围盒对齐
  const minX = Math.min(...sel.map((c) => c.x));
  const maxX = Math.max(...sel.map((c) => c.x + c.width));
  const minY = Math.min(...sel.map((c) => c.y));
  const maxY = Math.max(...sel.map((c) => c.y + c.height));
  const cx = (minX + maxX) / 2;
  const cy = (minY + maxY) / 2;
  const updates = sel.map((c) => {
    let u: Partial<HMIComponent> = {};
    switch (direction) {
      case 'left': u = { x: minX }; break;
      case 'right': u = { x: Math.round(maxX - c.width) }; break;
      case 'top': u = { y: minY }; break;
      case 'bottom': u = { y: Math.round(maxY - c.height) }; break;
      case 'h-center': u = { x: Math.round(cx - c.width / 2) }; break;
      case 'v-center': u = { y: Math.round(cy - c.height / 2) }; break;
    }
    return { id: c.id, updates: u };
  });
  emit('updateComponents', updates);
};

const onAlignChange = (e: Event) => {
  const dir = (e.target as HTMLSelectElement).value;
  if (dir) alignComponents(dir);
  (e.target as HTMLSelectElement).value = '';
};

// 阶段5-3：分辨率预设切换
const onPresetChange = (e: Event) => {
  const [w, h] = (e.target as HTMLSelectElement).value.split('x').map(Number);
  if (w && h) emit('updateCanvasSize', w, h);
};

// 阶段5-4：组件库拖拽投放（按 zoom 反算 + 网格吸附得到画布坐标）
const onDrop = (e: DragEvent) => {
  e.preventDefault();
  const raw = e.dataTransfer?.getData('application/x-scada-widget');
  if (!raw || !canvasRef.value) return;
  let parsed: { type: string; w: number; h: number; name: string };
  try {
    parsed = JSON.parse(raw);
  } catch {
    return;
  }
  const rect = canvasRef.value.getBoundingClientRect();
  let x = (e.clientX - rect.left) / zoom.value;
  let y = (e.clientY - rect.top) / zoom.value;
  if (snapToGrid.value) {
    x = Math.round(x / 10) * 10;
    y = Math.round(y / 10) * 10;
  }
  x = Math.max(0, x);
  y = Math.max(0, y);
  emit('addComponentAt', parsed.type, parsed.w, parsed.h, parsed.name, x, y);
};

onMounted(() => {
  window.addEventListener('keydown', handleKeyDown);
  window.addEventListener('mouseup', handleMouseUp);
  window.addEventListener('mouseup', handleStageMouseUp);
});

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeyDown);
  window.removeEventListener('mouseup', handleMouseUp);
  window.removeEventListener('mouseup', handleStageMouseUp);
});
</script>

<template>
  <div class="flex-1 flex flex-col bg-[#eaeaea] text-[#262626] overflow-hidden relative select-none" @mouseup="handleMouseUp">
    <!-- Top Toolbar controls -->
    <div class="h-12 border-b border-[#d9d9d9] bg-[#fafafa] px-4 flex items-center justify-between z-10 gap-2 flex-wrap shadow-sm">
      <!-- Run/Edit Mode toggle -->
      <div class="flex items-center gap-1 bg-white p-0.5 rounded border border-[#d9d9d9]">
        <button
          @click="emit('toggleMode'); emit('selectComponents', [])"
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
          @click="emit('toggleMode'); emit('selectComponents', [])"
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
        <div v-if="selectedIds.length > 0 && !isActiveMode" class="flex items-center gap-1">
          <button
            v-if="selectedIds.length === 1"
            @click="alignComponents('layer-up')"
            class="p-1.5 hover:bg-gray-100 rounded border border-[#d9d9d9] text-gray-500 hover:text-[#1890ff] cursor-pointer"
            title="置于顶层"
          >
            <Layers class="w-3.5 h-3.5 text-orange-500" />
          </button>
          <button
            v-if="selectedIds.length === 1"
            @click="alignComponents('layer-down')"
            class="p-1.5 hover:bg-gray-100 rounded border border-[#d9d9d9] text-gray-500 hover:text-[#1890ff] cursor-pointer"
            title="置于底层"
          >
            <Layers class="w-3.5 h-3.5 text-slate-400" />
          </button>
          <button
            @click="emit('duplicateComponents', [...selectedIds])"
            class="p-1.5 hover:bg-gray-100 rounded border border-[#d9d9d9] text-gray-500 hover:text-[#1890ff] cursor-pointer"
            title="复制 (Ctrl+D)"
          >
            <Copy class="w-3.5 h-3.5 text-cyan-600" />
          </button>
          <button
            @click="emit('deleteComponents', [...selectedIds])"
            class="p-1.5 hover:bg-gray-100 rounded border border-[#d9d9d9] text-gray-500 hover:text-red-500 cursor-pointer"
            title="删除"
          >
            <Trash2 class="w-3.5 h-3.5 text-red-500" />
          </button>
          <span v-if="selectedIds.length > 1" class="text-[10px] text-gray-400 font-mono px-1">
            已选 {{ selectedIds.length }}
          </span>
        </div>

        <!-- 阶段5-2：对齐工具条（单选→画布；多选≥2→选区包围盒/等距分布） -->
        <select
          v-if="selectedIds.length > 0 && !isActiveMode"
          @change="onAlignChange"
          class="hidden lg:block text-[10px] h-7 bg-white border border-[#d9d9d9] rounded px-1 text-gray-600 focus:outline-none cursor-pointer"
          title="组件对齐"
        >
          <option value="" disabled selected>对齐…</option>
          <template v-if="selectedIds.length === 1">
            <option value="left">左对齐</option>
            <option value="right">右对齐</option>
            <option value="top">顶对齐</option>
            <option value="bottom">底对齐</option>
            <option value="h-center">水平居中</option>
            <option value="v-center">垂直居中</option>
          </template>
          <template v-else>
            <option value="left">左对齐</option>
            <option value="right">右对齐</option>
            <option value="top">顶对齐</option>
            <option value="bottom">底对齐</option>
            <option value="h-center">水平居中</option>
            <option value="v-center">垂直居中</option>
            <option value="distribute-h">水平等距分布</option>
            <option value="distribute-v">垂直等距分布</option>
          </template>
        </select>

        <!-- 分辨率预设 -->
        <div class="hidden lg:flex items-center gap-1">
          <span class="text-[10px] text-gray-400 font-mono">分辨率</span>
          <select
            :value="`${canvasWidth}x${canvasHeight}`"
            @change="onPresetChange($event)"
            class="text-[10px] h-7 bg-white border border-[#d9d9d9] rounded px-1 text-gray-600 focus:outline-none cursor-pointer"
            title="切换画布分辨率"
          >
            <option value="1920x1080">1920×1080</option>
            <option value="1366x768">1366×768</option>
            <option value="1280x720">1280×720</option>
            <option value="1100x700">1100×700</option>
          </select>
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
      @mousedown="handleStageMouseDown"
      @mousemove="handleMouseMove"
      @mouseup="handleStageMouseUp"
    >
      <!-- Canvas bounding card container -->
      <div
        ref="canvasRef"
        class="bg-white border border-[#d9d9d9] rounded shadow-lg relative transition-shadow duration-150"
        @dragover.prevent
        @drop="onDrop"
        :style="{
          width: canvasWidth + 'px',
          height: canvasHeight + 'px',
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
          画布尺寸: {{ canvasWidth }} × {{ canvasHeight }} 像素
        </div>

        <!-- 阶段5-2：框选橡皮筋 -->
        <div
          v-if="isBoxSelecting && boxRect.w > 0 && boxRect.h > 0"
          class="absolute border border-[#1890ff] bg-[#1890ff]/10 pointer-events-none z-40"
          :style="{
            left: boxRect.x + 'px',
            top: boxRect.y + 'px',
            width: boxRect.w + 'px',
            height: boxRect.h + 'px'
          }"
        />

        <!-- Render individual canvas components -->
        <div
          v-for="component in components"
          :key="component.id"
          @mousedown="handleDragStart($event, component)"
          @click.stop
          :class="[
            'absolute rounded transition-shadow',
            isActiveMode ? 'cursor-pointer hover:brightness-105' : 'cursor-grab active:cursor-grabbing',
            selectedIds.includes(component.id) && !isActiveMode
              ? component.id === selectedId
                ? 'ring-2 ring-offset-2 ring-offset-white z-50 shadow'
                : 'ring-1 ring-[#1890ff]/60 z-40'
              : ''
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
            :control-locked="props.isActiveMode && props.canControlWrite === false"
          />

          <!-- Editable labels in component container -->
          <div
            v-if="!component.props.showValue && component.type !== 'text' && component.type !== 'led' && component.type !== 'gauge-level' && component.type !== 'gauge-dial' && component.type !== 'digital-val'"
            class="absolute -top-5 left-1/2 -translate-x-1/2 whitespace-nowrap text-[9px] bg-white/95 border border-[#d9d9d9] text-gray-600 font-mono px-1.5 py-0.5 rounded shadow-sm truncate max-w-full pointer-events-none"
          >
            {{ component.label }}
          </div>

          <!-- Edit overlay elements like resize pointers -->
          <template v-if="component.id === selectedId && !isActiveMode && selectedIds.length === 1">
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
