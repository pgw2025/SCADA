<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue';
import { dataModels, devices, addLog, createDataModelOnBackend, updateDataModelOnBackend, deleteDataModelOnBackend, fetchDataModelsFromBackend, createVariable } from '../store/index';
import { DataModel, ModelVariable, DeviceType } from '../types';

onMounted(() => {
  fetchDataModelsFromBackend();
});

import { 
  FileCode, 
  Layers, 
  Cpu, 
  Trash2, 
  Plus, 
  Tag, 
  Sliders, 
  X,
  FileJson,
  Binary
} from 'lucide-vue-next';

// Active selection
const selectedModelId = ref<string>(dataModels.value[0]?.id || '');

const currentModel = computed(() => {
  return dataModels.value.find(m => m.id === selectedModelId.value) || dataModels.value[0];
});

// Create model form state
const showModelModal = ref<boolean>(false);
const modelName = ref<string>('');
const modelDesc = ref<string>('');
const modelType = ref<DeviceType>('OPCUA');

// Create variable form state Inside Model
const showVarModal = ref<boolean>(false);
const varKey = ref<string>('');
const varName = ref<string>('');
const varType = ref<'analog' | 'digital'>('analog');
const varDataType = ref<string>('Float');
const varUnit = ref<string>('');
const varMin = ref<number>(0);
const varMax = ref<number>(100);
const varAddress = ref<string>('');
const varDesc = ref<string>('');

// Allowed dataType options dependent on active device format
const dataTypeOptions = computed(() => {
  if (!currentModel.value) return [];
  const protocol = currentModel.value.type;
  if (protocol === 'S7') {
    return [
      { label: 'BOOL (布尔点位)', value: 'BOOL', type: 'digital' },
      { label: 'INT (16位带符号整型)', value: 'INT', type: 'analog' },
      { label: 'DINT (32位带符号整型)', value: 'DINT', type: 'analog' },
      { label: 'REAL (32位单精度浮点数)', value: 'REAL', type: 'analog' },
      { label: 'BYTE (8位无符号字节)', value: 'BYTE', type: 'analog' },
      { label: 'WORD (16位无符号字类型)', value: 'WORD', type: 'analog' },
      { label: 'CHAR (单字符字段)', value: 'CHAR', type: 'analog' },
      { label: 'STRING (变长字符串型)', value: 'STRING', type: 'analog' }
    ];
  } else if (protocol === 'OPCUA') {
    return [
      { label: 'Boolean (布尔触发类)', value: 'Boolean', type: 'digital' },
      { label: 'Int16 (双字节带符号整型)', value: 'Int16', type: 'analog' },
      { label: 'Int32 (四字节带符号整型)', value: 'Int32', type: 'analog' },
      { label: 'UInt16 (16位无符号整型)', value: 'UInt16', type: 'analog' },
      { label: 'UInt32 (32位无符号整型)', value: 'UInt32', type: 'analog' },
      { label: 'Float (单精度浮点数)', value: 'Float', type: 'analog' },
      { label: 'Double (双精度高精密浮点数)', value: 'Double', type: 'analog' },
      { label: 'String (通信文本字符串)', value: 'String', type: 'analog' },
      { label: 'DateTime (工业时间戳常数)', value: 'DateTime', type: 'analog' }
    ];
  } else if (protocol === 'MQTT') {
    return [
      { label: 'Boolean (消息布尔值)', value: 'Boolean', type: 'digital' },
      { label: 'Integer (32位消息整型)', value: 'Integer', type: 'analog' },
      { label: 'Float (高精密消息浮点数)', value: 'Float', type: 'analog' },
      { label: 'String (常规JSON通信文本)', value: 'String', type: 'analog' }
    ];
  } else {
    return [
      { label: 'Boolean (虚拟布尔)', value: 'Boolean', type: 'digital' },
      { label: 'Integer (虚拟整型数)', value: 'Integer', type: 'analog' },
      { label: 'Float (虚拟浮点数值)', value: 'Float', type: 'analog' },
      { label: 'String (虚拟文本字段)', value: 'String', type: 'analog' }
    ];
  }
});

