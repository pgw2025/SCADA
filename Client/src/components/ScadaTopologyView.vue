<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import {
  beginChange,
  endChange,
  recordDiscrete,
  undo,
  redo,
  undoAvailable,
  redoAvailable,
} from '../services/historyService';
import { 
  scadaProjects,
  selectedProjectId,
  selectedPageId,
  currentProject,
  currentPage,
  currentPlatform,
  desktopPages,
  mobilePages,
  initializeScada
} from '../store/scadaStore';
import { 
  updateCurrentPageComponents,
  ensureProjectSaved,
  ensurePageSaved,
  persistNewComponent,
  persistComponentUpdate,
  persistComponentDelete,
  persistPageUpdate,
  persistPageDelete,
  persistProjectUpdate,
  persistDuplicatePage
} from '../services/scadaService';
import { devices } from '../store/deviceStore';
import { loginUser } from '../store/userStore';
import { ROLE_ADMIN, ROLE_OPERATOR } from '../constants/roles';
import { addLog } from '../store/index';
import { getDeviceVariableValue, setDeviceVariableValue } from '../services/dataOrchestration';
import { showToast } from '../services/toastService';
import { HMIComponent, ComponentType } from '../types';
import WidgetLibrary from './WidgetLibrary.vue';
import CanvasPanel from './CanvasPanel.vue';
import InspectorPanel from './InspectorPanel.vue';
import BindingCheckPanel from './BindingCheckPanel.vue';
import ConfirmModal from './ConfirmModal.vue';
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
  AlertTriangle
} from 'lucide-vue-next';

// Editor settings
// 阶段5-2：选中模型由单值升级为集合（支持多选/框选）；selectedId 为「单选」派生值，供 Inspector 单组件编辑
const selectedIds = ref<string[]>([]);
const selectedId = computed<string | null>(() =>
  selectedIds.value.length === 1 ? selectedIds.value[0] : null
);
const isActiveMode = ref<boolean>(false);

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
}>({ open: false, title: '', message: '', danger: true, confirmText: '确认', onConfirm: () => {} });

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

// 阶段2：挂载时从后端整树加载组态（后端为空则保留本地模板）
onMounted(() => {
  initializeScada();
  window.addEventListener('keydown', onHistoryKey);
});

onUnmounted(() => {
  window.removeEventListener('keydown', onHistoryKey);
});

// 阶段5-1：撤销/重做（纯前端快照栈，不影响后端持久化）
const applyRestored = (restored: HMIComponent[] | null) => {
  if (!restored) return;
  updateCurrentPageComponents(restored.map((c) => ({ ...c })));
  if (selectedId.value && !restored.find((c) => c.id === selectedId.value)) {
    selectedIds.value = [];
  }
  addLog('组态编辑', '已撤销/重做编辑操作', 'normal');
};

