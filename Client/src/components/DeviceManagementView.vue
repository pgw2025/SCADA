<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { devices } from '../store/deviceStore';
import { areas } from '../store/areaStore';
import { syncAreas, createAreaAndSync, deleteAreaAndSync } from '../services/areaService';
import { dataModels, addLog, fetchDataModelsFromBackend } from '../store/index';
import { syncDevices, createDeviceAndSync, updateDeviceAndSync, deleteDeviceAndSync } from '../services/deviceService';
import { startBackendPolling, stopBackendPolling } from '../services/pollService';

onMounted(() => {
  syncAreas();
  syncDevices();
  // 模型列表在 App.vue 启动时(登录前)拉取会因无 token 而 401,登录后不会重拉;
  // 这里兜底刷新,确保 dataModels 就绪,设备卡片的"数据模型"才能正确显示名称。
  fetchDataModelsFromBackend();
  startBackendPolling();
});

onUnmounted(() => {
  stopBackendPolling();
});
import { 
  Cpu, 
  MapPin, 
  Plus, 
  Trash2, 
  Edit3, 
  Braces, 
  Server, 
  Sliders, 
  X, 
  Check, 
  ToggleLeft, 
  Info 
} from 'lucide-vue-next';
import { Device, Area, DeviceType, protocolKeyToDeviceType } from '../types';
import { useRouter } from 'vue-router';

const router = useRouter();

// Area Form States
const showAreaModal = ref<boolean>(false);
const newAreaName = ref<string>('');
const newAreaDesc = ref<string>('');
const areaFormErrors = ref<Record<string, string>>({});
const areaFormErrorMessage = ref<string>('');

// Device Form States
const showDeviceModal = ref<boolean>(false);
const isEditingDevice = ref<boolean>(false);
const editingDeviceId = ref<number | null>(null);
const deviceFormErrors = ref<Record<string, string>>({});
const deviceFormErrorMessage = ref<string>('');

const devName = ref<string>('');
const devKey = ref<string>('');
const devArea = ref<number>(0);
const devModel = ref<string>('');
const devType = ref<DeviceType>('OPCUA');
const devIP = ref<string>('');
const devPort = ref<string>('');

const devStatus = ref<'online' | 'offline'>('online');

// S7-specific connection details
const devCpuType = ref<string>('S7-1205');
const devRack = ref<number>(0);
const devSlot = ref<number>(1);

// Virtual-specific config (matches backend VirtualConfig)
const devVirtualIntervalMs = ref<number>(1000);
const devVirtualRandomValues = ref<boolean>(true);

// Active view
const activeSection = ref<'list' | 'areas'>('list');

// Expanded areas state for collapsible panels
const expandedAreas = ref<Set<number>>(new Set());

// Computed: devices grouped by area
const devicesByArea = computed(() => {
  const grouped: Record<number, typeof devices.value> = {};
  devices.value.forEach(d => {
    if (!grouped[d.areaId]) {
      grouped[d.areaId] = [];
    }
    grouped[d.areaId].push(d);
  });
  return grouped;
});

// Toggle area expansion
const toggleArea = (areaId: number) => {
  if (expandedAreas.value.has(areaId)) {
    expandedAreas.value.delete(areaId);
  } else {
    expandedAreas.value.add(areaId);
  }
};

// Check if area is expanded
const isAreaExpanded = (areaId: number) => expandedAreas.value.has(areaId);

// Expand all areas
const expandAllAreas = () => {
  areas.value.forEach(a => expandedAreas.value.add(a.id));
};

// Collapse all areas
const collapseAllAreas = () => {
  expandedAreas.value.clear();
};

// Trigger add area
const handleAddArea = async () => {
  areaFormErrors.value = {};
  areaFormErrorMessage.value = '';

  if (!newAreaName.value.trim()) {
    areaFormErrors.value = { Name: '区域名称不能为空' };
    return;
  }

  const result = await createAreaAndSync({
    name: newAreaName.value,
    description: newAreaDesc.value
  });

  if (result.success) {
    addLog('设备管理', `添加新工艺区域: [${newAreaName.value}]`, 'normal');
    newAreaName.value = '';
    newAreaDesc.value = '';
    showAreaModal.value = false;
    areaFormErrors.value = {};
  } else if (result.error) {
    if (result.error.type === 'validation' && result.error.fieldErrors) {
      areaFormErrors.value = result.error.fieldErrors;
    } else {
      areaFormErrorMessage.value = result.error.message;
    }
  }
};