// Watch showVarModal or currentModel changes to initialize matching dataType and type
watch([showVarModal, currentModel], () => {
  if (showVarModal.value && currentModel.value) {
    const opts = dataTypeOptions.value;
    if (opts.length > 0) {
      // Find a datatype that fits current simulated values if switching
      varDataType.value = opts[0].value;
      varType.value = opts[0].type as 'analog' | 'digital';
    }
  }
});

// Sync type automatically when dataType changes
const handleDataTypeChange = () => {
  const opt = dataTypeOptions.value.find(o => o.value === varDataType.value);
  if (opt) {
    varType.value = opt.type as 'analog' | 'digital';
  }
};

const totalVariableCount = computed(() => {
  return currentModel.value ? currentModel.value.variables.length : 0;
});

// Advanced variables search state
const varSearchQuery = ref<string>('');

// S7 variables custom state properties
const varAccessLevel = ref<'RO' | 'RW'>('RW');
const varScaleExpr = ref<string>('x * 1.0');
const varIsStored = ref<boolean>(true);
const varStoreMode = ref<'Change' | 'Cycle'>('Change');

// OPCUA custom state properties
const varUpdateMode = ref<'subscription' | 'polling'>('subscription');

// Common polling timers (S7, OPCUA polling, MQTT variables)
const varPollIntervalSecs = ref<number>(5);

// 工业级增强字段
const varBitOffset = ref<number | null>(null);
const varScaleSlope = ref<number>(1.0);
const varScaleOffset = ref<number>(0.0);
const varDeadBand = ref<number | null>(null);
const varIsReadOnly = ref<boolean>(true);

// Filtered variables for search
const filteredVariables = computed(() => {
  if (!currentModel.value) return [];
  const query = varSearchQuery.value.trim().toLowerCase();
  if (!query) return currentModel.value.variables;
  return currentModel.value.variables.filter(v => 
    v.key.toLowerCase().includes(query) || 
    v.name.toLowerCase().includes(query) || 
    (v.description && v.description.toLowerCase().includes(query)) ||
    (v.address && v.address.toLowerCase().includes(query))
  );
});

// Create standard new model schema
const handleCreateModel = async () => {
  if (!modelName.value.trim()) return;

  const newModel = await createDataModelOnBackend({
    name: modelName.value,
    description: modelDesc.value,
    type: modelType.value as any,
    variables: []
  });

  if (newModel) {
    selectedModelId.value = newModel.id;
    showModelModal.value = false;

    // Clear
    modelName.value = '';
    modelDesc.value = '';
  }
};

// Delete model template
const handleDeleteModel = async (id: string, name: string) => {
  // Check if any devices rely on it
  const reliesCount = devices.value.filter(d => d.modelId === id).length;
  if (reliesCount > 0) {
    alert(`无法删除此模型 [${name}]: 仍然有 ${reliesCount} 台在网物理设备实例依赖于该模型。`);
    return;
  }
  
  const success = await deleteDataModelOnBackend(id);
  if (success) {
    addLog('模型建立', `删除数据模型 [${name}]`, 'warning');
    selectedModelId.value = dataModels.value[0]?.id || '';
  }
};

