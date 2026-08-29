<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue';
import { dataModels, devices, addLog, createDataModelOnBackend, updateDataModelOnBackend, deleteDataModelOnBackend, fetchDataModelsFromBackend, fetchProtocols, createVariable, updateVariable, deleteVariable, exportVariables } from '../store/index';
import { DataModel, ModelVariable, DeviceType, DataTypeEnum, Protocol, protocolKeyToDeviceType } from '../types';
import VariableImportDialog from './VariableImportDialog.vue';

onMounted(() => {
  fetchDataModelsFromBackend();
  fetchProtocols().then(list => { protocols.value = list; });
});

// 协议下拉数据源（协议/驱动解耦，来自 /api/Protocol，作为数据模型协议选择）
const protocols = ref<Protocol[]>([]);

import { 
  FileCode, 
  Layers, 
  Cpu, 
  Trash2, 
  Plus, 
  Pencil,
  Tag, 
  Sliders, 
  X,
  FileJson,
  Binary,
  ChevronDown,
  Check,
  Upload,
  Download
} from 'lucide-vue-next';

// Mobile Drawer state
const isMobileModelDrawerOpen = ref<boolean>(false);

// Active selection
const selectedModelId = ref<string>(dataModels.value[0]?.id || '');

const currentModel = computed(() => {
  return dataModels.value.find(m => m.id === selectedModelId.value) || dataModels.value[0];
});

// 当前模型的协议类型：协议真相源在 Protocol 实体（protocolKey），
// 新建模型即使未绑定任何设备也有明确协议，不再臆造 Virtual。
const currentModelProtocol = computed<DeviceType>(() => {
  return protocolKeyToDeviceType(currentModel.value?.protocolKey);
});

// 列表项协议徽章：直接读模型自带的 protocolKey 派生协议类型
const protocolOf = (model: { id: string; protocolKey?: string }) => {
  const m = dataModels.value.find(x => x.id === model.id);
  return protocolKeyToDeviceType(m?.protocolKey);
};

// Create model form state
const showModelModal = ref<boolean>(false);
const modelName = ref<string>('');
const modelDesc = ref<string>('');
const modelProtocolId = ref<number>(0);

// Create variable form state Inside Model
const showVarModal = ref<boolean>(false);
// 正在编辑的变量 ID（null = 创建模式）；编辑模式禁止修改 Key（DeviceKey+VariableKey 全局身份元组的一部分）
const editingVariableId = ref<number | null>(null);
const varKey = ref<string>('');
const varName = ref<string>('');
const varType = ref<'analog' | 'digital'>('analog');
const varDataType = ref<string>('Float');
const varUnit = ref<string>('');
// number 输入框清空后 v-model 运行时值为 ''，类型放宽以匹配真实值（提交时再归一）
const varMin = ref<number | ''>(0);
const varMax = ref<number | ''>(100);
const varDesc = ref<string>('');

// Allowed dataType options dependent on active device format
// value 统一使用后端 DataTypeEnum 真实枚举名(INT/REAL/BOOL/...)，
// 由后端 DataTypeEnumJsonConverter 做别名容错，避免前后端命名不一致导致 400。
const dataTypeOptions = computed(() => {
  if (!currentModel.value) return [];
  const protocol = currentModelProtocol.value;
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
      { label: 'BOOL (布尔触发类)', value: 'BOOL', type: 'digital' },
      { label: 'INT (16位带符号整型)', value: 'INT', type: 'analog' },
      { label: 'DINT (32位带符号整型)', value: 'DINT', type: 'analog' },
      { label: 'UINT16 (16位无符号整型)', value: 'UINT16', type: 'analog' },
      { label: 'UINT32 (32位无符号整型)', value: 'UINT32', type: 'analog' },
      { label: 'FLOAT (单精度浮点数)', value: 'FLOAT', type: 'analog' },
      { label: 'DOUBLE (双精度高精密浮点数)', value: 'DOUBLE', type: 'analog' },
      { label: 'STRING (通信文本字符串)', value: 'STRING', type: 'analog' }
    ];
  } else if (protocol === 'MQTT') {
    return [
      { label: 'BOOL (消息布尔值)', value: 'BOOL', type: 'digital' },
      { label: 'DINT (32位消息整型)', value: 'DINT', type: 'analog' },
      { label: 'FLOAT (高精密消息浮点数)', value: 'FLOAT', type: 'analog' },
      { label: 'STRING (常规JSON通信文本)', value: 'STRING', type: 'analog' }
    ];
  } else {
    return [
      { label: 'BOOL (虚拟布尔)', value: 'BOOL', type: 'digital' },
      { label: 'INT (虚拟整型数)', value: 'INT', type: 'analog' },
      { label: 'FLOAT (虚拟浮点数值)', value: 'FLOAT', type: 'analog' },
      { label: 'STRING (虚拟文本字段)', value: 'STRING', type: 'analog' }
    ];
  }
});