// Trigger delete area
const handleDeleteArea = async (id: number, name: string) => {
  const counts = devices.value.filter(d => d.areaId === id).length;
  if (counts > 0) {
    alert(`无法删除区域 [${name}]: 有 ${counts} 个处于连接中的工业设备已被部署在该区域内。`);
    return;
  }

  const result = await deleteAreaAndSync(id, name);
  if (result.success) {
    addLog('设备管理', `删除了工艺区域 [${name}]`, 'warning');
  }
  // 失败提示由 http 拦截器统一 Toast 弹出
};

// 初始化时从后端获取区域数据
onMounted(() => {
  syncAreas();
});

// Open Device modal
const openNewDeviceModal = () => {
  isEditingDevice.value = false;
  editingDeviceId.value = null;
  devName.value = '';
  devKey.value = `PLC-DEV-${Date.now().toString().slice(-4)}`;
  devArea.value = areas.value[0]?.id || 0;
  // 优先使用当前在下拉框中选中的数据模型,而不是列表中的第一个,
  // 否则当第一个模型为 OPCUA 类型时,即使选中虚拟设备也会错误显示 OPC UA 地址。
  const initialModel = dataModels.value.find(m => m.id === devModel.value) || dataModels.value[0];
  devModel.value = initialModel?.id || '';
  devType.value = initialModel ? protocolKeyToDeviceType(initialModel.protocolKey) : 'OPCUA';
  devIP.value = '192.168.1.100';
  devPort.value = '4840';
  devStatus.value = 'online';

  // S7 init
  devCpuType.value = 'S7-1200';
  devRack.value = 0;
  devSlot.value = 1;

  // Virtual init
  devVirtualIntervalMs.value = 1000;
  devVirtualRandomValues.value = true;

  // 依据当前选中模型类型,统一刷新协议相关默认值(IP/端口/虚拟参数等),
  // 避免上一个设备的残留值(如 OPC UA 的 4840 端口)污染本次初始化。
  onModelChange();

  showDeviceModal.value = true;
};

// Automatically adjust device protocol based on chosen Data Model
const onModelChange = () => {
  const model = dataModels.value.find(m => m.id === devModel.value);
  if (model) {
    // 协议真相源在 Protocol 实体，由 model.protocolKey 派生设备类型
    devType.value = protocolKeyToDeviceType(model.protocolKey);
    if (devType.value === 'OPCUA') {
      devPort.value = '4840';
      devIP.value = '192.168.1.10';
    } else if (devType.value === 'S7') {
      devPort.value = '102';
      devIP.value = '192.168.1.12';
      devCpuType.value = 'S7-1200';
      devRack.value = 0;
      devSlot.value = 1;
    } else if (devType.value === 'MQTT') {
    } else if (devType.value === 'Virtual') {
      devVirtualIntervalMs.value = 1000;
      devVirtualRandomValues.value = true;
    }
  }
};

const openEditDeviceModal = (device: Device) => {
  isEditingDevice.value = true;
  editingDeviceId.value = device.id;
  
  devName.value = device.name;
  devKey.value = device.key;
  devArea.value = Number(device.areaId);
  // dataModels[].id 为 string(后端 int 序列化后由 modelApi 统一 String 化),
  // device.modelId 为 number,须归一为 string 才能命中下拉框 option 与后续 find。
  devModel.value = String(device.modelId ?? '');
  devType.value = device.type;
  devIP.value = device.ipAddress || '';
  devPort.value = device.port || '';
  devStatus.value = device.status === 1 ? 'online' : 'offline';

  // S7 connections
  devCpuType.value = device.cpuType || 'S7-1200';
  devRack.value = device.rack !== undefined ? device.rack : 0;
  devSlot.value = device.slot !== undefined ? device.slot : 1;

  // Virtual config: 从 configJson 回填表单
  devVirtualIntervalMs.value = 1000;
  devVirtualRandomValues.value = true;
  const rawConfig = (device as any).configJson;
  if (typeof rawConfig === 'string' && rawConfig.trim()) {
    try {
      const parsed = JSON.parse(rawConfig);
      if (typeof parsed.IntervalMs === 'number') devVirtualIntervalMs.value = parsed.IntervalMs;
      if (typeof parsed.RandomValues === 'boolean') devVirtualRandomValues.value = parsed.RandomValues;
    } catch {
      // 旧配置格式不兼容,保留默认值
    }
  }

  showDeviceModal.value = true;
};

