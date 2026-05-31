<script setup lang="ts">
import { ref, computed } from 'vue';
import { 
  scadaProjects, 
  selectedProjectId, 
  selectedPageId, 
  currentProject, 
  currentPage,
  updateCurrentPageComponents,
  getDeviceVariableValue,
  setDeviceVariableValue,
  devices,
  addLog
} from '../store';
import { HMIComponent, ComponentType } from '../types';
import WidgetLibrary from './WidgetLibrary.vue';
import CanvasPanel from './CanvasPanel.vue';
import InspectorPanel from './InspectorPanel.vue';
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
  Activity
} from 'lucide-vue-next';

// Editor settings
const selectedId = ref<string | null>(null);
const isActiveMode = ref<boolean>(false);

// Modal state for custom screen creation
const showProjectModal = ref<boolean>(false);
const newProjectName = ref<string>('');
const newProjectDesc = ref<string>('');

// Inline page renaming states
const isRenamingPageId = ref<string | null>(null);
const renamePageInput = ref<string>('');

// Inline project renaming states
const isRenamingProjId = ref<string | null>(null);
const renameProjInput = ref<string>('');

// Fetch dynamic telemetry for the active drawing canvas
const simulatedDataComputed = computed(() => {
  const res: Record<string, number | boolean> = {};
  devices.value.forEach((d) => {
    if (d.status === 'online') {
      Object.keys(d.variables).forEach((key) => {
        res[key] = d.variables[key];
      });
    }
  });
  return res;
});

// Map CanvasPanel updates directly to active project page
const handleUpdateComponent = (id: string, updates: Partial<HMIComponent>) => {
  const currentComps = currentPage.value.components;
  const newComps = currentComps.map((comp) => {
    if (comp.id === id) {
      return { ...comp, ...updates };
    }
    return comp;
  });
  updateCurrentPageComponents(newComps);
};

// Add a widget from panel library
const handleAddWidget = (type: ComponentType, defaultW: number, defaultH: number, label: string) => {
  const currentComps = currentPage.value.components;
  const newId = `${type}-${Date.now().toString().slice(-6)}`;
  
  const newComponent: HMIComponent = {
    id: newId,
    type,
    name: `${label} ${currentComps.filter((c) => c.type === type).length + 1}`,
    x: 40,
    y: 60,
    width: defaultW,
    height: defaultH,
    label: label,
    bindField: '',
    zIndex: currentComps.length + 1,
    props: {
      activeColor: type === 'valve' || type === 'led' ? '#10b981' : '#3b82f6',
      inactiveColor: '#94a3b8',
      maxValue: type === 'gauge-dial' ? 120 : 100,
      unit: type === 'gauge-dial' ? '℃' : '',
      showValue: false,
      fontSize: 12,
      bold: false,
      align: 'center',
    },
  };

  updateCurrentPageComponents([...currentComps, newComponent]);
  selectedId.value = newComponent.id;
  addLog('组态编辑', `在画面 [${currentPage.value.name}] 添加组态控件 [${label}]`, 'info');
};

// Duplicate widget
const handleDuplicateComponent = (id: string) => {
  const currentComps = currentPage.value.components;
  const target = currentComps.find(c => c.id === id);
  if (!target) return;

  const cloned: HMIComponent = {
    ...target,
    id: `${target.type}-${Date.now().toString().slice(-6)}`,
    name: `${target.name} (副本)`,
    x: target.x + 20,
    y: target.y + 20,
    zIndex: currentComps.length + 1,
  };

  updateCurrentPageComponents([...currentComps, cloned]);
  selectedId.value = cloned.id;
  addLog('组态编辑', `克隆组态控件: [${target.name}]`, 'info');
};

// Delete widget
const handleDeleteComponent = (id: string) => {
  const currentComps = currentPage.value.components;
  const target = currentComps.find(c => c.id === id);
  const name = target ? target.name : 'Unknown';
  
  updateCurrentPageComponents(currentComps.filter(c => c.id !== id));
  if (selectedId.value === id) {
    selectedId.value = null;
  }
  addLog('组态编辑', `移除了画布控件 [${name}]`, 'warning');
};

// Clear active drawing canvas Layout
const handleClearCanvas = () => {
  if (confirm('确定要清空整张组态画布吗？此动作不可逆。')) {
    updateCurrentPageComponents([]);
    selectedId.value = null;
    addLog('组态编辑', `清空了整个画布: [${currentPage.value.name}]`, 'warning');
  }
};

