<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue';
import {
  beginChange,
  endChange,
  recordDiscrete,
  undo,
  redo,
  undoAvailable,
  redoAvailable,
  resetHistory,
} from '../services/historyService';
import {
  scadaProjects,
  selectedProjectId,
  selectedPageId,
  currentProject,
  currentPage,
  currentPageSafe,
  currentPlatform,
  desktopPages,
  mobilePages,
  scadaLoading,
  initializeScada,
  isScadaFullscreen,
  toggleScadaFullscreen,
  projectSummaries,
  reloadProjectTree,
  upsertProjectSummary
} from '../store/scadaStore';
import { exportProjectFile, exportPageFile, parseTransferFile, importProject, importPage } from '../api/scadaApi';
import {
  updateCurrentPageComponents,
  ensurePageSaved,
  persistNewComponent,
  persistComponentUpdate,
  persistComponentDelete,
  persistPageUpdate,
  persistPageDelete,
  persistProjectUpdate,
  persistProjectDelete,
  persistDuplicatePage,
  reconcileComponents
} from '../services/scadaService';
import { devices } from '../store/deviceStore';
import { loginUser } from '../store/userStore';
import { ROLE_ADMIN, ROLE_OPERATOR } from '../constants/roles';
import { addLog } from '../store/index';
import { getDeviceVariableValue, setDeviceVariableValue } from '../services/dataOrchestration';
import { pushTrendPoint, clearTrendHistory, trendHistory } from '../utils/trendHistory';
import { isSamePageRef } from '../utils/pageId';
import { subscribeDeviceTelemetry, unsubscribeDeviceTelemetry } from '../services/signalRService';
import { showToast } from '../services/toastService';
import { triggerRuntimeScript } from '../services/scriptService';
import { HMIComponent, ComponentType, ScadaScreenProject, ScadaPage, HMILayer, HmiEventType } from '../types';
import { dispatchComponentEvent, useHmiDataEvents, HmiEventDispatchContext } from '../services/hmiEventService';
import WidgetLibrary from './WidgetLibrary.vue';
import CanvasPanel from './CanvasPanel.vue';
import InspectorPanel from './InspectorPanel.vue';
import EventPanel from './EventPanel.vue';
import SetValueDialog from './SetValueDialog.vue';
import LayersPanel from './LayersPanel.vue';
import BindingCheckPanel from './BindingCheckPanel.vue';
import ConfirmModal from './ConfirmModal.vue';
import ImageLibraryDialog from './ImageLibraryDialog.vue';
import { getWidgetDef } from '../widgetRegistry';
import {
  FolderIcon,
  Layers,
  Plus,
  Trash2,
  Copy,
  PenTool,
  Edit,
  X,
  FileCode,
  Check,
  Activity,
  Undo2,
  Redo2,
  Home,
  AlertTriangle,
  Loader2,
  LayoutGrid,
  Maximize2,
  Minimize2,
  Package,
  Sliders,
  Zap,
  ChevronLeft,
  ChevronRight,
  Download,
  Upload
} from 'lucide-vue-next';

// 面板收起/展开控制状态
const isWidgetLibraryOpen = ref<boolean>(true);
// 工程列表侧边栏收起/展开控制状态
const isProjectListOpen = ref<boolean>(true);
// 右侧侧边栏（以选项卡模式整合属性配置、事件配置与图层管理）
const isRightSidebarOpen = ref<boolean>(true);
const rightActiveTab = ref<'inspector' | 'events' | 'layers'>('inspector');
// 当前选中的活动图层
const activeLayerId = ref<string | null>(null);

const toggleRightTab = (tab: 'inspector' | 'events' | 'layers') => {
  if (!isRightSidebarOpen.value) {
    isRightSidebarOpen.value = true;
    rightActiveTab.value = tab;
  } else if (rightActiveTab.value === tab) {
    isRightSidebarOpen.value = false;
  } else {
    rightActiveTab.value = tab;
  }
};

const handleInspectComponent = (id: string) => {
  selectedIds.value = [id];
  isBackgroundSelected.value = false;
  isRightSidebarOpen.value = true;
  rightActiveTab.value = 'inspector';
};

// Editor settings
// 阶段5-2：选中模型由单值升级为集合（支持多选/框选）；selectedId 为「单选」派生值，供 Inspector 单组件编辑
const selectedIds = ref<string[]>([]);
const selectedId = computed<string | null>(() =>
  selectedIds.value.length === 1 ? selectedIds.value[0] : null
);
const isActiveMode = ref<boolean>(false);

// 页面属性：背景选中态（与组件选中互斥；点击画布空白背景 → InspectorPanel 切换为「页面属性」表单）
const isBackgroundSelected = ref<boolean>(false);

// 切换页面 / 进入运行模式时退出背景属性编辑
watch(selectedPageId, () => { isBackgroundSelected.value = false; });
watch(isActiveMode, (active) => { if (active) isBackgroundSelected.value = false; });

// 阶段6：绑定检查面板（严格模式：组件必须绑定设备维度）
const showBindingCheck = ref<boolean>(false);
function onLocateComponent(id: string) {
  selectedIds.value = [id];
  showBindingCheck.value = false;
}

// Modal state for custom screen creation
const showProjectModal = ref<boolean>(false);
const newProjectName = ref<string>('');
const newProjectDesc = ref<string>('');

// 阶段5-6：统一危险操作确认模态（替换原生 confirm/alert）
const confirmState = ref<{
  open: boolean;
  title: string;
  message: string;
  danger: boolean;
  confirmText: string;
  onConfirm: () => void;
}>({ open: false, title: '', message: '', danger: true, confirmText: '确认', onConfirm: () => { } });

function askConfirm(title: string, message: string, onConfirm: () => void, danger = true, confirmText = '确认') {
  confirmState.value = { open: true, title, message, danger, confirmText, onConfirm };
}
function onConfirmModal() {
  const fn = confirmState.value.onConfirm;
  confirmState.value = { ...confirmState.value, open: false };
  fn();
}

// Inline page renaming states
const isRenamingPageId = ref<string | null>(null);
const renamePageInput = ref<string>('');

// Inline project renaming states
const isRenamingProjId = ref<string | null>(null);
const renameProjInput = ref<string>('');

// 生成全局唯一的组件 id（避免同秒内「添加+复制」撞车导致 Vue key 冲突/误删）
let _componentSeq = 0;
function genComponentId(type: string): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return `${type}-${crypto.randomUUID()}`;
  }
  _componentSeq += 1;
  return `${type}-${Date.now()}-${_componentSeq}`;
}

// 组态设计全屏模式切换
const toggleFullscreen = (targetVal?: boolean) => {
  const nextVal = typeof targetVal === 'boolean' ? targetVal : !isScadaFullscreen.value;
  toggleScadaFullscreen(nextVal);
  if (nextVal) {
    if (document.fullscreenElement == null && document.documentElement?.requestFullscreen) {
      document.documentElement.requestFullscreen().catch(() => { });
    }
    showToast('已进入全屏设计模式（按 Esc 键可退出）', 'info');
  } else {
    if (document.fullscreenElement != null && document.exitFullscreen) {
      document.exitFullscreen().catch(() => { });
    }
  }
};

const handleGlobalKeydown = (e: KeyboardEvent) => {
  if (e.key === 'Escape' && isScadaFullscreen.value) {
    toggleFullscreen(false);
    return;
  }
  onHistoryKey(e);
};

// 阶段2：挂载时从后端整树加载组态（后端为空则显示空态，引导新建工程）
onMounted(() => {
  initializeScada();
  window.addEventListener('keydown', handleGlobalKeydown);
});

onUnmounted(() => {
  window.removeEventListener('keydown', handleGlobalKeydown);
  isScadaFullscreen.value = false;
});

// 阶段3 撤销对账：切换工程/页面时清空全局命令栈，
// 避免把上一页的组件快照撤销到当前页（跨页污染）。
watch([selectedProjectId, selectedPageId], () => {
  resetHistory();
  selectedIds.value = [];
});

// 阶段5-1：撤销/重做（纯前端快照栈 + 阶段3 后端对账）
const applyRestored = (restored: HMIComponent[] | null) => {
  const pg = currentPage.value;
  if (!restored || !pg) return;
  const before = pg.components;
  updateCurrentPageComponents(restored.map((c) => ({ ...c })));
  // 阶段3 撤销对账：撤销/重做后补齐与后端差异（清孤儿/重建丢失/静默校正属性）
  reconcileComponents(pg, currentProject.value, before, restored).catch(() => { });
  if (selectedId.value && !restored.find((c) => c.id === selectedId.value)) {
    selectedIds.value = [];
  }
  addLog('组态编辑', '已撤销/重做编辑操作', 'normal');
};

const onHistoryKey = (e: KeyboardEvent) => {
  if (isActiveMode.value) return;
  const tag = (e.target as HTMLElement).tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
  if (!currentPage.value) return;
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') {
    e.preventDefault();
    if (e.shiftKey) {
      applyRestored(redo(currentPage.value.components));
    } else {
      applyRestored(undo(currentPage.value.components));
    }
  }
};

// 严格模式：运行时实时值解析（仅复合绑定 deviceId+variableKey；禁止裸 key 取值）
const warnedUnboundIds = new Set<string>();
const componentValues = computed(() => {
  const composite: Record<string, number | boolean> = {};
  devices.value.forEach((d) => {
    // 兼容字符串 'online'（模拟态）与数字 1（P0-1 修复后 mapRuntimeStatusToStatus 产出 0–4）
    if (d.status === 'online' || d.status === 1) {
      Object.keys(d.variables).forEach((key) => {
        composite[`${d.id}:${key}`] = d.variables[key];
      });
    }
  });

  const result: Record<string, number | boolean> = {};
  currentPageSafe.value.components.forEach((c) => {
    if (c.bindDeviceId != null && c.bindVariableKey) {
      const v = composite[`${c.bindDeviceId}:${c.bindVariableKey}`];
      if (v !== undefined) {
        result[c.id] = v;
        return;
      }
    }
    // 严格模式：未绑定设备/变量的组件禁止裸 key 取值，显示 0 并给出一次性警告
    if (!warnedUnboundIds.has(c.id)) {
      warnedUnboundIds.add(c.id);
      addLog('组态拓扑', `组件 [${c.id}] 未绑定设备/变量（bindDeviceId=${c.bindDeviceId}），禁止裸 key 取值，显示 0`, 'warning');
    }
    result[c.id] = 0;
  });
  return result;
});