const onHistoryKey = (e: KeyboardEvent) => {
  if (isActiveMode.value) return;
  const tag = (e.target as HTMLElement).tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
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
  currentPage.value.components.forEach((c) => {
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

// 阶段5-3：画布分辨率（由页面属性驱动，回退默认 1100×700）
const pageWidth = computed(() => currentPage.value.width ?? 1100);
const pageHeight = computed(() => currentPage.value.height ?? 700);

// 阶段6-2：写控制角色权限。仅 Operator/Admin 可在运行模式下发写指令；
// 其它角色（如 Viewer）即便已通过 JWT 认证，前端也拦截写控件、后端以 [Authorize(Roles)] 兜底 403。
const canControlWrite = computed(() => {
  const r = loginUser.value?.role;
  return r === ROLE_OPERATOR || r === ROLE_ADMIN;
});
const handleUpdateCanvasSize = (w: number, h: number) => {
  const pg = currentPage.value;
  pg.width = w;
  pg.height = h;
  addLog('组态编辑', `画布分辨率调整为 [${w} × ${h}]`, 'normal');
  // 阶段2：落库页面尺寸（新建未落库页面仅前端生效）
  persistPageUpdate(pg).catch(() => {});
};

// Map CanvasPanel updates directly to active project page
const handleUpdateComponent = (id: string, updates: Partial<HMIComponent>) => {
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
  if (!updates.length) return;
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

// 阶段5-2：选中集合变更（单选/多选/框选统一入口）
const handleSelectComponents = (ids: string[]) => {
  selectedIds.value = ids;
};

// Add a widget from panel library
// 阶段5-5：默认尺寸 / props 取自 widgetRegistry（消除与图库两处散改）
const handleAddWidget = (type: ComponentType, defaultW: number, defaultH: number, label: string, x = 40, y = 60) => {
  const currentComps = currentPage.value.components;
  const newId = genComponentId(type);
  const def = getWidgetDef(type);
  const w = defaultW || def?.defaultWidth || 100;
  const h = defaultH || def?.defaultHeight || 50;

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
    zIndex: currentComps.length + 1,
    props: def?.defaultProps() ?? {},
  };

  recordDiscrete(currentComps);
  updateCurrentPageComponents([...currentComps, newComponent]);
  selectedIds.value = [newId];
  addLog('组态编辑', `在页面 [${currentPage.value.name}] 添加组件 [${label}]`, 'info');

  // 阶段2：落库并回填 serverId（确保所属页面已落库）
  persistNewComponent(currentPage.value, currentProject.value, newComponent).catch(() => {});
};

// 阶段5-4：组件库拖拽投放落点（由 CanvasPanel 反算坐标后调用，x/y 为画布内坐标）
const handleAddWidgetAt = (type: string, w: number, h: number, name: string, x: number, y: number) => {
  handleAddWidget(type as ComponentType, w, h, name, x, y);
};

// Duplicate widget(s) — 阶段5-2 支持批量复制
const handleDuplicateComponents = (ids: string[]) => {
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
    persistNewComponent(currentPage.value, currentProject.value, c).catch(() => {})
  );
};

// Delete widget(s) — 阶段5-2 支持批量删除（统一一次确认）
const handleDeleteComponents = (ids: string[]) => {
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
      persistComponentDelete(t).catch(() => {})
    );
  });
};

// Clear active drawing canvas Layout
const handleClearCanvas = () => {
  askConfirm('清空画布', '确定要清空当前画布吗？此操作将移除本页全部组件且不可逆。', () => {
    recordDiscrete(currentPage.value.components);
    const toDelete = currentPage.value.components.filter(c => c.serverId);
    updateCurrentPageComponents([]);
    selectedIds.value = [];
    addLog('组态编辑', `清空画布: [${currentPage.value.name}]`, 'warning');

    // 阶段2：批量删除已落库的组件
    Promise.all(toDelete.map(c => persistComponentDelete(c))).catch(() => {});
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
  // 后端未配置 camelCase，DTO 默认 PascalCase；两种命名均兼容读取。
  const isReadOnly = meta?.effectiveIsReadOnly ?? meta?.EffectiveIsReadOnly ?? false;
  if (isReadOnly) {
    showToast(`变量 [${key}] 为只读，禁止写入`, 'warning');
    addLog('SCADA 写控', `写拦截：变量 [设备${deviceId}.${key}] 为只读`, 'warning');
    return;
  }

  const current = getDeviceVariableValue(deviceId, key);

  let targetVal: any;
  if (actionType === 'setValue' && val !== undefined) {
    targetVal = val;
  } else if (actionType === 'momentary' && val !== undefined) {
    if (typeof current === 'boolean') {
      targetVal = val;
    } else {
      targetVal = val ? 1 : 0;
    }
  } else {
    // Toggle
    if (typeof current === 'boolean') {
      targetVal = !current;
    } else if (typeof current === 'number') {
      targetVal = current === 0 ? 100 : 0;
    }
  }
  setDeviceVariableValue(deviceId, key, targetVal);
};

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

  // 阶段2：落库并回填 serverId
  ensureProjectSaved(newProj).catch(() => {});
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
  ensurePageSaved(newPage, proj).catch(() => {});
};

// 视口切换：切换到指定端，并选中该端首个画面（保持编辑上下文一致）。
const switchPlatform = (platform: 'Desktop' | 'Mobile') => {
  currentPlatform.value = platform;
  const list = platform === 'Mobile' ? mobilePages.value : desktopPages.value;
  if (list.length > 0) selectedPageId.value = list[0].id;
};

// 阶段3：运行模式（预览）下点击「导航」按钮 → 切换到目标画面（目标必为同端，编辑器不跨端）。
const handleNavigate = (pageId: string) => {
  const target = currentProject.value?.pages.find(p => p.id === pageId);
  if (!target) return;
  selectedPageId.value = pageId;
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
    .forEach(pg => persistPageUpdate(pg).catch(() => {}));
};

// Copy / Duplicate child page
const handleDuplicatePage = (page: { id: string; name: string; components: any[] }) => {
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
    components: JSON.parse(JSON.stringify(page.components))
  };
  proj.pages.push(newPage);
  selectedPageId.value = newPageId;
  addLog('组态编辑', `复制页面: [${page.name}]`, 'normal');

  // 阶段2：落库页面 + 其下全部组件并回填 serverId
  persistDuplicatePage(newPage, proj).catch(() => {});
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
      persistPageDelete(target).catch(() => {});
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
    persistPageUpdate(pg).catch(() => {});
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
    persistProjectUpdate(proj).catch(() => {});
  }
  isRenamingProjId.value = null;
};

