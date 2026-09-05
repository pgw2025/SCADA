<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue';
import { useRouter } from 'vue-router';
import { devices } from '../store/deviceStore';
import { areas } from '../store/areaStore';
import { dataModels, addLog, fetchDataModelsFromBackend } from '../store/index';
import {
  syncAreas,
  createAreaAndSync,
  updateAreaAndSync,
  deleteAreaAndSync,
  getAreaTree,
  getSubtreeDeviceIds,
  AreaFormData
} from '../services/areaService';
import {
  syncDevices,
  createDeviceAndSync,
  updateDeviceAndSync,
  deleteDeviceAndSync,
  setDeviceEnabledAndSync
} from '../services/deviceService';
import { fetchControllerOptions } from '../api/controllerApi';
import { fetchDeviceConnections, createDeviceConnection } from '../api/connectionApi';
import { fetchProtocols } from '../api/protocolApi';
import { startBackendPolling, stopBackendPolling } from '../services/pollService';
import {
  Device,
  Area,
  AreaTreeNode,
  ControllerOption,
  DeviceConnectionSummary,
  DeviceConnectionRequest,
  DeviceConnection,
  Protocol
} from '../types';

// Modular Child Components
import DeviceMetricsBar from './device/DeviceMetricsBar.vue';
import DeviceTopologySidebar from './device/DeviceTopologySidebar.vue';
import DeviceTableView from './device/DeviceTableView.vue';
import DeviceCardGrid from './device/DeviceCardGrid.vue';
import DeviceDetailDrawer from './device/DeviceDetailDrawer.vue';
import MobileAreaDrawer from './device/MobileAreaDrawer.vue';
import DeviceModal from './device/DeviceModal.vue';
import AreaModal from './device/AreaModal.vue';

// Icons
import {
  Plus,
  Search,
  LayoutGrid,
  List,
  FolderTree,
  Trash2,
  Play,
  Pause,
  RefreshCw,
  SlidersHorizontal,
  X
} from 'lucide-vue-next';

const router = useRouter();

// View Mode: 'table' or 'cards'
const viewMode = ref<'table' | 'cards'>('table');
// Topology sidebar collapsed state
const sidebarCollapsed = ref(false);

// Filter states
const searchKeyword = ref('');
const statusFilter = ref<'all' | 'online' | 'offline' | 'enabled' | 'disabled'>('all');
const selectedAreaId = ref<number | null>(null);
const includeSubareas = ref(false);
const subtreeDeviceIds = ref<Set<number>>(new Set());

// Multi-selection
const selectedDeviceIds = ref<Set<number>>(new Set());

// Drawer & Modal states
const showMobileAreaDrawer = ref(false);
const showDetailDrawer = ref(false);
const inspectedDevice = ref<Device | null>(null);

const showDeviceModal = ref(false);
const isEditingDevice = ref(false);
const editingDeviceId = ref<number | null>(null);
const deviceModalInitial = ref<any>({});
const deviceFormErrors = ref<Record<string, string>>({});
const deviceFormErrorMessage = ref('');

const showAreaModal = ref(false);
const isEditingArea = ref(false);
const editingAreaId = ref<number | null>(null);
const areaModalInitial = ref<AreaFormData>({
  name: '',
  description: '',
  parentId: null,
  code: '',
  areaType: 4,
  sort: 0,
  isEnabled: true
});
const areaFormErrors = ref<Record<string, string>>({});
const areaFormErrorMessage = ref('');

// Collection toggling ID for single device loading state
const togglingId = ref<number | null>(null);

// Controllers & Connections for Device Modal
const controllerOptions = ref<ControllerOption[]>([]);
const connectionSummaries = ref<DeviceConnectionSummary[]>([]);
const areaTree = ref<AreaTreeNode[]>([]);

onMounted(async () => {
  if (window.innerWidth < 1024) {
    viewMode.value = 'cards';
    sidebarCollapsed.value = true;
  }
  syncAreas();
  loadAreaTree();
  syncDevices();
  fetchDataModelsFromBackend();
  startBackendPolling();
  loadControllerAndConnectionData();
});

onUnmounted(() => {
  stopBackendPolling();
});

const loadAreaTree = async () => {
  areaTree.value = await getAreaTree();
};