// 按设备类型构造后端 ConfigJson,字段命名与后端 *Config DTO 保持一致。
// 后端 DeviceAppService.ValidateConfigJson 会反序列化校验,字段缺失或类型不符会拒绝。
const buildConfigJson = (type: DeviceType): string => {
  switch (type) {
    case 'OPCUA':
      return JSON.stringify({
        EndpointUrl: `opc.tcp://${devIP.value || '127.0.0.1'}:${devPort.value || '4840'}`,
        SecurityPolicy: 'None'
      });
    case 'S7':
      return JSON.stringify({
        IpAddress: devIP.value || '127.0.0.1',
        Port: Number(devPort.value) || 102,
        Rack: Number(devRack.value) || 0,
        Slot: Number(devSlot.value) || 1,
        CpuType: devCpuType.value || 'S71500'
      });
    case 'MQTT':
      return JSON.stringify({
        Broker: devIP.value || '127.0.0.1',
        Port: Number(devPort.value) || 1883,
        Topic: '',
        ClientId: `scada-${Date.now()}`
      });
    case 'Virtual':
      return JSON.stringify({
        IntervalMs: Number(devVirtualIntervalMs.value) || 1000,
        RandomValues: devVirtualRandomValues.value
      });
    default:
      return '{}';
  }
};

const handleSaveDevice = async () => {
  addLog('调试', `开始保存设备: ${devName.value}`, 'normal');
  if (!devName.value.trim()) {
    addLog('调试', '校验失败: 名称为空', 'warning');
    return;
  }

  const chosenModel = dataModels.value.find(m => m.id === devModel.value);
  addLog('调试', `Chosen Model ID: ${devModel.value}, Found: ${!!chosenModel}`, 'normal');
  
  // Set default initial variables if adding new
  const initialVars: Record<string, any> = {};
  if (chosenModel) {
    chosenModel.variables.forEach((v) => {
      initialVars[v.key] = v.type === 'digital' ? false : v.min;
    });
  }

  const deviceData = {
    name: devName.value,
    key: devKey.value.trim(),
    areaId: devArea.value,
    modelId: devModel.value,
    // 协议由后端从 modelId 推导,前端不再提交 type
    ipAddress: devIP.value,
    port: devPort.value,
    status: devStatus.value === 'online' ? 1 : 0,
    cpuType: devCpuType.value,
    rack: Number(devRack.value),
    slot: Number(devSlot.value),
    // 协议由后端从 modelId 推导，前端按模型 protocolKey 派生类型构造 ConfigJson
    configJson: buildConfigJson(chosenModel ? protocolKeyToDeviceType(chosenModel.protocolKey) : devType.value)
  };

  deviceFormErrors.value = {};
  deviceFormErrorMessage.value = '';

  if (!devName.value.trim()) {
    deviceFormErrors.value = { Name: '设备名称不能为空' };
    return;
  }

  if (isEditingDevice.value && editingDeviceId.value) {
    // Edit existing
    const result = await updateDeviceAndSync(editingDeviceId.value, deviceData);
    if (result.success) {
      addLog('设备管理', `保存了工业设备配置 [${devName.value}]`, 'normal');
      showDeviceModal.value = false;
    } else if (result.error) {
      if (result.error.type === 'validation' && result.error.fieldErrors) {
        deviceFormErrors.value = result.error.fieldErrors;
      } else {
        deviceFormErrorMessage.value = result.error.message;
      }
    }
  } else {
    // Add new
    const result = await createDeviceAndSync(deviceData);
    if (result.success) {
      addLog('设备管理', `添加新网关通道: [${devName.value}] (${devType.value})`, 'normal');
      showDeviceModal.value = false;
    } else if (result.error) {
      if (result.error.type === 'validation' && result.error.fieldErrors) {
        deviceFormErrors.value = result.error.fieldErrors;
      } else {
        deviceFormErrorMessage.value = result.error.message;
      }
    }
  }

  if (!deviceFormErrorMessage.value && !Object.keys(deviceFormErrors.value).length) {
    addLog('调试', '模态框已关闭', 'normal');
  }
};

const handleDeleteDevice = async (id: number, name: string) => {
  const result = await deleteDeviceAndSync(id, name);
  if (result.success) {
    addLog('设备管理', `删除了工业网络网关 [${name}]`, 'warning');
  }
  // 失败提示由 http 拦截器统一 Toast 弹出
};