// Toggle or forces live registries value on active devices
const handleTriggerToggleValue = (bindField: string, actionType?: string, val?: any) => {
  if (!bindField) return;
  const current = getDeviceVariableValue(bindField);

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
  setDeviceVariableValue(bindField, targetVal);
};

// Create new SCADA project screen
const handleCreateProject = () => {
  if (!newProjectName.value.trim()) return;

  const newProjId = `project-${Date.now()}`;
  scadaProjects.value.push({
    id: newProjId,
    name: newProjectName.value,
    description: newProjectDesc.value || '自定义新HMI中控总网图',
    pages: [
      {
        id: `page-${Date.now()}-primary`,
        name: '未命名画面 1',
        components: []
      }
    ]
  });

  addLog('组态编辑', `创建了全新SCADA中控工程: [${newProjectName.value}]`, 'normal');
  selectedProjectId.value = newProjId;
  selectedPageId.value = scadaProjects.value[scadaProjects.value.length - 1].pages[0].id;
  
  newProjectName.value = '';
  newProjectDesc.value = '';
  showProjectModal.value = false;
};

// Add child page to active screen project
const handleAddPage = () => {
  const proj = currentProject.value;
  if (!proj) return;

  const newPageId = `page-${Date.now()}`;
  const newPage = {
    id: newPageId,
    name: `未命名主控画面 ${proj.pages.length + 1}`,
    components: []
  };

  proj.pages.push(newPage);
  selectedPageId.value = newPageId;
  addLog('组态编辑', `项目 [${proj.name}] 追加了新图幅: [${newPage.name}]`, 'normal');
};

// Copy / Duplicate child page
const handleDuplicatePage = (page: { id: string; name: string; components: any[] }) => {
  const proj = currentProject.value;
  if (!proj) return;

  const newPageId = `page-${Date.now()}`;
  proj.pages.push({
    id: newPageId,
    name: `${page.name} - 副本`,
    components: JSON.parse(JSON.stringify(page.components))
  });
  selectedPageId.value = newPageId;
  addLog('组态编辑', `克隆整幅画面: [${page.name}]`, 'normal');
};

