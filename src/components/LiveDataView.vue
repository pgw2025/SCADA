<script setup lang="ts">
import { ref, computed } from 'vue';
import { devices } from '../store/deviceStore';
import { dataModels } from '../store/index';
import { addLog } from '../store/index';
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
  return dataModels.value.find(m => m.id === selectedDevice.value.modelId);
});

// Simulated variable values storage
const simulatedValues = ref<Record<string, number | boolean>>({});

// Compile active variables array with models mapping
const renderedVariables = computed(() => {
  if (!selectedDevice.value || !currentModel.value) return [];
  
  const modelVars = currentModel.value.variables || [];
  
  return modelVars.map((v) => {
    const storedValue = simulatedValues.value[v.key];
    const value = storedValue !== undefined ? storedValue : (v.type === 'digital' ? false : v.min ?? 0);
    
    return {
      key: v.key,
      name: v.name || '未定义系统点位',
      address: v.address || `REG_${v.key.toUpperCase()}`,
      type: v.type || 'analog',
      dataType: v.dataType || '',
      unit: v.unit || '',
      min: v.min ?? 0,
      max: v.max ?? 100,
      description: v.description || '现场控制元件回写值',
      value: value,
      updatedAt: selectedDevice.value?.lastUpdated || new Date().toISOString().replace('T', ' ').slice(0, 19)
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

// Control override state variables
const isWritingVarKey = ref<string | null>(null);
const overrideValueInput = ref<string>('');

// Perform override operation
const startOverride = (varKey: string, currentVal: number | boolean) => {
  isWritingVarKey.value = varKey;
  overrideValueInput.value = currentVal.toString();
};

const commitOverride = (varKey: string, type: 'analog' | 'digital') => {
  if (!selectedDevice.value) return;

  let finalVal: number | boolean = false;
  if (type === 'digital') {
    finalVal = overrideValueInput.value === 'true' || overrideValueInput.value === '1';
  } else {
    const num = parseFloat(overrideValueInput.value);
    finalVal = isNaN(num) ? 0 : num;
  }

  // Update simulated values
  simulatedValues.value[varKey] = finalVal;
  
  // Submit trace logger
  const typeLabel = type === 'digital' ? 'Boolean' : 'Analog';
  addLog(
    '调试面板', 
    `下发强制命令 [${selectedDevice.value.key}]: 强制点位 [${varKey}] 写入 (值为 ${finalVal}) [${typeLabel}]`, 
    'info'
  );

  isWritingVarKey.value = null;
};

const cancelOverride = () => {
  isWritingVarKey.value = null;
};
</script>

<template>
  <div class="h-full flex flex-col md:flex-row text-[#1e293b] select-none bg-slate-50">
    
    <!-- LEFT PANEL: Devices list & Search -->
    <div class="w-full md:w-80 bg-white border-b md:border-b-0 md:border-r border-slate-200 flex flex-col shrink-0 md:flex-none">
      
      <!-- Top header search -->
      <div class="p-4 border-b border-slate-100 space-y-3">
        <div class="flex items-center gap-1.5 font-bold text-sm text-[#0f172a]">
          <Database class="w-4 h-4 text-[#1890ff]" />
          <span>设备列表</span>
        </div>

        <div class="relative">
          <Search class="absolute left-2.5 top-2.5 w-4 h-4 text-slate-400" />
          <input
            v-model="searchQuery"
            type="text"
            placeholder="搜索设备名称或编码"
            class="w-full bg-slate-50 border border-slate-200 focus:bg-white rounded-lg pl-9 pr-3 py-1.5 text-xs text-[#262626] focus:outline-none focus:border-[#1890ff]"
          />
        </div>

        <!-- System driver select tags -->
        <div class="flex flex-wrap gap-1">
          <button 
            v-for="opt in ['ALL', 'OPCUA', 'S7', 'MQTT', 'Virtual']" 
            :key="opt"
            @click="selectedTypeFilter = opt"
            class="text-[9px] font-bold px-2 py-0.5 rounded transition-all"
            :class="selectedTypeFilter === opt ? 'bg-slate-900 text-white' : 'bg-slate-100 text-slate-500 hover:bg-slate-200'"
          >
            {{ opt }}
          </button>
        </div>
      </div>

      <!-- List of active devices -->
      <div class="flex-1 overflow-y-auto divide-y divide-slate-100 max-h-[160px] md:max-h-none">
        <div 
          v-for="dev in filteredDevices" 
          :key="dev.id"
          @click="selectedDevId = dev.id"
          class="p-3.5 cursor-pointer hover:bg-slate-50/50 transition-all text-left flex items-start gap-2.5 relative"
          :class="selectedDevId === dev.id ? 'bg-sky-50/50 border-r-4 border-r-[#1890ff]' : ''"
        >
          <!-- Online status dot -->
          <span 
            class="w-2 h-2 rounded-full mt-1 shrink-0"
            :class="dev.status === 'online' ? 'bg-emerald-500 shadow-[0_0_6px_#10b981]' : 'bg-slate-300'"
          />
          
          <div class="space-y-1 overflow-hidden flex-1">
            <h4 class="font-bold text-xs text-slate-800 truncate leading-snug">
              {{ dev.name }}
            </h4>
            <div class="flex items-center gap-2 text-[9px] font-mono text-slate-500">
              <span class="bg-slate-100 px-1 rounded text-slate-600 leading-none py-0.5">{{ dev.type }}</span>
              <span>-</span>
              <span class="truncate">{{ dev.code }}</span>
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
    <div class="flex-1 flex flex-col min-w-0 bg-slate-50/50 overflow-hidden">
      
      <!-- Panel Header banner -->
      <div v-if="selectedDevice" class="bg-white p-5 border-b border-slate-200 shadow-sm flex flex-col sm:flex-row sm:items-center justify-between gap-4 shrink-0 font-sans">
        <div class="space-y-1.5 text-left">
          <div class="flex items-center gap-2">
            <span class="text-[10px] font-bold px-2 py-0.5 bg-slate-100 border border-slate-200/50 rounded-full font-mono uppercase text-slate-500">
              {{ selectedDevice.type }}
            </span>
            <span 
              class="text-[9px] font-bold px-1.5 py-0.5 rounded leading-none flex items-center gap-1"
              :class="selectedDevice.status === 'online' ? 'bg-emerald-50 text-emerald-600' : 'bg-slate-100 text-slate-500'"
            >
              <span class="w-1.5 h-1.5 rounded-full" :class="selectedDevice.status === 'online' ? 'bg-emerald-500' : 'bg-slate-400'" />
              {{ selectedDevice.status === 'online' ? '在线' : '离线' }}
            </span>
          </div>
          <h2 class="font-bold text-base text-slate-900 tracking-tight">
            {{ selectedDevice.name }}
          </h2>
          <p class="text-xs font-mono text-slate-500 flex items-center gap-3 flex-wrap">
            <span>通信地址: {{ selectedDevice.ipAddress ? `${selectedDevice.ipAddress}:${selectedDevice.port || 502}` : selectedDevice.topic || '本地总线' }}</span>
          </p>
        </div>

        <div class="flex items-center gap-2 shrink-0 self-start sm:self-center">
          <span class="text-xs font-mono text-slate-400 bg-slate-50 px-2 py-1 rounded border border-slate-200/40">
            变量总数: <b class="text-emerald-600">{{ renderedVariables.length }} 个</b>
          </span>
        </div>
      </div>

      <!-- Variables list -->
      <div class="flex-1 p-3 sm:p-6 overflow-y-auto space-y-4">
        
        <!-- NEW: Live Variable Search Filter panel -->
        <div v-if="selectedDevice" class="bg-white p-3 border border-slate-200 shadow-3xs rounded-xl flex flex-col sm:flex-row items-center gap-3 text-left shrink-0">
          <div class="relative w-full sm:w-80">
            <input 
              v-model="varQuery"
              type="text"
              placeholder="搜索变量标识、地址或名称"
              class="w-full bg-slate-50 border border-slate-200 focus:bg-white rounded-lg pl-8 pr-7 py-1.5 text-xs text-[#262626] focus:outline-none focus:border-[#1890ff] focus:ring-1 focus:ring-[#1890ff]"
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
        <div v-if="selectedDevice && selectedDevice.status !== 'online'" class="bg-rose-50 border border-rose-200 text-rose-800 rounded-xl p-4 flex gap-3 text-xs leading-relaxed text-left">
          <AlertTriangle class="w-4 h-4 text-rose-500 shrink-0 mt-0.5" />
          <div>
            <h5 class="font-bold">设备已离线</h5>
            <p class="mt-0.5 text-rose-600 opacity-90">
              设备连接超时，写入功能已禁用。请在设备管理中恢复连接。
            </p>
          </div>
        </div>

        <!-- Variables Grid table card -->
        <div v-if="selectedDevice" class="bg-white border border-slate-200/80 rounded-xl overflow-hidden shadow-sm animate-in fade-in duration-200">
          <div class="overflow-x-auto hidden md:block">
            <table class="w-full text-left text-xs font-mono divide-y divide-slate-100">
              <thead>
                <tr class="bg-slate-50/50 text-slate-400 font-bold text-[10px] uppercase tracking-wider">
                  <th class="px-4 py-3.5">变量标识</th>
                  <th class="px-4 py-3.5">变量名称</th>
                  <th class="px-4 py-3.5">寄存器地址</th>
                  <th class="px-4 py-3.5">当前值</th>
                  <th class="px-4 py-3.5">更新时间</th>
                  <th class="px-4 py-3.5 text-right">操作</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-100 bg-white">
                <tr 
                  v-for="v in filteredRenderedVariables" 
                  :key="v.key"
                  class="hover:bg-slate-50/40 transition-all font-mono"
                >
                  <!-- Tag Key -->
                  <td class="px-4 py-3.5 font-bold text-slate-600">
                    <span class="flex items-center gap-1.5">
                      <Binary class="w-3 h-3 text-slate-400" />
                      {{ v.key }}
                    </span>
                    <span class="inline-block mt-1 px-1.5 py-0.5 text-[9px] font-bold rounded border uppercase tracking-wider scale-95 origin-left"
                      :class="v.type === 'digital' ? 
                        (selectedDevice?.type === 'S7' ? 'bg-indigo-50 text-indigo-700 border-indigo-200' : 'bg-emerald-50 text-emerald-700 border-emerald-200') : 
                        (selectedDevice?.type === 'S7' ? 'bg-indigo-50/70 text-indigo-600 border-indigo-200' : 'bg-sky-50 text-sky-700 border-sky-200')"
                    >
                      {{ v.dataType || (v.type === 'digital' ? (selectedDevice?.type === 'S7' ? 'BOOL' : 'Boolean') : (selectedDevice?.type === 'S7' ? 'REAL' : 'Float')) }}
                    </span>
                  </td>

                  <!-- Label/Name -->
                  <td class="px-4 py-3.5 text-slate-800 font-sans font-medium">
                    {{ v.name }}
                    <span class="block text-[10px] font-mono text-slate-400 mt-0.5 font-normal">
                      {{ v.description }}
                    </span>
                  </td>

                  <!-- Register Address -->
                  <td class="px-4 py-3.5 text-slate-500 text-[11px]">
                    <span class="bg-slate-100 font-bold px-1.5 py-0.5 rounded text-slate-600">
                      {{ v.address }}
                    </span>
                  </td>

                  <!-- Active Value display -->
                  <td class="px-4 py-3.5">
                    <!-- If boolean type -->
                    <span 
                      v-if="v.type === 'digital'"
                      class="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold"
                      :class="v.value ? 'bg-emerald-50 text-emerald-600' : 'bg-rose-50 text-rose-500'"
                    >
                      <span class="w-1.5 h-1.5 rounded-full" :class="v.value ? 'bg-emerald-500 animate-pulse' : 'bg-rose-500'" />
                      {{ v.value ? 'ON / 闭合' : 'OFF / 断开' }}
                    </span>
                    <!-- Numerical type -->
                    <span v-else class="text-sm font-bold text-slate-900 flex items-center gap-1">
                      {{ v.value }} <span class="text-[10px] font-normal text-slate-500 font-sans">{{ v.unit }}</span>
                    </span>
                  </td>

                  <!-- Variable specific timestamp -->
                  <td class="px-4 py-3.5 text-slate-500 text-[11px] font-mono leading-none">
                    <span class="flex items-center gap-1.5 matches">
                      <Clock class="w-3.5 h-3.5 text-slate-400" />
                      {{ v.updatedAt }}
                    </span>
                  </td>

                  <!-- Manual overrides -->
                  <td class="px-4 py-3.5 text-right">
                    <!-- Overwrite inline input -->
                    <div v-if="isWritingVarKey === v.key" class="flex items-center justify-end gap-1.5">
                      <select 
                        v-if="v.type === 'digital'"
                        v-model="overrideValueInput"
                        class="bg-white border border-slate-300 rounded px-1.5 py-1 text-[11px] focus:outline-none"
                      >
                        <option value="true">ON</option>
                        <option value="false">OFF</option>
                      </select>
                      <input 
                        v-else
                        v-model="overrideValueInput"
                        type="number"
                        step="0.1"
                        :min="v.min"
                        :max="v.max"
                        class="w-16 bg-white border border-slate-300 text-slate-900 rounded px-1.5 py-1 text-[11px] text-right focus:outline-none"
                      />
                      <button 
                        @click="commitOverride(v.key, v.type)"
                        class="p-1 rounded bg-[#1890ff] text-white hover:bg-sky-600 cursor-pointer"
                        title="确认强制"
                      >
                        <Check class="w-3.5 h-3.5" />
                      </button>
                      <button 
                        @click="cancelOverride"
                        class="p-1 rounded bg-slate-100 text-slate-400 hover:bg-slate-200 cursor-pointer text-xs font-sans font-medium"
                      >
                        取消
                      </button>
                    </div>

                    <!-- Open overwrite button -->
                    <button 
                      v-else-if="selectedDevice.status === 'online'"
                      @click="startOverride(v.key, v.value)"
                      class="text-[11px] font-sans font-bold text-[#1890ff] hover:text-sky-600 border border-slate-200 px-2 py-1 rounded hover:bg-slate-50 inline-flex items-center gap-1 transition-all"
                    >
                      <Settings class="w-3 h-3" />
                      写入
                    </button>
                    <span v-else class="text-slate-300 text-[10px] font-sans">已锁定</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Mobile responsive variables card list -->
          <div class="block md:hidden divide-y divide-slate-100 max-h-[500px] overflow-y-auto">
            <div 
              v-for="v in filteredRenderedVariables" 
              :key="v.key + '_mob'"
              class="p-4 space-y-3 text-left bg-white"
            >
              <!-- Top row: key name & status -->
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0 space-y-1">
                  <div class="flex items-center gap-1 text-slate-900 font-bold font-mono text-xs truncate flex-wrap">
                    <Binary class="w-3.5 h-3.5 text-slate-400 shrink-0" />
                    <span class="truncate select-all bg-slate-50 px-1 rounded border border-slate-100 max-w-[130px] inline-block">{{ v.key }}</span>
                    <span class="inline-block px-1.5 py-0.5 text-[9px] font-bold rounded border uppercase tracking-wider scale-90 origin-left ml-1"
                      :class="v.type === 'digital' ? 
                        (selectedDevice?.type === 'S7' ? 'bg-indigo-50 text-indigo-700 border-indigo-200' : 'bg-emerald-50 text-emerald-700 border-emerald-200') : 
                        (selectedDevice?.type === 'S7' ? 'bg-indigo-50/70 text-indigo-600 border-indigo-200' : 'bg-sky-50 text-sky-700 border-sky-200')"
                    >
                      {{ v.dataType || (v.type === 'digital' ? (selectedDevice?.type === 'S7' ? 'BOOL' : 'Boolean') : (selectedDevice?.type === 'S7' ? 'REAL' : 'Float')) }}
                    </span>
                  </div>
                  <div>
                    <span class="inline-block text-[9px] bg-slate-100 font-bold px-1.5 py-0.5 rounded text-slate-600 font-mono">
                      {{ v.address }}
                    </span>
                  </div>
                </div>

                <!-- Active Value -->
                <div class="shrink-0">
                  <span 
                    v-if="v.type === 'digital'"
                    class="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold"
                    :class="v.value ? 'bg-emerald-50 text-emerald-600 border border-emerald-100/30' : 'bg-rose-50 text-rose-505 bg-rose-50 text-rose-500 border border-rose-100/30'"
                  >
                    <span class="w-1.5 h-1.5 rounded-full" :class="v.value ? 'bg-emerald-500 animate-pulse' : 'bg-rose-500'" />
                    {{ v.value ? 'ON / 闭合' : 'OFF / 断开' }}
                  </span>
                  <span v-else class="text-xs font-bold text-slate-900 bg-slate-100 px-2 py-0.5 rounded border border-slate-200/50 inline-flex items-center gap-1 font-mono">
                    {{ v.value }} <span class="text-[9px] font-sans font-normal text-slate-500">{{ v.unit }}</span>
                  </span>
                </div>
              </div>

              <!-- Variable Name & Description -->
              <div class="bg-slate-50/70 p-2.5 rounded-xl border border-slate-100/80 space-y-1">
                <div class="text-xs font-bold text-slate-850 font-sans break-words">{{ v.name }}</div>
                <div class="text-[10px] text-slate-400 font-sans leading-relaxed break-words">{{ v.description }}</div>
              </div>

              <!-- Variable custom timestamp indicator -->
              <div class="flex items-center justify-between text-[9px] font-mono text-slate-400 bg-slate-50 p-2 rounded-lg border border-slate-100">
                <span class="font-sans">更新时间:</span>
                <span class="font-bold text-slate-600 flex items-center gap-1">
                  <Clock class="w-3 h-3 text-slate-405 shrink-0" />
                  {{ v.updatedAt }}
                </span>
              </div>

              <!-- Overrides control -->
              <div class="flex items-center justify-between pt-2">
                <span class="text-[10px] text-slate-400 font-sans">数值写入</span>
                
                <div class="shrink-0 font-sans">
                  <!-- Mode Override Active -->
                  <div v-if="isWritingVarKey === v.key" class="flex items-center gap-1.5">
                    <select 
                      v-if="v.type === 'digital'"
                      v-model="overrideValueInput"
                      class="bg-white border border-slate-200 rounded-lg px-1.5 py-1 text-xs focus:outline-none font-sans"
                    >
                      <option value="true">ON</option>
                      <option value="false">OFF</option>
                    </select>
                    <input 
                      v-else
                      v-model="overrideValueInput"
                      type="number"
                      step="0.1"
                      :min="v.min"
                      :max="v.max"
                      class="w-16 bg-white border border-slate-200 text-slate-900 rounded-lg px-2 py-1 text-xs text-right focus:outline-none font-mono font-bold"
                    />
                    <button 
                      @click="commitOverride(v.key, v.type)"
                      class="p-1 px-1.5 rounded-lg bg-[#1890ff] text-white hover:bg-sky-600 cursor-pointer flex items-center justify-center shadow-sm"
                    >
                      <Check class="w-3.5 h-3.5" />
                    </button>
                    <button 
                      @click="cancelOverride"
                      class="text-xs font-bold text-slate-400 hover:text-slate-600 px-1"
                    >
                      取消
                    </button>
                  </div>

                  <!-- Read only block / override button -->
                  <button 
                    v-else-if="selectedDevice.status === 'online'"
                    @click="startOverride(v.key, v.value)"
                    class="text-[10px] font-sans font-bold text-[#1890ff] border border-slate-200 px-2 py-1 rounded-lg bg-white hover:bg-slate-50 inline-flex items-center gap-1 shadow-2xs transition-all cursor-pointer"
                  >
                    <Settings class="w-3 h-3" />
                    写入
                  </button>
                  <span v-else class="text-slate-300 text-[10px] font-sans">已锁定</span>
                </div>
              </div>

            </div>
          </div>
        </div>

        <!-- Select Device Empty state -->
        <div v-else class="h-64 flex flex-col items-center justify-center text-slate-400 gap-2">
          <Database class="w-8 h-8 text-slate-300" />
          <p class="text-xs">请选择设备查看变量数据</p>
        </div>
      </div>
    </div>
  </div>
</template>