const toggleDeviceStateInGrid = (device: Device) => {
  device.status = device.status === 'online' ? 'offline' : 'online';
  addLog(
    '设备管理', 
    `双位开关改写: [${device.name}] 已被迫切换为 ${device.status === 'online' ? '联机(Online)' : '脱机(Offline)'}`,
    device.status === 'online' ? 'normal' : 'warning'
  );
};
</script>

<template>
  <div class="h-full overflow-y-auto space-y-6 text-[#1e293b] dark:text-slate-100 select-none p-4 sm:p-6 bg-slate-50/50 dark:bg-transparent">
    
    <!-- Header panel with tab switches -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between border-b border-slate-200 dark:border-slate-800 pb-5 gap-4 text-left">
      <div>
        <h1 class="text-xl font-bold font-sans text-slate-900 dark:text-white tracking-tight">设备管理</h1>
        <p class="text-xs text-slate-500 dark:text-slate-400 mt-1">
          管理设备、区域及通信配置
        </p>
      </div>

      <!-- Option tags -->
      <div class="flex items-center gap-2">
        <button 
          @click="activeSection = 'list'"
          class="px-4 py-1.5 rounded-lg text-xs font-bold border cursor-pointer select-none transition-all"
          :class="activeSection === 'list' ? 'bg-slate-900 dark:bg-sky-600 text-white border-slate-900 dark:border-sky-600 shadow-sm' : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-400 hover:bg-slate-50 dark:hover:bg-slate-800 border-slate-200 dark:border-slate-700'"
        >
          设备列表
        </button>
        <button 
          @click="activeSection = 'areas'"
          class="px-4 py-1.5 rounded-lg text-xs font-bold border cursor-pointer select-none transition-all"
          :class="activeSection === 'areas' ? 'bg-slate-900 dark:bg-sky-600 text-white border-slate-900 dark:border-sky-600 shadow-sm' : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-400 hover:bg-slate-50 dark:hover:bg-slate-800 border-slate-200 dark:border-slate-700'"
        >
          区域管理 ({{ areas.length }})
        </button>
      </div>
    </div>

    <!-- 1. SECTION: DEVICES LIST VIEW -->
    <div v-if="activeSection === 'list'" class="space-y-4">
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-3">
          <h3 class="text-xs font-bold tracking-widest uppercase text-slate-500 dark:text-slate-400">
            分组设备 ({{ devices.length }} 台)
          </h3>
          <div class="flex items-center gap-1">
            <button 
              @click="expandAllAreas"
              class="text-[10px] text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 font-bold px-2 py-0.5 rounded border border-slate-200 dark:border-slate-700 hover:border-slate-300 transition-all cursor-pointer"
            >
              全部展开
            </button>
            <button 
              @click="collapseAllAreas"
              class="text-[10px] text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 font-bold px-2 py-0.5 rounded border border-slate-200 dark:border-slate-700 hover:border-slate-300 transition-all cursor-pointer"
            >
              全部折叠
            </button>
          </div>
        </div>
        
        <button 
          @click="openNewDeviceModal"
          class="bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 shadow-sm"
        >
          <Plus class="w-4 h-4" />
          添加设备
        </button>
      </div>

      <!-- Devices grouped by area with collapsible panels -->
      <div class="space-y-3">
        <div v-for="area in areas" :key="area.id" class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm transition-colors">
          <!-- Area header (clickable to expand/collapse) -->
          <div 
            @click="toggleArea(area.id)"
            class="flex items-center justify-between px-4 py-3 bg-slate-50 dark:bg-slate-950/60 cursor-pointer hover:bg-slate-100 dark:hover:bg-slate-800/60 transition-all border-b border-slate-100 dark:border-slate-800"
          >
            <div class="flex items-center gap-3">
              <div 
                class="w-5 h-5 rounded flex items-center justify-center text-[10px] font-bold transition-all"
                :class="isAreaExpanded(area.id) ? 'bg-[#1890ff] text-white rotate-90' : 'bg-slate-300 dark:bg-slate-700 text-white'"
              >
                ▶
              </div>
              <span class="text-[11px] font-bold text-slate-600 dark:text-slate-300 uppercase tracking-wider">{{ area.name }}</span>
              <span class="bg-sky-100 dark:bg-sky-950/60 text-[#1890ff] dark:text-sky-400 font-bold px-2 py-0.5 rounded-full text-[10px]">
                {{ devicesByArea[area.id]?.length || 0 }} 台
              </span>
            </div>
            <div class="flex items-center gap-4 text-[10px] text-slate-400 dark:text-slate-500">
              <span v-if="devicesByArea[area.id]?.length === 0" class="italic">暂无设备</span>
              <span class="font-mono">ID: {{ area.id }}</span>
            </div>
          </div>
          
          <!-- Device cards (shown when expanded) -->
          <div v-if="isAreaExpanded(area.id)" class="p-4">
            <div v-if="devicesByArea[area.id]?.length === 0" class="text-center py-8 text-slate-400 dark:text-slate-500 text-xs">
              <Cpu class="w-8 h-8 mx-auto mb-2 opacity-30" />
              <span>该区域暂无设备，请点击"添加设备"创建</span>
            </div>
            <div v-else class="grid grid-cols-1 xl:grid-cols-2 gap-3">
              <div 
                v-for="d in devicesByArea[area.id]" 
                :key="d.id"
                class="bg-white dark:bg-slate-950/60 border border-slate-100 dark:border-slate-800 rounded-lg p-4 text-left flex flex-col justify-between hover:shadow-md transition-all relative overflow-hidden"
              >
                <!-- Status indicator -->
                <div 
                  class="absolute top-0 left-0 right-0 h-1"
                  :class="d.status === 1 ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-700'"
                />

          <div class="flex items-start justify-between gap-4 mt-1">
            <div class="space-y-1">
              <div class="flex items-center gap-1.5">
                <span class="text-[9px] font-mono font-bold bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 px-1.5 py-0.5 rounded uppercase">
                  {{ d.type }}
                </span>
                <span class="text-xs text-slate-400 dark:text-slate-500 font-mono">KEY: {{ d.key }}</span>
              </div>
              <h4 class="font-bold text-sm text-slate-900 dark:text-white font-sans mt-1.5 leading-snug">
                {{ d.name }}
              </h4>
            </div>

            <!-- Online toggler slider button -->
            <button 
              @click="toggleDeviceStateInGrid(d)"
              class="text-[10px] font-bold px-2 py-1 rounded-full flex items-center gap-1 border transition-all cursor-pointer"
              :class="d.status === 1 ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-800' : 'bg-slate-50 dark:bg-slate-800 text-slate-400 dark:text-slate-400 border-slate-200 dark:border-slate-700'">
              <div class="w-1.5 h-1.5 rounded-full" :class="d.status === 1 ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'" />
              {{ d.status === 1 ? '在线' : '离线' }}
            </button>
          </div>

          <!-- Mid: Address properties -->
          <div class="grid grid-cols-2 gap-x-4 gap-y-1.5 py-3 border-t border-b border-slate-100/80 dark:border-slate-800/80 mt-4 text-[11px] font-mono">
            <div>
              <span class="text-slate-400 dark:text-slate-500">所属区域:</span>
              <span class="text-slate-800 dark:text-slate-200 font-sans font-medium block">
                {{ areas.find(a => a.id === d.areaId)?.name || '未选择' }}
              </span>
            </div>
            <div>
              <span class="text-slate-400 dark:text-slate-500">数据模型:</span>
              <span class="text-[#1890ff] dark:text-sky-400 font-sans font-medium block">
                {{ dataModels.find(m => String(m.id) === String(d.modelId))?.name || '未配置' }}
              </span>
            </div>
            <div class="col-span-2 space-y-1">
              <span class="text-slate-400 dark:text-slate-500">连接地址:</span>
              <div class="text-slate-700 dark:text-slate-300 font-bold block truncate leading-relaxed">
                <template v-if="d.type === 'OPCUA'">
                  <span class="text-sky-600 dark:text-sky-400 font-bold">OPCUA:</span> opc.tcp://{{ d.ipAddress || '127.0.0.1' }}:{{ d.port || '4840' }}
                </template>
                <template v-else-if="d.type === 'S7'">
                  <span class="text-indigo-600 dark:text-indigo-400 font-bold">S7 Link:</span> {{ d.ipAddress || '192.168.1.12' }}:{{ d.port || '102' }} 
                  <span class="bg-indigo-50 dark:bg-indigo-950/60 text-indigo-700 dark:text-indigo-300 border border-indigo-150 dark:border-indigo-800 px-1 rounded text-[10px] font-normal font-sans ml-1.5">
                    {{ d.cpuType || 'S7-1200' }} (R{{ d.rack || 0 }}/S{{ d.slot || 1 }})
                  </span>
                </template>
                <template v-else-if="d.type === 'MQTT'">
                  <div class="text-xs text-slate-800 dark:text-slate-200 break-all">
                    <span class="text-emerald-600 dark:text-emerald-400 font-bold">MQTT Device</span>
                  </div>
                </template>
                <template v-else>
                  <span>Local Bus Simulation:</span> 宿主机虚拟工业网关
                </template>
              </div>
            </div>
          </div>

          <!-- Bottom: action edits -->
          <div class="flex items-center justify-between mt-3 text-[11px]">
            <span class="text-slate-400 dark:text-slate-500">最后更新: <b class="font-mono text-slate-600 dark:text-slate-300">{{ d.lastUpdated }}</b></span>
            
            <div class="flex items-center gap-2">
              <button 
                @click="openEditDeviceModal(d)"
                class="text-[#1890ff] dark:text-sky-400 hover:text-sky-600 font-bold inline-flex items-center gap-0.5 cursor-pointer"
              >
                <Edit3 class="w-3.5 h-3.5" />
                编辑
              </button>
              <button 
                @click="router.push(`/device-variables?deviceId=${d.id}`)"
                class="text-emerald-600 dark:text-emerald-400 hover:text-emerald-700 font-bold inline-flex items-center gap-0.5 cursor-pointer ml-1"
                :title="`管理设备 ${d.name} 的变量实例`"
              >
                <Braces class="w-3.5 h-3.5" />
                变量
              </button>
              <button 
                @click="handleDeleteDevice(d.id, d.name)"
                class="text-rose-500 hover:text-rose-700 font-bold inline-flex items-center gap-0.5 cursor-pointer ml-1"
              >
                <Trash2 class="w-3.5 h-3.5" />
                删除
              </button>
            </div>
          </div>
        </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 2. SECTION: AREAS CONFIGURATION LIST -->
    <div v-else-if="activeSection === 'areas'" class="space-y-4">
      <div class="flex items-center justify-between">
        <h3 class="text-xs font-bold tracking-widest uppercase text-slate-500 dark:text-slate-400">
          所有区域
        </h3>
        
        <button 
          @click="showAreaModal = true"
          class="bg-slate-900 dark:bg-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all text-center"
        >
          <Plus class="w-4 h-4" />
          添加区域
        </button>
      </div>

      <!-- Area table card -->
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm text-left transition-colors">
        <table class="w-full text-xs hover:border-collapse">
          <thead>
            <tr class="bg-slate-50 dark:bg-slate-950/60 ring-1 ring-slate-100 dark:ring-slate-800 uppercase text-[10px] text-slate-400 dark:text-slate-500 font-bold tracking-wider">
              <th class="px-6 py-4">区域ID</th>
              <th class="px-6 py-4">区域名称</th>
              <th class="px-6 py-4">描述</th>
              <th class="px-6 py-4">设备数量</th>
              <th class="px-6 py-4 text-right">操作</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100 dark:divide-slate-800 font-mono">
            <tr v-for="a in areas" :key="a.id" class="hover:bg-slate-50/50 dark:hover:bg-slate-800/40 transition-all">
              <td class="px-6 py-4 font-bold text-slate-500 dark:text-slate-400">{{ a.id }}</td>
              <td class="px-6 py-4 font-sans font-bold text-slate-800 dark:text-white text-[13px]">{{ a.name }}</td>
              <td class="px-6 py-4 font-sans text-slate-500 dark:text-slate-400 text-[11px] leading-relaxed max-w-sm">{{ a.description }}</td>
              <td class="px-6 py-4 text-center">
                <span class="bg-sky-50 dark:bg-sky-950/60 font-sans text-[#1890ff] dark:text-sky-400 font-bold px-2 py-0.5 rounded-full text-[10px]">
                  {{ devices.filter(d => d.areaId === a.id).length }} 台
                </span>
              </td>
              <td class="px-6 py-4 text-right">
                <button 
                  @click="handleDeleteArea(a.id, a.name)"
                  class="text-rose-500 hover:text-rose-700 cursor-pointer font-sans font-bold inline-flex items-center gap-0.5"
                >
                  <Trash2 class="w-3.5 h-3.5" />
                  删除
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- MODAL: CREATE WORKSPACE AREA -->
    <div v-if="showAreaModal" class="fixed inset-0 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <MapPin class="w-4 h-4 text-sky-400" />
            <span>添加区域</span>
          </div>
          <button @click="showAreaModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div v-if="areaFormErrorMessage" class="bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 rounded-lg p-3 text-rose-600 dark:text-rose-400">
            {{ areaFormErrorMessage }}
          </div>
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">区域名称</label>
            <input
              v-model="newAreaName"
              type="text"
              placeholder="例如: 智能三级精细沉降池"
              :class="areaFormErrors.Name ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200 dark:border-slate-700 focus:border-[#1890ff]'"
              class="w-full bg-slate-50 dark:bg-slate-950 border rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
            />
            <span v-if="areaFormErrors.Name" class="text-rose-500 text-[10px] mt-1 block">{{ areaFormErrors.Name }}</span>
          </div>
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">描述</label>
            <textarea
              v-model="newAreaDesc"
              rows="3"
              placeholder="阐述本区域所属流程及测温、变频流水分拣的具体物料方向..."
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] leading-relaxed"
            />
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800">
          <button
            @click="showAreaModal = false; areaFormErrors = {}; areaFormErrorMessage = ''"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleAddArea"
            class="px-4 py-1.5 rounded-lg bg-slate-900 dark:bg-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white cursor-pointer"
          >
            保存
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: ADD / EDIT DEVICE COMM LINK -->
    <div v-if="showDeviceModal" class="fixed inset-0 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        
        <!-- Header banner -->
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Cpu class="w-4 h-4 text-[#1890ff]" />
            <span>{{ isEditingDevice ? '编辑设备' : '添加设备' }}</span>
          </div>
          <button @click="showDeviceModal = false; deviceFormErrors = {}; deviceFormErrorMessage = ''" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs overflow-y-auto max-h-[450px]">
          <div v-if="deviceFormErrorMessage" class="bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 rounded-lg p-3 text-rose-600 dark:text-rose-400">
            {{ deviceFormErrorMessage }}
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">设备名称</label>
              <input 
                v-model="devName"
                type="text"
                placeholder="例如: 3号二次鼓风风机"
                :class="deviceFormErrors.Name ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200 dark:border-slate-700 focus:border-[#1890ff]'"
                class="w-full bg-slate-50 dark:bg-slate-950 border rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none text-xs font-sans font-semibold"
              />
              <span v-if="deviceFormErrors.Name" class="text-rose-500 text-[10px] mt-1 block">{{ deviceFormErrors.Name }}</span>
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">设备编号</label>
              <input 
                v-model="devKey"
                type="text"
                placeholder="例如: S7-BLR-202"
                :class="deviceFormErrors.Key ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200 dark:border-slate-700 focus:border-[#1890ff]'"
                class="w-full bg-slate-50 dark:bg-slate-950 border rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none text-xs font-mono font-bold uppercase"
              />
              <span v-if="deviceFormErrors.Key" class="text-rose-500 text-[10px] mt-1 block">{{ deviceFormErrors.Key }}</span>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">所属区域</label>
              <select 
                v-model="devArea"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
              >
                <option v-for="a in areas" :key="a.id" :value="a.id">{{ a.name }}</option>
              </select>
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">数据模型</label>
              <select 
                v-model="devModel"
                @change="onModelChange"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none text-[#1890ff] dark:text-sky-400 font-bold"
              >
                <option v-for="m in dataModels" :key="m.id" :value="m.id">{{ m.name }}</option>
              </select>
            </div>
          </div>

          <!-- Dynamic settings based on Protocol -->
          <div class="p-3 bg-slate-50 dark:bg-slate-950/70 rounded-xl space-y-3 border border-slate-100 dark:border-slate-800">
            <div class="flex items-center gap-1.5 text-slate-400 dark:text-slate-400 font-mono scale-95 origin-left">
              <Info class="w-3.5 h-3.5" />
              <span>协议类型: {{ devType }}</span>
            </div>

            <!-- OPCUA Connection Setup -->
            <div v-if="devType === 'OPCUA'" class="space-y-2">
              <div class="text-[10px] text-[#1890ff] dark:text-sky-400 font-bold uppercase tracking-wider mb-1">OPC UA 连接配置</div>
              <div class="grid grid-cols-3 gap-2">
                <div class="col-span-2">
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">IP 地址</label>
                  <input 
                    v-model="devIP"
                    type="text"
                    placeholder="e.g. 192.168.1.100"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800 dark:text-white"
                  />
                </div>
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">端口</label>
                  <input 
                    v-model="devPort"
                    type="text"
                    placeholder="e.g. 4840"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800 dark:text-white"
                  />
                </div>
              </div>
            </div>

            <!-- Virtual Device Setup -->
            <div v-if="devType === 'Virtual'" class="space-y-2">
              <div class="text-[10px] text-amber-600 dark:text-amber-400 font-bold uppercase tracking-wider mb-1">虚拟设备配置</div>
              <div class="text-[11px] text-slate-500 dark:text-slate-400 leading-relaxed">
                虚拟设备不发起网络通信,根据变量 Min/Max 范围生成随机模拟值,用于无硬件环境下的联调测试。
              </div>
              <div class="grid grid-cols-2 gap-2">
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">生成间隔 (ms)</label>
                  <input 
                    v-model.number="devVirtualIntervalMs"
                    type="number"
                    min="10"
                    step="100"
                    placeholder="e.g. 1000"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800 dark:text-white"
                  />
                </div>
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">随机模式</label>
                  <label class="flex items-center gap-1.5 h-[30px] text-xs font-bold text-slate-700 dark:text-slate-300 cursor-pointer">
                    <input
                      type="checkbox"
                      v-model="devVirtualRandomValues"
                      class="text-amber-500 focus:ring-0"
                    />
                    启用随机值
                  </label>
                </div>
              </div>
            </div>

            <!-- Siemens S7 Connection Setup -->
            <div v-if="devType === 'S7'" class="space-y-3">
              <div class="text-[10px] text-indigo-500 dark:text-indigo-400 font-bold uppercase tracking-wider mb-1">S7 连接配置</div>
              
              <div class="grid grid-cols-3 gap-2">
                <div class="col-span-2">
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">IP 地址</label>
                  <input 
                    v-model="devIP"
                    type="text"
                    placeholder="e.g. 192.168.1.12"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800 dark:text-white"
                  />
                </div>
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">端口</label>
                  <input 
                    v-model="devPort"
                    type="text"
                    placeholder="e.g. 102"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800 dark:text-white"
                  />
                </div>
              </div>

              <div class="grid grid-cols-3 gap-2">
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">CPU 型号</label>
                  <select 
                    v-model="devCpuType"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1.5 focus:outline-none text-[11px] font-bold text-slate-700 dark:text-slate-200"
                  >
                    <option value="S7-1200">S7-1200</option>
                    <option value="S7-1500">S7-1500</option>
                    <option value="S7-300">S7-300</option>
                    <option value="S7-400">S7-400</option>
                  </select>
                </div>
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">机架</label>
                  <input 
                    v-model="devRack"
                    type="number"
                    min="0"
                    max="10"
                    :class="deviceFormErrors.Rack ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200 dark:border-slate-700'"
                    class="w-full bg-white dark:bg-slate-900 border rounded px-2 py-1 focus:outline-none text-xs font-mono text-slate-850 dark:text-white"
                  />
                  <span v-if="deviceFormErrors.Rack" class="text-rose-500 text-[10px] mt-1 block">{{ deviceFormErrors.Rack }}</span>
                </div>
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">插槽</label>
                  <input 
                    v-model="devSlot"
                    type="number"
                    min="0"
                    max="10"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs font-mono text-slate-850 dark:text-white"
                  />
                </div>
              </div>
            </div>

            <!-- MQTT Router Configuration -->
            <div v-if="devType === 'MQTT'" class="space-y-2.5">
              <div class="text-[10px] text-emerald-500 dark:text-emerald-400 font-bold uppercase tracking-wider mb-1">MQTT 连接配置</div>
            </div>
          </div>

          <!-- Device state radio selection -->
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">连接状态</label>
            <div class="flex items-center gap-4 py-1">
              <label class="flex items-center gap-1.5 font-bold text-slate-700 dark:text-slate-300 cursor-pointer text-xs">
                <input type="radio" value="online" v-model="devStatus" class="text-emerald-500 focus:ring-0" />
                在线
              </label>
              <label class="flex items-center gap-1.5 font-bold text-slate-400 dark:text-slate-500 cursor-pointer text-xs">
                <input type="radio" value="offline" v-model="devStatus" class="text-rose-500 focus:ring-0" />
                离线
              </label>
            </div>
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-4 border-t border-slate-100 dark:border-slate-800 flex justify-end gap-2">
          <button 
            @click="showDeviceModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleSaveDevice"
            class="px-4 py-1.5 rounded-lg bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white cursor-pointer"
          >
            保存
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
