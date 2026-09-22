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
  setDeviceEnabledAndSync,
  batchSetDeviceEnabledAndSync,
  precheckBatchSetDeviceEnabled
} from '../services/deviceService';
import { fetchControllerOptions } from '../api/controllerApi';
import { fetchDeviceConnections } from '../api/connectionApi';
import { startBackendPolling, stopBackendPolling } from '../services/pollService';
import {
  Device,
  Area,
  AreaTreeNode,
  ControllerOption,
  DeviceConnectionSummary,
  DeviceConnection
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
  X,
  LayoutList,
  AlignJustify,
  CheckSquare,
  Square,
  Loader2,
  Eye,
  Braces,
  Edit3,
  ChevronRight
} from 'lucide-vue-next';

const router = useRouter();

// View Mode: 'table' or 'cards' (Desktop)
const viewMode = ref<'table' | 'cards'>('table');

// Mobile View Mode: 'card' or 'compact' (方案一: 卡片 / 方案二: 紧凑)
const mobileDeviceViewMode = ref<'card' | 'compact'>(
  (localStorage.getItem('scada_device_mobile_view') as 'card' | 'compact') || 'card'
);
const setMobileDeviceViewMode = (mode: 'card' | 'compact') => {
  mobileDeviceViewMode.value = mode;
  localStorage.setItem('scada_device_mobile_view', mode);
};

const getAreaName = (areaId?: number) => {
  if (!areaId) return '未指定';
  return areas.value.find(a => a.id === areaId)?.name || '未指定';
};

const formatEndpoint = (d: Device): string => {
  if (d.connection) {
    const parts = [d.connection.protocolName || d.type];
    if (d.connection.host) parts.push(`${d.connection.host}${d.connection.port ? ':' + d.connection.port : ''}`);
    return parts.join(' · ');
  }
  if (d.type === 'OPCUA') return d.endpointUrl || 'opc.tcp://127.0.0.1:4840';
  if (d.type === 'S7') return `${d.ipAddress || '127.0.0.1'}:${d.port || 102} (${d.cpuType || 'S7'})`;
  if (d.type === 'MQTT') return 'MQTT Broker';
  return '宿主机虚拟工业总线';
};

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

/** 批量启用/停用的统一入口：启用先预检，执行后展示汇总；覆盖"表格多选"与"区域树"两个入口。 */
const runBatchSetEnabled = async (req: { areaId?: number; includeSubAreas?: boolean; deviceIds?: number[]; enabled: boolean }) => {
  // 启用：先预检，把"可启动数 + 阻塞清单"在点击前告诉用户。
  if (req.enabled) {
    const precheck = await precheckBatchSetDeviceEnabled(req);
    if (!precheck.success || !precheck.data) {
      alert(precheck.error?.message || '预检失败，请稍后重试');
      return;
    }
    const { total, startable, blocked } = precheck.data;
    if (blocked.length > 0) {
      const lines = blocked.slice(0, 5).map(b => `• ${b.name}：${b.reason}`).join('\n');
      const more = blocked.length > 5 ? `\n… 等共 ${blocked.length} 台被阻塞` : '';
      const msg = `共 ${total} 台设备：${startable} 台可启动，${blocked.length} 台因地址未配置被阻塞。\n\n${lines}${more}\n\n仅启动可启动的 ${startable} 台？`;
      if (!confirm(msg)) return;
    } else if (!confirm(`确认启用 ${total} 台设备的采集？`)) {
      return;
    }
  }

  const result = await batchSetDeviceEnabledAndSync({ ...req, skipInvalid: true });
  if (!result.success || !result.data) {
    alert(`批量操作失败：${result.error?.message || '未知错误'}`);
    return;
  }
  showBatchResult(result.data);
  clearSelection();
};

/** 批量结果汇总（成功/跳过/失败 + 失败明细）。 */
const showBatchResult = (data: { succeeded: number; skipped: number; failed: number; items: Array<{ name?: string | null; result: string; reason?: string | null }> }) => {
  let msg = `完成：成功 ${data.succeeded} 台，跳过 ${data.skipped} 台，失败 ${data.failed} 台。`;
  const failedItems = (data.items || []).filter(i => i.result === 'Failed');
  if (failedItems.length > 0) {
    const lines = failedItems.slice(0, 8).map(i => `• ${i.name}：${i.reason}`).join('\n');
    const more = failedItems.length > 8 ? `\n… 等共 ${failedItems.length} 台失败` : '';
    msg += `\n\n失败明细：\n${lines}${more}`;
  }
  alert(msg);
};