// 趋势图真实数据源：把当前页 trend-chart 组件的实时值推入滚动缓冲
watch(componentValues, (vals) => {
  (currentPageSafe.value.components ?? []).forEach((c) => {
    if (c.type === 'trend-chart') pushTrendPoint(c.id, vals[c.id] ?? 0);
  });
}, { immediate: true });

// 页面切换时清理趋势缓冲，防止跨页残留
watch(() => currentPageSafe.value.id, () => {
  Object.keys(trendHistory).forEach(clearTrendHistory);
});

// 阶段5-3：画布分辨率（由页面属性驱动，回退默认 1100×700）
const pageWidth = computed(() => currentPageSafe.value.width ?? 1100);
const pageHeight = computed(() => currentPageSafe.value.height ?? 700);

// 设备级 SignalR 订阅：运行态实时值解析只消费当前页面已绑定组件的设备，
// 按绑定设备集合订阅/退订（变量更新仅推送已订阅设备分组，替代全连接广播）。
const boundDeviceIds = computed(() => {
  const ids = new Set<number>();
  currentPageSafe.value.components.forEach((c: any) => {
    if (c.bindDeviceId != null) ids.add(Number(c.bindDeviceId));
    // 圆角按钮「操作变量」独立绑定的设备也纳入订阅：取反模式需读取操作变量当前值
    if (c.type === 'rounded-btn' && c.props?.opDeviceId != null) ids.add(Number(c.props.opDeviceId));
    // multi-var-dashboard 多变量看板：每个子项可独立绑定设备，需纳入订阅，
    // 否则服务端不会推送这些设备的 ReceiveVariableUpdate（看板直接读 devices store 会陈旧）。
    // 注意：HMIWidget.dashboardResolvedItems 在「子项无 deviceId 且组件无 bindDeviceId」时
    // 会回退读 devices.value[0]（首台设备）。此分支同样需把首台设备纳入订阅，
    // 否则看板仍收不到推送而陈旧（首台设备通常未被其它控件订阅）。
    if (c.type === 'multi-var-dashboard' && Array.isArray(c.props?.dashboardItems)) {
      let hasUnboundItem = false;
      for (const item of c.props.dashboardItems) {
        const devId = item?.deviceId != null ? item.deviceId : c.bindDeviceId;
        if (devId != null) ids.add(Number(devId));
        else hasUnboundItem = true;
      }
      // 回退路径：订阅首台设备，使其 ReceiveVariableUpdate 能到达客户端。
      // 此处读取 devices.value 会建立依赖，设备加载/重排后 watch 自动重新对账订阅。
      if (hasUnboundItem && devices.value.length > 0) ids.add(Number(devices.value[0].id));
    }
  });
  return ids;
});

watch(boundDeviceIds, (newIds, oldIds) => {
  oldIds?.forEach(id => { if (!newIds.has(id)) unsubscribeDeviceTelemetry(id); });
  newIds.forEach(id => { if (!oldIds?.has(id)) subscribeDeviceTelemetry(id); });
}, { immediate: true });

onUnmounted(() => {
  boundDeviceIds.value.forEach(id => unsubscribeDeviceTelemetry(id));
});

// 阶段6-2：写控制角色权限。仅 Operator/Admin 可在运行模式下发写指令；
// 其它角色（如 Viewer）即便已通过 JWT 认证，前端也拦截写控件、后端以 [Authorize(Roles)] 兜底 403。
const canControlWrite = computed(() => {
  const r = loginUser.value?.role;
  return r === ROLE_OPERATOR || r === ROLE_ADMIN;
});
// 画布尺寸变更：将越界组件拉回画布内（右/下边缘贴齐）并逐个防抖落库
const clampComponentsToCanvas = (pg: ScadaPage) => {
  const w = pg.width ?? 1100;
  const h = pg.height ?? 700;
  const moved = new Map<string, { x: number; y: number }>();
  pg.components.forEach((c: HMIComponent) => {
    const nx = Math.max(0, Math.min(c.x, Math.max(0, w - c.width)));
    const ny = Math.max(0, Math.min(c.y, Math.max(0, h - c.height)));
    if (nx !== c.x || ny !== c.y) moved.set(c.id, { x: nx, y: ny });
  });
  if (!moved.size) return;
  const newComps = pg.components.map((comp) => {
    const u = moved.get(comp.id);
    return u ? { ...comp, x: u.x, y: u.y } : comp;
  });
  updateCurrentPageComponents(newComps);
  newComps.forEach((c) => {
    if (moved.has(c.id)) persistComponentUpdate(pg, c);
  });
  addLog('组态编辑', `画布尺寸变更，${moved.size} 个越界组件已拉回画面内`, 'normal');
};

const handleUpdateCanvasSize = (w: number, h: number) => {
  const pg = currentPage.value;
  if (!pg) return;
  pg.width = w;
  pg.height = h;
  addLog('组态编辑', `画布分辨率调整为 [${w} × ${h}]`, 'normal');
  clampComponentsToCanvas(pg);
  // 阶段2：落库页面尺寸（新建未落库页面仅前端生效）
  persistPageUpdate(pg).catch(() => { });
};

// Map CanvasPanel updates directly to active project page
const handleUpdateComponent = (id: string, updates: Partial<HMIComponent>) => {
  if (!currentPage.value) return;
  beginChange(currentPage.value.components, id);
  const currentComps = currentPage.value.components;
  const newComps = currentComps.map((comp) => {
    if (comp.id === id) {
      return { ...comp, ...updates };
    }
    return comp;
  });
  updateCurrentPageComponents(newComps);

  // 阶段2：本地乐观更新后，防抖落库（拖拽/缩放/属性编辑高频，统一走 600ms 防抖 PUT）
  const updated = newComps.find((c) => c.id === id);
  if (updated) {
    persistComponentUpdate(currentPage.value, updated);
  }
  endChange();
};

// 阶段5-2：批量属性更新（多选整体拖动/对齐/分布）；连续编辑段合并为单条历史
const handleUpdateComponents = (updates: { id: string; updates: Partial<HMIComponent> }[]) => {
  if (!updates.length || !currentPage.value) return;
  beginChange(currentPage.value.components, 'batch');
  const idSet = new Set(updates.map((u) => u.id));
  const newComps = currentPage.value.components.map((comp) => {
    const u = updates.find((x) => x.id === comp.id);
    return u ? { ...comp, ...u.updates } : comp;
  });
  updateCurrentPageComponents(newComps);
  updates.forEach((u) => {
    const updated = newComps.find((c) => c.id === u.id);
    if (updated) persistComponentUpdate(currentPage.value, updated);
  });
  endChange();
};

// 阶段5-2：选中集合变更（单选/多选/框选统一入口）；与背景属性编辑互斥
const handleSelectComponents = (ids: string[]) => {
  selectedIds.value = ids;
  isBackgroundSelected.value = false;
};

// 页面属性：点击画布空白背景 → 进入背景属性编辑（清空组件选中）
const handleSelectBackground = () => {
  selectedIds.value = [];
  isBackgroundSelected.value = true;
};

// 页面属性：背景/自适应配置变更 → 本地更新 + 落库
const handleUpdatePage = (updates: Partial<ScadaPage>) => {
  const pg = currentPage.value;
  if (!pg) return;
  Object.assign(pg, updates);
  addLog('组态编辑', `页面属性更新: [${pg.name}]`, 'normal');
  // 画布尺寸变更时同步拉回越界组件
  if (updates.width != null || updates.height != null) clampComponentsToCanvas(pg);
  // 落库页面配置（新建未落库页面仅前端生效）
  persistPageUpdate(pg).catch(() => { });
};

// PS 图层管理：更新当前页面的图层配置
const handleUpdateLayers = (layers: HMILayer[]) => {
  const pg = currentPage.value;
  if (!pg) return;
  pg.layers = layers;
  addLog('组态编辑', `更新图层架构: 共 ${layers.length} 个图层`, 'normal');
  persistPageUpdate(pg).catch(() => { });
};

const handleSetActiveLayer = (layerId: string) => {
  activeLayerId.value = layerId;
};