const loadControllerAndConnectionData = async () => {
  try {
    controllerOptions.value = await fetchControllerOptions();
    const conns = await fetchDeviceConnections();
    connectionSummaries.value = conns.map((c: DeviceConnection) => ({
      id: c.id,
      controllerId: c.controllerId,
      controllerCode: c.controllerCode,
      controllerName: c.controllerName,
      protocolId: c.protocolId,
      protocolKey: c.protocolKey,
      protocolName: c.protocolName,
      host: c.host,
      port: c.port,
      timeoutMs: c.timeoutMs,
      reconnectIntervalMs: c.reconnectIntervalMs,
      isEnabled: c.isEnabled,
      updatedAt: c.updatedAt
    }));
  } catch (e) {
    console.error('Failed loading controllers/connections', e);
  }
};

// Subtree sync when area or includeSubareas changes
watch([selectedAreaId, includeSubareas], async ([aid, incl]) => {
  if (aid != null && incl) {
    const ids = await getSubtreeDeviceIds(aid);
    subtreeDeviceIds.value = new Set(ids);
  } else {
    subtreeDeviceIds.value = new Set();
  }
});

// Filtered Devices
const filteredDevices = computed(() => {
  return devices.value.filter(d => {
    // 1. Area filter
    if (selectedAreaId.value != null) {
      if (includeSubareas.value) {
        if (!subtreeDeviceIds.value.has(d.id)) return false;
      } else {
        if (d.areaId !== selectedAreaId.value) return false;
      }
    }

    // 2. Status filter
    if (statusFilter.value === 'online') {
      if (d.status !== 1 && d.status !== 'online') return false;
    } else if (statusFilter.value === 'offline') {
      if (d.status === 1 || d.status === 'online') return false;
    } else if (statusFilter.value === 'enabled') {
      if (!d.isEnabled) return false;
    } else if (statusFilter.value === 'disabled') {
      if (d.isEnabled) return false;
    }

    // 3. Search keyword
    if (searchKeyword.value.trim()) {
      const q = searchKeyword.value.trim().toLowerCase();
      const matchName = d.name.toLowerCase().includes(q);
      const matchKey = d.key.toLowerCase().includes(q);
      const matchIp = (d.ipAddress || '').toLowerCase().includes(q);
      const matchType = d.type.toLowerCase().includes(q);
      if (!matchName && !matchKey && !matchIp && !matchType) return false;
    }

    return true;
  });
});

// Selection handling
const toggleSelectDevice = (id: number) => {
  if (selectedDeviceIds.value.has(id)) {
    selectedDeviceIds.value.delete(id);
  } else {
    selectedDeviceIds.value.add(id);
  }
};

const toggleSelectAll = () => {
  const current = filteredDevices.value;
  const allIn = current.length > 0 && current.every(d => selectedDeviceIds.value.has(d.id));
  if (allIn) {
    current.forEach(d => selectedDeviceIds.value.delete(d.id));
  } else {
    current.forEach(d => selectedDeviceIds.value.add(d.id));
  }
};

const clearSelection = () => {
  selectedDeviceIds.value.clear();
};

// Batch Operations
const batchEnable = async () => {
  const ids = Array.from(selectedDeviceIds.value);
  for (const id of ids) {
    await setDeviceEnabledAndSync(id, true);
  }
  addLog('设备管理', `批量启用了 ${ids.length} 台设备的采集`, 'normal');
  clearSelection();
};

const batchDisable = async () => {
  if (!confirm(`确认停用选中的 ${selectedDeviceIds.value.size} 台设备采集？`)) return;
  const ids = Array.from(selectedDeviceIds.value);
  for (const id of ids) {
    await setDeviceEnabledAndSync(id, false);
  }
  addLog('设备管理', `批量停用了 ${ids.length} 台设备的采集`, 'warning');
  clearSelection();
};

const batchDelete = async () => {
  if (!confirm(`确认删除选中的 ${selectedDeviceIds.value.size} 台设备？此操作不可逆！`)) return;
  const ids = Array.from(selectedDeviceIds.value);
  for (const id of ids) {
    const d = devices.value.find(x => x.id === id);
    if (d) await deleteDeviceAndSync(id, d.name);
  }
  addLog('设备管理', `批量删除了 ${ids.length} 台设备`, 'warning');
  clearSelection();
};

// Single device actions
const handleToggleDeviceEnabled = async (device: Device) => {
  if (togglingId.value === device.id) return;
  const next = !device.isEnabled;
  if (!next && !confirm(`确认停用设备 [${device.name}] 的采集吗？`)) return;
  togglingId.value = device.id;
  try {
    await setDeviceEnabledAndSync(device.id, next);
  } finally {
    togglingId.value = null;
  }
};

