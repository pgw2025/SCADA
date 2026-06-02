<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { devices } from '../store/deviceStore';
import { areas } from '../store/areaStore';
import { syncAreas, createAreaAndSync, deleteAreaAndSync } from '../services/areaService';
import { dataModels, addLog } from '../store/index';
import { syncDevices, createDeviceAndSync, updateDeviceAndSync, deleteDeviceAndSync } from '../services/deviceService';
import { startBackendPolling, stopBackendPolling } from '../services/pollService';

onMounted(() => {
  syncAreas();
  syncDevices();
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
  Server, 
  Sliders, 
  X, 
  Check, 
  ToggleLeft, 
  Info 
} from 'lucide-vue-next';
import { Device, Area, DeviceType } from '../types';

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

// Active view
const activeSection = ref<'list' | 'areas'>('list');

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
  } else if (result.error) {
    alert(result.error.message);
  }
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
  devModel.value = dataModels.value[0]?.id || '';
  devType.value = dataModels.value[0]?.type || 'OPCUA';
  devIP.value = '192.168.1.100';
  devPort.value = '4840';
  devStatus.value = 'online';

  // S7 init
  devCpuType.value = 'S7-1200';
  devRack.value = 0;
  devSlot.value = 1;
  
  showDeviceModal.value = true;
};

// Automatically adjust device protocol based on chosen Data Model
const onModelChange = () => {
  const model = dataModels.value.find(m => m.id === devModel.value);
  if (model) {
    devType.value = model.type;
    if (model.type === 'OPCUA') {
      devPort.value = '4840';
      devIP.value = '192.168.1.10';
    } else if (model.type === 'S7') {
      devPort.value = '102';
      devIP.value = '192.168.1.12';
      devCpuType.value = 'S7-1200';
      devRack.value = 0;
      devSlot.value = 1;
    } else if (model.type === 'MQTT') {
    }
  }
};

const openEditDeviceModal = (device: Device) => {
  isEditingDevice.value = true;
  editingDeviceId.value = device.id;
  
  devName.value = device.name;
  devKey.value = device.key;
  devArea.value = Number(device.areaId);
  devModel.value = device.modelId;
  devType.value = device.type;
  devIP.value = device.ipAddress || '';
  devPort.value = device.port || '';
  devStatus.value = device.status === 1 ? 'online' : 'offline';

  // S7 connections
  devCpuType.value = device.cpuType || 'S7-1200';
  devRack.value = device.rack !== undefined ? device.rack : 0;
  devSlot.value = device.slot !== undefined ? device.slot : 1;

  showDeviceModal.value = true;
};