// Add a widget from panel library
// 阶段5-5：默认尺寸 / props 取自 widgetRegistry（消除与图库两处散改）
// extraProps：落布时覆盖默认 props（图片图元选图后传 imageUrl，确保首次落库即带图）
const handleAddWidget = (type: ComponentType, defaultW: number, defaultH: number, label: string, x = 40, y = 60, extraProps?: Record<string, any>) => {
  if (!currentPage.value) return;
  // 图片图元：未选图时先打开图库（拿到 URL 与原始宽高比后再落布），不直接添加空图元；
  // 已带 imageUrl（图库选图回调传入 extraProps）时放行落布——否则选图回调再入此处会
  // 重新弹开图库并 return，形成"点击照片无反应"的死循环。
  if (type === 'image' && !extraProps?.imageUrl) {
    showImageLibrary.value = true;
    return;
  }
  const currentComps = currentPage.value.components;
  const newId = genComponentId(type);
  const def = getWidgetDef(type);
  const w = defaultW || def?.defaultWidth || 100;
  const h = defaultH || def?.defaultHeight || 50;

  // 确定新添加组件的归属图层（优先使用当前激活图层或首个可用图层）
  const pageLayers = currentPage.value.layers || [];
  const assignedLayerId = (activeLayerId.value && pageLayers.some(l => l.id === activeLayerId.value))
    ? activeLayerId.value
    : (pageLayers[0]?.id ?? 'layer-default');

  const newComponent: HMIComponent = {
    id: newId,
    type,
    name: `${label} ${currentComps.filter((c) => c.type === type).length + 1}`,
    x,
    y,
    width: w,
    height: h,
    label: label,
    bindField: '',
    layerId: assignedLayerId,
    zIndex: currentComps.length + 1,
    props: { ...(def?.defaultProps() ?? {}), ...(extraProps ?? {}) },
  };

  recordDiscrete(currentComps);
  updateCurrentPageComponents([...currentComps, newComponent]);
  selectedIds.value = [newId];
  addLog('组态编辑', `在页面 [${currentPage.value.name}] 添加组件 [${label}]`, 'info');

  // 阶段2：落库并回填 serverId（确保所属页面已落库）
  persistNewComponent(currentPage.value, currentProject.value, newComponent).catch(() => { });
};

// 阶段5-4：组件库拖拽投放落点（由 CanvasPanel 反算坐标后调用，x/y 为画布内坐标）
const handleAddWidgetAt = (type: string, w: number, h: number, name: string, x: number, y: number, extraProps?: Record<string, any>) => {
  handleAddWidget(type as ComponentType, w, h, name, x, y, extraProps);
};

// ===== 图片图元：图库选图 → 按原图宽高比落布 =====
const showImageLibrary = ref<boolean>(false);

// 图库选图回调：复用 handleAddWidget 落布链路（含落库/回填 serverId），imageUrl 经 extraProps 随首次落库写入
const handleImageSelected = (img: { url: string; originalName: string; width: number; height: number }) => {
  const pg = currentPage.value;
  if (!pg) return;
  showImageLibrary.value = false;

  // 等比缩放：最长边 60~400，避免小图标不可选/大图铺满画布
  const ratio = img.width > 0 && img.height > 0 ? img.width / img.height : 4 / 3;
  let w: number, h: number;
  if (ratio >= 1) {
    w = Math.min(400, Math.max(60, img.width));
    h = Math.round(w / ratio);
  } else {
    h = Math.min(400, Math.max(60, img.height));
    w = Math.round(h * ratio);
  }
  // 落点取画布中心附近，避免每次都叠在默认 (40,60)
  const x = Math.max(0, Math.round(((pg.width ?? 1100) - w) / 2));
  const y = Math.max(0, Math.round(((pg.height ?? 700) - h) / 2));
  const label = img.originalName.replace(/\.[^.]+$/, '').trim() || '图片';

  handleAddWidget('image', w, h, label, x, y, { imageUrl: img.url, imageFit: 'fill' as const });
  addLog('组态编辑', `在页面 [${pg.name}] 添加图片图元 [${label}]`, 'info');
};

// Duplicate widget(s) — 阶段5-2 支持批量复制
const handleDuplicateComponents = (ids: string[]) => {
  if (!currentPage.value) return;
  const currentComps = currentPage.value.components;
  const targets = currentComps.filter((c) => ids.includes(c.id));
  if (!targets.length) return;

  const clones: HMIComponent[] = targets.map((t) => ({
    ...t,
    id: genComponentId(t.type),
    name: `${t.name} (副本)`,
    x: t.x + 20,
    y: t.y + 20,
    zIndex: currentComps.length + 1,
  }));

  recordDiscrete(currentComps);
  updateCurrentPageComponents([...currentComps, ...clones]);
  selectedIds.value = clones.map((c) => c.id);
  addLog('组态编辑', `复制组件: ${targets.length} 个`, 'info');

  // 阶段2：落库并回填 serverId
  clones.forEach((c) =>
    persistNewComponent(currentPage.value, currentProject.value, c).catch(() => { })
  );
};

// Delete widget(s) — 阶段5-2 支持批量删除（统一一次确认）
const handleDeleteComponents = (ids: string[]) => {
  if (!currentPage.value) return;
  const targets = currentPage.value.components.filter((c) => ids.includes(c.id));
  if (!targets.length) return;

  askConfirm('删除组件', `确定删除选中的 ${targets.length} 个组件吗？`, () => {
    recordDiscrete(currentPage.value.components);
    const idSet = new Set(ids);
    updateCurrentPageComponents(currentPage.value.components.filter((c) => !idSet.has(c.id)));
    selectedIds.value = [];
    addLog('组态编辑', `批量删除组件: ${targets.length} 个`, 'warning');

    // 阶段2：若已落库则删除后端记录
    targets.filter((t) => t.serverId).forEach((t) =>
      persistComponentDelete(t).catch(() => { })
    );
  });
};

// Clear active drawing canvas Layout
const handleClearCanvas = () => {
  if (!currentPage.value) return;
  askConfirm('清空画布', '确定要清空当前画布吗？此操作将移除本页全部组件且不可逆。', () => {
    recordDiscrete(currentPage.value.components);
    const toDelete = currentPage.value.components.filter(c => c.serverId);
    updateCurrentPageComponents([]);
    selectedIds.value = [];
    addLog('组态编辑', `清空画布: [${currentPage.value.name}]`, 'warning');

    // 阶段2：批量删除已落库的组件
    Promise.all(toDelete.map(c => persistComponentDelete(c))).catch(() => { });
  });
};

// Toggle or forces live registries value on active devices
const handleTriggerToggleValue = (deviceId: number | null, variableKey: string, legacyKey: string, actionType?: string, val?: any) => {
  const key = variableKey || legacyKey;
  if (!key) return;

  // 严格模式：控件未绑定设备 → 禁止裸 key 写指令
  if (deviceId == null) {
    showToast('该控件未绑定设备，禁止写入（请到编辑器补全绑定）', 'warning');
    addLog('SCADA 写控', `写指令被拒绝：组件未绑定设备 (key=${key})`, 'warning');
    return;
  }

  // 只读拦截：设备级有效只读权限优先于写操作（后端 RuntimeManager 仍会兜底校验 IsReadOnly）。
  const dev = devices.value.find((d) => String(d.id) === String(deviceId));
  const meta = dev?.variableMeta?.[key];
  const isReadOnly = meta?.effectiveIsReadOnly ?? false;
  if (isReadOnly) {
    showToast(`变量 [${key}] 为只读，禁止写入`, 'warning');
    addLog('SCADA 写控', `写拦截：变量 [设备${deviceId}.${key}] 为只读`, 'warning');
    return;
  }

  const current = getDeviceVariableValue(deviceId, key);

  let targetVal: any;
  if (actionType === 'setValue' && val !== undefined) {
    targetVal = val;
  } else if (actionType === 'setBit') {
    targetVal = typeof current === 'boolean' ? true : 1;
  } else if (actionType === 'resetBit') {
    targetVal = typeof current === 'boolean' ? false : 0;
  } else if (actionType === 'momentary' && val !== undefined) {
    if (typeof current === 'boolean') {
      targetVal = val;
    } else {
      targetVal = val ? 1 : 0;
    }
  } else {
    // Toggle (取反)
    if (typeof current === 'boolean') {
      targetVal = !current;
    } else if (typeof current === 'number') {
      targetVal = current === 0 ? 1 : 0;
    } else {
      targetVal = !current;
    }
  }
  setDeviceVariableValue(deviceId, key, targetVal);
};

// ===== var-display 设值弹窗 =====
// 点击可设定的数值显示组件 → 记录目标组件弹数字键盘；确认后走既有 setValue 写管道（含只读/权限拦截）。
// 写入冷却 1.2s：防触摸屏抖动/连点重复下发。
const setValueTarget = ref<HMIComponent | null>(null);
const setValueCurrentValue = computed<number | boolean | undefined>(() => {
  const c = setValueTarget.value;
  if (!c || c.bindDeviceId == null || !c.bindVariableKey) return undefined;
  return getDeviceVariableValue(c.bindDeviceId, c.bindVariableKey);
});
let lastSetValueAt = 0;
const SET_VALUE_COOLDOWN_MS = 1200;

const handleRequestSetValue = (component: HMIComponent) => {
  if (component.bindDeviceId == null || !(component.bindVariableKey || component.bindField)) {
    showToast('该组件未绑定设备/变量，无法设定', 'warning');
    return;
  }
  setValueTarget.value = component;
};

const handleSetValueConfirm = (value: number | boolean) => {
  const c = setValueTarget.value;
  if (!c) return;
  const now = Date.now();
  if (now - lastSetValueAt < SET_VALUE_COOLDOWN_MS) {
    showToast('写入冷却中，请稍候', 'warning');
    return;
  }
  lastSetValueAt = now;
  const varKey = c.bindVariableKey || c.bindField || '';
  setValueTarget.value = null;
  addLog('SCADA 写控', `设值弹窗写入: 设备${c.bindDeviceId}.${varKey} → ${typeof value === 'boolean' ? (value ? '开' : '关') : value}`, 'normal');
  handleTriggerToggleValue(c.bindDeviceId ?? null, varKey, c.bindField, 'setValue', value);
};

// 圆角按钮 run-script 模式：点击触发服务端系统脚本（/api/ScriptRuntime，Operator/Admin）
const handleTriggerRunScript = async (scriptId: number) => {
  if (!scriptId) return;
  try {
    await triggerRuntimeScript(scriptId);
    showToast(`脚本 #${scriptId} 已触发执行`, 'success');
    addLog('组态拓扑', `按钮触发脚本 #${scriptId} 执行`, 'normal');
  } catch {
    // 失败提示由 http 拦截器统一弹出（403/404/脚本熔断等）
  }
};