const handleInspectDevice = (device: Device) => {
  inspectedDevice.value = device;
  showDetailDrawer.value = true;
};

const handleOpenVariables = (device: Device) => {
  router.push(`/device-variables?deviceId=${device.id}`);
};

// Device Modal handling
const openNewDeviceModal = () => {
  isEditingDevice.value = false;
  editingDeviceId.value = null;
  deviceFormErrors.value = {};
  deviceFormErrorMessage.value = '';
  deviceModalInitial.value = {
    name: '',
    key: '',
    areaId: selectedAreaId.value || areas.value[0]?.id || 1,
    modelId: dataModels.value[0]?.id || null,
    isEnabled: true
  };
  showDeviceModal.value = true;
};

const openEditDeviceModal = (device: Device) => {
  isEditingDevice.value = true;
  editingDeviceId.value = device.id;
  deviceFormErrors.value = {};
  deviceFormErrorMessage.value = '';
  deviceModalInitial.value = {
    name: device.name,
    key: device.key,
    areaId: device.areaId,
    modelId: device.modelId,
    models: device.models || [],
    controllerId: device.controllerId,
    connectionId: device.connectionId,
    protocolKey: device.protocolKey,
    type: device.type,
    ipAddress: device.ipAddress,
    port: device.port,
    endpointUrl: device.endpointUrl,
    cpuType: device.cpuType,
    rack: device.rack,
    slot: device.slot,
    isEnabled: device.isEnabled
  };
  showDeviceModal.value = true;
};

const handleSaveDevice = async (formData: any) => {
  deviceFormErrors.value = {};
  deviceFormErrorMessage.value = '';

  if (!formData.name?.trim()) {
    deviceFormErrors.value = { Name: '设备名称不能为空' };
    return;
  }

  let targetConnectionId = formData.connectionId;

  // Handle direct/quick mode connection creation if needed
  if (formData.connectionMode === 'quick' && !targetConnectionId) {
    try {
      const protoKey = formData.protocolKey || 'OPCUA';
      const protocols = await fetchProtocols();
      const matchedProto = protocols.find((p: Protocol) => p.key.toUpperCase() === protoKey.toUpperCase()) || protocols[0];
      const controllers = await fetchControllerOptions();
      const defaultCtrl = controllers[0];

      if (matchedProto && defaultCtrl) {
        let configJson = '{}';
        if (protoKey === 'OPCUA') {
          configJson = JSON.stringify({
            EndpointUrl: formData.endpointUrl || `opc.tcp://${formData.ipAddress || '127.0.0.1'}:${formData.port || 4840}`,
            SecurityPolicy: 'None'
          });
        } else if (protoKey === 'S7') {
          configJson = JSON.stringify({
            IpAddress: formData.ipAddress || '127.0.0.1',
            Port: Number(formData.port) || 102,
            Rack: Number(formData.rack) || 0,
            Slot: Number(formData.slot) || 1,
            CpuType: formData.cpuType || 'S71500'
          });
        } else if (protoKey === 'MODBUSTCP') {
          configJson = JSON.stringify({
            IpAddress: formData.ipAddress || '127.0.0.1',
            Port: Number(formData.port) || 502
          });
        }

        const connReq: DeviceConnectionRequest = {
          ControllerId: defaultCtrl.id,
          Name: `${formData.name} 连接`,
          ProtocolId: matchedProto.id,
          ConfigJson: configJson,
          TimeoutMs: 5000,
          ReconnectIntervalMs: 5000,
          IsEnabled: true
        };
        const resp = await createDeviceConnection(connReq);
        targetConnectionId = resp?.data?.id;
        formData.controllerId = defaultCtrl.id;
      }
    } catch (err: any) {
      deviceFormErrorMessage.value = err?.message || '自动创建通信连接失败';
      return;
    }
  }

  const payload = {
    name: formData.name,
    key: formData.key,
    areaId: formData.areaId,
    modelId: Number(formData.modelId) || 0,
    controllerId: formData.controllerId || null,
    connectionId: targetConnectionId || null
  };

  const result = isEditingDevice.value && editingDeviceId.value != null
    ? await updateDeviceAndSync(editingDeviceId.value, payload as any)
    : await createDeviceAndSync(payload as any);

  if (result.success) {
    addLog('设备管理', isEditingDevice.value ? `更新设备 [${formData.name}]` : `创建设备 [${formData.name}]`, 'normal');
    showDeviceModal.value = false;
    await syncDevices();
  } else if (result.error) {
    if (result.error.type === 'validation' && result.error.fieldErrors) {
      deviceFormErrors.value = result.error.fieldErrors;
    } else {
      deviceFormErrorMessage.value = result.error.message;
    }
  }
};