// Delete page
const handleDeletePage = (pId: string, pName: string) => {
  const proj = currentProject.value;
  if (!proj) return;

  if (proj.pages.length <= 1) {
    alert('组态项目必须含有至少一幅主控图层，不允许将其完全排空。');
    return;
  }

  proj.pages = proj.pages.filter(pg => pg.id !== pId);
  if (selectedPageId.value === pId) {
    selectedPageId.value = proj.pages[0].id;
  }
  addLog('组态编辑', `移除了图幅: [${pName}]`, 'warning');
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
    addLog('组态编辑', `图层画面更名: [${oldName}] 更名为 [${pg.name}]`, 'normal');
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
    addLog('组态编辑', `组态项目工程更名: [${oldName}] -> [${proj.name}]`, 'normal');
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

const plcTagsList = computed(() => {
  const res: Array<{ key: string; name: string }> = [];
  dataModels.value.forEach((m) => {
    m.variables.forEach((v) => {
      if (!res.some(existing => existing.key === v.key)) {
        res.push({ key: v.key, name: `${v.name} (${v.key})` });
      }
    });
  });
  return res;
});
</script>

<template>
  <div class="h-full overflow-y-auto md:overflow-y-hidden flex flex-col md:flex-row text-[#1e293b] select-none bg-slate-50">
    
    <!-- LEFT CONTROL BAR: Scada Projects and multiple subpages directory -->
    <div class="w-full md:w-64 bg-white border-r border-slate-200 flex flex-col shrink-0 flex-1 md:flex-none">
      
      <!-- Top Screen/Project select -->
      <div class="p-4 border-b border-slate-100 space-y-3">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-1.5 font-bold text-sm text-slate-900">
            <FolderIcon class="w-4 h-4 text-amber-500" />
            <span>发布项目选择</span>
          </div>
          <button 
            @click="showProjectModal = true"
            class="p-1 rounded hover:bg-slate-100 text-slate-500 cursor-pointer"
            title="添加新组态项目"
          >
            <Plus class="w-4 h-4" />
          </button>
        </div>

        <select 
          :value="selectedProjectId"
          @change="selectProjectDirectly(($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 border border-slate-200 text-[#1890ff] font-bold rounded-lg p-2 focus:bg-white text-xs focus:outline-none"
        >
          <option v-for="p in scadaProjects" :key="p.id" :value="p.id">
            {{ p.name }}
          </option>
        </select>
      </div>

      <!-- Active Project Header description -->
      <div v-if="currentProject" class="px-4 py-2 bg-slate-50/50 border-b border-slate-100 text-left">
        <!-- Project direct quick edit rename -->
        <div class="flex items-center justify-between">
          <span class="text-[9px] uppercase font-bold tracking-wider text-slate-400">项目工程属性</span>
          <button 
            v-if="isRenamingProjId !== currentProject.id"
            @click="startRenameProj(currentProject.id, currentProject.name)"
            class="text-[10px] text-slate-400 hover:text-[#1890ff]"
          >
            编辑名称
          </button>
        </div>

        <div v-if="isRenamingProjId === currentProject.id" class="flex gap-1 items-center mt-1">
          <input 
            type="text"
            v-model="renameProjInput"
            @keydown.enter="saveProjRename(currentProject.id)"
            class="bg-white border rounded px-1.5 py-0.5 text-[11px] font-sans text-slate-800 focus:outline-none w-full"
          />
          <button @click="saveProjRename(currentProject.id)" class="p-0.5 bg-emerald-500 text-white rounded"><Check class="w-3 h-3" /></button>
        </div>
        <p v-else class="text-[10px] text-slate-500 mt-1 leading-relaxed">
          {{ currentProject.description }}
        </p>
      </div>

      <!-- Subpages Directory explorer -->
      <div class="p-4 flex items-center justify-between border-b border-slate-100/60 font-bold text-xs uppercase tracking-wider text-slate-400">
        <span>画布幅图目录 ({{ currentProject?.pages.length || 0 }})</span>
        <button 
          @click="handleAddPage"
          class="p-0.5 rounded hover:bg-slate-100 text-slate-500 cursor-pointer"
          title="新增画层"
        >
          <Plus class="w-3.5 h-3.5" />
        </button>
      </div>

      <!-- Pages catalog list -->
      <div v-if="currentProject" class="flex-1 overflow-y-auto divide-y divide-slate-100 max-h-[180px] md:max-h-none text-left font-sans">
        <div 
          v-for="page in currentProject.pages" 
          :key="page.id"
          @click="selectedPageId = page.id"
          class="p-3 cursor-pointer hover:bg-slate-50/50 transition-all space-y-1 relative"
          :class="selectedPageId === page.id ? 'bg-sky-50/50 text-[#1890ff] border-r-4 border-r-[#1890ff]' : 'text-slate-700'"
        >
          <div class="flex items-center justify-between gap-2 overflow-hidden">
            
            <div v-if="isRenamingPageId === page.id" class="flex items-center gap-1 w-full" @click.stopPropagation>
              <input 
                v-model="renamePageInput"
                type="text"
                class="w-full bg-white border border-slate-300 rounded px-1 py-0.5 text-xs text-slate-800 outline-none"
                @keyup.enter="savePageRename(page.id)"
              />
              <button @click="savePageRename(page.id)" class="text-emerald-600 hover:text-emerald-700"><Check class="w-4 h-4" /></button>
            </div>

            <span v-else class="font-bold text-xs truncate flex-1 leading-relaxed">
              {{ page.name }}
            </span>

            <!-- Actions popovers -->
            <div v-if="isRenamingPageId !== page.id" class="flex items-center gap-1.5 shrink-0 opacity-0 hover:opacity-100 focus-within:opacity-100 transition-all">
              <button 
                @click.stop="startRenamePage(page.id, page.name)"
                class="text-xs text-slate-400 hover:text-slate-700" 
                title="重新命名"
              >
                <Edit class="w-3 h-3" />
              </button>
              <button 
                @click.stop="handleDuplicatePage(page)"
                class="text-xs text-slate-400 hover:text-slate-700" 
                title="复制图幅"
              >
                <Copy class="w-3 h-3" />
              </button>
              <button 
                @click.stop="handleDeletePage(page.id, page.name)"
                class="text-xs text-rose-400 hover:text-rose-600" 
                title="剔除图层"
              >
                <Trash2 class="w-3 h-3" />
              </button>
            </div>

          </div>

          <p class="text-[9px] font-mono text-slate-400">
            点件数: {{ page.components.length }} 个节点
          </p>
        </div>
      </div>

    </div>

    <!-- MAIN CO-WORKING VISUAL BUILDER WORKSPACE -->
    <div v-if="currentPage" class="flex-1 flex flex-col md:flex-row min-w-0">
      
      <!-- Center section: WidgetLibrary shelf (left nested) + Dragging HMI canvas (middle) -->
      <div class="flex-1 flex flex-col min-w-0 relative">
        
        <!-- Canvas bar toggler stats -->
        <div class="bg-white px-5 py-3 border-b border-slate-200 shadow-sm flex items-center justify-between shrink-0">
          <div class="flex items-center gap-2 text-left">
            <span class="w-2 h-2 rounded-full bg-amber-500 shadow-[0_0_6px_#f59e0b]" />
            <div>
              <h3 class="font-bold text-xs text-slate-800">当前活跃组态画布: <b class="text-slate-900 font-sans font-bold text-[13px] ml-1">{{ currentPage.name }}</b></h3>
              <p class="text-[10px] text-slate-400 font-mono">画布辨率: Responsive Canvas Floor (Grid alignment layout)</p>
            </div>
          </div>

          <div class="flex items-center gap-2">
            <!-- Mode switch toggle indicator -->
            <button 
              @click="isActiveMode = !isActiveMode"
              class="px-3.5 py-1 rounded-full text-xs font-bold inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 border"
              :class="isActiveMode ? 'bg-emerald-600 text-white border-emerald-600 shadow-[0_0_8px_rgba(16,185,129,0.3)]' : 'bg-slate-900 text-[#cbd5e1] border-slate-900'"
            >
              <Activity class="w-3.5 h-3.5" :class="isActiveMode ? 'animate-pulse' : ''" />
              {{ isActiveMode ? '监控中 (Run Mode / 运行)' : '设计中 (Edit Mode / 组态)' }}
            </button>
          </div>
        </div>

        <!-- HMI dragging field with Library -->
        <div class="flex-1 flex flex-col md:flex-row min-h-0">
          <!-- Widget Library -->
          <div v-if="!isActiveMode" class="w-60 bg-white border-r border-slate-200 shrink-0 hidden lg:block overflow-y-auto">
            <WidgetLibrary @addWidget="handleAddWidget" />
          </div>

          <!-- Sandbox Design canvas panel -->
          <div class="flex-1 bg-slate-900 relative overflow-hidden flex flex-col min-h-[350px] md:min-h-0">
            <CanvasPanel 
              :components="currentPage.components"
              :selectedId="selectedId"
              :isActiveMode="isActiveMode"
              :simulatedData="simulatedDataComputed"
              @selectComponent="selectedId = $event"
              @updateComponent="handleUpdateComponent"
              @toggleMode="isActiveMode = !isActiveMode"
              @triggerToggleValue="handleTriggerToggleValue"
              @deleteComponent="handleDeleteComponent"
              @duplicateComponent="handleDuplicateComponent"
              @clearCanvas="handleClearCanvas"
            />
          </div>
        </div>
      </div>

      <!-- Right section: Inspector Property Editor -->
      <div v-if="!isActiveMode && selectedId" class="w-full md:w-80 bg-white border-t md:border-t-0 md:border-l border-slate-200 overflow-y-auto shrink-0">
        <!-- Render Inspector Panel directly targeting chosen component -->
        <InspectorPanel 
          :selectedComponent="selectedCompObj"
          :plcTags="plcTagsList"
          @updateComponent="handleUpdateComponent"
        />
      </div>

    </div>

    <!-- MODAL: ADD SCADA PROJECT ENGINEERING -->
    <div v-if="showProjectModal" class="fixed inset-0 bg-slate-900/40 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white rounded-xl shadow-xl border border-slate-100 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 text-white p-4 flex items-center justify-between">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <FileCode class="w-4 h-4 text-[#1890ff]" />
            <span>注册全新组态大屏工程</span>
          </div>
          <button @click="showProjectModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div>
            <label class="text-slate-500 font-bold block mb-1">工程大项目名称</label>
            <input 
              v-model="newProjectName"
              type="text"
              placeholder="如: 食品车间3号流水线监控层"
              class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 font-sans focus:bg-white text-slate-900 focus:outline-none focus:border-[#1890ff]"
            />
          </div>
          <div>
            <label class="text-slate-500 font-bold block mb-1">工程概要指标描述</label>
            <textarea 
              v-model="newProjectDesc"
              rows="3"
              placeholder="概括说明本项目管控的PLC设备与遥测数据特征..."
              class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 font-sans focus:bg-white text-slate-900 focus:outline-none focus:border-[#1890ff] leading-relaxed"
            />
          </div>
        </div>

        <div class="bg-slate-50 p-3 flex justify-end gap-2 border-t border-slate-100">
          <button 
            @click="showProjectModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 bg-white hover:bg-slate-50 font-bold text-xs text-slate-600 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleCreateProject"
            class="px-4 py-1.5 rounded-lg bg-slate-900 hover:bg-slate-800 font-bold text-xs text-white cursor-pointer"
          >
            配置保存并初始化
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