const batchEnable = async () => {
  const ids = Array.from(selectedDeviceIds.value);
  if (ids.length === 0) return;
  await runBatchSetEnabled({ deviceIds: ids, enabled: true });
};

const batchDisable = async () => {
  if (!confirm(`确认停用选中的 ${selectedDeviceIds.value.size} 台设备采集？`)) return;
  const ids = Array.from(selectedDeviceIds.value);
  if (ids.length === 0) return;
  await runBatchSetEnabled({ deviceIds: ids, enabled: false });
};

/** 区域树节点批量启停入口（DeviceTopologySidebar / MobileAreaDrawer 触发）。 */
const handleBatchToggleArea = async (node: AreaTreeNode, enabled: boolean) => {
  await runBatchSetEnabled({ areaId: node.id, includeSubAreas: includeSubareas.value, enabled });
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

  const payload = {
    name: formData.name,
    key: formData.key,
    areaId: formData.areaId,
    modelId: Number(formData.modelId) || 0,
    controllerId: formData.controllerId || null,
    connectionId: formData.connectionId || null
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
  <div class="h-full flex flex-col bg-slate-50/50 dark:bg-slate-950 text-slate-900 dark:text-slate-100 overflow-hidden select-none">
    <!-- Top Action Header -->
    <header class="px-4 sm:px-6 py-3.5 bg-white dark:bg-slate-900 border-b border-slate-200 dark:border-slate-800 flex flex-col sm:flex-row sm:items-center justify-between gap-3 shrink-0">
      <div class="flex items-center gap-2">
        <h1 class="text-base sm:text-lg font-bold text-slate-900 dark:text-white tracking-tight">
          设备管理工作台
        </h1>
        <span class="text-[11px] px-2 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-500 font-mono font-semibold">
          {{ devices.length }} 台设备
        </span>
      </div>

      <!-- Action Buttons -->
      <div class="flex items-center gap-2 self-end sm:self-auto">
        <!-- Mobile Area Filter Button (Visible on mobile/tablet) -->
        <button
          type="button"
          @click="showMobileAreaDrawer = true"
          class="lg:hidden flex items-center gap-1.5 px-3 py-2 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 text-slate-700 dark:text-slate-200 text-xs font-bold cursor-pointer"
        >
          <FolderTree class="w-3.5 h-3.5 text-[#1890ff]" />
          <span class="truncate max-w-[100px]">{{ activeAreaLabel }}</span>
        </button>

        <button
          type="button"
          @click="syncDevices"
          class="p-2 rounded-xl border border-slate-200 dark:border-slate-800 text-slate-500 hover:text-slate-800 dark:hover:text-white cursor-pointer transition-colors"
          title="刷新设备数据"
        >
          <RefreshCw class="w-4 h-4" />
        </button>

        <button
          type="button"
          @click="openNewDeviceModal"
          class="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl bg-[#1890ff] hover:bg-sky-600 text-white text-xs font-bold cursor-pointer shadow-xs transition-all hover:scale-102"
        >
          <Plus class="w-4 h-4" />
          <span>接入设备</span>
        </button>
      </div>
    </header>

    <!-- Main Workspace Split-Pane -->
    <div class="flex-1 flex overflow-hidden">
      <!-- Left: Process Area Topology Sidebar (Desktop & Tablet) -->
      <DeviceTopologySidebar
        class="hidden lg:flex"
        :nodes="areaTree"
        :selectedId="selectedAreaId"
        :includeSubareas="includeSubareas"
        :totalAreas="areas.length"
        :collapsed="sidebarCollapsed"
        @select="id => selectedAreaId = id"
        @update:includeSubareas="val => includeSubareas = val"
        @update:collapsed="val => sidebarCollapsed = val"
        @addArea="openAddArea"
        @editArea="openEditArea"
        @deleteArea="handleDeleteArea"
        @batchToggle="handleBatchToggleArea"
      />

      <!-- Right: Main Workbench Area -->
      <main class="flex-1 flex flex-col min-w-0 overflow-hidden bg-slate-50/60 dark:bg-slate-950/60 p-3 sm:p-5 gap-3.5">
        <!-- Status Metrics Chips Bar -->
        <DeviceMetricsBar
          :devices="devices"
          :activeStatus="statusFilter"
          @update:activeStatus="s => statusFilter = s"
        />

        <!-- Workbench Controls Bar -->
        <div class="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-2.5">
          <!-- Search Input -->
          <div class="relative flex-1 max-w-md">
            <Search class="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
            <input
              v-model="searchKeyword"
              type="text"
              placeholder="搜索设备名称、Key、IP或协议类型..."
              class="w-full pl-9 pr-8 py-2 rounded-xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 text-xs text-slate-900 dark:text-white placeholder-slate-400 focus:outline-none focus:border-[#1890ff] focus:ring-1 focus:ring-[#1890ff]/20 transition-all shadow-2xs"
            />
            <button
              v-if="searchKeyword"
              type="button"
              @click="searchKeyword = ''"
              class="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
            >
              <X class="w-3.5 h-3.5" />
            </button>
          </div>

          <!-- Right: View Mode Toggle and Info -->
          <div class="flex items-center gap-2 self-end sm:self-auto shrink-0">
            <!-- Active Filter Badge if area is selected -->
            <div
              v-if="selectedAreaId !== null"
              class="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg bg-sky-50 dark:bg-sky-950/60 border border-sky-200 dark:border-sky-800 text-sky-700 dark:text-sky-300 text-[11px] font-bold"
            >
              <span>{{ activeAreaLabel }}</span>
              <button type="button" @click="selectedAreaId = null" class="hover:text-sky-900">
                <X class="w-3 h-3" />
              </button>
            </div>

            <!-- View Mode Switcher: Desktop (Table vs Cards) -->
            <div class="hidden md:flex items-center p-0.5 rounded-xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-2xs">
              <button
                type="button"
                @click="viewMode = 'table'"
                class="p-1.5 rounded-lg transition-colors cursor-pointer"
                :class="viewMode === 'table' ? 'bg-slate-100 dark:bg-slate-800 text-[#1890ff]' : 'text-slate-400 hover:text-slate-600'"
                title="高密度表格视图"
              >
                <List class="w-4 h-4" />
              </button>
              <button
                type="button"
                @click="viewMode = 'cards'"
                class="p-1.5 rounded-lg transition-colors cursor-pointer"
                :class="viewMode === 'cards' ? 'bg-slate-100 dark:bg-slate-800 text-[#1890ff]' : 'text-slate-400 hover:text-slate-600'"
                title="卡片看板视图"
              >
                <LayoutGrid class="w-4 h-4" />
              </button>
            </div>

            <!-- View Mode Switcher: Mobile (方案一卡片 vs 方案二紧凑) -->
            <div class="flex md:hidden items-center p-0.5 rounded-xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-2xs">
              <button
                type="button"
                @click="setMobileDeviceViewMode('card')"
                class="flex items-center gap-1 px-2 py-1 text-xs font-bold rounded-lg transition-all cursor-pointer"
                :class="mobileDeviceViewMode === 'card' ? 'bg-slate-100 dark:bg-slate-800 text-[#1890ff]' : 'text-slate-400 hover:text-slate-600'"
                title="方案一：卡片模式"
              >
                <LayoutList class="w-3.5 h-3.5" />
                <span>卡片</span>
              </button>
              <button
                type="button"
                @click="setMobileDeviceViewMode('compact')"
                class="flex items-center gap-1 px-2 py-1 text-xs font-bold rounded-lg transition-all cursor-pointer"
                :class="mobileDeviceViewMode === 'compact' ? 'bg-slate-100 dark:bg-slate-800 text-[#1890ff]' : 'text-slate-400 hover:text-slate-600'"
                title="方案二：紧凑列表模式"
              >
                <AlignJustify class="w-3.5 h-3.5" />
                <span>紧凑</span>
              </button>
            </div>
          </div>
        </div>

        <!-- Batch Operations Bar (Conditionally shown when devices selected) -->
        <div
          v-if="selectedDeviceIds.size > 0"
          class="px-4 py-2 bg-slate-900 text-white rounded-xl flex items-center justify-between gap-3 text-xs shadow-md animate-in fade-in slide-in-from-top-2 duration-150 shrink-0"
        >
          <div class="flex items-center gap-2">
            <span class="font-bold">已选择 {{ selectedDeviceIds.size }} 台设备</span>
            <button type="button" @click="clearSelection" class="text-slate-400 hover:text-white underline cursor-pointer">
              取消选择
            </button>
          </div>

          <div class="flex items-center gap-2">
            <button
              type="button"
              @click="batchEnable"
              class="px-3 py-1 rounded-lg bg-emerald-600 hover:bg-emerald-700 text-white font-bold cursor-pointer inline-flex items-center gap-1"
            >
              <Play class="w-3 h-3" />
              <span>批量采集</span>
            </button>
            <button
              type="button"
              @click="batchDisable"
              class="px-3 py-1 rounded-lg bg-amber-600 hover:bg-amber-700 text-white font-bold cursor-pointer inline-flex items-center gap-1"
            >
              <Pause class="w-3 h-3" />
              <span>批量停采</span>
            </button>
            <button
              type="button"
              @click="batchDelete"
              class="px-3 py-1 rounded-lg bg-rose-600 hover:bg-rose-700 text-white font-bold cursor-pointer inline-flex items-center gap-1"
            >
              <Trash2 class="w-3 h-3" />
              <span>批量删除</span>
            </button>
          </div>
        </div>

        <!-- Desktop Devices Container (Table or Cards) -->
        <div class="hidden md:block flex-1 overflow-y-auto">
          <DeviceTableView
            v-if="viewMode === 'table'"
            :devices="filteredDevices"
            :selectedDeviceIds="selectedDeviceIds"
            :areas="areas"
            :dataModels="dataModels"
            :togglingId="togglingId"
            @toggleSelect="toggleSelectDevice"
            @toggleSelectAll="toggleSelectAll"
            @toggleEnabled="handleToggleDeviceEnabled"
            @inspect="handleInspectDevice"
            @edit="openEditDeviceModal"
            @delete="handleDeleteDevice"
            @variables="handleOpenVariables"
          />

          <DeviceCardGrid
            v-else
            :devices="filteredDevices"
            :selectedDeviceIds="selectedDeviceIds"
            :areas="areas"
            :dataModels="dataModels"
            :togglingId="togglingId"
            @toggleSelect="toggleSelectDevice"
            @toggleEnabled="handleToggleDeviceEnabled"
            @inspect="handleInspectDevice"
            @edit="openEditDeviceModal"
            @delete="handleDeleteDevice"
            @variables="handleOpenVariables"
          />
        </div>

        <!-- Mobile Devices Container (Dual-mode: 方案一卡片 / 方案二紧凑列表) -->
        <div class="block md:hidden flex-1 overflow-y-auto">
          <div v-if="filteredDevices.length === 0" class="p-8 text-center text-xs text-slate-400">
            暂无匹配的设备数据
          </div>

          <!-- 方案一：移动端卡片模式 (Card Mode) -->
          <div v-else-if="mobileDeviceViewMode === 'card'" class="space-y-3 pb-4">
            <div
              v-for="d in filteredDevices"
              :key="d.id"
              class="bg-white dark:bg-slate-900 border rounded-xl p-3.5 space-y-3 transition-all text-left shadow-2xs relative"
              :class="selectedDeviceIds.has(d.id)
                ? 'border-[#1890ff] ring-1 ring-[#1890ff]/30'
                : 'border-slate-200 dark:border-slate-800'"
            >
              <!-- 顶部状态栏与名称 -->
              <div class="flex items-start justify-between gap-2.5">
                <div class="flex items-center gap-2.5 min-w-0 flex-1">
                  <!-- 多选框 -->
                  <button
                    type="button"
                    @click.stop="toggleSelectDevice(d.id)"
                    class="text-slate-400 hover:text-[#1890ff] transition-colors cursor-pointer shrink-0"
                  >
                    <CheckSquare v-if="selectedDeviceIds.has(d.id)" class="w-4 h-4 text-[#1890ff]" />
                    <Square v-else class="w-4 h-4" />
                  </button>

                  <div class="min-w-0 flex-1">
                    <div class="flex items-center gap-1.5 flex-wrap">
                      <span class="font-bold text-sm text-slate-900 dark:text-white truncate">{{ d.name }}</span>
                      <!-- 状态指示胶囊 -->
                      <span
                        class="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-full text-[10px] font-bold"
                        :class="d.status === 1 || d.status === 'online'
                          ? 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400 border border-emerald-200 dark:border-emerald-800'
                          : 'bg-slate-100 dark:bg-slate-800 text-slate-400 border border-slate-200 dark:border-slate-700'"
                      >
                        <span class="w-1.5 h-1.5 rounded-full" :class="d.status === 1 || d.status === 'online' ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'" />
                        {{ d.status === 1 || d.status === 'online' ? '在线' : '离线' }}
                      </span>
                    </div>
                    <div class="text-[11px] font-mono text-slate-400 truncate mt-0.5">KEY: {{ d.key }}</div>
                  </div>
                </div>

                <!-- 采集开关切换 -->
                <button
                  type="button"
                  @click.stop="handleToggleDeviceEnabled(d)"
                  :disabled="togglingId === d.id"
                  class="px-2.5 py-1 rounded-lg text-xs font-bold inline-flex items-center gap-1 cursor-pointer shrink-0 transition-colors shadow-2xs"
                  :class="d.isEnabled
                    ? 'bg-sky-50 dark:bg-sky-950/60 text-[#1890ff] border border-sky-200 dark:border-sky-800 hover:bg-sky-100'
                    : 'bg-slate-100 dark:bg-slate-800 text-slate-500 border border-slate-200 dark:border-slate-700 hover:bg-slate-200'"
                >
                  <Loader2 v-if="togglingId === d.id" class="w-3 h-3 animate-spin" />
                  <span>{{ d.isEnabled ? '采集中' : '已停采' }}</span>
                </button>
              </div>

              <!-- 参数信息块 -->
              <div class="grid grid-cols-2 gap-2 text-[11px] bg-slate-50 dark:bg-slate-950/60 p-2.5 rounded-xl border border-slate-100 dark:border-slate-800/80">
                <div>
                  <span class="text-slate-400 block text-[10px]">通讯协议</span>
                  <span class="font-mono font-bold text-sky-600 dark:text-sky-400 uppercase">{{ d.type }}</span>
                </div>
                <div>
                  <span class="text-slate-400 block text-[10px]">所属区域</span>
                  <span class="font-medium text-slate-700 dark:text-slate-300 truncate block">{{ getAreaName(d.areaId) }}</span>
                </div>
                <div class="col-span-2">
                  <span class="text-slate-400 block text-[10px]">通讯端点 / IP</span>
                  <span class="font-mono text-slate-600 dark:text-slate-400 truncate block">{{ formatEndpoint(d) }}</span>
                </div>
              </div>

              <!-- 操作按钮行 -->
              <div class="pt-2 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between gap-1 text-xs">
                <button
                  type="button"
                  @click.stop="handleInspectDevice(d)"
                  class="px-2.5 py-1.5 rounded-lg text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 font-bold text-[11px] inline-flex items-center gap-1 cursor-pointer"
                >
                  <Eye class="w-3.5 h-3.5 text-slate-400" />
                  <span>详情</span>
                </button>

                <div class="flex items-center gap-1.5">
                  <button
                    type="button"
                    @click.stop="handleOpenVariables(d)"
                    class="px-2.5 py-1.5 rounded-lg bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800 font-bold text-[11px] inline-flex items-center gap-1 cursor-pointer"
                  >
                    <Braces class="w-3.5 h-3.5" />
                    <span>变量</span>
                  </button>
                  <button
                    type="button"
                    @click.stop="openEditDeviceModal(d)"
                    class="p-1.5 rounded-lg text-slate-500 hover:text-slate-700 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
                    title="编辑"
                  >
                    <Edit3 class="w-3.5 h-3.5" />
                  </button>
                  <button
                    type="button"
                    @click.stop="handleDeleteDevice(d)"
                    class="p-1.5 rounded-lg text-rose-500 hover:text-rose-700 hover:bg-rose-50 dark:hover:bg-rose-950/60 cursor-pointer"
                    title="删除"
                  >
                    <Trash2 class="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>
            </div>
          </div>

          <!-- 方案二：移动端高密度紧凑列表模式 (Compact Mode) -->
          <div v-else-if="mobileDeviceViewMode === 'compact'" class="divide-y divide-slate-100 dark:divide-slate-800 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 overflow-hidden text-left shadow-2xs pb-2">
            <div
              v-for="d in filteredDevices"
              :key="d.id + '_compact'"
              @click="handleInspectDevice(d)"
              class="px-3.5 py-2.5 flex items-center gap-3 hover:bg-slate-50 dark:hover:bg-slate-800/40 active:bg-slate-100 dark:active:bg-slate-800 transition-colors cursor-pointer"
            >
              <!-- 状态呼吸灯圆点 -->
              <div class="relative flex items-center justify-center shrink-0">
                <span v-if="d.status === 1 || d.status === 'online'" class="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-ping absolute opacity-75" />
                <span class="w-2.5 h-2.5 rounded-full" :class="d.status === 1 || d.status === 'online' ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-600'" />
              </div>

              <!-- 核心信息 -->
              <div class="flex-1 min-w-0">
                <div class="flex items-center gap-1.5">
                  <span class="font-bold text-xs text-slate-800 dark:text-white truncate font-sans">{{ d.name }}</span>
                  <span class="px-1 py-0.2 rounded text-[9px] font-mono font-bold bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 border border-sky-200 dark:border-sky-800 shrink-0 uppercase">
                    {{ d.type }}
                  </span>
                </div>
                <div class="text-[10px] text-slate-400 dark:text-slate-500 font-mono truncate mt-0.5">
                  <span>{{ d.key }}</span>
                  <span class="mx-1">·</span>
                  <span>{{ getAreaName(d.areaId) }}</span>
                </div>
              </div>

              <!-- 状态与采集标徽 -->
              <div class="shrink-0 flex items-center gap-2">
                <span
                  class="text-[10px] font-bold px-1.5 py-0.5 rounded font-mono"
                  :class="d.isEnabled
                    ? 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400'
                    : 'bg-slate-100 dark:bg-slate-800 text-slate-400'"
                >
                  {{ d.isEnabled ? '采集中' : '停采' }}
                </span>
                <ChevronRight class="w-3.5 h-3.5 text-slate-300 dark:text-slate-600" />
              </div>
            </div>
          </div>
        </div>
      </main>
    </div>

    <!-- Slide-over Drawer for Device Details -->
    <DeviceDetailDrawer
      :open="showDetailDrawer"
      :device="inspectedDevice"
      :areas="areas"
      :dataModels="dataModels"
      @close="showDetailDrawer = false"
      @edit="openEditDeviceModal"
      @variables="handleOpenVariables"
      @toggleEnabled="handleToggleDeviceEnabled"
    />

    <!-- Mobile Area Drawer (Bottom Sheet) -->
    <MobileAreaDrawer
      :open="showMobileAreaDrawer"
      :nodes="areaTree"
      :selectedId="selectedAreaId"
      :includeSubareas="includeSubareas"
      @close="showMobileAreaDrawer = false"
      @select="id => selectedAreaId = id"
      @update:includeSubareas="val => includeSubareas = val"
      @addArea="openAddArea"
      @editArea="openEditArea"
      @deleteArea="handleDeleteArea"
      @batchToggle="handleBatchToggleArea"
    />

    <!-- Device Create / Edit Modal -->
    <DeviceModal
      :show="showDeviceModal"
      :isEditing="isEditingDevice"
      :areas="areas"
      :dataModels="dataModels"
      :controllers="controllerOptions"
      :connections="connectionSummaries"
      :initialData="deviceModalInitial"
      :errors="deviceFormErrors"
      :errorMessage="deviceFormErrorMessage"
      @close="showDeviceModal = false"
      @save="handleSaveDevice"
    />

    <!-- Area Create / Edit Modal -->
    <AreaModal
      :show="showAreaModal"
      :isEditing="isEditingArea"
      :editingId="editingAreaId"
      :areaTree="areaTree"
      :initialData="areaModalInitial"
      :errors="areaFormErrors"
      :errorMessage="areaFormErrorMessage"
      @close="showAreaModal = false"
      @save="handleSaveArea"
    />
  </div>
</template>