const handleDeleteDevice = async (device: Device) => {
  if (!confirm(`确认删除设备 [${device.name}] 吗？`)) return;
  const result = await deleteDeviceAndSync(device.id, device.name);
  if (result.success) {
    addLog('设备管理', `删除了设备 [${device.name}]`, 'warning');
    if (inspectedDevice.value?.id === device.id) {
      showDetailDrawer.value = false;
      inspectedDevice.value = null;
    }
  }
};

// Area modal handling
const openAddArea = (parentId?: number | null) => {
  isEditingArea.value = false;
  editingAreaId.value = null;
  areaFormErrors.value = {};
  areaFormErrorMessage.value = '';
  areaModalInitial.value = {
    name: '',
    description: '',
    parentId: parentId ?? null,
    code: '',
    areaType: 4,
    sort: 0,
    isEnabled: true
  };
  showAreaModal.value = true;
};

const openEditArea = (node: AreaTreeNode) => {
  isEditingArea.value = true;
  editingAreaId.value = node.id;
  areaFormErrors.value = {};
  areaFormErrorMessage.value = '';
  areaModalInitial.value = {
    name: node.name,
    description: node.description || '',
    parentId: node.parentId ?? null,
    code: node.code || '',
    areaType: node.areaType ?? 4,
    sort: node.sort ?? 0,
    isEnabled: node.isEnabled ?? true
  };
  showAreaModal.value = true;
};

const handleSaveArea = async (formData: AreaFormData) => {
  areaFormErrors.value = {};
  areaFormErrorMessage.value = '';

  if (!formData.name?.trim()) {
    areaFormErrors.value = { Name: '区域名称不能为空' };
    return;
  }

  const result = isEditingArea.value && editingAreaId.value != null
    ? await updateAreaAndSync(editingAreaId.value, formData)
    : await createAreaAndSync(formData);

  if (result.success) {
    addLog('设备管理', isEditingArea.value ? `更新区域 [${formData.name}]` : `添加区域 [${formData.name}]`, 'normal');
    showAreaModal.value = false;
    await loadAreaTree();
    await syncAreas();
  } else if (result.error) {
    if (result.error.type === 'validation' && result.error.fieldErrors) {
      areaFormErrors.value = result.error.fieldErrors;
    } else {
      areaFormErrorMessage.value = result.error.message;
    }
  }
};

const handleDeleteArea = async (node: AreaTreeNode) => {
  if (node.children && node.children.length > 0) {
    alert(`无法删除区域 [${node.name}]: 存在 ${node.children.length} 个子区域，请先清理子区域。`);
    return;
  }
  const count = devices.value.filter(d => d.areaId === node.id).length;
  if (count > 0) {
    alert(`无法删除区域 [${node.name}]: 部署了 ${count} 台设备，请先转移或移除设备。`);
    return;
  }
  if (!confirm(`确认删除工艺区域 [${node.name}]？`)) return;
  const result = await deleteAreaAndSync(node.id, node.name);
  if (result.success) {
    addLog('设备管理', `删除了工艺区域 [${node.name}]`, 'warning');
    await loadAreaTree();
    await syncAreas();
    if (selectedAreaId.value === node.id) selectedAreaId.value = null;
  }
};

const activeAreaLabel = computed(() => {
  if (selectedAreaId.value == null) return '全部工艺区域';
  const a = areas.value.find(x => x.id === selectedAreaId.value);
  return a ? a.name : '全部区域';
});
</script>