// ===== 事件系统（编辑器预览 isActiveMode）：分发上下文与处理器 =====
// setProp 动作仅改本地渲染数据（不落库），退出预览后仍保留原设计值由撤销/刷新兜底
const previewEventCtx = computed<HmiEventDispatchContext>(() => ({
  canControlWrite: canControlWrite.value,
  writeVariable: (deviceId, variableKey, writeMode, value) => {
    handleTriggerToggleValue(deviceId, variableKey, '', writeMode, value);
  },
  navigateToPage: handleNavigate,
  runScript: (scriptId) => { handleTriggerRunScript(scriptId); },
  applyRuntimePatch: (componentId, patch) => {
    const target = currentPage.value?.components.find((c) => c.id === componentId);
    if (!target) return;
    if (patch.visible !== undefined) target.visible = patch.visible;
    if (patch.label !== undefined) target.label = patch.label;
    if (patch.props) target.props = { ...target.props, ...patch.props };
  },
  onBlocked: (msg) => showToast(msg, 'warning'),
}));

// 事件系统：预览模式交互类事件（click/press/release）由 CanvasPanel 上抛分发
const handleComponentEvent = (component: HMIComponent, eventType: string) => {
  if (!isActiveMode.value) return;
  dispatchComponentEvent(component, eventType as HmiEventType, previewEventCtx.value);
};

// 事件系统：预览模式数据类事件（valueChange/alarm），编辑态禁用
useHmiDataEvents({
  components: () => currentPage.value?.components ?? [],
  componentValues,
  ctx: () => previewEventCtx.value,
  enabled: () => isActiveMode.value,
});

// Create new SCADA project screen
const handleCreateProject = () => {
  if (!newProjectName.value.trim()) return;

  const newProjId = `project-${Date.now()}`;
  const newProj: ScadaScreenProject = {
    id: newProjId,
    serverId: undefined,
    name: newProjectName.value,
    description: newProjectDesc.value || '新建SCADA工程',
    pages: [
      {
        id: `page-${Date.now()}-primary`,
        serverId: undefined,
        name: '未命名页面 1',
        components: []
      }
    ]
  };
  scadaProjects.value.push(newProj);

  addLog('组态编辑', `创建新工程: [${newProjectName.value}]`, 'normal');
  selectedProjectId.value = newProjId;
  selectedPageId.value = newProj.pages[0].id;

  newProjectName.value = '';
  newProjectDesc.value = '';
  showProjectModal.value = false;

  // 阶段2：落库工程 + 初始页面并回填 serverId（避免「空工程的首个画面」刷新即丢失，与新增画面行为一致）
  ensurePageSaved(newProj.pages[0], newProj).catch(() => { });
};

// Add child page to active screen project.
// platform：新增画面归属端（桌面端/移动端），缺省 Desktop；不同端使用各自默认画布尺寸。
const PAGE_SIZES: Record<'Desktop' | 'Mobile', { w: number; h: number }> = {
  Desktop: { w: 1100, h: 700 },
  Mobile: { w: 375, h: 812 }
};
const handleAddPage = (platform: 'Desktop' | 'Mobile' = 'Desktop') => {
  const proj = currentProject.value;
  if (!proj) return;

  const list = platform === 'Mobile' ? mobilePages.value : desktopPages.value;
  const size = PAGE_SIZES[platform];
  const newPageId = `page-${Date.now()}`;
  const newPage: ScadaPage = {
    id: newPageId,
    serverId: undefined,
    name: `${platform === 'Mobile' ? '移动端画面' : '桌面端画面'} ${list.length + 1}`,
    platform,
    width: size.w,
    height: size.h,
    components: []
  };

  proj.pages.push(newPage);
  selectedPageId.value = newPageId;
  currentPlatform.value = platform;
  addLog('组态编辑', `项目 [${proj.name}] 新增${platform === 'Mobile' ? '移动端' : '桌面端'}画面: [${newPage.name}]`, 'normal');

  // 阶段2：确保工程已落库后落库页面并回填 serverId
  ensurePageSaved(newPage, proj).catch(() => { });
};

// 视口切换：切换到指定端，并选中该端首个画面（保持编辑上下文一致）。
const switchPlatform = (platform: 'Desktop' | 'Mobile') => {
  currentPlatform.value = platform;
  const list = platform === 'Mobile' ? mobilePages.value : desktopPages.value;
  if (list.length > 0) selectedPageId.value = list[0].id;
};

// 阶段3：运行模式（预览）下点击「导航」按钮 → 切换到目标画面（目标必为同端，编辑器不跨端）。
const handleNavigate = (pageId: string) => {
  // 归一化兜底比较（与播放器一致）：兼容 srv-{serverId} 与本地 id 双轨
  const target = currentProject.value?.pages.find(p => isSamePageRef(pageId, p.id));
  if (!target) return;
  selectedPageId.value = target.id;
  currentPlatform.value = (target.platform ?? 'Desktop') as 'Desktop' | 'Mobile';
};

// 设置/取消某画面为「所在端首页」：同端仅保留一个首页（由后端 AppService 兜底唯一性）。
const setHomePage = (page: ScadaPage) => {
  const proj = currentProject.value;
  if (!proj) return;
  const platform = (page.platform ?? 'Desktop') as 'Desktop' | 'Mobile';
  proj.pages.forEach(pg => {
    if ((pg.platform ?? 'Desktop') === platform) pg.isHome = false;
  });
  page.isHome = true;
  addLog('组态编辑', `设置首页: [${page.name}] (${platform === 'Mobile' ? '移动端' : '桌面端'})`, 'normal');
  proj.pages
    .filter(pg => (pg.platform ?? 'Desktop') === platform)
    .forEach(pg => persistPageUpdate(pg).catch(() => { }));
};

// Copy / Duplicate child page
const handleDuplicatePage = (page: ScadaPage) => {
  const proj = currentProject.value;
  if (!proj) return;

  const newPageId = `page-${Date.now()}`;
  const newPage: ScadaPage = {
    id: newPageId,
    serverId: undefined,
    name: `${page.name} - 副本`,
    platform: page.platform ?? 'Desktop',
    width: page.width,
    height: page.height,
    // 深拷贝后重新生成组件 uid 并清空 serverId：
    // 若保留源组件 id，两页同 id 组件会在切换页面时「幻影选中」、防抖落库互相顶掉。
    components: (JSON.parse(JSON.stringify(page.components)) as HMIComponent[]).map((c) => ({
      ...c,
      id: genComponentId(c.type),
      serverId: undefined,
    }))
  };
  proj.pages.push(newPage);
  selectedPageId.value = newPageId;
  addLog('组态编辑', `复制页面: [${page.name}]`, 'normal');

  // 阶段2：落库页面 + 其下全部组件并回填 serverId
  persistDuplicatePage(newPage, proj).catch(() => { });
};

// Delete page
const handleDeletePage = (pId: string, pName: string) => {
  const proj = currentProject.value;
  if (!proj) return;

  if (proj.pages.length <= 1) {
    showToast('项目至少需要保留一个页面。', 'warning');
    return;
  }

  askConfirm('删除页面', `确定删除页面 [${pName}] 吗？其下所有组件将一并移除。`, () => {
    const target = proj.pages.find(pg => pg.id === pId);
    proj.pages = proj.pages.filter(pg => pg.id !== pId);
    if (selectedPageId.value === pId) {
      selectedPageId.value = proj.pages[0].id;
    }
    addLog('组态编辑', `删除页面: [${pName}]`, 'warning');

    // 阶段2：若已落库则级联删除后端页面（后端事务删组件）
    if (target?.serverId) {
      persistPageDelete(target).catch(() => { });
    }
  });
};

// Inline rename actions
const startRenamePage = (pId: string, currentText: string) => {
  isRenamingPageId.value = pId;
  renamePageInput.value = currentText;
};

const savePageRename = (pId: string) => {
  const proj = currentProject.value;
  if (!proj) return;

  const pg = proj.pages.find(p => p.id === pId);
  if (pg && renamePageInput.value.trim()) {
    const oldName = pg.name;
    pg.name = renamePageInput.value.trim();
    addLog('组态编辑', `页面更名: [${oldName}] -> [${pg.name}]`, 'normal');
    // 阶段2：落库
    persistPageUpdate(pg).catch(() => { });
  }
  isRenamingPageId.value = null;
};

const startRenameProj = (pId: string, currentText: string) => {
  isRenamingProjId.value = pId;
  renameProjInput.value = currentText;
};

const saveProjRename = (pId: string) => {
  const proj = scadaProjects.value.find(p => p.id === pId);
  if (proj && renameProjInput.value.trim()) {
    const oldName = proj.name;
    proj.name = renameProjInput.value.trim();
    addLog('组态编辑', `工程更名: [${oldName}] -> [${proj.name}]`, 'normal');
    // 阶段2：落库
    persistProjectUpdate(proj).catch(() => { });
  }
  isRenamingProjId.value = null;
};

// Delete project（确认后删除；后端事务级联删除其下全部画面与组件）
const handleDeleteProject = () => {
  const proj = currentProject.value;
  if (!proj) return;

  askConfirm('删除工程', `确定删除工程 [${proj.name}] 吗？其下所有画面与组件将一并删除，且不可恢复。`, () => {
    // 阶段2：若已落库则删除后端工程（事务级联删页面与组件）
    persistProjectDelete(proj).catch(() => { });

    // 本地移除工程并同步清理工程摘要列表（卡片页数据源）
    const idx = scadaProjects.value.findIndex(p => p.id === proj.id);
    if (idx !== -1) scadaProjects.value.splice(idx, 1);
    if (proj.serverId) {
      const sIdx = projectSummaries.value.findIndex(s => s.id === proj.serverId);
      if (sIdx !== -1) projectSummaries.value.splice(sIdx, 1);
    }

    // 切换选中到剩余首个工程；无工程则清空选中（空态 UI 引导新建）
    const next = scadaProjects.value[0];
    selectedProjectId.value = next?.id ?? '';
    selectedPageId.value = next?.pages[0]?.id ?? '';

    addLog('组态编辑', `删除工程: [${proj.name}]`, 'warning');
  }, true, '删除');
};