// Append tag to current selected model variables
const handleSaveVariable = async () => {
  if (!varKey.value.trim() || !varName.value.trim()) return;
  const model = currentModel.value;
  if (!model) return;

  // Check unique key
  if (model.variables.some(v => v.key === varKey.value)) {
    alert('变量 Key 在该数据模型中已存在, 请确认后再试。');
    return;
  }

  const newVar: ModelVariable = {
    modelId: Number(model.id),
    key: varKey.value,
    name: varName.value,
    type: varType.value,
    dataType: varDataType.value as DataTypeEnum,
    unit: varUnit.value || undefined,
    min: varMin.value || undefined,
    max: varMax.value || undefined,
    address: varAddress.value || `${varKey.value.toUpperCase()}_ADDR`,
    description: varDesc.value || undefined,
    isStored: varIsStored.value,
    storeMode: varStoreMode.value,
    updateMode: varUpdateMode.value,
    pollingIntervalMs: Number(varPollIntervalSecs.value) * 1000,
    bitOffset: varBitOffset.value,
    scaleSlope: varScaleSlope.value,
    scaleOffset: varScaleOffset.value,
    deadBand: varDeadBand.value,
    isReadOnly: varIsReadOnly.value,
    extensionData: {
      accessLevel: varAccessLevel.value,
      scaleExpr: varScaleExpr.value
    }
  };

  // Persist to server using new variable API
  try {
    await createVariable(newVar);
    // Refresh model list to ensure consistency with backend
    await fetchDataModelsFromBackend();
  } catch (err: any) {
    alert('保存变量到服务器失败: ' + err.message);
    return;
  }

  // Synchronize new variable in all existing online devices relying on this model!
  devices.value.forEach((d) => {
    if (d.modelId === model.id) {
      if (d.variables[varKey.value] === undefined) {
        d.variables[varKey.value] = varType.value === 'digital' ? false : varMin.value;
      }
    }
  });

  addLog('模型建立', `模型 [${model.name}] 添加变量 [${varName.value}]`, 'normal');

  // Clear states
  varKey.value = '';
  varName.value = '';
  varUnit.value = '';
  varAddress.value = '';
  varDesc.value = '';
  
  // Clear advanced connection details
  varAccessLevel.value = 'RW';
  varScaleExpr.value = 'x * 1.0';
  varIsStored.value = true;
  varStoreMode.value = 'Change';
  varUpdateMode.value = 'subscription';
  varPollIntervalSecs.value = 5;
  
  // Clear industrial-grade fields
  varBitOffset.value = null;
  varScaleSlope.value = 1.0;
  varScaleOffset.value = 0.0;
  varDeadBand.value = null;
  varIsReadOnly.value = true;
  
  showVarModal.value = false;
};

// Delete a variable mapping from the active data blueprint
const handleDeleteVariable = (key: string, name: string) => {
  const model = currentModel.value;
  if (!model) return;

  model.variables = model.variables.filter(v => v.key !== key);
  
  // Clean up in device instances
  devices.value.forEach((d) => {
    if (d.modelId === model.id) {
      delete d.variables[key];
    }
  });

  addLog('模型建立', `模型 [${model.name}] 删除变量 [${name}]`, 'warning');
};
</script>

