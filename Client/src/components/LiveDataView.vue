<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue';
import { devices } from '../store/deviceStore';
import { dataModels } from '../store/index';
import { addLog } from '../store/index';
import { syncDevices } from '../services/deviceService';
import { fetchDataModelsFromBackend } from '../api/modelApi';
import { writeDeviceVariable } from '../api/deviceApi';
import { extractApiError } from '../api/http';
import { DEVICE_TYPES } from '../types';
import {
  Database, 
  Search, 
  Settings, 
  Check, 
  Filter, 
  Activity, 
  RefreshCw,
  AlertTriangle,
  Flame,
  Binary,
  Clock,
  X
} from 'lucide-vue-next';

// Selection and query states
const selectedDevId = ref<string>(devices.value[0]?.id || '');
const searchQuery = ref<string>('');
const selectedTypeFilter = ref<string>('ALL');

// Computed list of devices based on search and filters
const filteredDevices = computed(() => {
  return devices.value.filter((d) => {
    const matchesSearch = d.name.toLowerCase().includes(searchQuery.value.toLowerCase()) || 
                          d.key.toLowerCase().includes(searchQuery.value.toLowerCase());
    const matchesType = selectedTypeFilter.value === 'ALL' || d.type === selectedTypeFilter.value;
    return matchesSearch && matchesType;
  });
});

// Currently selected device object
const selectedDevice = computed(() => {
  return devices.value.find(d => String(d.id) === selectedDevId.value) || devices.value[0];
});

// Variables dictionary detail from corresponding Data Model
const currentModel = computed(() => {
  if (!selectedDevice.value) return null;
  // dataModels.id 为 string（modelApi.ts 中转 String(m.id)），device.modelId 为 number，
  // 需两端统一转字符串比较，否则 === 恒为 false 导致模型匹配不上、变量列表恒空。
  return dataModels.value.find(m => String(m.id) === String(selectedDevice.value.modelId));
});

// Simulated variable values storage
const simulatedValues = ref<Record<string, number | boolean>>({});

// Compile active variables array with models mapping
const renderedVariables = computed(() => {
  if (!selectedDevice.value || !currentModel.value) return [];
  
  const modelVars = currentModel.value.variables || [];
  
  return modelVars.map((v) => {
    // 取值优先级：① 本地强制值（simulatedValues，写入后的即时反馈）
    //            → ② SignalR/轮询写入设备的真实遥测值（selectedDevice.variables[key]）
    //            → ③ 默认值（digital 反 false，analog 反 min）
    const forcedValue = simulatedValues.value[v.key];
    const liveValue = selectedDevice.value?.variables?.[v.key];
    const value = forcedValue !== undefined
      ? forcedValue
      : (liveValue !== undefined && liveValue !== null
          ? liveValue
          : (v.type === 'digital' ? false : v.min ?? 0));

    return {
      key: v.key,
      name: v.name || '未定义系统点位',
      // 地址已下放至设备实例级（DeviceVariable），模板变量不再持有地址，此处用 Key 派生占位
      address: `REG_${v.key.toUpperCase()}`,
      type: v.type || 'analog',
      dataType: v.dataType || '',
      unit: v.unit || '',
      min: v.min ?? 0,
      max: v.max ?? 100,
      description: v.description || '现场控制元件回写值',
      value: value,
      // 读写权限：优先取设备实例级有效权限（variableMeta.effectiveIsReadOnly，
      // 含设备级 IsReadOnlyOverride 覆盖结果），后端未下发时回退模板 isReadOnly。
      // 这样用户在"设备变量"视图把变量覆盖为可写后，实时监控页写入按钮即可显示。
      isReadOnly: selectedDevice.value?.variableMeta?.[v.key]?.effectiveIsReadOnly
        ?? v.isReadOnly
        ?? true,
      // 优先展示变量级实时推送时间戳，无推送时回退设备更新时间
      updatedAt: selectedDevice.value?.variableTimestamps?.[v.key]
        || selectedDevice.value?.lastUpdated
        || new Date().toISOString().replace('T', ' ').slice(0, 19)
    };
  });
});

// Variable specific live search input State
const varQuery = ref<string>('');