const selectProjectDirectly = (projId: string) => {
  selectedProjectId.value = projId;
  const proj = scadaProjects.value.find(p => p.id === projId);
  if (proj && proj.pages.length > 0) {
    selectedPageId.value = proj.pages[0].id;
  }
};

const selectedCompObj = computed(() => {
  return currentPageSafe.value.components.find((c) => c.id === selectedId.value) || null;
});

// ===== 组态导入导出（工程/画面迁移文件） =====
// 导入写后端为 RequireAdmin；前端按角色隐藏入口，后端仍兜底 403。
const isAdminUser = computed(() => loginUser.value?.role === ROLE_ADMIN);

const projectImportInput = ref<HTMLInputElement | null>(null);
const triggerImportProject = () => projectImportInput.value?.click();

const handleImportProjectFile = async (e: Event) => {
  const input = e.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = ''; // 允许重复选择同一文件
  if (!file) return;
  try {
    const pkg = await parseTransferFile(file);
    const result = await importProject(pkg);
    showToast(`已导入工程「${result.projectName}」（画面 ${result.importedPages}、组件 ${result.importedComponents}）`, 'success');
    if (result.warnings.length) showToast(result.warnings.join('\n'), 'warning');
    upsertProjectSummary({ id: result.projectId, name: result.projectName, description: pkg.project?.description || '' });
    await reloadProjectTree(result.projectId);
    selectedPageId.value = currentProject.value?.pages[0]?.id || '';
    addLog('组态编辑', `导入工程: [${result.projectName}]`, 'normal');
  } catch (err: any) {
    // HTTP 错误已由 http 拦截器统一 toast；此处只兜解析/本地错误
    if (!err?.response) showToast(err?.message || '导入失败', 'error');
  }
};

const handleExportProject = async () => {
  const proj = currentProject.value;
  if (!proj?.serverId) return;
  try {
    await exportProjectFile(proj.serverId, proj.name);
    addLog('组态编辑', `导出工程: [${proj.name}]`, 'normal');
  } catch { }
};

const pageImportInput = ref<HTMLInputElement | null>(null);
const triggerImportPage = () => {
  if (!currentProject.value?.serverId) {
    showToast('当前工程尚未保存到后端，无法导入画面', 'warning');
    return;
  }
  pageImportInput.value?.click();
};

const handleImportPageFile = async (e: Event) => {
  const input = e.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = '';
  if (!file) return;
  const proj = currentProject.value;
  if (!proj?.serverId) return;
  try {
    const pkg = await parseTransferFile(file);
    if (pkg.format !== 'scada-page') {
      showToast('请选择画面导出文件（format 应为 scada-page）', 'error');
      return;
    }
    const result = await importPage(proj.serverId, pkg);
    showToast(`已导入画面「${result.pageName}」（组件 ${result.importedComponents}）`, 'success');
    if (result.warnings.length) showToast(result.warnings.join('\n'), 'warning');
    await reloadProjectTree(proj.serverId);
    if (result.pageId) selectedPageId.value = `srv-${result.pageId}`;
    addLog('组态编辑', `导入画面: [${result.pageName}]`, 'normal');
  } catch (err: any) {
    if (!err?.response) showToast(err?.message || '导入失败', 'error');
  }
};

const handleExportPage = async (page: ScadaPage) => {
  if (!page.serverId) {
    showToast('该画面尚未保存到后端，无法导出', 'warning');
    return;
  }
  try {
    await exportPageFile(page.serverId, page.name);
    addLog('组态编辑', `导出画面: [${page.name}]`, 'normal');
  } catch { }
};
</script>