const selectProjectDirectly = (projId: string) => {
  selectedProjectId.value = projId;
  const proj = scadaProjects.value.find(p => p.id === projId);
  if (proj && proj.pages.length > 0) {
    selectedPageId.value = proj.pages[0].id;
  }
};

const selectedCompObj = computed(() => {
  return currentPage.value.components.find((c) => c.id === selectedId.value) || null;
});
</script>

<template>
  <div class="h-full overflow-y-auto md:overflow-y-hidden flex flex-col md:flex-row text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">
    
    <!-- LEFT CONTROL BAR: Scada Projects and multiple subpages directory -->
    <div class="w-full md:w-64 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex flex-col shrink-0 flex-1 md:flex-none transition-colors">
      
      <!-- Top Screen/Project select -->
      <div class="p-4 border-b border-slate-100 dark:border-slate-800 space-y-3">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-1.5 font-bold text-sm text-slate-900 dark:text-white">
            <FolderIcon class="w-4 h-4 text-amber-500" />
            <span>工程列表</span>
          </div>
          <button 
            @click="showProjectModal = true"
            class="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 cursor-pointer"
            title="新建工程"
          >
            <Plus class="w-4 h-4" />
          </button>
        </div>

        <select 
          :value="selectedProjectId"
          @change="selectProjectDirectly(($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-[#1890ff] dark:text-sky-400 font-bold rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-xs focus:outline-none"
        >
          <option v-for="p in scadaProjects" :key="p.id" :value="p.id">
            {{ p.name }}
          </option>
        </select>
      </div>

      <!-- Active Project Header description -->
      <div v-if="currentProject" class="px-4 py-2 bg-slate-50/50 dark:bg-slate-950/40 border-b border-slate-100 dark:border-slate-800 text-left">
        <!-- Project direct quick edit rename -->
        <div class="flex items-center justify-between">
          <span class="text-[9px] uppercase font-bold tracking-wider text-slate-400 dark:text-slate-500">工程属性</span>
          <button 
            v-if="isRenamingProjId !== currentProject.id"
            @click="startRenameProj(currentProject.id, currentProject.name)"
            class="text-[10px] text-slate-400 dark:text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 cursor-pointer"
          >
            重命名
          </button>
        </div>

        <div v-if="isRenamingProjId === currentProject.id" class="flex gap-1 items-center mt-1">
          <input 
            type="text"
            v-model="renameProjInput"
            @keydown.enter="saveProjRename(currentProject.id)"
            class="bg-white dark:bg-slate-900 border dark:border-slate-700 rounded px-1.5 py-0.5 text-[11px] font-sans text-slate-800 dark:text-slate-100 focus:outline-none w-full"
          />
          <button @click="saveProjRename(currentProject.id)" class="p-0.5 bg-emerald-500 text-white rounded cursor-pointer"><Check class="w-3 h-3" /></button>
        </div>
        <p v-else class="text-[10px] text-slate-500 dark:text-slate-400 mt-1 leading-relaxed">
          {{ currentProject.description }}
        </p>
      </div>

      <!-- Subpages Directory explorer：按归属端分「桌面端 / 移动端」两组 -->
      <div class="flex items-center justify-between px-4 py-3 font-bold text-xs uppercase tracking-wider text-slate-400 dark:text-slate-500 border-b border-slate-100/60 dark:border-slate-800">
        <span>画面列表</span>
        <button
          @click="showBindingCheck = !showBindingCheck"
          class="flex items-center gap-1 normal-case font-semibold text-[11px] px-2 py-1 rounded border transition-colors"
          :class="showBindingCheck
            ? 'bg-amber-500 text-white border-amber-500'
            : 'bg-white dark:bg-slate-800 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:border-amber-400'"
          title="检查当前画面中未绑定设备的组件（裸 Key 风险）"
        >
          <AlertTriangle class="w-3.5 h-3.5" />
          绑定检查
        </button>
      </div>

      <!-- 桌面端分组 -->
      <div class="flex items-center justify-between px-4 py-1.5 bg-slate-50/60 dark:bg-slate-800/40 border-b border-slate-100/60 dark:border-slate-800">
        <span class="text-[11px] font-bold text-slate-500 dark:text-slate-400">🖥 桌面端 ({{ desktopPages.length }})</span>
        <button 
          @click="handleAddPage('Desktop')"
          class="p-0.5 rounded hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-500 dark:text-slate-400 cursor-pointer"
          title="新增桌面端画面"
        >
          <Plus class="w-3.5 h-3.5" />
        </button>
      </div>
      <div v-if="currentProject" class="overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800 max-h-[140px] md:max-h-none text-left font-sans">
        <div 
          v-for="page in desktopPages" 
          :key="page.id"
          @click="selectedPageId = page.id"
          class="p-3 cursor-pointer hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-all space-y-1 relative"
          :class="selectedPageId === page.id ? 'bg-sky-50/50 dark:bg-sky-950/40 text-[#1890ff] dark:text-sky-400 border-r-4 border-r-[#1890ff] dark:border-r-sky-500' : 'text-slate-700 dark:text-slate-300'"
        >
          <div class="flex items-center justify-between gap-2 overflow-hidden">
            <div v-if="isRenamingPageId === page.id" class="flex items-center gap-1 w-full" @click.stopPropagation>
              <input 
                v-model="renamePageInput"
                type="text"
                class="w-full bg-white dark:bg-slate-900 border border-slate-300 dark:border-slate-700 rounded px-1 py-0.5 text-xs text-slate-800 dark:text-slate-100 outline-none"
                @keyup.enter="savePageRename(page.id)"
              />
              <button @click="savePageRename(page.id)" class="text-emerald-600 dark:text-emerald-400 hover:text-emerald-700"><Check class="w-4 h-4" /></button>
            </div>
            <span v-else class="font-bold text-xs flex-1 leading-relaxed flex items-center gap-1 min-w-0">
              <span v-if="page.isHome" class="shrink-0 text-[8px] bg-amber-500 text-white px-1 py-0.5 rounded leading-none">首页</span>
              <span class="truncate">{{ page.name }}</span>
            </span>
            <div v-if="isRenamingPageId !== page.id" class="flex items-center gap-1.5 shrink-0 opacity-0 hover:opacity-100 focus-within:opacity-100 transition-all">
              <button @click.stop="setHomePage(page)" class="text-xs text-slate-400 hover:text-amber-500" :title="page.isHome ? '当前已是该端首页' : '设为该端首页'"><Home class="w-3 h-3" :class="page.isHome ? 'text-amber-500' : ''" /></button>
              <button @click.stop="startRenamePage(page.id, page.name)" class="text-xs text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="重命名"><Edit class="w-3 h-3" /></button>
              <button @click.stop="handleDuplicatePage(page)" class="text-xs text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="复制页面"><Copy class="w-3 h-3" /></button>
              <button @click.stop="handleDeletePage(page.id, page.name)" class="text-xs text-rose-400 hover:text-rose-600 dark:hover:text-rose-300" title="删除页面"><Trash2 class="w-3 h-3" /></button>
            </div>
          </div>
          <p class="text-[9px] font-mono text-slate-400 dark:text-slate-500">组件数: {{ page.components.length }}</p>
        </div>
      </div>

      <!-- 移动端分组 -->
      <div class="flex items-center justify-between px-4 py-1.5 bg-slate-50/60 dark:bg-slate-800/40 border-y border-slate-100/60 dark:border-slate-800 mt-1">
        <span class="text-[11px] font-bold text-slate-500 dark:text-slate-400">📱 移动端 ({{ mobilePages.length }})</span>
        <button 
          @click="handleAddPage('Mobile')"
          class="p-0.5 rounded hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-500 dark:text-slate-400 cursor-pointer"
          title="新增移动端画面"
        >
          <Plus class="w-3.5 h-3.5" />
        </button>
      </div>
      <div v-if="currentProject" class="overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800 max-h-[140px] md:max-h-none text-left font-sans">
        <div 
          v-for="page in mobilePages" 
          :key="page.id"
          @click="selectedPageId = page.id"
          class="p-3 cursor-pointer hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-all space-y-1 relative"
          :class="selectedPageId === page.id ? 'bg-sky-50/50 dark:bg-sky-950/40 text-[#1890ff] dark:text-sky-400 border-r-4 border-r-[#1890ff] dark:border-r-sky-500' : 'text-slate-700 dark:text-slate-300'"
        >
          <div class="flex items-center justify-between gap-2 overflow-hidden">
            <div v-if="isRenamingPageId === page.id" class="flex items-center gap-1 w-full" @click.stopPropagation>
              <input 
                v-model="renamePageInput"
                type="text"
                class="w-full bg-white dark:bg-slate-900 border border-slate-300 dark:border-slate-700 rounded px-1 py-0.5 text-xs text-slate-800 dark:text-slate-100 outline-none"
                @keyup.enter="savePageRename(page.id)"
              />
              <button @click="savePageRename(page.id)" class="text-emerald-600 dark:text-emerald-400 hover:text-emerald-700"><Check class="w-4 h-4" /></button>
            </div>
            <span v-else class="font-bold text-xs flex-1 leading-relaxed flex items-center gap-1 min-w-0">
              <span v-if="page.isHome" class="shrink-0 text-[8px] bg-amber-500 text-white px-1 py-0.5 rounded leading-none">首页</span>
              <span class="truncate">{{ page.name }}</span>
            </span>
            <div v-if="isRenamingPageId !== page.id" class="flex items-center gap-1.5 shrink-0 opacity-0 hover:opacity-100 focus-within:opacity-100 transition-all">
              <button @click.stop="setHomePage(page)" class="text-xs text-slate-400 hover:text-amber-500" :title="page.isHome ? '当前已是该端首页' : '设为该端首页'"><Home class="w-3 h-3" :class="page.isHome ? 'text-amber-500' : ''" /></button>
              <button @click.stop="startRenamePage(page.id, page.name)" class="text-xs text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="重命名"><Edit class="w-3 h-3" /></button>
              <button @click.stop="handleDuplicatePage(page)" class="text-xs text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="复制页面"><Copy class="w-3 h-3" /></button>
              <button @click.stop="handleDeletePage(page.id, page.name)" class="text-xs text-rose-400 hover:text-rose-600 dark:hover:text-rose-300" title="删除页面"><Trash2 class="w-3 h-3" /></button>
            </div>
          </div>
          <p class="text-[9px] font-mono text-slate-400 dark:text-slate-500">组件数: {{ page.components.length }}</p>
        </div>
      </div>

    </div>

    <!-- MAIN CO-WORKING VISUAL BUILDER WORKSPACE -->
    <div v-if="currentPage" class="flex-1 flex flex-col md:flex-row min-w-0">
      
      <!-- Center section: WidgetLibrary shelf (left nested) + Dragging HMI canvas (middle) -->
      <div class="flex-1 flex flex-col min-w-0 relative">
        
        <!-- Canvas bar toggler stats -->
        <div class="bg-white dark:bg-slate-900 px-5 py-3 border-b border-slate-200 dark:border-slate-800 shadow-sm flex items-center justify-between shrink-0 transition-colors">
          <div class="flex items-center gap-2 text-left">
            <span class="w-2 h-2 rounded-full bg-amber-500 shadow-[0_0_6px_#f59e0b]" />
            <div>
              <h3 class="font-bold text-xs text-slate-800 dark:text-slate-100">当前页面: <b class="text-slate-900 dark:text-white font-sans font-bold text-[13px] ml-1">{{ currentPage.name }}</b></h3>
              <p class="text-[10px] text-slate-400 dark:text-slate-500 font-mono">画布布局: 响应式画布</p>
            </div>
          </div>

          <div class="flex items-center gap-2">
            <!-- 视口切换：桌面端 / 移动端 -->
            <div class="hidden md:flex items-center rounded-full border border-slate-200 dark:border-slate-700 overflow-hidden text-[11px] font-bold">
              <button
                @click="switchPlatform('Desktop')"
                class="px-2.5 py-1 cursor-pointer transition-colors"
                :class="currentPlatform === 'Desktop' ? 'bg-[#1890ff] text-white' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800'"
                title="桌面端视口"
              >🖥 桌面</button>
              <button
                @click="switchPlatform('Mobile')"
                class="px-2.5 py-1 cursor-pointer transition-colors"
                :class="currentPlatform === 'Mobile' ? 'bg-[#1890ff] text-white' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800'"
                title="移动端视口"
              >📱 移动</button>
            </div>

            <!-- 撤销/重做 -->
            <div class="hidden md:flex items-center gap-1">
              <button
                @click="applyRestored(undo(currentPage.value.components))"
                :disabled="!undoAvailable"
                title="撤销 (Ctrl+Z)"
                class="p-1.5 rounded border transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
                :class="undoAvailable ? 'border-[#d9d9d9] text-gray-500 hover:text-[#1890ff]' : 'border-transparent text-gray-300'"
              >
                <Undo2 class="w-3.5 h-3.5" />
              </button>
              <button
                @click="applyRestored(redo(currentPage.value.components))"
                :disabled="!redoAvailable"
                title="重做 (Ctrl+Shift+Z)"
                class="p-1.5 rounded border transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
                :class="redoAvailable ? 'border-[#d9d9d9] text-gray-500 hover:text-[#1890ff]' : 'border-transparent text-gray-300'"
              >
                <Redo2 class="w-3.5 h-3.5" />
              </button>
            </div>

            <!-- Mode switch toggle indicator -->
            <button 
              @click="isActiveMode = !isActiveMode"
              class="px-3.5 py-1 rounded-full text-xs font-bold inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 border"
              :class="isActiveMode ? 'bg-emerald-600 text-white border-emerald-600 shadow-[0_0_8px_rgba(16,185,129,0.3)]' : 'bg-slate-900 dark:bg-slate-800 text-[#cbd5e1] border-slate-900 dark:border-slate-700'"
            >
              <Activity class="w-3.5 h-3.5" :class="isActiveMode ? 'animate-pulse' : ''" />
              {{ isActiveMode ? '运行模式' : '设计模式' }}
            </button>
          </div>
        </div>

        <!-- HMI dragging field with Library -->
        <div class="flex-1 flex flex-col md:flex-row min-h-0">
          <!-- Widget Library -->
          <div v-if="!isActiveMode" class="w-60 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 shrink-0 hidden lg:block overflow-y-auto transition-colors">
            <WidgetLibrary @addWidget="handleAddWidget" />
          </div>

          <!-- Sandbox Design canvas panel -->
          <div class="flex-1 bg-slate-900 relative overflow-hidden flex flex-col min-h-[350px] md:min-h-0">
            <div
              class="flex-1 overflow-auto p-4"
              :class="currentPlatform === 'Mobile' ? 'flex justify-center items-start md:items-center' : ''"
            >
              <!-- 移动端：套一层手机外框，强化移动视口区分 -->
              <div
                v-if="currentPlatform === 'Mobile'"
                class="shrink-0 rounded-[2.25rem] bg-neutral-900 p-2.5 shadow-2xl ring-1 ring-neutral-700"
              >
                <div class="rounded-[1.6rem] overflow-hidden bg-slate-900">
                  <CanvasPanel 
                    :components="currentPage.components"
                    :selectedId="selectedId"
                    :selectedIds="selectedIds"
                    :isActiveMode="isActiveMode"
                    :component-values="componentValues"
                    :canvas-width="pageWidth"
                    :canvas-height="pageHeight"
                    :can-control-write="canControlWrite"
                    @select-components="handleSelectComponents"
                    @updateComponent="handleUpdateComponent"
                    @update-components="handleUpdateComponents"
                    @toggleMode="isActiveMode = !isActiveMode"
                    @triggerToggleValue="handleTriggerToggleValue"
                    @delete-components="handleDeleteComponents"
                    @duplicate-components="handleDuplicateComponents"
                    @clearCanvas="handleClearCanvas"
                    @update-canvas-size="handleUpdateCanvasSize"
                    @add-component-at="handleAddWidgetAt"
                    @navigate-to-page="handleNavigate"
                  />
                </div>
              </div>
              <CanvasPanel 
                v-else
                :components="currentPage.components"
                :selectedId="selectedId"
                :selectedIds="selectedIds"
                :isActiveMode="isActiveMode"
                :component-values="componentValues"
                :canvas-width="pageWidth"
                :canvas-height="pageHeight"
                :can-control-write="canControlWrite"
                @select-components="handleSelectComponents"
                @updateComponent="handleUpdateComponent"
                @update-components="handleUpdateComponents"
                @toggleMode="isActiveMode = !isActiveMode"
                @triggerToggleValue="handleTriggerToggleValue"
                @delete-components="handleDeleteComponents"
                @duplicate-components="handleDuplicateComponents"
                @clearCanvas="handleClearCanvas"
                @update-canvas-size="handleUpdateCanvasSize"
                @add-component-at="handleAddWidgetAt"
                @navigate-to-page="handleNavigate"
              />
            </div>
          </div>
        </div>
      </div>

      <!-- Right section: Inspector Property Editor -->
      <div v-if="!isActiveMode && selectedId" class="w-full md:w-80 bg-white dark:bg-slate-900 border-t md:border-t-0 md:border-l border-slate-200 dark:border-slate-800 overflow-y-auto shrink-0 transition-colors">
        <!-- Render Inspector Panel directly targeting chosen component -->
        <InspectorPanel 
          :selectedComponent="selectedCompObj"
          @updateComponent="handleUpdateComponent"
        />
      </div>

    </div>

    <!-- 绑定检查浮动面板（严格模式：组件必须绑定设备维度） -->
    <div v-if="showBindingCheck" class="fixed top-16 right-4 z-40">
      <BindingCheckPanel @locate="onLocateComponent" />
    </div>

    <!-- MODAL: ADD SCADA PROJECT ENGINEERING -->
    <div v-if="showProjectModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest text-[#1890ff] dark:text-sky-400">
            <FileCode class="w-4 h-4" />
            <span>新建工程</span>
          </div>
          <button @click="showProjectModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">工程名称</label>
            <input 
              v-model="newProjectName"
              type="text"
              placeholder="如: 食品车间3号流水线监控层"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500"
            />
          </div>
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">工程描述</label>
            <textarea 
              v-model="newProjectDesc"
              rows="3"
              placeholder="概括说明本项目管控的PLC设备与遥测数据特征..."
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 leading-relaxed"
            />
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800">
          <button 
            @click="showProjectModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleCreateProject"
            class="px-4 py-1.5 rounded-lg bg-slate-900 dark:bg-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white cursor-pointer"
          >
            配置保存并初始化
          </button>
        </div>
      </div>
    </div>

    <!-- 阶段5-6：统一危险操作确认模态 -->
    <ConfirmModal
      :open="confirmState.open"
      :title="confirmState.title"
      :message="confirmState.message"
      :danger="confirmState.danger"
      :confirm-text="confirmState.confirmText"
      @confirm="onConfirmModal"
      @cancel="confirmState.open = false"
    />

  </div>
</template>