// Computation of filtered variables matching custom key query
const filteredRenderedVariables = computed(() => {
  const query = varQuery.value.trim().toLowerCase();
  if (!query) return renderedVariables.value;
  
  return renderedVariables.value.filter(v => 
    v.key.toLowerCase().includes(query) || 
    v.name.toLowerCase().includes(query) || 
    v.address.toLowerCase().includes(query)
  );
});

// 写入弹窗状态：writingTarget 为待写入变量元数据，overrideValueInput 为输入值
const showWriteModal = ref(false);
const writingTarget = ref<{
  key: string;
  name: string;
  type: 'analog' | 'digital';
  value: number | boolean;
  min: number;
  max: number;
  unit: string;
} | null>(null);
const overrideValueInput = ref<string>('');
const isSubmitting = ref(false);

// 点击"写入"：打开写值弹窗并预填当前值
const startOverride = (v: any) => {
  writingTarget.value = {
    key: v.key,
    name: v.name,
    type: v.type,
    value: v.value,
    min: v.min,
    max: v.max,
    unit: v.unit
  };
  overrideValueInput.value = String(v.value);
  showWriteModal.value = true;
};

const commitOverride = async (varKey: string, type: 'analog' | 'digital') => {
  if (!selectedDevice.value || isSubmitting.value) return;

  let finalVal: number | boolean = false;
  if (type === 'digital') {
    finalVal = overrideValueInput.value === 'true' || overrideValueInput.value === '1';
  } else {
    const num = parseFloat(overrideValueInput.value);
    finalVal = isNaN(num) ? 0 : num;
  }

  try {
    isSubmitting.value = true;
    // 真实写入链路：后端下发到设备驱动，成功后经 SignalR 广播回所有客户端（含刷新后）。
    await writeDeviceVariable(Number(selectedDevice.value.id as any), varKey, finalVal);
    // 乐观更新本地即时反馈（SignalR 广播到达后被真实遥测值覆盖）
    simulatedValues.value[varKey] = finalVal;

    // Submit trace logger
    const typeLabel = type === 'digital' ? 'Boolean' : 'Analog';
    addLog(
      '调试面板',
      `下发强制命令 [${selectedDevice.value.key}]: 强制点位 [${varKey}] 写入 (值为 ${finalVal}) [${typeLabel}]`,
      'info'
    );
    showWriteModal.value = false;
  } catch (err: any) {
    // 写入失败：保留本地旧值，仅展示后端返回的具体原因（只读/越界/离线等）
    addLog('调试面板', `写入失败 [${varKey}]: ${extractApiError(err)}`, 'warning');
  } finally {
    isSubmitting.value = false;
  }
};

const cancelOverride = () => {
  showWriteModal.value = false;
  writingTarget.value = null;
};

// 页面自举：直接进入实时监控页时主动拉取设备与数据模型，避免依赖"先访问设备管理页"
// 填充全局 store 才能显示数据。devices/models 的全局兜底见 App.vue 登录后预载。
onMounted(() => {
  syncDevices();
  fetchDataModelsFromBackend();
});

// selectedDevId 在 setup 时一次性取 devices[0]，若挂载时 store 为空会停在空串。
// 全部设备首次非空时回填默认选中，保证直进本页也能自动选中第一台设备。
watch(() => devices.value.length, (len) => {
  if (len && !devices.value.some(d => String(d.id) === selectedDevId.value)) {
    selectedDevId.value = String(devices.value[0].id);
  }
});
</script>