<template>
  <div
    class="h-full overflow-y-auto md:overflow-y-hidden flex flex-col md:flex-row text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">

    <!-- LEFT CONTROL BAR: Scada Projects and multiple subpages directory (全屏模式下隐藏) -->
    <div v-show="!isScadaFullscreen && isProjectListOpen"
      class="w-full md:w-64 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex flex-col shrink-0 flex-1 md:flex-none transition-colors">

      <!-- Top Screen/Project select -->
      <div class="p-4 border-b border-slate-100 dark:border-slate-800 space-y-3">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-1.5 font-bold text-sm text-slate-900 dark:text-white">
            <FolderIcon class="w-4 h-4 text-amber-500" />
            <span>工程列表</span>
          </div>
          <div class="flex items-center gap-1">
            <button v-if="isAdminUser" @click="triggerImportProject"
              class="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 cursor-pointer"
              title="导入工程（.scada-project.json）">
              <Upload class="w-4 h-4" />
            </button>
            <button v-if="currentProject?.serverId" @click="handleExportProject"
              class="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 cursor-pointer"
              title="导出当前工程">
              <Download class="w-4 h-4" />
            </button>
            <button @click="showProjectModal = true"
              class="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 cursor-pointer"
              title="新建工程">
              <Plus class="w-4 h-4" />
            </button>
            <button @click="isProjectListOpen = false"
              class="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 cursor-pointer hidden md:block"
              title="收起工程列表">
              <ChevronLeft class="w-4 h-4" />
            </button>
          </div>
        </div>

        <select :value="selectedProjectId" @change="selectProjectDirectly(($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-[#1890ff] dark:text-sky-400 font-bold rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-xs focus:outline-none">
          <option v-for="p in scadaProjects" :key="p.id" :value="p.id">
            {{ p.name }}
          </option>
        </select>
      </div>

      <!-- Active Project Header description -->
      <div v-if="currentProject"
        class="px-4 py-2 bg-slate-50/50 dark:bg-slate-950/40 border-b border-slate-100 dark:border-slate-800 text-left">
        <!-- Project direct quick edit rename -->
        <div class="flex items-center justify-between">
          <span class="text-[9px] uppercase font-bold tracking-wider text-slate-400 dark:text-slate-500">工程属性</span>
          <div class="flex items-center gap-2">
            <button v-if="isRenamingProjId !== currentProject.id"
              @click="startRenameProj(currentProject.id, currentProject.name)"
              class="text-[10px] text-slate-400 dark:text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 cursor-pointer">
              重命名
            </button>
            <button v-if="isRenamingProjId !== currentProject.id" @click="handleDeleteProject"
              class="text-slate-400 dark:text-slate-400 hover:text-rose-500 dark:hover:text-rose-400 cursor-pointer"
              title="删除工程">
              <Trash2 class="w-3 h-3" />
            </button>
          </div>
        </div>

        <div v-if="isRenamingProjId === currentProject.id" class="flex gap-1 items-center mt-1">
          <input type="text" v-model="renameProjInput" @keydown.enter="saveProjRename(currentProject.id)"
            class="bg-white dark:bg-slate-900 border dark:border-slate-700 rounded px-1.5 py-0.5 text-[11px] font-sans text-slate-800 dark:text-slate-100 focus:outline-none w-full" />
          <button @click="saveProjRename(currentProject.id)"
            class="p-0.5 bg-emerald-500 text-white rounded cursor-pointer">
            <Check class="w-3 h-3" />
          </button>
        </div>
        <p v-else class="text-[10px] text-slate-500 dark:text-slate-400 mt-1 leading-relaxed">
          {{ currentProject.description }}
        </p>
      </div>

      <!-- Subpages Directory explorer：按归属端分「桌面端 / 移动端」两组 -->
      <div
        class="flex items-center justify-between px-4 py-3 font-bold text-xs uppercase tracking-wider text-slate-400 dark:text-slate-500 border-b border-slate-100/60 dark:border-slate-800">
        <span>画面列表</span>
        <div class="flex items-center gap-1.5">
          <button v-if="isAdminUser" @click="triggerImportPage"
            class="flex items-center gap-1 normal-case font-semibold text-[11px] px-2 py-1 rounded border transition-colors bg-white dark:bg-slate-800 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:border-[#1890ff] dark:hover:border-sky-500"
            title="导入画面到当前工程（.scada-page.json）">
            <Upload class="w-3.5 h-3.5" />
            导入画面
          </button>
          <button @click="showBindingCheck = !showBindingCheck"
            class="flex items-center gap-1 normal-case font-semibold text-[11px] px-2 py-1 rounded border transition-colors"
            :class="showBindingCheck
              ? 'bg-amber-500 text-white border-amber-500'
              : 'bg-white dark:bg-slate-800 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:border-amber-400'"
            title="检查当前画面中未绑定设备的组件（裸 Key 风险）">
            <AlertTriangle class="w-3.5 h-3.5" />
            绑定检查
          </button>
        </div>
      </div>

      <!-- 桌面端分组 -->
      <div
        class="flex items-center justify-between px-4 py-1.5 bg-slate-50/60 dark:bg-slate-800/40 border-b border-slate-100/60 dark:border-slate-800">
        <span class="text-[11px] font-bold text-slate-500 dark:text-slate-400">🖥 桌面端 ({{ desktopPages.length }})</span>
        <button @click="handleAddPage('Desktop')"
          class="p-0.5 rounded hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-500 dark:text-slate-400 cursor-pointer"
          title="新增桌面端画面">
          <Plus class="w-3.5 h-3.5" />
        </button>
      </div>
      <div v-if="currentProject"
        class="overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800 max-h-[140px] md:max-h-none text-left font-sans">
        <div v-for="page in desktopPages" :key="page.id" @click="selectedPageId = page.id"
          class="p-3 cursor-pointer hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-all space-y-1 relative"
          :class="selectedPageId === page.id ? 'bg-sky-50/50 dark:bg-sky-950/40 text-[#1890ff] dark:text-sky-400 border-r-4 border-r-[#1890ff] dark:border-r-sky-500' : 'text-slate-700 dark:text-slate-300'">
          <div class="flex items-center justify-between gap-2 overflow-hidden">
            <div v-if="isRenamingPageId === page.id" class="flex items-center gap-1 w-full" @click.stopPropagation>
              <input v-model="renamePageInput" type="text"
                class="w-full bg-white dark:bg-slate-900 border border-slate-300 dark:border-slate-700 rounded px-1 py-0.5 text-xs text-slate-800 dark:text-slate-100 outline-none"
                @keyup.enter="savePageRename(page.id)" />
              <button @click="savePageRename(page.id)"
                class="text-emerald-600 dark:text-emerald-400 hover:text-emerald-700">
                <Check class="w-4 h-4" />
              </button>
            </div>
            <span v-else class="font-bold text-xs flex-1 leading-relaxed flex items-center gap-1 min-w-0">
              <span v-if="page.isHome"
                class="shrink-0 text-[8px] bg-amber-500 text-white px-1 py-0.5 rounded leading-none">首页</span>
              <span class="truncate">{{ page.name }}</span>
            </span>
            <div v-if="isRenamingPageId !== page.id"
              class="flex items-center gap-1.5 shrink-0 opacity-0 hover:opacity-100 focus-within:opacity-100 transition-all">
              <button @click.stop="setHomePage(page)" class="text-xs text-slate-400 hover:text-amber-500"
                :title="page.isHome ? '当前已是该端首页' : '设为该端首页'">
                <Home class="w-3 h-3" :class="page.isHome ? 'text-amber-500' : ''" />
              </button>
              <button @click.stop="startRenamePage(page.id, page.name)"
                class="text-xs text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="重命名">
                <Edit class="w-3 h-3" />
              </button>
              <button @click.stop="handleDuplicatePage(page)"
                class="text-xs text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="复制页面">
                <Copy class="w-3 h-3" />
              </button>
              <button @click.stop="handleExportPage(page)"
                class="text-xs text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="导出画面">
                <Download class="w-3 h-3" />
              </button>
              <button @click.stop="handleDeletePage(page.id, page.name)"
                class="text-xs text-rose-400 hover:text-rose-600 dark:hover:text-rose-300" title="删除页面">
                <Trash2 class="w-3 h-3" />
              </button>
            </div>
          </div>
          <p class="text-[9px] font-mono text-slate-400 dark:text-slate-500">组件数: {{ page.components.length }}</p>
        </div>
      </div>

      <!-- 移动端分组 -->
      <div
        class="flex items-center justify-between px-4 py-1.5 bg-slate-50/60 dark:bg-slate-800/40 border-y border-slate-100/60 dark:border-slate-800 mt-1">
        <span class="text-[11px] font-bold text-slate-500 dark:text-slate-400">📱 移动端 ({{ mobilePages.length }})</span>
        <button @click="handleAddPage('Mobile')"
          class="p-0.5 rounded hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-500 dark:text-slate-400 cursor-pointer"
          title="新增移动端画面">
          <Plus class="w-3.5 h-3.5" />
        </button>
      </div>
      <div v-if="currentProject"
        class="overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800 max-h-[140px] md:max-h-none text-left font-sans">
        <div v-for="page in mobilePages" :key="page.id" @click="selectedPageId = page.id"
          class="p-3 cursor-pointer hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-all space-y-1 relative"
          :class="selectedPageId === page.id ? 'bg-sky-50/50 dark:bg-sky-950/40 text-[#1890ff] dark:text-sky-400 border-r-4 border-r-[#1890ff] dark:border-r-sky-500' : 'text-slate-700 dark:text-slate-300'">
          <div class="flex items-center justify-between gap-2 overflow-hidden">
            <div v-if="isRenamingPageId === page.id" class="flex items-center gap-1 w-full" @click.stopPropagation>
              <input v-model="renamePageInput" type="text"
                class="w-full bg-white dark:bg-slate-900 border border-slate-300 dark:border-slate-700 rounded px-1 py-0.5 text-xs text-slate-800 dark:text-slate-100 outline-none"
                @keyup.enter="savePageRename(page.id)" />
              <button @click="savePageRename(page.id)"
                class="text-emerald-600 dark:text-emerald-400 hover:text-emerald-700">
                <Check class="w-4 h-4" />
              </button>
            </div>
            <span v-else class="font-bold text-xs flex-1 leading-relaxed flex items-center gap-1 min-w-0">
              <span v-if="page.isHome"
                class="shrink-0 text-[8px] bg-amber-500 text-white px-1 py-0.5 rounded leading-none">首页</span>
              <span class="truncate">{{ page.name }}</span>
            </span>
            <div v-if="isRenamingPageId !== page.id"
              class="flex items-center gap-1.5 shrink-0 opacity-0 hover:opacity-100 focus-within:opacity-100 transition-all">
              <button @click.stop="setHomePage(page)" class="text-xs text-slate-400 hover:text-amber-500"
                :title="page.isHome ? '当前已是该端首页' : '设为该端首页'">
                <Home class="w-3 h-3" :class="page.isHome ? 'text-amber-500' : ''" />
              </button>
              <button @click.stop="startRenamePage(page.id, page.name)"
                class="text-xs text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="重命名">
                <Edit class="w-3 h-3" />
              </button>
              <button @click.stop="handleDuplicatePage(page)"
                class="text-xs text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="复制页面">
                <Copy class="w-3 h-3" />
              </button>
              <button @click.stop="handleExportPage(page)"
                class="text-xs text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="导出画面">
                <Download class="w-3 h-3" />
              </button>
              <button @click.stop="handleDeletePage(page.id, page.name)"
                class="text-xs text-rose-400 hover:text-rose-600 dark:hover:text-rose-300" title="删除页面">
                <Trash2 class="w-3 h-3" />
              </button>
            </div>
          </div>
          <p class="text-[9px] font-mono text-slate-400 dark:text-slate-500">组件数: {{ page.components.length }}</p>
        </div>
      </div>

    </div>

    <!-- 工程列表收起态把手（md+：左侧竖条；移动端：顶部横条，点击展开） -->
    <div v-show="!isScadaFullscreen && !isProjectListOpen" @click="isProjectListOpen = true"
      class="bg-white dark:bg-slate-900 border-b md:border-b-0 md:border-r border-slate-200 dark:border-slate-800 cursor-pointer hover:bg-slate-100 dark:hover:bg-slate-800/80 transition-colors text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 shrink-0 select-none group shadow-xs">
      <!-- 移动端：横向把手 -->
      <div class="md:hidden flex items-center justify-center gap-1.5 py-2">
        <ChevronRight class="w-4 h-4 rotate-90" />
        <span class="text-[11px] font-bold tracking-widest">工程列表</span>
      </div>
      <!-- 桌面端：竖向把手 -->
      <div class="hidden md:flex flex-col items-center justify-center w-7 h-full min-h-[120px] py-4 gap-2.5">
        <ChevronRight class="w-4 h-4 transition-transform group-hover:translate-x-0.5" />
        <span
          class="text-[11px] font-bold [writing-mode:vertical-rl] tracking-widest text-slate-500 dark:text-slate-400 group-hover:text-[#1890ff] dark:group-hover:text-sky-400">工程列表</span>
      </div>
    </div>

    <!-- 加载态：整树拉取中 -->
    <div v-if="scadaLoading" class="flex-1 flex items-center justify-center bg-slate-50 dark:bg-transparent">
      <div class="text-center text-slate-400 dark:text-slate-500">
        <Loader2 class="w-8 h-8 mx-auto mb-2 animate-spin" />
        <p class="text-xs">正在加载组态工程…</p>
      </div>
    </div>

    <!-- 空态：无工程，引导新建（D1=方案B 消除模板后不再内置示例） -->
    <div v-else-if="!currentPage" class="flex-1 flex items-center justify-center bg-slate-50 dark:bg-transparent">
      <div class="text-center text-slate-500 dark:text-slate-400">
        <LayoutGrid class="w-12 h-12 mx-auto mb-3 opacity-40" />
        <p class="text-sm font-bold">暂无组态工程</p>
        <p class="text-[11px] mt-1">点击下方按钮创建第一个工程，再拖入组件开始设计。</p>
        <button @click="showProjectModal = true"
          class="mt-4 inline-flex items-center gap-1.5 px-4 py-1.5 rounded-lg text-xs font-bold bg-[#1890ff] text-white hover:bg-[#40a9ff] cursor-pointer">
          <Plus class="w-3.5 h-3.5" /> 新建工程
        </button>
      </div>
    </div>

    <!-- MAIN CO-WORKING VISUAL BUILDER WORKSPACE -->
    <div v-else class="flex-1 flex flex-col md:flex-row min-w-0">

      <!-- Center section: WidgetLibrary shelf (left nested) + Dragging HMI canvas (middle) -->
      <div class="flex-1 flex flex-col min-w-0 relative">

        <!-- Canvas bar toggler stats -->
        <div
          class="bg-white dark:bg-slate-900 px-4 sm:px-5 py-2.5 sm:py-3 border-b border-slate-200 dark:border-slate-800 shadow-sm flex items-center justify-between shrink-0 transition-colors">
          <div class="flex items-center gap-2 text-left">
            <span class="w-2 h-2 rounded-full bg-amber-500 shadow-[0_0_6px_#f59e0b]" />
            <div>
              <div class="flex items-center gap-1.5 flex-wrap">
                <h3 class="font-bold text-xs text-slate-800 dark:text-slate-100 flex items-center gap-1">
                  <span>当前页面:</span>
                  <b class="text-slate-900 dark:text-white font-sans font-bold text-[13px]">{{ currentPage.name }}</b>
                </h3>
                <!-- 全屏模式下的快速切页选择器 -->
                <select v-if="isScadaFullscreen" :value="selectedPageId"
                  @change="selectedPageId = ($event.target as HTMLSelectElement).value"
                  class="bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-200 text-[11px] font-bold rounded px-2 py-0.5 border border-slate-300 dark:border-slate-700 outline-none ml-1 cursor-pointer"
                  title="全屏模式下快速切换页面">
                  <optgroup label="桌面端画面" v-if="desktopPages.length">
                    <option v-for="p in desktopPages" :key="p.id" :value="p.id">🖥 {{ p.name }}</option>
                  </optgroup>
                  <optgroup label="移动端画面" v-if="mobilePages.length">
                    <option v-for="p in mobilePages" :key="p.id" :value="p.id">📱 {{ p.name }}</option>
                  </optgroup>
                </select>
              </div>
              <p class="text-[10px] text-slate-400 dark:text-slate-500 font-mono">
                {{ isScadaFullscreen ? '全屏模式 (按 Esc 或点击按钮退出)' : '画布布局: 响应式画布' }}
              </p>
            </div>
          </div>

          <div class="flex items-center gap-2">
            <!-- 视口切换：桌面端 / 移动端 -->
            <div
              class="hidden md:flex items-center rounded-full border border-slate-200 dark:border-slate-700 overflow-hidden text-[11px] font-bold">
              <button @click="switchPlatform('Desktop')" class="px-2.5 py-1 cursor-pointer transition-colors"
                :class="currentPlatform === 'Desktop' ? 'bg-[#1890ff] text-white' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800'"
                title="桌面端视口">🖥 桌面</button>
              <button @click="switchPlatform('Mobile')" class="px-2.5 py-1 cursor-pointer transition-colors"
                :class="currentPlatform === 'Mobile' ? 'bg-[#1890ff] text-white' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800'"
                title="移动端视口">📱 移动</button>
            </div>

            <!-- 撤销/重做 -->
            <div class="hidden md:flex items-center gap-1">
              <button @click="applyRestored(undo(currentPage.components))" :disabled="!undoAvailable"
                title="撤销 (Ctrl+Z)"
                class="p-1.5 rounded border transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
                :class="undoAvailable ? 'border-[#d9d9d9] text-gray-500 hover:text-[#1890ff]' : 'border-transparent text-gray-300'">
                <Undo2 class="w-3.5 h-3.5" />
              </button>
              <button @click="applyRestored(redo(currentPage.components))" :disabled="!redoAvailable"
                title="重做 (Ctrl+Shift+Z)"
                class="p-1.5 rounded border transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
                :class="redoAvailable ? 'border-[#d9d9d9] text-gray-500 hover:text-[#1890ff]' : 'border-transparent text-gray-300'">
                <Redo2 class="w-3.5 h-3.5" />
              </button>
            </div>

            <!-- 面板显隐快捷切换 (工程/图库/图层/属性) -->
            <div v-if="!isActiveMode"
              class="hidden sm:flex items-center gap-1 border-l border-slate-200 dark:border-slate-800 pl-2">
              <button @click="isProjectListOpen = !isProjectListOpen"
                class="px-2 py-1 rounded text-xs font-medium inline-flex items-center gap-1 border transition-all cursor-pointer select-none"
                :class="isProjectListOpen
                  ? 'bg-amber-50 dark:bg-amber-950/50 text-amber-600 dark:text-amber-400 border-amber-200 dark:border-amber-800 shadow-2xs font-semibold'
                  : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-800 dark:hover:text-slate-200'"
                :title="isProjectListOpen ? '收起工程列表' : '展开工程列表'">
                <FolderIcon class="w-3.5 h-3.5" />
                <span>工程</span>
              </button>
              <button @click="isWidgetLibraryOpen = !isWidgetLibraryOpen"
                class="px-2 py-1 rounded text-xs font-medium inline-flex items-center gap-1 border transition-all cursor-pointer select-none"
                :class="isWidgetLibraryOpen
                  ? 'bg-blue-50 dark:bg-sky-950/50 text-blue-600 dark:text-sky-400 border-blue-200 dark:border-sky-800 shadow-2xs font-semibold'
                  : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-800 dark:hover:text-slate-200'"
                :title="isWidgetLibraryOpen ? '收起器件图库' : '展开器件图库'">
                <Package class="w-3.5 h-3.5" />
                <span>图库</span>
              </button>
              <button @click="toggleRightTab('layers')"
                class="px-2 py-1 rounded text-xs font-medium inline-flex items-center gap-1 border transition-all cursor-pointer select-none"
                :class="isRightSidebarOpen && rightActiveTab === 'layers'
                  ? 'bg-indigo-50 dark:bg-indigo-950/50 text-indigo-600 dark:text-indigo-400 border-indigo-200 dark:border-indigo-800 shadow-2xs font-semibold'
                  : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-800 dark:hover:text-slate-200'"
                :title="isRightSidebarOpen && rightActiveTab === 'layers' ? '收起图层面板' : '切换至PS图层管理选项卡'">
                <Layers class="w-3.5 h-3.5" />
                <span>图层</span>
              </button>
              <button @click="toggleRightTab('inspector')"
                class="px-2 py-1 rounded text-xs font-medium inline-flex items-center gap-1 border transition-all cursor-pointer select-none"
                :class="isRightSidebarOpen && rightActiveTab === 'inspector'
                  ? 'bg-blue-50 dark:bg-sky-950/50 text-blue-600 dark:text-sky-400 border-blue-200 dark:border-sky-800 shadow-2xs font-semibold'
                  : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-800 dark:hover:text-slate-200'"
                :title="isRightSidebarOpen && rightActiveTab === 'inspector' ? '收起属性面板' : '切换至属性配置选项卡'">
                <Sliders class="w-3.5 h-3.5" />
                <span>属性</span>
              </button>
            </div>

            <!-- Mode switch toggle indicator -->
            <button @click="isActiveMode = !isActiveMode"
              class="px-3.5 py-1 rounded-full text-xs font-bold inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 border"
              :class="isActiveMode ? 'bg-emerald-600 text-white border-emerald-600 shadow-[0_0_8px_rgba(16,185,129,0.3)]' : 'bg-slate-900 dark:bg-slate-800 text-[#cbd5e1] border-slate-900 dark:border-slate-700'">
              <Activity class="w-3.5 h-3.5" :class="isActiveMode ? 'animate-pulse' : ''" />
              {{ isActiveMode ? '运行模式' : '设计模式' }}
            </button>

            <!-- 全屏模式切换按钮 -->
            <button @click="toggleFullscreen()"
              class="px-2.5 sm:px-3 py-1 rounded-full text-xs font-bold inline-flex items-center gap-1.5 cursor-pointer transition-all active:translate-y-0.5 border"
              :class="isScadaFullscreen
                ? 'bg-sky-600 hover:bg-sky-700 text-white border-sky-600 shadow-[0_0_8px_rgba(2,132,199,0.3)]'
                : 'bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-200 border-slate-200 dark:border-slate-700 hover:bg-slate-200/70 dark:hover:bg-slate-700'"
              :title="isScadaFullscreen ? '退出全屏模式 (Esc)' : '全屏设计模式 (隐藏菜单与工程列表)'">
              <Minimize2 v-if="isScadaFullscreen" class="w-3.5 h-3.5" />
              <Maximize2 v-else class="w-3.5 h-3.5" />
              <span class="hidden sm:inline">{{ isScadaFullscreen ? '退出全屏' : '全屏模式' }}</span>
            </button>
          </div>
        </div>

        <!-- HMI dragging field with Library -->
        <div class="flex-1 flex flex-col md:flex-row min-h-0 relative">
          <!-- Widget Library 展开态 -->
          <div v-if="!isActiveMode && isWidgetLibraryOpen"
            class="w-60 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 shrink-0 hidden lg:block overflow-y-auto transition-colors">
            <WidgetLibrary @addWidget="handleAddWidget" @collapse="isWidgetLibraryOpen = false" />
          </div>

          <!-- Widget Library 收起态把手 -->
          <div v-if="!isActiveMode && !isWidgetLibraryOpen" @click="isWidgetLibraryOpen = true"
            class="hidden lg:flex flex-col items-center justify-center w-7 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 cursor-pointer hover:bg-slate-100 dark:hover:bg-slate-800/80 transition-colors text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 shrink-0 select-none py-4 gap-2.5 z-10 group shadow-xs"
            title="点击展开工业器件图库">
            <ChevronRight class="w-4 h-4 transition-transform group-hover:translate-x-0.5" />
            <span
              class="text-[11px] font-bold [writing-mode:vertical-rl] tracking-widest text-slate-500 dark:text-slate-400 group-hover:text-[#1890ff] dark:group-hover:text-sky-400">器件图库</span>
          </div>

          <!-- Sandbox Design canvas panel -->
          <div class="flex-1 bg-slate-900 relative overflow-hidden flex flex-col min-h-[350px] md:min-h-0">
            <div class="flex-1 overflow-auto p-4"
              :class="currentPlatform === 'Mobile' ? 'flex justify-center items-start md:items-center' : ''">
              <!-- 移动端：套一层手机外框，强化移动视口区分 -->
              <div v-if="currentPlatform === 'Mobile'"
                class="shrink-0 rounded-[2.25rem] bg-neutral-900 p-2.5 shadow-2xl ring-1 ring-neutral-700">
                <div class="rounded-[1.6rem] overflow-hidden bg-slate-900">
                  <CanvasPanel :components="currentPage.components" :selectedId="selectedId" :selectedIds="selectedIds"
                    :isActiveMode="isActiveMode" :component-values="componentValues" :canvas-width="pageWidth"
                    :canvas-height="pageHeight" :can-control-write="canControlWrite"
                    :background="currentPage.background" :adapt-mode="currentPage.adaptMode"
                    :layers="currentPage.layers" :current-page-id="currentPage.id" @select-components="handleSelectComponents"
                    @updateComponent="handleUpdateComponent" @update-components="handleUpdateComponents"
                    @toggleMode="isActiveMode = !isActiveMode" @triggerToggleValue="handleTriggerToggleValue"
                    @delete-components="handleDeleteComponents" @duplicate-components="handleDuplicateComponents"
                    @clearCanvas="handleClearCanvas" @update-canvas-size="handleUpdateCanvasSize"
                    @add-component-at="handleAddWidgetAt" @navigate-to-page="handleNavigate"
                    @trigger-run-script="handleTriggerRunScript" @select-background="handleSelectBackground"
                    @request-set-value="handleRequestSetValue"
                    @component-event="handleComponentEvent" />
                </div>
              </div>
              <CanvasPanel v-else :components="currentPage.components" :selectedId="selectedId"
                :selectedIds="selectedIds" :isActiveMode="isActiveMode" :component-values="componentValues"
                :canvas-width="pageWidth" :canvas-height="pageHeight" :can-control-write="canControlWrite"
                :background="currentPage.background" :adapt-mode="currentPage.adaptMode" :layers="currentPage.layers"
                :current-page-id="currentPage.id"
                @select-components="handleSelectComponents" @updateComponent="handleUpdateComponent"
                @update-components="handleUpdateComponents" @toggleMode="isActiveMode = !isActiveMode"
                @triggerToggleValue="handleTriggerToggleValue" @delete-components="handleDeleteComponents"
                @duplicate-components="handleDuplicateComponents" @clearCanvas="handleClearCanvas"
                @update-canvas-size="handleUpdateCanvasSize" @add-component-at="handleAddWidgetAt"
                @navigate-to-page="handleNavigate" @trigger-run-script="handleTriggerRunScript"
                @select-background="handleSelectBackground" @request-set-value="handleRequestSetValue"
                @component-event="handleComponentEvent" />
            </div>
          </div>
        </div>
      </div>

      <!-- var-display 设值弹窗：确认后走 handleTriggerToggleValue('setValue') 写管道 -->
      <SetValueDialog v-if="setValueTarget" :component="setValueTarget" :current="setValueCurrentValue"
        @close="setValueTarget = null" @confirm="handleSetValueConfirm" />

      <!-- Right section: Tabbed Right Sidebar (属性配置 & PS 图层管理选项卡) -->
      <div v-if="!isActiveMode && isRightSidebarOpen"
        class="w-full md:w-80 lg:w-84 xl:w-90 bg-white dark:bg-slate-900 border-t md:border-t-0 md:border-l border-slate-200 dark:border-slate-800 flex flex-col shrink-0 transition-all overflow-hidden z-10 shadow-xs">

        <!-- Tab Bar Header -->
        <div
          class="flex items-center justify-between px-3 py-2 border-b border-slate-200 dark:border-slate-800 bg-slate-50/90 dark:bg-slate-950/90 shrink-0 select-none">
          <!-- Segmented Tab Switcher -->
          <div
            class="flex items-center bg-slate-200/90 dark:bg-slate-800/90 p-0.5 rounded-lg gap-0.5 border border-slate-300/40 dark:border-slate-700/60">
            <button @click="rightActiveTab = 'inspector'"
              class="flex items-center gap-1.5 px-3 py-1 text-xs font-semibold rounded-md transition-all cursor-pointer"
              :class="rightActiveTab === 'inspector'
                ? 'bg-white dark:bg-slate-900 text-[#1890ff] dark:text-sky-400 shadow-xs'
                : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'">
              <Sliders class="w-3.5 h-3.5" />
              <span>属性配置</span>
            </button>
            <button @click="rightActiveTab = 'events'"
              class="flex items-center gap-1.5 px-3 py-1 text-xs font-semibold rounded-md transition-all cursor-pointer"
              :class="rightActiveTab === 'events'
                ? 'bg-white dark:bg-slate-900 text-[#1890ff] dark:text-sky-400 shadow-xs'
                : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'">
              <Zap class="w-3.5 h-3.5" />
              <span>事件</span>
            </button>
            <button @click="rightActiveTab = 'layers'"
              class="flex items-center gap-1.5 px-3 py-1 text-xs font-semibold rounded-md transition-all cursor-pointer"
              :class="rightActiveTab === 'layers'
                ? 'bg-white dark:bg-slate-900 text-indigo-600 dark:text-indigo-400 shadow-xs'
                : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'">
              <Layers class="w-3.5 h-3.5" />
              <span>图层管理</span>
              <span class="px-1.5 py-0.2 text-[10px] font-bold rounded-full transition-colors" :class="rightActiveTab === 'layers'
                ? 'bg-indigo-100 dark:bg-indigo-950/70 text-indigo-600 dark:text-indigo-300'
                : 'bg-slate-300/70 dark:bg-slate-700 text-slate-600 dark:text-slate-300'">
                {{ (currentPage.layers || []).length || 1 }}
              </span>
            </button>
          </div>

          <!-- Collapse Button -->
          <button @click="isRightSidebarOpen = false"
            class="p-1 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
            title="收起右侧面板">
            <ChevronRight class="w-4 h-4" />
          </button>
        </div>

        <!-- Tab Content Area -->
        <div class="flex-1 min-h-0 overflow-y-auto">
          <!-- 属性配置选项卡 -->
          <div v-show="rightActiveTab === 'inspector'" class="h-full">
            <InspectorPanel :selectedComponent="selectedCompObj" :current-page-id="currentPage.id"
              :background-page="isBackgroundSelected ? currentPage : null" :layers="currentPage.layers"
              @updateComponent="handleUpdateComponent" @updatePage="handleUpdatePage"
              @collapse="isRightSidebarOpen = false" />
          </div>

          <!-- 事件配置选项卡 -->
          <div v-show="rightActiveTab === 'events'" class="h-full">
            <EventPanel :selectedComponent="selectedCompObj" :current-page-id="currentPage.id"
              :page-components="currentPage.components"
              @updateComponent="handleUpdateComponent" @collapse="isRightSidebarOpen = false" />
          </div>

          <!-- PS 图层管理选项卡 -->
          <div v-show="rightActiveTab === 'layers'" class="h-full">
            <LayersPanel :layers="currentPage.layers || []" :components="currentPage.components"
              :selected-id="selectedId" :selected-ids="selectedIds" :active-layer-id="activeLayerId"
              @update-layers="handleUpdateLayers" @update-component="handleUpdateComponent"
              @update-components="handleUpdateComponents" @select-components="handleSelectComponents"
              @set-active-layer="handleSetActiveLayer" @delete-components="handleDeleteComponents"
              @duplicate-components="handleDuplicateComponents" @inspect-component="handleInspectComponent"
              @collapse="isRightSidebarOpen = false" />
          </div>
        </div>
      </div>

      <!-- 右侧面板收起态把手 -->
      <div v-if="!isActiveMode && !isRightSidebarOpen" @click="isRightSidebarOpen = true"
        class="hidden md:flex flex-col items-center justify-center w-7 bg-white dark:bg-slate-900 border-l border-slate-200 dark:border-slate-800 cursor-pointer hover:bg-slate-100 dark:hover:bg-slate-800/80 transition-colors text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 shrink-0 select-none py-4 gap-2.5 z-10 group shadow-xs"
        title="点击展开右侧面板 (属性配置 / 事件 / 图层管理)">
        <ChevronLeft class="w-4 h-4 transition-transform group-hover:-translate-x-0.5" />
        <span
          class="text-[11px] font-bold [writing-mode:vertical-rl] tracking-widest text-slate-500 dark:text-slate-400 group-hover:text-[#1890ff] dark:group-hover:text-sky-400">属性
          / 事件 / 图层</span>
      </div>

    </div>

    <!-- 组态图片图库：图元添加选图（图元换图/背景选图由 InspectorPanel 内嵌实例处理） -->
    <ImageLibraryDialog v-model="showImageLibrary" @select="handleImageSelected" />

    <!-- 绑定检查浮动面板（严格模式：组件必须绑定设备维度） -->
    <div v-if="showBindingCheck" class="fixed top-16 right-4 z-40">
      <BindingCheckPanel @locate="onLocateComponent" />
    </div>

    <!-- MODAL: ADD SCADA PROJECT ENGINEERING -->
    <div v-if="showProjectModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div
        class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div
          class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div
            class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest text-[#1890ff] dark:text-sky-400">
            <FileCode class="w-4 h-4" />
            <span>新建工程</span>
          </div>
          <button @click="showProjectModal = false" class="text-slate-400 hover:text-white cursor-pointer">
            <X class="w-4 h-4" />
          </button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">工程名称</label>
            <input v-model="newProjectName" type="text" placeholder="如: 食品车间3号流水线监控层"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500" />
          </div>
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">工程描述</label>
            <textarea v-model="newProjectDesc" rows="3" placeholder="概括说明本项目管控的PLC设备与遥测数据特征..."
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 leading-relaxed" />
          </div>
        </div>

        <div
          class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800">
          <button @click="showProjectModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer">
            取消
          </button>
          <button @click="handleCreateProject"
            class="px-4 py-1.5 rounded-lg bg-slate-900 dark:bg-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white cursor-pointer">
            配置保存并初始化
          </button>
        </div>
      </div>
    </div>

    <!-- 阶段5-6：统一危险操作确认模态 -->
    <ConfirmModal :open="confirmState.open" :title="confirmState.title" :message="confirmState.message"
      :danger="confirmState.danger" :confirm-text="confirmState.confirmText" @confirm="onConfirmModal"
      @cancel="confirmState.open = false" />

    <!-- 组态导入导出：隐藏文件选择（触发后端迁移包导入） -->
    <input ref="projectImportInput" type="file" accept=".json,application/json" class="hidden"
      @change="handleImportProjectFile" />
    <input ref="pageImportInput" type="file" accept=".json,application/json" class="hidden"
      @change="handleImportPageFile" />

  </div>
</template>