// Watch showVarModal or currentModel changes to initialize matching dataType and type
// 仅创建模式重置默认数据类型；编辑模式须保留 openEditVariable 预填值不被覆盖
watch([showVarModal, currentModel], () => {
  if (showVarModal.value && currentModel.value && editingVariableId.value === null) {
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
const varStoreMode = ref<'None' | 'Change' | 'Cycle' | 'Compressed' | 'Aggregated'>('Change');
const varStoreIntervalMs = ref<number | ''>(300000);

// OPCUA custom state properties
const varUpdateMode = ref<'subscription' | 'polling'>('subscription');

// 工业级增强字段（地址/位偏移/采集周期已下放至设备实例级 DeviceVariable，模板层不再维护）
const varScaleSlope = ref<number | ''>(1.0);
const varScaleOffset = ref<number | ''>(0.0);
const varDeadBand = ref<number | null | ''>(null);
const varIsReadOnly = ref<boolean>(true);

// Filtered variables for search
const filteredVariables = computed(() => {
  if (!currentModel.value) return [];
  const query = varSearchQuery.value.trim().toLowerCase();
  if (!query) return currentModel.value.variables;
  return currentModel.value.variables.filter(v => 
    v.key.toLowerCase().includes(query) || 
    v.name.toLowerCase().includes(query) || 
    (v.description && v.description.toLowerCase().includes(query))
  );
});

// Create standard new model schema
const handleCreateModel = async () => {
  if (!modelName.value.trim()) return;

  // 协议真相源在 Protocol 实体：创建模型必须选择协议（ProtocolId 必填）
  if (!modelProtocolId.value) {
    alert('请选择通信协议');
    return;
  }

  const newModel = await createDataModelOnBackend({
    name: modelName.value,
    description: modelDesc.value,
    protocolId: modelProtocolId.value,
    variables: []
  });

  if (newModel) {
    selectedModelId.value = newModel.id;
    showModelModal.value = false;

    // Clear
    modelName.value = '';
    modelDesc.value = '';
    modelProtocolId.value = 0;
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

// 与后端 ModelVariableDto 的 DataAnnotations 保持一致的前端校验
const KEY_PATTERN = /^[a-zA-Z0-9_]+$/;

// 打开编辑弹窗：将既有变量各字段回填至表单（含协议专属/存储/工业级字段）
const openEditVariable = (v: ModelVariable) => {
  editingVariableId.value = v.id;
  varKey.value = v.key;
  varName.value = v.name;
  varDataType.value = v.dataType;
  varType.value = v.type === 'digital' ? 'digital' : 'analog';
  varUnit.value = v.unit || '';
  varMin.value = v.min ?? '';
  varMax.value = v.max ?? '';
  varDesc.value = v.description || '';
  // S7 专属（extensionData）
  varAccessLevel.value = (v.extensionData?.accessLevel as 'RO' | 'RW') || 'RW';
  varScaleExpr.value = v.extensionData?.scaleExpr || 'x * 1.0';
  // 历史存储
  varIsStored.value = v.isStored !== false && v.storeMode !== 'None';
  varStoreMode.value = v.storeMode && v.storeMode !== 'None' ? v.storeMode : 'Change';
  varStoreIntervalMs.value = v.storeIntervalMs ?? 300000;
  // OPCUA / MQTT
  varUpdateMode.value = v.updateMode || 'subscription';
  // 工业级参数
  varScaleSlope.value = v.scaleSlope ?? 1.0;
  varScaleOffset.value = v.scaleOffset ?? 0.0;
  varDeadBand.value = v.deadBand ?? null;
  varIsReadOnly.value = v.isReadOnly ?? true;
  showVarModal.value = true;
};

// 重置表单为创建模式默认值
const resetVarForm = () => {
  varKey.value = '';
  varName.value = '';
  varUnit.value = '';
  varDesc.value = '';
  varMin.value = 0;
  varMax.value = 100;
  varAccessLevel.value = 'RW';
  varScaleExpr.value = 'x * 1.0';
  varIsStored.value = true;
  varStoreMode.value = 'Change';
  varStoreIntervalMs.value = 300000;
  varUpdateMode.value = 'subscription';
  varScaleSlope.value = 1.0;
  varScaleOffset.value = 0.0;
  varDeadBand.value = null;
  varIsReadOnly.value = true;
};

// 关闭变量弹窗：清空编辑态并复位表单，避免下次"添加变量"残留编辑预填值
const closeVarModal = () => {
  showVarModal.value = false;
  editingVariableId.value = null;
  resetVarForm();
};

// Append tag to current selected model variables
const handleSaveVariable = async () => {
  // 统一 trim，避免首尾空格在本地查重与后端正则间产生不一致
  const key = varKey.value.trim();
  const name = varName.value.trim();
  if (!key || !name) return;

  const isEditing = editingVariableId.value !== null;

  // 前置校验（与后端 [RegularExpression] / [StringLength] 对齐），拦截非法字符避免 400
  // 编辑模式 Key 输入框已禁用，此处对创建模式生效
  if (!KEY_PATTERN.test(key)) {
    alert('变量 Key 只能包含字母、数字和下划线（不能用中文、空格、连字符、点号）');
    return;
  }
  if (key.length > 50) {
    alert('变量 Key 不能超过 50 个字符');
    return;
  }

  const model = currentModel.value;
  if (!model) return;

  // Check unique key（编辑模式排除自身，与后端 UpdateAsync 查重口径一致）
  if (model.variables.some(v => v.key === key && v.id !== editingVariableId.value)) {
    alert('变量 Key 在该数据模型中已存在, 请确认后再试。');
    return;
  }

  // 模板变量不再承载协议地址/采集周期：Address / BitOffset / PollingIntervalMs
  // 已下放至设备实例级 DeviceVariable，由运行时按设备实例配置采集细节。

  const newVar: ModelVariable = {
    // 创建模式 id=0 占位（后端 CreateAsync 忽略入参 Id）；编辑模式为真实主键
    id: editingVariableId.value ?? 0,
    modelId: Number(model.id),
    key,
    name,
    // type 由后端按 DataType 派生,前端不冗余传递(后端 Type 为 IsIgnore 派生字段)
    dataType: varDataType.value as DataTypeEnum,
    unit: varUnit.value || undefined,
    // number 输入框清空后值为 ''，显式归一：空串不发送、0 保留（避免 || 误吞 0）
    min: varMin.value === '' ? undefined : varMin.value,
    max: varMax.value === '' ? undefined : varMax.value,
    description: varDesc.value || undefined,
    isStored: varIsStored.value,
    // 未勾选"存储历史"时显式发 None,后端据此派生 IsStored=false,不写时序库
    storeMode: varIsStored.value ? varStoreMode.value : 'None',
    storeIntervalMs: varIsStored.value ? (varStoreIntervalMs.value === '' ? 300000 : varStoreIntervalMs.value) : 300000,
    updateMode: varUpdateMode.value,
    scaleSlope: varScaleSlope.value === '' ? 1.0 : varScaleSlope.value,
    scaleOffset: varScaleOffset.value === '' ? 0.0 : varScaleOffset.value,
    deadBand: varDeadBand.value === '' ? null : varDeadBand.value,
    isReadOnly: varIsReadOnly.value,
    extensionData: {
      accessLevel: varAccessLevel.value,
      scaleExpr: varScaleExpr.value
    }
  };

  // Persist to server using variable API
  try {
    if (isEditing) {
      // 更新：后端返回更新后的 DTO，就地替换当前模型中的条目
      const updated = await updateVariable(newVar);
      const idx = model.variables.findIndex(v => v.id === editingVariableId.value);
      if (idx !== -1 && currentModel.value) {
        currentModel.value.variables[idx] = {
          ...updated,
          // 后端 VariableType 为大写(Analog/Digital),前端约定小写,统一归一化
          type: String(updated.type).toLowerCase() === 'digital' ? 'digital' : 'analog'
        } as ModelVariable;
      }
      addLog('模型建立', `模型 [${model.name}] 更新变量 [${name}]`, 'normal');
    } else {
      const created = await createVariable(newVar);
      // 增量并入当前模型,避免无脑全量重拉(fetchDataModelsFromBackend)导致
      // 视图跳回第一个模型、选中态丢失以及大量模型时的卡顿。
      if (created && currentModel.value) {
        currentModel.value.variables.push({
          ...created,
          // 后端 VariableType 为大写(Analog/Digital),前端约定小写,统一归一化
          type: String(created.type).toLowerCase() === 'digital' ? 'digital' : 'analog'
        } as ModelVariable);
      }

      // Synchronize new variable in all existing online devices relying on this model!
      devices.value.forEach((d) => {
        if (d.modelId === model.id) {
          if (d.variables[key] === undefined) {
            d.variables[key] = varType.value === 'digital' ? false : (newVar.min ?? 0);
          }
        }
      });

      addLog('模型建立', `模型 [${model.name}] 添加变量 [${name}]`, 'normal');
    }
  } catch {
    // 失败提示由 http 拦截器统一 Toast 弹出（含 BusinessException 文案 / 校验 errors）
    return;
  }

  // 关闭弹窗并复位表单与编辑态
  closeVarModal();
};

// Delete a variable mapping from the active data blueprint（先落库再改本地，失败则中止）
const handleDeleteVariable = async (v: ModelVariable) => {
  const model = currentModel.value;
  if (!model) return;

  try {
    await deleteVariable(v.id);
  } catch {
    // 失败提示由 http 拦截器统一 Toast 弹出
    return;
  }

  model.variables = model.variables.filter(x => x.key !== v.key);

  // Clean up in device instances
  devices.value.forEach((d) => {
    if (d.modelId === model.id) {
      delete d.variables[v.key];
    }
  });

  addLog('模型建立', `模型 [${model.name}] 删除变量 [${v.name}]`, 'warning');
};

// ---- 变量批量导入 / 导出 ----

// 导入向导开关
const showImportDialog = ref<boolean>(false);

// 导出：根据 format 下载 xlsx/csv（URL.createObjectURL 落地，不新增 api 层依赖）
const handleExport = async (format: 'xlsx' | 'csv') => {
  const model = currentModel.value;
  if (!model) return;
  try {
    const blob = await exportVariables(Number(model.id), format);
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `Model-${model.id}-Variables-${new Date().toISOString().replace(/[-:T]/g, '').slice(0, 14)}.${format}`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
    addLog('模型建立', `模型 [${model.name}] 导出变量(${format.toUpperCase()})`, 'normal');
  } catch {
    // 失败提示由 http 拦截器统一 Toast 弹出
  }
};

// 导入完成后刷新当前模型变量列表（增量并入当前模型，避免全量重拉导致选中态丢失）
const handleImportDone = async () => {
  await fetchDataModelsFromBackend();
};
</script>

<template>
  <div class="h-full flex flex-col md:flex-row text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent overflow-hidden">
    
    <!-- Mobile Model Switcher Header (方案一: 移动端顶部紧凑切换条) -->
    <div class="md:hidden bg-violet-50/80 dark:bg-slate-900 border-b border-violet-100 dark:border-slate-800 px-4 py-2.5 flex items-center justify-between gap-2 shrink-0">
      <button
        id="btn-open-model-drawer-v"
        @click="isMobileModelDrawerOpen = true"
        class="flex-1 flex items-center justify-between bg-white dark:bg-slate-800 border border-violet-200/70 dark:border-slate-700 rounded-lg px-3 py-2 text-left shadow-2xs active:scale-[0.99] transition-transform cursor-pointer"
      >
        <div class="flex items-center gap-2 min-w-0">
          <Layers class="w-4 h-4 text-violet-600 dark:text-violet-400 shrink-0" />
          <div class="min-w-0">
            <div class="text-xs font-bold text-slate-800 dark:text-white truncate">
              {{ currentModel?.name || '选择数据模型' }}
            </div>
            <div class="text-[10px] text-slate-500 dark:text-slate-400 flex items-center gap-1.5 mt-0.5">
              <span class="bg-violet-50 dark:bg-violet-950/60 text-violet-700 dark:text-violet-300 border border-violet-100 dark:border-violet-800 px-1 rounded font-mono font-bold">{{ currentModelProtocol }} 协议</span>
              <span>•</span>
              <span>{{ totalVariableCount }} 个变量</span>
            </div>
          </div>
        </div>
        <div class="flex items-center gap-1 text-slate-400 pl-2">
          <ChevronDown class="w-4 h-4 text-violet-600 dark:text-violet-400" />
        </div>
      </button>

      <button 
        @click="showVarModal = true"
        class="bg-violet-600 hover:bg-violet-700 text-white p-2.5 rounded-lg flex items-center justify-center shrink-0 shadow-2xs cursor-pointer"
        title="添加变量"
      >
        <Plus class="w-4 h-4" />
      </button>
    </div>

    <!-- LEFT LIST: Models directories (md 及以上桌面端侧边栏) -->
    <div class="hidden md:flex w-80 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex-col shrink-0 transition-colors">
      
      <div class="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
        <div class="flex items-center gap-1.5 font-bold text-sm text-slate-900 dark:text-white">
          <Layers class="w-4 h-4 text-violet-500" />
          <span>数据模型 ({{ dataModels.length }})</span>
        </div>

        <button 
          @click="showModelModal = true"
          class="p-1 rounded bg-[#1890ff] hover:bg-sky-600 text-white cursor-pointer transition-all"
          title="新建模型"
        >
          <Plus class="w-4 h-4" />
        </button>
      </div>

      <div class="flex-1 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800 text-left">
        <div 
          v-for="model in dataModels" 
          :key="model.id"
          @click="selectedModelId = model.id"
          class="p-4 cursor-pointer hover:bg-slate-50/50 dark:hover:bg-slate-800/40 transition-all space-y-1.5"
          :class="selectedModelId === model.id ? 'bg-violet-50/40 dark:bg-violet-950/30 border-r-4 border-r-violet-600' : ''"
        >
          <div class="flex items-center justify-between">
            <span class="text-[9px] font-mono font-bold bg-violet-50 dark:bg-violet-950/60 text-violet-600 dark:text-violet-400 px-1.5 py-0.5 rounded uppercase">
              {{ protocolOf(model) }} 协议
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
    <div class="flex-1 flex flex-col bg-slate-50/50 dark:bg-transparent text-left min-w-0 overflow-hidden">
      
      <div v-if="currentModel" class="bg-white dark:bg-slate-900 p-4 md:p-5 border-b border-slate-200 dark:border-slate-800 shadow-xs flex flex-col sm:flex-row sm:items-center justify-between gap-3 transition-colors shrink-0">
        <div class="space-y-1">
          <div class="flex items-center gap-2 flex-wrap">
            <h2 class="font-bold text-sm md:text-base text-slate-950 dark:text-white font-sans tracking-tight">
              {{ currentModel.name }}
            </h2>
            <span class="bg-violet-50 dark:bg-violet-950/60 text-violet-600 dark:text-violet-400 border border-violet-100 dark:border-violet-800 text-[10px] uppercase font-mono font-bold px-1.5 py-0.5 rounded leading-none">
              {{ currentModelProtocol }} 架构
            </span>
          </div>
          <p class="text-xs text-slate-500 dark:text-slate-400 font-sans line-clamp-2 sm:line-clamp-none">
            {{ currentModel.description || '暂无模型描述' }}
          </p>
        </div>

        <div class="flex items-center gap-2 shrink-0">
          <button 
            @click="showImportDialog = true"
            class="bg-emerald-600 hover:bg-emerald-700 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all shadow-xs"
            title="批量导入变量（TIA xlsx / CSV）"
          >
            <Upload class="w-4 h-4" />
            导入
          </button>
          <div class="flex items-center border border-slate-200 dark:border-slate-700 rounded-lg overflow-hidden">
            <button
              @click="handleExport('xlsx')"
              class="bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 px-3 py-1.5 inline-flex items-center gap-1 cursor-pointer transition-all"
              title="导出为 Excel"
            >
              <Download class="w-4 h-4" />
              导出
            </button>
            <button
              @click="handleExport('csv')"
              class="bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-400 dark:text-slate-500 px-2 py-1.5 border-l border-slate-200 dark:border-slate-700 cursor-pointer transition-all"
              title="导出为 CSV"
            >
              CSV
            </button>
          </div>

          <button 
            @click="showVarModal = true"
            class="bg-violet-600 hover:bg-violet-700 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all shadow-xs"
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

      <!-- Variables template viewer & Search bar -->
      <div class="flex-1 flex flex-col min-h-0 p-3 md:p-5 space-y-3 overflow-hidden">
        
        <div v-if="currentModel" class="flex-1 flex flex-col min-h-0 space-y-3">
          <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5 bg-white dark:bg-slate-900 p-3 rounded-xl border border-slate-200 dark:border-slate-800 transition-colors shrink-0 shadow-2xs">
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

          <!-- Desktop Table (md 及以上显示) -->
          <div class="hidden md:block flex-1 overflow-y-auto bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-2xs transition-colors">
            <table class="w-full text-xs font-mono divide-y divide-slate-100 dark:divide-slate-800">
              <thead class="sticky top-0 bg-slate-50 dark:bg-slate-950/90 backdrop-blur-xs z-10">
                <tr class="text-slate-400 dark:text-slate-500 font-bold text-[10px] uppercase tracking-wider">
                  <th class="px-4 py-3.5">标识</th>
                  <th class="px-4 py-3.5">名称</th>
                  <th class="px-4 py-3.5">类型</th>
                  <th class="px-4 py-3.5">单位</th>
                  <th class="px-4 py-3.5 text-right">操作</th>
                </tr>
              </thead>
              <tbody class="bg-white dark:bg-slate-900 divide-y divide-slate-100 dark:divide-slate-800 font-mono">
                <tr 
                  v-for="v in filteredVariables" 
                  :key="v.key"
                  class="hover:bg-slate-50/50 dark:hover:bg-slate-800/40 transition-all text-left"
                >
                  <td class="px-4 py-3.5 font-bold text-violet-700 dark:text-violet-400">
                    <span class="flex items-center gap-1">
                      <Binary class="w-3.5 h-3.5 text-violet-400" />
                      {{ v.key }}
                    </span>
                  </td>
                  <td class="px-4 py-3.5 font-sans font-medium">
                    <span class="text-slate-800 dark:text-slate-200 font-bold block">{{ v.name }}</span>
                    <span class="block text-[10px] font-mono text-slate-400 dark:text-slate-500 font-normal leading-relaxed mt-0.5">{{ v.description }}</span>
                    
                    <!-- Protocol advanced variable badges -->
                    <div v-if="currentModelProtocol === 'S7'" class="flex flex-wrap gap-1 mt-1.5 select-none text-[9px] font-sans">
                      <span class="bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-700 px-1.5 py-0.5 rounded" title="读写特权">特权: {{ v.extensionData?.accessLevel || 'RW' }}</span>
                      <span class="bg-violet-50 dark:bg-violet-950/60 text-violet-700 dark:text-violet-300 border border-violet-100 dark:border-violet-800 px-1.5 py-0.5 rounded" title="放缩公式">放缩: {{ v.extensionData?.scaleExpr || 'x' }}</span>
                      <span class="bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 border border-emerald-100 dark:border-emerald-800 px-1.5 py-0.5 rounded animate-pulse" title="时序库存储">
                        {{ v.isStored !== false ? '写入TSDB' : '仅内存变量' }} · {{ v.storeMode === 'Change' ? '变动存' : '定时存' }}
                      </span>
                    </div>

                    <div v-else-if="currentModelProtocol === 'OPCUA'" class="flex flex-wrap gap-1 mt-1.5 select-none text-[9px] font-sans">
                      <span class="bg-slate-50 dark:bg-slate-800 text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-700 px-1.5 py-0.5 rounded">
                        更新: {{ v.updateMode === 'subscription' ? '协议订阅' : '定时轮询' }}
                      </span>
                    </div>

                    <div v-else-if="currentModelProtocol === 'MQTT'" class="flex flex-wrap gap-1 mt-1.5 select-none text-[9px] font-sans">
                      <span class="bg-teal-50 dark:bg-teal-950/60 text-teal-700 dark:text-teal-300 border border-teal-100 dark:border-teal-800 px-1.5 py-0.5 rounded">
                        更新: {{ v.updateMode === 'subscription' ? '协议订阅' : '定时轮询' }}
                      </span>
                    </div>
                  </td>
                  <td class="px-4 py-3.5">
                    <span 
                      v-if="v.dataType"
                      class="px-2 py-0.5 rounded text-[10.5px] font-bold font-mono border shadow-3xs tracking-wider uppercase"
                      :class="v.type === 'digital' ? 
                        (currentModelProtocol === 'S7' ? 'bg-indigo-50 dark:bg-indigo-950/60 text-indigo-700 dark:text-indigo-300 border-indigo-200 dark:border-indigo-800' : 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800') : 
                        (currentModelProtocol === 'S7' ? 'bg-indigo-50/70 dark:bg-indigo-950/40 text-indigo-600 dark:text-indigo-400 border-indigo-200 dark:border-indigo-800' : 'bg-sky-50 dark:bg-sky-950/60 text-sky-700 dark:text-sky-300 border-sky-200 dark:border-sky-800')"
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
                  <td class="px-4 py-3.5 text-slate-600 dark:text-slate-300 font-bold">{{ v.unit || '无' }}</td>
                  <td class="px-4 py-3.5 text-right">
                    <div class="inline-flex items-center gap-1">
                      <button
                        @click="openEditVariable(v)"
                        class="p-1 rounded bg-slate-50 dark:bg-slate-800 hover:bg-violet-50 dark:hover:bg-violet-950/40 text-slate-400 hover:text-violet-600 dark:hover:text-violet-400 transition-all cursor-pointer"
                        title="编辑此字段"
                      >
                        <Pencil class="w-3.5 h-3.5" />
                      </button>
                      <button
                        @click="handleDeleteVariable(v)"
                        class="p-1 rounded bg-slate-50 dark:bg-slate-800 hover:bg-rose-50 dark:hover:bg-rose-950/40 text-slate-400 dark:text-slate-400 hover:text-rose-600 dark:hover:text-rose-400 transition-all cursor-pointer"
                        title="删除此字段"
                      >
                        <Trash2 class="w-3.5 h-3.5" />
                      </button>
                    </div>
                  </td>
                </tr>

                <tr v-if="currentModel.variables.length === 0">
                  <td colspan="5" class="p-8 text-center text-slate-400 dark:text-slate-500 font-sans">
                    暂无变量，点击"添加变量"创建
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Mobile Cards View (移动端卡片式流) -->
          <div class="md:hidden flex-1 overflow-y-auto space-y-2.5">
            <div
              v-for="v in filteredVariables"
              :key="v.key"
              class="bg-white dark:bg-slate-900 rounded-xl p-3.5 border border-slate-200/80 dark:border-slate-800 shadow-2xs text-left"
            >
              <div class="flex items-start justify-between gap-2">
                <div class="min-w-0 flex-1">
                  <div class="flex items-center gap-1.5 flex-wrap">
                    <span class="font-bold text-xs text-slate-800 dark:text-slate-100">{{ v.name }}</span>
                    <span 
                      v-if="v.dataType"
                      class="px-1.5 py-0.5 rounded text-[10px] font-bold font-mono border"
                      :class="v.type === 'digital' ? 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800' : 'bg-sky-50 dark:bg-sky-950/60 text-sky-700 dark:text-sky-300 border-sky-200 dark:border-sky-800'"
                    >
                      {{ v.dataType }}
                    </span>
                  </div>
                  <div class="text-[11px] font-mono text-slate-500 dark:text-slate-400 mt-1 flex items-center gap-1">
                    <Binary class="w-3 h-3 text-violet-500" />
                    <span>Key: <strong class="text-violet-700 dark:text-violet-400 font-bold">{{ v.key }}</strong></span>
                  </div>
                  <p v-if="v.description" class="text-[10px] text-slate-400 dark:text-slate-500 mt-1 leading-snug">
                    {{ v.description }}
                  </p>
                </div>

                <div class="flex items-center gap-1 shrink-0">
                  <button
                    @click="openEditVariable(v)"
                    class="p-1.5 bg-slate-50 dark:bg-slate-800 hover:bg-violet-50 rounded-md text-slate-400 hover:text-violet-600"
                    title="编辑变量"
                  >
                    <Pencil class="w-3.5 h-3.5" />
                  </button>
                  <button
                    @click="handleDeleteVariable(v)"
                    class="p-1.5 bg-slate-50 dark:bg-slate-800 hover:bg-rose-50 rounded-md text-slate-400 hover:text-rose-600"
                    title="删除变量"
                  >
                    <Trash2 class="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>

              <div class="mt-2.5 pt-2.5 border-t border-slate-100 dark:border-slate-800/80 grid grid-cols-3 gap-2 text-[11px]">
                <div>
                  <span class="text-slate-400 block text-[10px]">单位</span>
                  <span class="font-mono text-slate-700 dark:text-slate-200 font-medium">{{ v.unit || '-' }}</span>
                </div>
                <div>
                  <span class="text-slate-400 block text-[10px]">量程</span>
                  <span class="font-mono text-slate-700 dark:text-slate-200">
                    {{ v.min !== undefined && v.max !== undefined ? `[${v.min}, ${v.max}]` : '-' }}
                  </span>
                </div>
                <div>
                  <span class="text-slate-400 block text-[10px]">历史存储</span>
                  <span class="text-emerald-600 dark:text-emerald-400 font-medium">
                    {{ v.isStored !== false ? '写入TSDB' : '仅内存' }}
                  </span>
                </div>
              </div>
            </div>

            <div v-if="filteredVariables.length === 0" class="text-center py-12 text-slate-400 text-xs">
              <Layers class="w-10 h-10 stroke-[1.5] mb-2 mx-auto text-slate-300 dark:text-slate-600" />
              <p>{{ varSearchQuery ? '未匹配到相关变量' : '暂无点位变量定义' }}</p>
              <button
                @click="showVarModal = true"
                class="mt-3 text-xs bg-violet-600 text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 shadow-2xs"
              >
                <Plus class="w-3.5 h-3.5" /> 立即添加变量
              </button>
            </div>
          </div>
        </div>

        <div v-else class="h-64 flex flex-col items-center justify-center text-slate-400 dark:text-slate-500">
          <Layers class="w-8 h-8 text-slate-300 dark:text-slate-600 mb-2" />
          <p class="text-xs">请注册或选择一个变量数据模型</p>
        </div>
      </div>
    </div>

    <!-- Mobile Model Selection Bottom Drawer (移动端模型选择抽屉) -->
    <div
      v-if="isMobileModelDrawerOpen"
      class="fixed inset-0 z-50 md:hidden bg-slate-900/60 backdrop-blur-xs flex flex-col justify-end"
      @click.self="isMobileModelDrawerOpen = false"
    >
      <div class="bg-white dark:bg-slate-900 rounded-t-2xl max-h-[80vh] flex flex-col shadow-2xl border-t border-slate-200 dark:border-slate-800 animate-in slide-in-from-bottom duration-200">
        <!-- Drawer Header -->
        <div class="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
          <div class="flex items-center gap-2">
            <Layers class="w-5 h-5 text-violet-600 dark:text-violet-400" />
            <span class="font-bold text-sm text-slate-800 dark:text-white">选择数据模型 ({{ dataModels.length }})</span>
          </div>
          <div class="flex items-center gap-2">
            <button
              @click="showModelModal = true; isMobileModelDrawerOpen = false"
              class="text-xs bg-violet-600 text-white px-2.5 py-1 rounded-md flex items-center gap-1 font-medium cursor-pointer"
            >
              <Plus class="w-3.5 h-3.5" /> 新建
            </button>
            <button
              @click="isMobileModelDrawerOpen = false"
              class="p-1 rounded-md text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
            >
              <X class="w-4 h-4" />
            </button>
          </div>
        </div>

        <!-- Drawer Model Items -->
        <div class="flex-1 overflow-y-auto p-3 space-y-2 max-h-96">
          <div
            v-for="model in dataModels"
            :key="model.id"
            @click="selectedModelId = model.id; isMobileModelDrawerOpen = false"
            class="p-3 rounded-xl border text-left flex items-center justify-between gap-3 cursor-pointer transition-all"
            :class="selectedModelId === model.id ? 'bg-violet-50/70 dark:bg-violet-950/40 border-violet-300 dark:border-violet-700' : 'bg-white dark:bg-slate-800 border-slate-200 dark:border-slate-800'"
          >
            <div class="min-w-0 flex-1">
              <div class="flex items-center gap-2">
                <div class="font-bold text-xs text-slate-800 dark:text-white truncate">
                  {{ model.name }}
                </div>
                <span class="bg-violet-50 dark:bg-violet-950/60 text-violet-700 dark:text-violet-300 border border-violet-100 dark:border-violet-800 px-1 rounded text-[10px] font-mono">
                  {{ protocolOf(model) }}
                </span>
              </div>
              <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-1">
                ID: {{ model.id }} • {{ model.variables?.length || 0 }} 个变量
              </div>
            </div>
            <div v-if="selectedModelId === model.id" class="w-5 h-5 rounded-full bg-violet-600 text-white flex items-center justify-center shrink-0">
              <Check class="w-3 h-3" />
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- MODAL: CREATE BRAND NEW DATA BLUEPRINT MODEL -->
    <div v-if="showModelModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
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
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">通信协议</label>
            <select 
              v-model="modelProtocolId"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white font-bold focus:outline-none focus:border-violet-500"
            >
              <option :value="0" disabled>请选择协议</option>
              <option
                v-for="p in protocols.filter(x => x.isEnabled)"
                :key="p.id"
                :value="p.id"
              >
                {{ p.name }} ({{ p.key }})
              </option>
            </select>
            <p v-if="protocols.length === 0" class="text-[9px] text-slate-400 dark:text-slate-500 mt-1">未获取到可用协议，请确认后端协议已启用</p>
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
    <div v-if="showVarModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Tag class="w-4 h-4 text-[#1890ff]" />
            <span>{{ editingVariableId !== null ? '编辑变量' : '添加变量' }}</span>
          </div>
          <button @click="closeVarModal" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs overflow-y-auto max-h-[400px]">
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">变量标识</label>
              <input
                v-model="varKey"
                type="text"
                maxlength="50"
                pattern="[a-zA-Z0-9_]+"
                title="仅限字母、数字和下划线，最多50个字符"
                placeholder="例如: boiler_temp"
                :disabled="editingVariableId !== null"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none disabled:bg-slate-100 dark:disabled:bg-slate-800 disabled:text-slate-400 disabled:cursor-not-allowed"
              />
              <p v-if="editingVariableId !== null" class="text-[9px] text-slate-400 dark:text-slate-500 mt-0.5">
                变量 Key 是全局身份标识（DeviceKey + VariableKey），编辑时不可修改
              </p>
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">变量名称</label>
              <input 
                v-model="varName"
                type="text"
                maxlength="50"
                title="最多50个字符"
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
          <div v-if="currentModel && currentModelProtocol === 'S7'" class="p-3 bg-indigo-50/50 dark:bg-indigo-950/40 rounded-xl space-y-3.5 border border-indigo-100/50 dark:border-indigo-800 text-indigo-950 dark:text-indigo-200">
            <div class="font-bold text-[10px] text-indigo-700 dark:text-indigo-400 uppercase tracking-wider">S7 配置（寄存器地址在设备实例级配置）</div>
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
            <div v-if="varIsStored" class="flex items-center justify-between py-1">
              <label class="text-indigo-700 dark:text-indigo-300 font-bold text-xs">存储周期</label>
              <input
                v-model="varStoreIntervalMs"
                type="number"
                min="1000"
                step="1000"
                class="bg-white dark:bg-slate-900 border border-indigo-200 dark:border-indigo-700 rounded px-1.5 py-0.5 text-[10px] font-bold text-slate-600 dark:text-slate-300 focus:outline-none w-24 text-right"
              />
              <span class="text-[9px] text-slate-500 dark:text-slate-400">ms</span>
            </div>
            <p v-if="varIsStored" class="text-[9px] text-slate-400 dark:text-slate-500">
              {{ varStoreMode === 'Change' ? '值变化即存；长时间不变超过周期也存一条' : '按设定周期定时存储' }}
            </p>
          </div>

          <!-- OPCUA Specific Variable Fields -->
          <div v-if="currentModel && currentModelProtocol === 'OPCUA'" class="p-3 bg-sky-50/50 dark:bg-sky-950/40 rounded-xl space-y-3 border border-sky-100/50 dark:border-sky-800">
            <div class="font-bold text-[10px] text-sky-700 dark:text-sky-400 uppercase tracking-wider">OPC UA 配置（节点地址在设备实例级配置）</div>
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
            </div>
          </div>

          <!-- MQTT 协议：模板变量不再承载主题地址/刷新周期（已下放设备实例级 DeviceVariable） -->
          <div v-if="currentModel && currentModelProtocol === 'MQTT'" class="p-3 bg-teal-50/50 dark:bg-teal-950/40 rounded-xl border border-teal-100/50 dark:border-teal-800 text-teal-900 dark:text-teal-200">
            <div class="font-bold text-[10px] text-teal-800 dark:text-teal-400 uppercase tracking-wider">MQTT 配置（主题地址在设备实例级配置）</div>
            <p class="text-[10px] text-slate-500 dark:text-slate-400 mt-1">MQTT 主题地址与刷新周期已下放至设备实例级，此处仅维护变量定义。</p>
          </div>

          <!-- 历史存储配置 -->
          <div class="p-3 bg-emerald-50/50 dark:bg-emerald-950/40 rounded-xl space-y-3 border border-emerald-100 dark:border-emerald-800">
            <div class="font-bold text-[10px] text-emerald-700 dark:text-emerald-400 uppercase tracking-wider">历史存储</div>
            <div class="flex items-center justify-between py-1">
              <label class="flex items-center gap-1.5 font-bold text-slate-700 dark:text-slate-300 cursor-pointer text-xs">
                <input
                  type="checkbox"
                  v-model="varIsStored"
                  class="rounded text-emerald-600 focus:ring-0"
                />
                存储历史数据
              </label>
              <select
                v-if="varIsStored"
                v-model="varStoreMode"
                class="bg-white dark:bg-slate-900 border border-emerald-200 dark:border-emerald-700 rounded px-1.5 py-0.5 text-[10px] font-bold text-slate-600 dark:text-slate-300 focus:outline-none"
              >
                <option value="Change">变动存储</option>
                <option value="Cycle">定时存储</option>
                <option value="Compressed">压缩存储</option>
                <option value="Aggregated">聚合存储</option>
              </select>
            </div>
            <p v-if="!varIsStored" class="text-[9px] text-slate-400 dark:text-slate-500">不勾选则变量仅驻留内存,不写入时序数据库 (StoreMode=None)。</p>
          </div>

          <!-- Industrial-grade Enhanced Fields -->
          <div class="p-3 bg-orange-50/50 dark:bg-orange-950/40 rounded-xl space-y-3 border border-orange-100 dark:border-orange-800">
            <div class="font-bold text-[10px] text-orange-700 dark:text-orange-400 uppercase tracking-wider">工业级参数</div>
            <div class="grid grid-cols-2 gap-2">
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
            @click="closeVarModal"
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

    <!-- MODAL: 批量导入变量向导 -->
    <VariableImportDialog
      :open="showImportDialog"
      :model-id="currentModel ? Number(currentModel.id) : 0"
      @close="showImportDialog = false"
      @done="handleImportDone"
    />

  </div>
</template>