const handleSaveDevice = async () => {
  addLog('调试', `开始保存设备: ${devName.value}`, 'normal');
  if (!devName.value.trim() || !devKey.value.trim()) {
    addLog('调试', '校验失败: 名称或编码为空', 'warning');
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
    key: devKey.value,
    areaId: devArea.value,
    modelId: devModel.value,
    type: devType.value,
    ipAddress: devIP.value,
    port: devPort.value,
    status: devStatus.value === 'online' ? 1 : 0,
    cpuType: devCpuType.value,
    rack: Number(devRack.value),
    slot: Number(devSlot.value)
  };

  deviceFormErrors.value = {};
  deviceFormErrorMessage.value = '';

  if (!devName.value.trim()) {
    deviceFormErrors.value = { Name: '设备名称不能为空' };
    return;
  }

  if (!devKey.value.trim()) {
    deviceFormErrors.value = { Key: '设备编号不能为空' };
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
  } else if (result.error) {
    alert(result.error.message);
  }
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
  <div class="h-full overflow-y-auto space-y-6 text-[#1e293b] select-none p-4 sm:p-6 bg-slate-50/50">
    
    <!-- Header panel with tab switches -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between border-b border-slate-200 pb-5 gap-4 text-left">
      <div>
        <h1 class="text-xl font-bold font-sans text-slate-900 tracking-tight">设备管理</h1>
        <p class="text-xs text-slate-500 mt-1">
          管理设备、区域及通信配置
        </p>
      </div>

      <!-- Option tags -->
      <div class="flex items-center gap-2">
        <button 
          @click="activeSection = 'list'"
          class="px-4 py-1.5 rounded-lg text-xs font-bold border cursor-pointer select-none"
          :class="activeSection === 'list' ? 'bg-slate-900 text-white border-slate-900 shadow-sm' : 'bg-white text-slate-600 hover:bg-slate-50 border-slate-200'"
        >
          设备列表
        </button>
        <button 
          @click="activeSection = 'areas'"
          class="px-4 py-1.5 rounded-lg text-xs font-bold border cursor-pointer select-none"
          :class="activeSection === 'areas' ? 'bg-slate-900 text-white border-slate-900 shadow-sm' : 'bg-white text-slate-600 hover:bg-slate-50 border-slate-200'"
        >
          区域管理 ({{ areas.length }})
        </button>
      </div>
    </div>

    <!-- 1. SECTION: DEVICES LIST VIEW -->
    <div v-if="activeSection === 'list'" class="space-y-4">
      <div class="flex items-center justify-between">
        <h3 class="text-xs font-bold tracking-widest uppercase text-slate-500">
          所有设备 ({{ devices.length }})
        </h3>
        
        <button 
          @click="openNewDeviceModal"
          class="bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 shadow-sm"
        >
          <Plus class="w-4 h-4" />
          添加设备
        </button>
      </div>

      <!-- Grid of Devices -->
      <div class="grid grid-cols-1 xl:grid-cols-2 gap-4">
        <div 
          v-for="d in devices" 
          :key="d.id"
          class="bg-white border border-slate-200/80 rounded-xl p-5 shadow-sm text-left flex flex-col justify-between hover:shadow-md transition-all relative overflow-hidden"
        >
          <!-- Accent strip based on status -->
          <div 
            class="absolute top-0 left-0 right-0 h-1.5"
            :class="d.status === 1 ? 'bg-emerald-500' : 'bg-slate-300'"
          />

          <div class="flex items-start justify-between gap-4 mt-1">
            <div class="space-y-1">
              <div class="flex items-center gap-1.5">
                <span class="text-[9px] font-mono font-bold bg-slate-100 text-slate-600 px-1.5 py-0.5 rounded uppercase">
                  {{ d.type }}
                </span>
                <span class="text-xs text-slate-400 font-mono">KEY: {{ d.key }}</span>
              </div>
              <h4 class="font-bold text-sm text-slate-900 font-sans mt-1.5 leading-snug">
                {{ d.name }}
              </h4>
            </div>

            <!-- Online toggler slider button -->
            <button 
              @click="toggleDeviceStateInGrid(d)"
              class="text-[10px] font-bold px-2 py-1 rounded-full flex items-center gap-1 border transition-all cursor-pointer"
              :class="d.status === 1 ? 'bg-emerald-50 text-emerald-600 border-emerald-200' : 'bg-slate-50 text-slate-400 border-slate-200'">
              <div class="w-1.5 h-1.5 rounded-full" :class="d.status === 1 ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'" />
              {{ d.status === 1 ? '在线' : '离线' }}
            </button>
          </div>

          <!-- Mid: Address properties -->
          <div class="grid grid-cols-2 gap-x-4 gap-y-1.5 py-3 border-t border-b border-slate-100/80 mt-4 text-[11px] font-mono">
            <div>
              <span class="text-slate-400">所属区域:</span>
              <span class="text-slate-800 font-sans font-medium block">
                {{ areas.find(a => a.id === d.areaId)?.name || '未选择' }}
              </span>
            </div>
            <div>
              <span class="text-slate-400">数据模型:</span>
              <span class="text-[#1890ff] font-sans font-medium block">
                {{ dataModels.find(m => m.id === d.modelId)?.name || '未配置' }}
              </span>
            </div>
            <div class="col-span-2 space-y-1">
              <span class="text-slate-400">连接地址:</span>
              <div class="text-slate-700 font-bold block truncate leading-relaxed">
                <template v-if="d.type === 'OPCUA'">
                  <span class="text-sky-600 font-bold">OPCUA:</span> opc.tcp://{{ d.ipAddress || '127.0.0.1' }}:{{ d.port || '4840' }}
                </template>
                <template v-else-if="d.type === 'S7'">
                  <span class="text-indigo-600 font-bold">S7 Link:</span> {{ d.ipAddress || '192.168.1.12' }}:{{ d.port || '102' }} 
                  <span class="bg-indigo-50 text-indigo-700 border border-indigo-150 px-1 rounded text-[10px] font-normal font-sans ml-1.5">
                    {{ d.cpuType || 'S7-1200' }} (R{{ d.rack || 0 }}/S{{ d.slot || 1 }})
                  </span>
                </template>
                <template v-else-if="d.type === 'MQTT'">
                  <div class="text-xs text-slate-800 break-all">
                    <span class="text-emerald-600 font-bold">MQTT Device</span>
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
            <span class="text-slate-400">最后更新: <b class="font-mono text-slate-600">{{ d.lastUpdated }}</b></span>
            
            <div class="flex items-center gap-2">
              <button 
                @click="openEditDeviceModal(d)"
                class="text-[#1890ff] hover:text-sky-600 font-bold inline-flex items-center gap-0.5 cursor-pointer"
              >
                <Edit3 class="w-3.5 h-3.5" />
                编辑
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

    <!-- 2. SECTION: AREAS CONFIGURATION LIST -->
    <div v-else-if="activeSection === 'areas'" class="space-y-4">
      <div class="flex items-center justify-between">
        <h3 class="text-xs font-bold tracking-widest uppercase text-slate-500">
          所有区域
        </h3>
        
        <button 
          @click="showAreaModal = true"
          class="bg-slate-900 hover:bg-slate-800 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all text-center"
        >
          <Plus class="w-4 h-4" />
          添加区域
        </button>
      </div>

      <!-- Area table card -->
      <div class="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm text-left">
        <table class="w-full text-xs hover:border-collapse">
          <thead>
            <tr class="bg-slate-50 ring-1 ring-slate-100 uppercase text-[10px] text-slate-400 font-bold tracking-wider">
              <th class="px-6 py-4">区域ID</th>
              <th class="px-6 py-4">区域名称</th>
              <th class="px-6 py-4">描述</th>
              <th class="px-6 py-4">设备数量</th>
              <th class="px-6 py-4 text-right">操作</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100 font-mono">
            <tr v-for="a in areas" :key="a.id" class="hover:bg-slate-50/50 transition-all">
              <td class="px-6 py-4 font-bold text-slate-500">{{ a.id }}</td>
              <td class="px-6 py-4 font-sans font-bold text-slate-800 text-[13px]">{{ a.name }}</td>
              <td class="px-6 py-4 font-sans text-slate-500 text-[11px] leading-relaxed max-w-sm">{{ a.description }}</td>
              <td class="px-6 py-4 text-center">
                <span class="bg-sky-50 font-sans text-[#1890ff] font-bold px-2 py-0.5 rounded-full text-[10px]">
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
    <div v-if="showAreaModal" class="fixed inset-0 bg-slate-900/40 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white rounded-xl shadow-xl border border-slate-100 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 text-white p-4 flex items-center justify-between">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <MapPin class="w-4 h-4 text-sky-400" />
            <span>添加区域</span>
          </div>
          <button @click="showAreaModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div v-if="areaFormErrorMessage" class="bg-rose-50 border border-rose-200 rounded-lg p-3 text-rose-600">
            {{ areaFormErrorMessage }}
          </div>
          <div>
            <label class="text-slate-500 font-bold block mb-1">区域名称</label>
            <input
              v-model="newAreaName"
              type="text"
              placeholder="例如: 智能三级精细沉降池"
              :class="areaFormErrors.Name ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200 focus:border-[#1890ff]'"
              class="w-full bg-slate-50 border rounded-lg p-2 font-sans focus:bg-white text-slate-900 focus:outline-none"
            />
            <span v-if="areaFormErrors.Name" class="text-rose-500 text-[10px] mt-1 block">{{ areaFormErrors.Name }}</span>
          </div>
          <div>
            <label class="text-slate-500 font-bold block mb-1">描述</label>
            <textarea
              v-model="newAreaDesc"
              rows="3"
              placeholder="阐述本区域所属流程及测温、变频流水分拣的具体物料方向..."
              class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 font-sans focus:bg-white text-slate-900 focus:outline-none focus:border-[#1890ff] leading-relaxed"
            />
          </div>
        </div>

        <div class="bg-slate-50 p-3 flex justify-end gap-2 border-t border-slate-100">
          <button
            @click="showAreaModal = false; areaFormErrors = {}; areaFormErrorMessage = ''"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 bg-white hover:bg-slate-50 font-bold text-xs text-slate-600 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleAddArea"
            class="px-4 py-1.5 rounded-lg bg-slate-900 hover:bg-slate-800 font-bold text-xs text-white cursor-pointer"
          >
            保存
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: ADD / EDIT DEVICE COMM LINK -->
    <div v-if="showDeviceModal" class="fixed inset-0 bg-slate-900/40 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white rounded-xl shadow-xl border border-slate-100 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        
        <!-- Header banner -->
        <div class="bg-slate-900 text-white p-4 flex items-center justify-between">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Cpu class="w-4 h-4 text-[#1890ff]" />
            <span>{{ isEditingDevice ? '编辑设备' : '添加设备' }}</span>
          </div>
          <button @click="showDeviceModal = false; deviceFormErrors = {}; deviceFormErrorMessage = ''" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs overflow-y-auto max-h-[450px]">
          <div v-if="deviceFormErrorMessage" class="bg-rose-50 border border-rose-200 rounded-lg p-3 text-rose-600">
            {{ deviceFormErrorMessage }}
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 font-bold block mb-1">设备名称</label>
              <input 
                v-model="devName"
                type="text"
                placeholder="例如: 3号二次鼓风风机"
                :class="deviceFormErrors.Name ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200 focus:border-[#1890ff]'"
                class="w-full bg-slate-50 border rounded-lg p-2 focus:bg-white text-slate-900 focus:outline-none text-xs font-sans font-semibold"
              />
              <span v-if="deviceFormErrors.Name" class="text-rose-500 text-[10px] mt-1 block">{{ deviceFormErrors.Name }}</span>
            </div>
            <div>
              <label class="text-slate-500 font-bold block mb-1">设备编号</label>
              <input 
                v-model="devKey"
                type="text"
                placeholder="例如: S7-BLR-202"
                :class="deviceFormErrors.Key ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200 focus:border-[#1890ff]'"
                class="w-full bg-slate-50 border rounded-lg p-2 focus:bg-white text-slate-900 focus:outline-none text-xs font-mono font-bold uppercase"
              />
              <span v-if="deviceFormErrors.Key" class="text-rose-500 text-[10px] mt-1 block">{{ deviceFormErrors.Key }}</span>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 font-bold block mb-1">所属区域</label>
              <select 
                v-model="devArea"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-900 focus:outline-none focus:border-[#1890ff]"
              >
                <option v-for="a in areas" :key="a.id" :value="a.id">{{ a.name }}</option>
              </select>
            </div>
            <div>
              <label class="text-slate-500 font-bold block mb-1">数据模型</label>
              <select 
                v-model="devModel"
                @change="onModelChange"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-900 focus:outline-none text-[#1890ff] font-bold"
              >
                <option v-for="m in dataModels" :key="m.id" :value="m.id">{{ m.name }}</option>
              </select>
            </div>
          </div>

          <!-- Dynamic settings based on Protocol -->
          <div class="p-3 bg-slate-50 rounded-xl space-y-3 border border-slate-100">
            <div class="flex items-center gap-1.5 text-slate-400 font-mono scale-95 origin-left">
              <Info class="w-3.5 h-3.5" />
              <span>协议类型: {{ devType }}</span>
            </div>

            <!-- OPCUA / Virtual Connection Setup -->
            <div v-if="devType === 'OPCUA' || devType === 'Virtual'" class="space-y-2">
              <div class="text-[10px] text-[#1890ff] font-bold uppercase tracking-wider mb-1">OPC UA 连接配置</div>
              <div class="grid grid-cols-3 gap-2">
                <div class="col-span-2">
                  <label class="text-slate-400 font-bold block mb-0.5">IP 地址</label>
                  <input 
                    v-model="devIP"
                    type="text"
                    placeholder="e.g. 192.168.1.100"
                    class="w-full bg-white border border-slate-200 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800"
                  />
                </div>
                <div>
                  <label class="text-slate-400 font-bold block mb-0.5">端口</label>
                  <input 
                    v-model="devPort"
                    type="text"
                    placeholder="e.g. 4840"
                    class="w-full bg-white border border-slate-200 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800"
                  />
                </div>
              </div>
            </div>

            <!-- Siemens S7 Connection Setup -->
            <div v-if="devType === 'S7'" class="space-y-3">
              <div class="text-[10px] text-indigo-500 font-bold uppercase tracking-wider mb-1">S7 连接配置</div>
              
              <div class="grid grid-cols-3 gap-2">
                <div class="col-span-2">
                  <label class="text-slate-400 font-bold block mb-0.5">IP 地址</label>
                  <input 
                    v-model="devIP"
                    type="text"
                    placeholder="e.g. 192.168.1.12"
                    class="w-full bg-white border border-slate-200 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800"
                  />
                </div>
                <div>
                  <label class="text-slate-400 font-bold block mb-0.5">端口</label>
                  <input 
                    v-model="devPort"
                    type="text"
                    placeholder="e.g. 102"
                    class="w-full bg-white border border-slate-200 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800"
                  />
                </div>
              </div>

              <div class="grid grid-cols-3 gap-2">
                <div>
                  <label class="text-slate-400 font-bold block mb-0.5">CPU 型号</label>
                  <select 
                    v-model="devCpuType"
                    class="w-full bg-white border border-slate-200 rounded px-1.5 py-1.5 focus:outline-none text-[11px] font-bold text-slate-700"
                  >
                    <option value="S7-1200">S7-1200</option>
                    <option value="S7-1500">S7-1500</option>
                    <option value="S7-300">S7-300</option>
                    <option value="S7-400">S7-400</option>
                  </select>
                </div>
                <div>
                  <label class="text-slate-400 font-bold block mb-0.5">机架</label>
                  <input 
                    v-model="devRack"
                    type="number"
                    min="0"
                    max="10"
                    :class="deviceFormErrors.Rack ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200'"
                    class="w-full bg-white border rounded px-2 py-1 focus:outline-none text-xs font-mono text-slate-850"
                  />
                  <span v-if="deviceFormErrors.Rack" class="text-rose-500 text-[10px] mt-1 block">{{ deviceFormErrors.Rack }}</span>
                </div>
                <div>
                  <label class="text-slate-400 font-bold block mb-0.5">插槽</label>
                  <input 
                    v-model="devSlot"
                    type="number"
                    min="0"
                    max="10"
                    class="w-full bg-white border border-slate-200 rounded px-2 py-1 focus:outline-none text-xs font-mono text-slate-850"
                  />
                </div>
              </div>
            </div>

            <!-- MQTT Router Configuration -->
            <div v-if="devType === 'MQTT'" class="space-y-2.5">
              <div class="text-[10px] text-emerald-500 font-bold uppercase tracking-wider mb-1">MQTT 连接配置</div>
            </div>
          </div>

          <!-- Device state radio selection -->
          <div>
            <label class="text-slate-500 font-bold block mb-1">连接状态</label>
            <div class="flex items-center gap-4 py-1">
              <label class="flex items-center gap-1.5 font-bold text-slate-700 cursor-pointer text-xs">
                <input type="radio" value="online" v-model="devStatus" class="text-emerald-500 focus:ring-0" />
                在线
              </label>
              <label class="flex items-center gap-1.5 font-bold text-slate-400 cursor-pointer text-xs">
                <input type="radio" value="offline" v-model="devStatus" class="text-rose-500 focus:ring-0" />
                离线
              </label>
            </div>
          </div>
        </div>

        <div class="bg-slate-50 p-4 border-t border-slate-100 flex justify-end gap-2">
          <button 
            @click="showDeviceModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 bg-white hover:bg-slate-50 font-bold text-xs text-slate-600 cursor-pointer"
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