<template>
  <div class="h-full overflow-y-auto md:overflow-y-hidden flex flex-col md:flex-row text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">
    
    <!-- LEFT LIST: Models directories -->
    <div class="w-full md:w-80 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex flex-col shrink-0 flex-1 md:flex-none transition-colors">
      
      <div class="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
        <div class="flex items-center gap-1.5 font-bold text-sm text-slate-900 dark:text-white">
          <Layers class="w-4 h-4 text-violet-500" />
          <span>数据模型</span>
        </div>

        <button 
          @click="showModelModal = true"
          class="p-1 rounded bg-[#1890ff] hover:bg-sky-600 text-white cursor-pointer transition-all"
          title="新建模型"
        >
          <Plus class="w-4 h-4" />
        </button>
      </div>

      <div class="flex-1 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800 max-h-[220px] md:max-h-none text-left">
        <div 
          v-for="model in dataModels" 
          :key="model.id"
          @click="selectedModelId = model.id"
          class="p-4 cursor-pointer hover:bg-slate-50/50 dark:hover:bg-slate-800/40 transition-all space-y-1.5"
          :class="selectedModelId === model.id ? 'bg-violet-50/40 dark:bg-violet-950/30 border-r-4 border-r-violet-600' : ''"
        >
          <div class="flex items-center justify-between">
            <span class="text-[9px] font-mono font-bold bg-violet-50 dark:bg-violet-950/60 text-violet-600 dark:text-violet-400 px-1.5 py-0.5 rounded uppercase">
              {{ model.type }} 协议
            </span>
            <span class="text-[9px] font-mono text-slate-400 dark:text-slate-500">ID: {{ model.id }}</span>
          </div>
          <h4 class="font-bold text-xs text-slate-800 dark:text-slate-200 leading-tight block">
            {{ model.name }}
          </h4>
          <p class="text-[10px] text-slate-400 dark:text-slate-500 line-clamp-1 font-sans font-normal">
            {{ model.description }}
          </p>
        </div>
      </div>
    </div>

    <!-- RIGHT PANEL: Schema detail table and live append -->
    <div class="flex-1 flex flex-col bg-slate-50/50 dark:bg-transparent text-left min-w-0">
      
      <div v-if="currentModel" class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm flex flex-col sm:flex-row sm:items-center justify-between gap-4 transition-colors">
        <div class="space-y-1">
          <div class="flex items-center gap-2">
            <h2 class="font-bold text-base text-slate-950 dark:text-white font-sans tracking-tight">
              {{ currentModel.name }}
            </h2>
            <span class="bg-violet-50 dark:bg-violet-950/60 text-violet-600 dark:text-violet-400 border border-violet-100 dark:border-violet-800 text-[10px] uppercase font-mono font-bold px-1.5 py-0.5 rounded leading-none">
              {{ currentModel.type }} 架构
            </span>
          </div>
          <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
            {{ currentModel.description }}
          </p>
        </div>

        <div class="flex items-center gap-2 shrink-0">
          <button 
            @click="showVarModal = true"
            class="bg-violet-600 hover:bg-violet-700 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all shadow-sm"
          >
            <Plus class="w-4 h-4" />
            添加变量
          </button>
          
          <button 
            @click="handleDeleteModel(currentModel.id, currentModel.name)"
            class="text-rose-600 dark:text-rose-400 hover:text-rose-800 dark:hover:text-rose-300 border border-rose-100 dark:border-rose-900/60 font-bold text-xs px-2.5 py-1.5 rounded-lg bg-rose-50 dark:bg-rose-950/40 cursor-pointer transition-all"
            title="删除模型"
          >
            <Trash2 class="w-4 h-4" />
          </button>
        </div>
      </div>

      <!-- Variables template viewer table -->
      <div class="flex-1 p-5 md:overflow-y-auto overflow-y-visible space-y-4">
        
        <div v-if="currentModel" class="space-y-4">
          <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 bg-white dark:bg-slate-900 p-3 rounded-xl border border-slate-200 dark:border-slate-800 transition-colors">
            <div class="flex items-center gap-1.5 text-xs font-bold text-slate-500 dark:text-slate-400 tracking-wider uppercase">
              <Sliders class="w-4 h-4 text-violet-500" />
              <span>共 <b class="text-indigo-600 dark:text-indigo-400">{{ totalVariableCount }}</b> 个变量</span>
            </div>
            
            <div class="relative w-full sm:w-64 shrink-0">
              <input 
                v-model="varSearchQuery"
                type="text"
                placeholder="搜索变量..."
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg px-3 py-1.5 text-xs focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-violet-500 placeholder-slate-400 font-sans"
              />
            </div>
          </div>

          <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm transition-colors">
            <table class="w-full text-xs font-mono divide-y divide-slate-100 dark:divide-slate-800">
              <thead>
                <tr class="bg-slate-50 dark:bg-slate-950/60 text-slate-400 dark:text-slate-500 font-bold text-[10px] uppercase tracking-wider">
                  <th class="px-4 py-3.5">标识</th>
                  <th class="px-4 py-3.5">名称</th>
                  <th class="px-4 py-3.5">类型</th>
                  <th class="px-4 py-3.5">单位</th>
                  <th class="px-4 py-3.5">地址</th>
                  <th class="px-4 py-3.5 text-right">操作</th>
                </tr>
              </thead>
              <tbody class="bg-white dark:bg-slate-900 divide-y divide-slate-100 dark:divide-slate-800 font-mono">
                <tr 
                  v-for="v in filteredVariables" 
                  :key="v.key"
                  class="hover:bg-slate-50/50 dark:hover:bg-slate-800/40 transition-all text-left"
                >
                  <td class="px-4 py-4 font-bold text-violet-700 dark:text-violet-400">
                    <span class="flex items-center gap-1">
                      <Binary class="w-3.5 h-3.5 text-violet-400" />
                      {{ v.key }}
                    </span>
                  </td>
                  <td class="px-4 py-4 font-sans font-medium">
                    <span class="text-slate-800 dark:text-slate-200 font-bold block">{{ v.name }}</span>
                    <span class="block text-[10px] font-mono text-slate-400 dark:text-slate-500 font-normal leading-relaxed mt-0.5">{{ v.description }}</span>
                    
                    <!-- Protocol advanced variable badges -->
                    <div v-if="currentModel.type === 'S7'" class="flex flex-wrap gap-1 mt-1.5 select-none text-[9px] font-sans">
                      <span class="bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-700 px-1.5 py-0.5 rounded" title="读写特权">特权: {{ v.extensionData?.accessLevel || 'RW' }}</span>
                      <span class="bg-violet-50 dark:bg-violet-950/60 text-violet-700 dark:text-violet-300 border border-violet-100 dark:border-violet-800 px-1.5 py-0.5 rounded" title="放缩公式">放缩: {{ v.extensionData?.scaleExpr || 'x' }}</span>
                      <span class="bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 border border-emerald-100 dark:border-emerald-800 px-1.5 py-0.5 rounded animate-pulse" title="时序库存储">
                        {{ v.isStored !== false ? '写入TSDB' : '仅内存变量' }} · {{ v.storeMode === 'Change' ? '变动存' : '定时存' }}
                      </span>
                    </div>

                    <div v-else-if="currentModel.type === 'OPCUA'" class="flex flex-wrap gap-1 mt-1.5 select-none text-[9px] font-sans">
                      <span class="bg-sky-50 dark:bg-sky-950/60 text-sky-700 dark:text-sky-300 border border-sky-100 dark:border-sky-800 px-1.5 py-0.5 rounded font-mono">NodeId: {{ v.address }}</span>
                      <span class="bg-slate-50 dark:bg-slate-800 text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-700 px-1.5 py-0.5 rounded">
                        更新: {{ v.updateMode === 'subscription' ? '协议订阅' : `轮询 (${v.pollingIntervalMs ? v.pollingIntervalMs / 1000 : 5}s)` }}
                      </span>
                    </div>

                    <div v-else-if="currentModel.type === 'MQTT'" class="flex flex-wrap gap-1 mt-1.5 select-none text-[9px] font-sans">
                      <span class="bg-teal-50 dark:bg-teal-950/60 text-teal-700 dark:text-teal-300 border border-teal-100 dark:border-teal-800 px-1.5 py-0.5 rounded font-mono">刷新周期: {{ v.pollingIntervalMs ? v.pollingIntervalMs / 1000 : 5 }}s (MQTT Polling)</span>
                    </div>
                  </td>
                  <td class="px-4 py-4">
                    <span 
                      v-if="v.dataType"
                      class="px-2 py-0.5 rounded text-[10.5px] font-bold font-mono border shadow-3xs tracking-wider uppercase"
                      :class="v.type === 'digital' ? 
                        (currentModel.type === 'S7' ? 'bg-indigo-50 dark:bg-indigo-950/60 text-indigo-700 dark:text-indigo-300 border-indigo-200 dark:border-indigo-800' : 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800') : 
                        (currentModel.type === 'S7' ? 'bg-indigo-50/70 dark:bg-indigo-950/40 text-indigo-600 dark:text-indigo-400 border-indigo-200 dark:border-indigo-800' : 'bg-sky-50 dark:bg-sky-950/60 text-sky-700 dark:text-sky-300 border-sky-200 dark:border-sky-800')"
                    >
                      {{ v.dataType }}
                    </span>
                    <span 
                      v-else
                      class="px-1.5 py-0.5 rounded text-[10px] font-bold border"
                      :class="v.type === 'digital' ? 'bg-teal-50 dark:bg-teal-950/60 text-teal-600 dark:text-teal-300 border-teal-100 dark:border-teal-800' : 'bg-blue-50 dark:bg-blue-950/60 text-blue-600 dark:text-blue-300 border-blue-100 dark:border-blue-800'"
                    >
                      {{ v.type === 'digital' ? 'Boolean' : 'Analog' }}
                    </span>
                  </td>
                  <td class="px-4 py-4 text-slate-600 dark:text-slate-300 font-bold">{{ v.unit || '无' }}</td>
                  <td class="px-4 py-4 text-slate-500 dark:text-slate-400 font-bold text-[11px]">{{ v.address }}</td>
                  <td class="px-4 py-4 text-right">
                    <button 
                      @click="handleDeleteVariable(v.key, v.name)"
                      class="p-1 rounded bg-slate-50 dark:bg-slate-800 hover:bg-rose-50 dark:hover:bg-rose-950/40 text-slate-400 dark:text-slate-400 hover:text-rose-600 dark:hover:text-rose-400 transition-all cursor-pointer"
                      title="删除此字段"
                    >
                      <Trash2 class="w-3.5 h-3.5" />
                    </button>
                  </td>
                </tr>

                <tr v-if="currentModel.variables.length === 0">
                  <td colspan="6" class="p-8 text-center text-slate-400 dark:text-slate-500 font-sans">
                    暂无变量，点击"添加变量"创建
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div v-else class="h-64 flex flex-col items-center justify-center text-slate-400 dark:text-slate-500">
          <Layers class="w-8 h-8 text-slate-300 dark:text-slate-600 mb-2" />
          <p class="text-xs">请注册或选择一个变量数据模型</p>
        </div>
      </div>
    </div>

    <!-- MODAL: CREATE BRAND NEW DATA BLUEPRINT MODEL -->
    <div v-if="showModelModal" class="fixed inset-0 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <FileJson class="w-4 h-4 text-violet-400" />
            <span>新建数据模型</span>
          </div>
          <button @click="showModelModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">模型名称</label>
            <input 
              v-model="modelName"
              type="text"
              placeholder="例如: S7-1200 离心水冷泵模板"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-violet-500"
            />
          </div>
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">协议类型</label>
            <select 
              v-model="modelType"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white font-bold focus:outline-none focus:border-violet-500"
            >
              <option value="OPCUA">OPC UA</option>
              <option value="S7">Siemens S7</option>
              <option value="MQTT">MQTT</option>
              <option value="Virtual">Virtual</option>
            </select>
          </div>
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">描述</label>
            <textarea 
              v-model="modelDesc"
              rows="2"
              placeholder="如：西门子S7全系列可重用温压模型说明..."
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-violet-500 leading-relaxed"
            />
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800">
          <button 
            @click="showModelModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleCreateModel"
            class="px-4 py-1.5 rounded-lg bg-slate-900 dark:bg-violet-600 hover:bg-slate-800 dark:hover:bg-violet-500 font-bold text-xs text-white cursor-pointer"
          >
            保存
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: ADD VARIABLE / PLCTAG TO DATA MODEL -->
    <div v-if="showVarModal" class="fixed inset-0 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Tag class="w-4 h-4 text-[#1890ff]" />
            <span>添加变量</span>
          </div>
          <button @click="showVarModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs overflow-y-auto max-h-[400px]">
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">变量标识</label>
              <input 
                v-model="varKey"
                type="text"
                placeholder="例如: boiler_temp"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
              />
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">变量名称</label>
              <input 
                v-model="varName"
                type="text"
                placeholder="例如: 炉顶极限水套实温"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
              />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">数据类型</label>
              <select 
                v-model="varDataType"
                @change="handleDataTypeChange"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none font-bold"
              >
                <option v-for="opt in dataTypeOptions" :key="opt.value" :value="opt.value">
                  {{ opt.label }}
                </option>
              </select>
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">单位</label>
              <input 
                v-model="varUnit"
                type="text"
                placeholder="例如: ℃, kPa, rpm, %"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
              />
            </div>
          </div>

          <!-- Analog ranges -->
          <div v-if="varType === 'analog'" class="grid grid-cols-2 gap-3 p-3 bg-slate-50 dark:bg-slate-950/70 rounded-lg border border-slate-100 dark:border-slate-800">
            <div>
              <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">传感器下限 (Min)</label>
              <input 
                v-model="varMin"
                type="number"
                class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 focus:outline-none font-mono text-slate-800 dark:text-white"
              />
            </div>
            <div>
              <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">传感器上限 (Max)</label>
              <input 
                v-model="varMax"
                type="number"
                class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 focus:outline-none font-mono text-slate-800 dark:text-white"
              />
            </div>
          </div>

          <!-- Siemens S7 Specific Variable Fields -->
          <div v-if="currentModel && currentModel.type === 'S7'" class="p-3 bg-indigo-50/50 dark:bg-indigo-950/40 rounded-xl space-y-3.5 border border-indigo-100/50 dark:border-indigo-800 text-indigo-950 dark:text-indigo-200">
            <div class="font-bold text-[10px] text-indigo-700 dark:text-indigo-400 uppercase tracking-wider">S7 寄存器配置</div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">寄存器地址</label>
              <input
                v-model="varAddress"
                type="text"
                placeholder="例如: DB10.DBD12, MB0, IW0"
                class="w-full bg-white dark:bg-slate-900 border border-indigo-200 dark:border-indigo-700 rounded p-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800 dark:text-white"
              />
            </div>
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">访问权限</label>
                <select
                  v-model="varAccessLevel"
                  class="w-full bg-white dark:bg-slate-900 border border-indigo-200 dark:border-indigo-700 rounded p-1.5 focus:outline-none text-xs font-sans font-bold text-slate-800 dark:text-white"
                >
                  <option value="RW">读写</option>
                  <option value="RO">只读</option>
                </select>
              </div>
            </div>

            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">放缩公式</label>
              <input
                v-model="varScaleExpr"
                type="text"
                placeholder="例如: x * 0.1 | x * 1.5 - 20"
                class="w-full bg-white dark:bg-slate-900 border border-indigo-200 dark:border-indigo-700 rounded p-1.5 focus:outline-none text-xs font-mono font-bold text-indigo-700 dark:text-indigo-400"
              />
            </div>

            <div class="flex items-center justify-between py-1 border-t border-indigo-100 dark:border-indigo-800 mt-2">
              <label class="flex items-center gap-1.5 font-bold text-slate-700 dark:text-slate-300 cursor-pointer text-xs">
                <input type="checkbox" v-model="varIsStored" class="rounded text-indigo-600 focus:ring-0" />
                历史存储
              </label>
              <select
                v-if="varIsStored"
                v-model="varStoreMode"
                class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px] font-bold text-slate-600 dark:text-slate-300 focus:outline-none"
              >
                <option value="Change">变动存储</option>
                <option value="Cycle">定时存储</option>
              </select>
            </div>
          </div>

          <!-- OPCUA Specific Variable Fields -->
          <div v-if="currentModel && currentModel.type === 'OPCUA'" class="p-3 bg-sky-50/50 dark:bg-sky-950/40 rounded-xl space-y-3 border border-sky-100/50 dark:border-sky-800">
            <div class="font-bold text-[10px] text-sky-700 dark:text-sky-400 uppercase tracking-wider">OPC UA 配置</div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">节点ID (地址)</label>
              <input
                v-model="varAddress"
                type="text"
                placeholder="例如: ns=2;s=Line1.Temperature"
                class="w-full bg-white dark:bg-slate-900 border border-sky-200 dark:border-sky-700 rounded p-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800 dark:text-white"
              />
            </div>
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">更新模式</label>
                <select
                  v-model="varUpdateMode"
                  class="w-full bg-white dark:bg-slate-900 border border-sky-200 dark:border-sky-700 rounded p-1.5 focus:outline-none text-xs font-sans text-slate-800 dark:text-white font-medium"
                >
                  <option value="subscription">实时订阅</option>
                  <option value="polling">定时轮询</option>
                </select>
              </div>
              <div>
                <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">采样周期 (秒)</label>
                <input
                  v-model="varPollIntervalSecs"
                  type="number"
                  min="1"
                  max="60"
                  class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded p-1.5 focus:outline-none text-xs font-mono text-slate-800 dark:text-white"
                />
              </div>
            </div>
          </div>

          <!-- MQTT Specific Variable Fields -->
          <div v-if="currentModel && currentModel.type === 'MQTT'" class="p-3 bg-teal-50/50 dark:bg-teal-950/40 rounded-xl space-y-3 border border-teal-100 dark:border-teal-800 text-teal-900 dark:text-teal-200">
            <div class="font-bold text-[10px] text-teal-800 dark:text-teal-400 uppercase tracking-wider">MQTT 配置</div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">主题地址</label>
              <input
                v-model="varAddress"
                type="text"
                placeholder="例如: factory/line1/temperature"
                class="w-full bg-white dark:bg-slate-900 border border-teal-200 dark:border-teal-700 rounded p-1.5 focus:outline-none text-xs font-mono text-slate-800 dark:text-white"
              />
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">刷新周期 (秒)</label>
              <input
                v-model="varPollIntervalSecs"
                type="number"
                min="1"
                class="w-full bg-white dark:bg-slate-900 border border-teal-200 dark:border-teal-700 rounded p-1.5 focus:outline-none text-xs font-mono text-slate-800 dark:text-white"
              />
              <p class="text-[9px] text-slate-400 dark:text-slate-500 mt-1">设置变量在 MQTT 主题刷新消息提取解析的定时器速度 (秒)</p>
            </div>
          </div>

          <!-- Industrial-grade Enhanced Fields -->
          <div class="p-3 bg-orange-50/50 dark:bg-orange-950/40 rounded-xl space-y-3 border border-orange-100 dark:border-orange-800">
            <div class="font-bold text-[10px] text-orange-700 dark:text-orange-400 uppercase tracking-wider">工业级参数</div>
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">位偏移</label>
                <input 
                  v-model="varBitOffset"
                  type="number"
                  min="0"
                  max="15"
                  placeholder="0-15"
                  class="w-full bg-white dark:bg-slate-900 border border-orange-200 dark:border-orange-700 rounded p-1.5 focus:outline-none text-xs font-mono text-slate-800 dark:text-white"
                />
              </div>
              <div>
                <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">只读模式</label>
                <select 
                  v-model="varIsReadOnly"
                  class="w-full bg-white dark:bg-slate-900 border border-orange-200 dark:border-orange-700 rounded p-1.5 focus:outline-none text-xs font-sans text-slate-800 dark:text-white"
                >
                  <option :value="true">只读</option>
                  <option :value="false">可写</option>
                </select>
              </div>
            </div>
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">缩放斜率</label>
                <input 
                  v-model="varScaleSlope"
                  type="number"
                  step="0.01"
                  placeholder="1.0"
                  class="w-full bg-white dark:bg-slate-900 border border-orange-200 dark:border-orange-700 rounded p-1.5 focus:outline-none text-xs font-mono text-slate-800 dark:text-white"
                />
              </div>
              <div>
                <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">缩放偏移</label>
                <input 
                  v-model="varScaleOffset"
                  type="number"
                  step="0.01"
                  placeholder="0.0"
                  class="w-full bg-white dark:bg-slate-900 border border-orange-200 dark:border-orange-700 rounded p-1.5 focus:outline-none text-xs font-mono text-slate-800 dark:text-white"
                />
              </div>
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-0.5">死区阈值</label>
              <input 
                v-model="varDeadBand"
                type="number"
                step="0.001"
                placeholder="变化超过此值才触发更新"
                class="w-full bg-white dark:bg-slate-900 border border-orange-200 dark:border-orange-700 rounded p-1.5 focus:outline-none text-xs font-mono text-slate-800 dark:text-white"
              />
            </div>
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">描述</label>
            <input 
              v-model="varDesc"
              type="text"
              placeholder="热敏管端阻值防冻监测用..."
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
            />
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-4 border-t border-slate-100 dark:border-slate-800 flex justify-end gap-2">
          <button 
            @click="showVarModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleSaveVariable"
            class="px-4 py-1.5 rounded-lg bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white cursor-pointer"
          >
            保存
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