<template>
  <div class="h-full flex flex-col md:flex-row text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">
    
    <!-- LEFT PANEL: Devices list & Search -->
    <div class="w-full md:w-80 bg-white dark:bg-slate-900 border-b md:border-b-0 md:border-r border-slate-200 dark:border-slate-800 flex flex-col shrink-0 md:flex-none transition-colors">
      
      <!-- Top header search -->
      <div class="p-4 border-b border-slate-100 dark:border-slate-800 space-y-3">
        <div class="flex items-center gap-1.5 font-bold text-sm text-[#0f172a] dark:text-white">
          <Database class="w-4 h-4 text-[#1890ff]" />
          <span>设备列表</span>
        </div>

        <div class="relative">
          <Search class="absolute left-2.5 top-2.5 w-4 h-4 text-slate-400" />
          <input
            v-model="searchQuery"
            type="text"
            placeholder="搜索设备名称或编码"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:bg-white dark:focus:bg-slate-900 rounded-lg pl-9 pr-3 py-1.5 text-xs text-[#262626] dark:text-white focus:outline-none focus:border-[#1890ff]"
          />
        </div>

        <!-- System driver select tags -->
        <div class="flex flex-wrap gap-1">
          <button 
            @click="selectedTypeFilter = 'ALL'"
            class="text-[9px] font-bold px-2 py-0.5 rounded transition-all cursor-pointer"
            :class="selectedTypeFilter === 'ALL' ? 'bg-slate-900 dark:bg-sky-600 text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 hover:bg-slate-200 dark:hover:bg-slate-700'"
          >
            全部
          </button>
          <button 
            v-for="opt in DEVICE_TYPES" 
            :key="opt.value"
            @click="selectedTypeFilter = opt.value"
            :title="opt.implemented ? opt.label : `${opt.label}（驱动尚未实现）`"
            class="text-[9px] font-bold px-2 py-0.5 rounded transition-all cursor-pointer"
            :class="[
              selectedTypeFilter === opt.value ? 'bg-slate-900 dark:bg-sky-600 text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 hover:bg-slate-200 dark:hover:bg-slate-700',
              !opt.implemented ? 'opacity-60 line-through' : ''
            ]"
          >
            {{ opt.label }}
          </button>
        </div>
      </div>

      <!-- List of active devices -->
      <div class="flex-1 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800 max-h-[160px] md:max-h-none">
        <div 
          v-for="dev in filteredDevices" 
          :key="dev.id"
          @click="selectedDevId = dev.id"
          class="p-3.5 cursor-pointer hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-all text-left flex items-start gap-2.5 relative"
          :class="selectedDevId === dev.id ? 'bg-sky-50/50 dark:bg-sky-950/30 border-r-4 border-r-[#1890ff]' : ''"
        >
          <!-- Online status dot -->
          <span 
            class="w-2 h-2 rounded-full mt-1 shrink-0"
            :class="dev.status === 1 || dev.status === 'online' ? 'bg-emerald-500 shadow-[0_0_6px_#10b981]' : 'bg-slate-300 dark:bg-slate-600'"
          />
          
          <div class="space-y-1 overflow-hidden flex-1">
            <h4 class="font-bold text-xs text-slate-800 dark:text-white truncate leading-snug">
              {{ dev.name }}
            </h4>
            <div class="flex items-center gap-2 text-[9px] font-mono text-slate-500 dark:text-slate-400">
              <span class="bg-slate-100 dark:bg-slate-800 px-1 rounded text-slate-600 dark:text-slate-300 leading-none py-0.5">{{ dev.type }}</span>
              <span>-</span>
              <span class="truncate">{{ dev.key || dev.code }}</span>
            </div>
          </div>
        </div>

        <!-- Empty state tracker -->
        <div v-if="filteredDevices.length === 0" class="p-8 text-center text-xs text-slate-400 font-mono">
          未找到匹配的设备
        </div>
      </div>
    </div>

    <!-- RIGHT PANEL: Live Variable Inspection & Writes -->
    <div class="flex-1 flex flex-col min-w-0 bg-slate-50/50 dark:bg-transparent overflow-hidden">
      
      <!-- Panel Header banner -->
      <div v-if="selectedDevice" class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm flex flex-col sm:flex-row sm:items-center justify-between gap-4 shrink-0 font-sans transition-colors">
        <div class="space-y-1.5 text-left">
          <div class="flex items-center gap-2">
            <span class="text-[10px] font-bold px-2 py-0.5 bg-slate-100 dark:bg-slate-800 border border-slate-200/50 dark:border-slate-700 rounded-full font-mono uppercase text-slate-500 dark:text-slate-400">
              {{ selectedDevice.type }}
            </span>
            <span 
              class="text-[9px] font-bold px-1.5 py-0.5 rounded leading-none flex items-center gap-1"
              :class="selectedDevice.status === 1 || selectedDevice.status === 'online' ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400' : 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400'"
            >
              <span class="w-1.5 h-1.5 rounded-full" :class="selectedDevice.status === 1 || selectedDevice.status === 'online' ? 'bg-emerald-500' : 'bg-slate-400'" />
              {{ selectedDevice.status === 1 || selectedDevice.status === 'online' ? '在线' : '离线' }}
            </span>
          </div>
          <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight">
            {{ selectedDevice.name }}
          </h2>
          <p class="text-xs font-mono text-slate-500 dark:text-slate-400 flex items-center gap-3 flex-wrap">
            <span>通信地址: {{ selectedDevice.ipAddress ? `${selectedDevice.ipAddress}:${selectedDevice.port || 502}` : selectedDevice.topic || '本地总线' }}</span>
          </p>
        </div>

        <div class="flex items-center gap-2 shrink-0 self-start sm:self-center">
          <span class="text-xs font-mono text-slate-400 dark:text-slate-400 bg-slate-50 dark:bg-slate-950 px-2 py-1 rounded border border-slate-200/40 dark:border-slate-800">
            变量总数: <b class="text-emerald-600 dark:text-emerald-400">{{ renderedVariables.length }} 个</b>
          </span>
        </div>
      </div>

      <!-- Variables list -->
      <div class="flex-1 p-3 sm:p-6 overflow-y-auto space-y-4">
        
        <!-- Live Variable Search Filter panel -->
        <div v-if="selectedDevice" class="bg-white dark:bg-slate-900 p-3 border border-slate-200 dark:border-slate-800 shadow-3xs rounded-xl flex flex-col sm:flex-row items-center gap-3 text-left shrink-0 transition-colors">
          <div class="relative w-full sm:w-80">
            <input 
              v-model="varQuery"
              type="text"
              placeholder="搜索变量标识、地址或名称"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:bg-white dark:focus:bg-slate-900 rounded-lg pl-8 pr-7 py-1.5 text-xs text-[#262626] dark:text-white focus:outline-none focus:border-[#1890ff] focus:ring-1 focus:ring-[#1890ff]"
            />
            <Search class="absolute left-2.5 top-2.5 w-3.5 h-3.5 text-slate-400" />
            <button 
              v-if="varQuery" 
              @click="varQuery = ''" 
              class="absolute right-2.5 top-2.5 text-slate-400 hover:text-slate-600 focus:outline-none"
            >
              <X class="w-3.5 h-3.5" />
            </button>
          </div>

          <div class="text-[10px] sm:text-xs text-slate-400 font-sans ml-auto flex items-center gap-2">
            <span>找到 <b>{{ filteredRenderedVariables.length }}</b> / {{ renderedVariables.length }} 个变量</span>
          </div>
        </div>
        
        <!-- Offline Warning indicator -->
        <div v-if="selectedDevice && (selectedDevice.status !== 1 && selectedDevice.status !== 'online')" class="bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 text-rose-800 dark:text-rose-300 rounded-xl p-4 flex gap-3 text-xs leading-relaxed text-left">
          <AlertTriangle class="w-4 h-4 text-rose-500 shrink-0 mt-0.5" />
          <div>
            <h5 class="font-bold">设备已离线</h5>
            <p class="mt-0.5 text-rose-600 dark:text-rose-400 opacity-90">
              设备连接超时，写入功能已禁用。请在设备管理中恢复连接。
            </p>
          </div>
        </div>

        <!-- Variables Grid table card -->
        <div v-if="selectedDevice" class="bg-white dark:bg-slate-900 border border-slate-200/80 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm animate-in fade-in duration-200 transition-colors">
          <div class="overflow-x-auto hidden md:block">
            <table class="w-full text-left text-xs font-mono divide-y divide-slate-100 dark:divide-slate-800">
              <thead>
                <tr class="bg-slate-50/50 dark:bg-slate-950/60 text-slate-400 font-bold text-[10px] uppercase tracking-wider">
                  <th class="px-4 py-3.5">变量标识</th>
                  <th class="px-4 py-3.5">变量名称</th>
                  <th class="px-4 py-3.5">寄存器地址</th>
                  <th class="px-4 py-3.5">当前值</th>
                  <th class="px-4 py-3.5">更新时间</th>
                  <th class="px-4 py-3.5 text-right">操作</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-100 dark:divide-slate-800 bg-white dark:bg-slate-900">
                <tr 
                  v-for="v in filteredRenderedVariables" 
                  :key="v.key"
                  class="hover:bg-slate-50/40 dark:hover:bg-slate-800/40 transition-all font-mono"
                >
                  <!-- Tag Key -->
                  <td class="px-4 py-3.5 font-bold text-slate-600 dark:text-slate-300">
                    <span class="flex items-center gap-1.5">
                      <Binary class="w-3 h-3 text-slate-400" />
                      {{ v.key }}
                    </span>
                    <span class="inline-block mt-1 px-1.5 py-0.5 text-[9px] font-bold rounded border uppercase tracking-wider scale-95 origin-left"
                      :class="v.type === 'digital' ? 
                        (selectedDevice?.type === 'S7' ? 'bg-indigo-50 dark:bg-indigo-950/60 text-indigo-700 dark:text-indigo-300 border-indigo-200 dark:border-indigo-800' : 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800') : 
                        (selectedDevice?.type === 'S7' ? 'bg-indigo-50/70 dark:bg-indigo-950/60 text-indigo-600 dark:text-indigo-300 border-indigo-200 dark:border-indigo-800' : 'bg-sky-50 dark:bg-sky-950/60 text-sky-700 dark:text-sky-300 border-sky-200 dark:border-sky-800')"
                    >
                      {{ v.dataType || (v.type === 'digital' ? (selectedDevice?.type === 'S7' ? 'BOOL' : 'Boolean') : (selectedDevice?.type === 'S7' ? 'REAL' : 'Float')) }}
                    </span>
                  </td>

                  <!-- Label/Name -->
                  <td class="px-4 py-3.5 text-slate-800 dark:text-slate-200 font-sans font-medium">
                    {{ v.name }}
                    <span class="block text-[10px] font-mono text-slate-400 dark:text-slate-500 mt-0.5 font-normal">
                      {{ v.description }}
                    </span>
                  </td>

                  <!-- Register Address -->
                  <td class="px-4 py-3.5 text-slate-500 dark:text-slate-400 text-[11px]">
                    <span class="bg-slate-100 dark:bg-slate-800 font-bold px-1.5 py-0.5 rounded text-slate-600 dark:text-slate-300">
                      {{ v.address }}
                    </span>
                  </td>

                  <!-- Active Value display -->
                  <td class="px-4 py-3.5">
                    <!-- If boolean type -->
                    <span 
                      v-if="v.type === 'digital'"
                      class="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold"
                      :class="v.value ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400' : 'bg-rose-50 dark:bg-rose-950/40 text-rose-500 dark:text-rose-400'"
                    >
                      <span class="w-1.5 h-1.5 rounded-full" :class="v.value ? 'bg-emerald-500 animate-pulse' : 'bg-rose-500'" />
                      {{ v.value ? 'ON / 闭合' : 'OFF / 断开' }}
                    </span>
                    <!-- Numerical type -->
                    <span v-else class="text-sm font-bold text-slate-900 dark:text-white flex items-center gap-1">
                      {{ v.value }} <span class="text-[10px] font-normal text-slate-500 dark:text-slate-400 font-sans">{{ v.unit }}</span>
                    </span>
                  </td>

                  <!-- Variable specific timestamp -->
                  <td class="px-4 py-3.5 text-slate-500 dark:text-slate-400 text-[11px] font-mono leading-none">
                    <span class="flex items-center gap-1.5 matches">
                      <Clock class="w-3.5 h-3.5 text-slate-400" />
                      {{ v.updatedAt }}
                    </span>
                  </td>

                  <!-- Manual overrides -->
                  <td class="px-4 py-3.5 text-right">
                    <!-- Open write modal button -->
                    <button 
                      v-if="(selectedDevice.status === 1 || selectedDevice.status === 'online') && !v.isReadOnly"
                      @click="startOverride(v)"
                      class="text-[11px] font-sans font-bold text-[#1890ff] hover:text-sky-600 border border-slate-200 dark:border-slate-700 px-2 py-1 rounded hover:bg-slate-50 dark:hover:bg-slate-800 inline-flex items-center gap-1 transition-all cursor-pointer"
                    >
                      <Settings class="w-3 h-3" />
                      写入
                    </button>
                    <span v-else class="text-slate-300 dark:text-slate-600 text-[10px] font-sans">已锁定</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Mobile responsive variables card list -->
          <div class="block md:hidden divide-y divide-slate-100 dark:divide-slate-800 max-h-[500px] overflow-y-auto">
            <div 
              v-for="v in filteredRenderedVariables" 
              :key="v.key + '_mob'"
              class="p-4 space-y-3 text-left bg-white dark:bg-slate-900"
            >
              <!-- Top row: key name & status -->
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0 space-y-1">
                  <div class="flex items-center gap-1 text-slate-900 dark:text-white font-bold font-mono text-xs truncate flex-wrap">
                    <Binary class="w-3.5 h-3.5 text-slate-400 shrink-0" />
                    <span class="truncate select-all bg-slate-50 dark:bg-slate-950 px-1 rounded border border-slate-100 dark:border-slate-800 max-w-[130px] inline-block">{{ v.key }}</span>
                    <span class="inline-block px-1.5 py-0.5 text-[9px] font-bold rounded border uppercase tracking-wider scale-90 origin-left ml-1"
                      :class="v.type === 'digital' ? 
                        (selectedDevice?.type === 'S7' ? 'bg-indigo-50 dark:bg-indigo-950/60 text-indigo-700 dark:text-indigo-300 border-indigo-200 dark:border-indigo-800' : 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800') : 
                        (selectedDevice?.type === 'S7' ? 'bg-indigo-50/70 dark:bg-indigo-950/60 text-indigo-600 dark:text-indigo-300 border-indigo-200 dark:border-indigo-800' : 'bg-sky-50 dark:bg-sky-950/60 text-sky-700 dark:text-sky-300 border-sky-200 dark:border-sky-800')"
                    >
                      {{ v.dataType || (v.type === 'digital' ? (selectedDevice?.type === 'S7' ? 'BOOL' : 'Boolean') : (selectedDevice?.type === 'S7' ? 'REAL' : 'Float')) }}
                    </span>
                  </div>
                  <div>
                    <span class="inline-block text-[9px] bg-slate-100 dark:bg-slate-800 font-bold px-1.5 py-0.5 rounded text-slate-600 dark:text-slate-300 font-mono">
                      {{ v.address }}
                    </span>
                  </div>
                </div>

                <!-- Active Value -->
                <div class="shrink-0">
                  <span 
                    v-if="v.type === 'digital'"
                    class="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold"
                    :class="v.value ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400 border border-emerald-100/30' : 'bg-rose-50 dark:bg-rose-950/40 text-rose-500 dark:text-rose-400 border border-rose-100/30'"
                  >
                    <span class="w-1.5 h-1.5 rounded-full" :class="v.value ? 'bg-emerald-500 animate-pulse' : 'bg-rose-500'" />
                    {{ v.value ? 'ON / 闭合' : 'OFF / 断开' }}
                  </span>
                  <span v-else class="text-xs font-bold text-slate-900 dark:text-white bg-slate-100 dark:bg-slate-800 px-2 py-0.5 rounded border border-slate-200/50 dark:border-slate-700 inline-flex items-center gap-1 font-mono">
                    {{ v.value }} <span class="text-[9px] font-sans font-normal text-slate-500 dark:text-slate-400">{{ v.unit }}</span>
                  </span>
                </div>
              </div>

              <!-- Variable Name & Description -->
              <div class="bg-slate-50/70 dark:bg-slate-950/60 p-2.5 rounded-xl border border-slate-100/80 dark:border-slate-800/80 space-y-1">
                <div class="text-xs font-bold text-slate-850 dark:text-slate-200 font-sans break-words">{{ v.name }}</div>
                <div class="text-[10px] text-slate-400 dark:text-slate-500 font-sans leading-relaxed break-words">{{ v.description }}</div>
              </div>

              <!-- Variable custom timestamp indicator -->
              <div class="flex items-center justify-between text-[9px] font-mono text-slate-400 dark:text-slate-500 bg-slate-50 dark:bg-slate-950 p-2 rounded-lg border border-slate-100 dark:border-slate-800">
                <span class="font-sans">更新时间:</span>
                <span class="font-bold text-slate-600 dark:text-slate-300 flex items-center gap-1">
                  <Clock class="w-3 h-3 text-slate-400 shrink-0" />
                  {{ v.updatedAt }}
                </span>
              </div>

              <!-- Overrides control -->
              <div class="flex items-center justify-between pt-2">
                <span class="text-[10px] text-slate-400 dark:text-slate-500 font-sans">数值写入</span>
                
                <div class="shrink-0 font-sans">
                  <!-- Open write modal button -->
                  <button 
                    v-if="(selectedDevice.status === 1 || selectedDevice.status === 'online') && !v.isReadOnly"
                    @click="startOverride(v)"
                    class="text-[10px] font-sans font-bold text-[#1890ff] border border-slate-200 dark:border-slate-700 px-2 py-1 rounded-lg bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 inline-flex items-center gap-1 shadow-2xs transition-all cursor-pointer"
                  >
                    <Settings class="w-3 h-3" />
                    写入
                  </button>
                  <span v-else class="text-slate-300 dark:text-slate-600 text-[10px] font-sans">已锁定</span>
                </div>
              </div>

            </div>
          </div>
        </div>

        <!-- Select Device Empty state -->
        <div v-else class="h-64 flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 gap-2">
          <Database class="w-8 h-8 text-slate-300 dark:text-slate-700" />
          <p class="text-xs">请选择设备查看变量数据</p>
        </div>
      </div>
    </div>
  </div>

  <!-- 变量写入弹窗 -->
  <div v-if="showWriteModal && writingTarget" class="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4" @click.self="cancelOverride">
    <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl w-full max-w-sm max-h-[80vh] flex flex-col overflow-hidden">
      <div class="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
        <h3 class="font-bold text-sm text-slate-900 dark:text-white flex items-center gap-2">
          <Settings class="w-4 h-4 text-[#1890ff]" /> 写入变量 — <span class="font-mono">{{ writingTarget.key }}</span>
        </h3>
        <button @click="cancelOverride" class="text-slate-400 hover:text-slate-600 cursor-pointer"><X class="w-4 h-4" /></button>
      </div>
      <div class="p-4 space-y-3">
        <div>
          <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">变量名称</label>
          <div class="text-sm font-bold text-slate-800 dark:text-slate-200 font-sans">{{ writingTarget.name }}</div>
        </div>
        <div class="grid grid-cols-2 gap-3">
          <div>
            <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">当前值</label>
            <div class="text-xs font-mono text-slate-600 dark:text-slate-300">
              {{ writingTarget.type === 'digital' ? (writingTarget.value ? 'ON / 闭合' : 'OFF / 断开') : `${writingTarget.value} ${writingTarget.unit}` }}
            </div>
          </div>
          <div>
            <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">类型</label>
            <div class="text-xs font-mono text-slate-600 dark:text-slate-300">{{ writingTarget.type === 'digital' ? 'Boolean' : 'Analog' }}</div>
          </div>
        </div>
        <div>
          <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">
            写入值<span v-if="writingTarget.type === 'analog'" class="text-slate-400 font-normal">（范围 {{ writingTarget.min }} ~ {{ writingTarget.max }}{{ writingTarget.unit }}）</span>
          </label>
          <select
            v-if="writingTarget.type === 'digital'"
            v-model="overrideValueInput"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs focus:outline-none"
          >
            <option value="true">ON</option>
            <option value="false">OFF</option>
          </select>
          <input
            v-else
            v-model="overrideValueInput"
            type="number"
            step="0.1"
            :min="writingTarget.min"
            :max="writingTarget.max"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs font-mono focus:outline-none"
          />
        </div>
      </div>
      <div class="p-4 border-t border-slate-100 dark:border-slate-800 flex justify-end gap-2">
        <button @click="cancelOverride" :disabled="isSubmitting" class="px-3 py-1.5 text-xs font-bold border border-slate-200 dark:border-slate-700 rounded-lg text-slate-600 dark:text-slate-300 cursor-pointer disabled:opacity-40">取消</button>
        <button @click="commitOverride(writingTarget.key, writingTarget.type)" :disabled="isSubmitting" class="px-3 py-1.5 text-xs font-bold bg-[#1890ff] text-white rounded-lg hover:bg-sky-600 disabled:opacity-40 cursor-pointer inline-flex items-center gap-1">
          <Check v-if="!isSubmitting" class="w-3.5 h-3.5" />
          {{ isSubmitting ? '写入中...' : '确认写入' }}
        </button>
      </div>
    </div>
  </div>
</template>