<template>
  <div
    class="h-full flex flex-col bg-slate-50/50 dark:bg-slate-950 text-slate-900 dark:text-slate-100 overflow-hidden select-none">
    <!-- Top Action Header -->
    <header
      class="px-4 sm:px-6 py-3.5 bg-white dark:bg-slate-900 border-b border-slate-200 dark:border-slate-800 flex flex-col sm:flex-row sm:items-center justify-between gap-3 shrink-0">
      <div class="flex items-center gap-2">
        <h1 class="text-base sm:text-lg font-bold text-slate-900 dark:text-white tracking-tight">
          设备管理工作台
        </h1>
        <span
          class="text-[11px] px-2 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-500 font-mono font-semibold">
          {{ devices.length }} 台设备
        </span>
      </div>

      <!-- Action Buttons -->
      <div class="flex items-center gap-2 self-end sm:self-auto">
        <!-- Mobile Area Filter Button (Visible on mobile/tablet) -->
        <button type="button" @click="showMobileAreaDrawer = true"
          class="lg:hidden flex items-center gap-1.5 px-3 py-2 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 text-slate-700 dark:text-slate-200 text-xs font-bold cursor-pointer">
          <FolderTree class="w-3.5 h-3.5 text-[#1890ff]" />
          <span class="truncate max-w-[100px]">{{ activeAreaLabel }}</span>
        </button>

        <button type="button" @click="syncDevices"
          class="p-2 rounded-xl border border-slate-200 dark:border-slate-800 text-slate-500 hover:text-slate-800 dark:hover:text-white cursor-pointer transition-colors"
          title="刷新设备数据">
          <RefreshCw class="w-4 h-4" />
        </button>

        <button type="button" @click="openNewDeviceModal"
          class="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl bg-[#1890ff] hover:bg-sky-600 text-white text-xs font-bold cursor-pointer shadow-xs transition-all hover:scale-102">
          <Plus class="w-4 h-4" />
          <span>接入设备</span>
        </button>
      </div>
    </header>

    <!-- Main Workspace Split-Pane -->
    <div class="flex-1 flex overflow-hidden">
      <!-- Left: Process Area Topology Sidebar (Desktop & Tablet) -->
      <DeviceTopologySidebar class="hidden lg:flex" :nodes="areaTree" :selectedId="selectedAreaId"
        :includeSubareas="includeSubareas" :totalAreas="areas.length" :collapsed="sidebarCollapsed"
        @select="id => selectedAreaId = id" @update:includeSubareas="val => includeSubareas = val"
        @update:collapsed="val => sidebarCollapsed = val" @addArea="openAddArea" @editArea="openEditArea"
        @deleteArea="handleDeleteArea" />

      <!-- Right: Main Workbench Area -->
      <main class="flex-1 flex flex-col min-w-0 overflow-hidden bg-slate-50/60 dark:bg-slate-950/60 p-3 sm:p-5 gap-3.5">
        <!-- Status Metrics Chips Bar -->
        <DeviceMetricsBar :devices="devices" :activeStatus="statusFilter"
          @update:activeStatus="s => statusFilter = s" />

        <!-- Workbench Controls Bar -->
        <div class="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-2.5">
          <!-- Search Input -->
          <div class="relative flex-1 max-w-md">
            <Search class="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
            <input v-model="searchKeyword" type="text" placeholder="搜索设备名称、Key、IP或协议类型..."
              class="w-full pl-9 pr-8 py-2 rounded-xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 text-xs text-slate-900 dark:text-white placeholder-slate-400 focus:outline-none focus:border-[#1890ff] focus:ring-1 focus:ring-[#1890ff]/20 transition-all shadow-2xs" />
            <button v-if="searchKeyword" type="button" @click="searchKeyword = ''"
              class="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">
              <X class="w-3.5 h-3.5" />
            </button>
          </div>

          <!-- Right: View Mode Toggle and Info -->
          <div class="flex items-center gap-2 self-end sm:self-auto shrink-0">
            <!-- Active Filter Badge if area is selected -->
            <div v-if="selectedAreaId !== null"
              class="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg bg-sky-50 dark:bg-sky-950/60 border border-sky-200 dark:border-sky-800 text-sky-700 dark:text-sky-300 text-[11px] font-bold">
              <span>{{ activeAreaLabel }}</span>
              <button type="button" @click="selectedAreaId = null" class="hover:text-sky-900">
                <X class="w-3 h-3" />
              </button>
            </div>

            <!-- View Mode Switcher -->
            <div
              class="flex items-center p-0.5 rounded-xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-2xs">
              <button type="button" @click="viewMode = 'table'"
                class="p-1.5 rounded-lg transition-colors cursor-pointer"
                :class="viewMode === 'table' ? 'bg-slate-100 dark:bg-slate-800 text-[#1890ff]' : 'text-slate-400 hover:text-slate-600'"
                title="高密度表格视图">
                <List class="w-4 h-4" />
              </button>
              <button type="button" @click="viewMode = 'cards'"
                class="p-1.5 rounded-lg transition-colors cursor-pointer"
                :class="viewMode === 'cards' ? 'bg-slate-100 dark:bg-slate-800 text-[#1890ff]' : 'text-slate-400 hover:text-slate-600'"
                title="卡片看板视图">
                <LayoutGrid class="w-4 h-4" />
              </button>
            </div>
          </div>
        </div>

        <!-- Batch Operations Bar (Conditionally shown when devices selected) -->
        <div v-if="selectedDeviceIds.size > 0"
          class="px-4 py-2 bg-slate-900 text-white rounded-xl flex items-center justify-between gap-3 text-xs shadow-md animate-in fade-in slide-in-from-top-2 duration-150 shrink-0">
          <div class="flex items-center gap-2">
            <span class="font-bold">已选择 {{ selectedDeviceIds.size }} 台设备</span>
            <button type="button" @click="clearSelection"
              class="text-slate-400 hover:text-white underline cursor-pointer">
              取消选择
            </button>
          </div>

          <div class="flex items-center gap-2">
            <button type="button" @click="batchEnable"
              class="px-3 py-1 rounded-lg bg-emerald-600 hover:bg-emerald-700 text-white font-bold cursor-pointer inline-flex items-center gap-1">
              <Play class="w-3 h-3" />
              <span>批量采集</span>
            </button>
            <button type="button" @click="batchDisable"
              class="px-3 py-1 rounded-lg bg-amber-600 hover:bg-amber-700 text-white font-bold cursor-pointer inline-flex items-center gap-1">
              <Pause class="w-3 h-3" />
              <span>批量停采</span>
            </button>
            <button type="button" @click="batchDelete"
              class="px-3 py-1 rounded-lg bg-rose-600 hover:bg-rose-700 text-white font-bold cursor-pointer inline-flex items-center gap-1">
              <Trash2 class="w-3 h-3" />
              <span>批量删除</span>
            </button>
          </div>
        </div>

        <!-- Devices Container (Table or Cards) -->
        <div class="flex-1 overflow-y-auto">
          <DeviceTableView v-if="viewMode === 'table'" :devices="filteredDevices" :selectedDeviceIds="selectedDeviceIds"
            :areas="areas" :dataModels="dataModels" :togglingId="togglingId" @toggleSelect="toggleSelectDevice"
            @toggleSelectAll="toggleSelectAll" @toggleEnabled="handleToggleDeviceEnabled" @inspect="handleInspectDevice"
            @edit="openEditDeviceModal" @delete="handleDeleteDevice" @variables="handleOpenVariables" />

          <DeviceCardGrid v-else :devices="filteredDevices" :selectedDeviceIds="selectedDeviceIds" :areas="areas"
            :dataModels="dataModels" :togglingId="togglingId" @toggleSelect="toggleSelectDevice"
            @toggleEnabled="handleToggleDeviceEnabled" @inspect="handleInspectDevice" @edit="openEditDeviceModal"
            @delete="handleDeleteDevice" @variables="handleOpenVariables" />
        </div>
      </main>
    </div>

    <!-- Slide-over Drawer for Device Details -->
    <DeviceDetailDrawer :open="showDetailDrawer" :device="inspectedDevice" :areas="areas" :dataModels="dataModels"
      @close="showDetailDrawer = false" @edit="openEditDeviceModal" @variables="handleOpenVariables"
      @toggleEnabled="handleToggleDeviceEnabled" />

    <!-- Mobile Area Drawer (Bottom Sheet) -->
    <MobileAreaDrawer :open="showMobileAreaDrawer" :nodes="areaTree" :selectedId="selectedAreaId"
      :includeSubareas="includeSubareas" @close="showMobileAreaDrawer = false" @select="id => selectedAreaId = id"
      @update:includeSubareas="val => includeSubareas = val" @addArea="openAddArea" @editArea="openEditArea"
      @deleteArea="handleDeleteArea" />

    <!-- Device Create / Edit Modal -->
    <DeviceModal :show="showDeviceModal" :isEditing="isEditingDevice" :areas="areas" :dataModels="dataModels"
      :controllers="controllerOptions" :connections="connectionSummaries" :initialData="deviceModalInitial"
      :errors="deviceFormErrors" :errorMessage="deviceFormErrorMessage" @close="showDeviceModal = false"
      @save="handleSaveDevice" />

    <!-- Area Create / Edit Modal -->
    <AreaModal :show="showAreaModal" :isEditing="isEditingArea" :editingId="editingAreaId" :areaTree="areaTree"
      :initialData="areaModalInitial" :errors="areaFormErrors" :errorMessage="areaFormErrorMessage"
      @close="showAreaModal = false" @save="handleSaveArea" />
  </div>
</template>
