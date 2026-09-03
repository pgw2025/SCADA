<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import {
  Database,
  Search,
  Binary,
  Settings,
  Trash2,
  Plus,
  RefreshCw,
  AlertTriangle,
  Braces,
  X,
  ChevronDown,
  Check
} from 'lucide-vue-next';
import { devices } from '../store/deviceStore';
import { dataModels, addLog, systemConfig } from '../store/index';
import { DEVICE_TYPES, PROTOCOL_FIELD_CONFIG, ProtocolFieldConfig, DeviceVariable, ModelVariable, AddressConfig, newAddressConfig, parseAddressConfig, stringifyAddressConfig, buildAddressDisplay } from '../types';
import { syncDevices } from '../services/deviceService';
import { fetchDataModelsFromBackend } from '../api/modelApi';
import { extractApiError } from '../api/http';
import {
  fetchDeviceVariables,
  createDeviceVariable,
  updateDeviceVariable,
  deleteDeviceVariable
} from '../api/deviceVariableApi';

const route = useRoute();

// ---------- 设备列表（左栏） ----------
const isMobileDeviceDrawerOpen = ref<boolean>(false);
const selectedDevId = ref<number>(Number(route.query.deviceId) || devices.value[0]?.id || 0);
const searchQuery = ref<string>('');
const selectedTypeFilter = ref<string>('ALL');

const filteredDevices = computed(() => {
  return devices.value.filter((d) => {
    const matchesSearch = d.name.toLowerCase().includes(searchQuery.value.toLowerCase()) ||
      d.key.toLowerCase().includes(searchQuery.value.toLowerCase());
    const matchesType = selectedTypeFilter.value === 'ALL' || d.type === selectedTypeFilter.value;
    return matchesSearch && matchesType;
  });
});

const selectedDevice = computed(() => {
  return devices.value.find(d => String(d.id) === String(selectedDevId.value)) || devices.value[0];
});

// 选中设备绑定的数据模型（id 需统一转字符串比较：dataModels.id 为 string，device.modelId 为 number）
const currentModel = computed(() => {
  if (!selectedDevice.value) return null;
  return dataModels.value.find(m => String(m.id) === String(selectedDevice.value.modelId)) || null;
});

// ---------- 变量实例表格（右栏） ----------
const deviceVariables = ref<DeviceVariable[]>([]);
const isLoading = ref<boolean>(false);
const loadError = ref<string>('');

// 该设备模型下未实例化的模板（用于“添加/一键补齐”去重）
const modelTemplates = computed<ModelVariable[]>(() => currentModel.value?.variables || []);
const instancedTemplateIds = computed(() => new Set(deviceVariables.value.map(v => v.modelVariableId)));
const uninstancedTemplates = computed(() => modelTemplates.value.filter(mv => !instancedTemplateIds.value.has(mv.id)));

const loadVariables = async () => {
  if (!selectedDevice.value || systemConfig.value.isSimulationActive) {
    deviceVariables.value = [];
    return;
  }
  isLoading.value = true;
  loadError.value = '';
  try {
    deviceVariables.value = await fetchDeviceVariables(selectedDevice.value.id);
  } catch (e: any) {
    // extractApiError 提取后端 message，避免只显示 axios 泛泛的 "Request failed with status code xxx"
    loadError.value = extractApiError(e);
  } finally {
    isLoading.value = false;
  }
};

const refreshAll = async () => {
  await Promise.all([syncDevices(), loadVariables()]);
};

watch(selectedDevId, () => { loadVariables(); });

// ---------- 添加实例 ----------
const showAddModal = ref<boolean>(false);
const addSelectedIds = ref<number[]>([]);

const openAddModal = () => {
  addSelectedIds.value = [];
  showAddModal.value = true;
};

// 一键补齐：把该设备所有未实例化的模板全部创建
const addAllMissing = async () => {
  if (!selectedDevice.value || uninstancedTemplates.value.length === 0) return;
  addSelectedIds.value = uninstancedTemplates.value.map(t => t.id);
  await confirmAdd();
};

const confirmAdd = async () => {
  if (!selectedDevice.value || addSelectedIds.value.length === 0) return;
  const deviceId = selectedDevice.value.id;
  let ok = 0;
  addLog('设备变量', `开始为设备#${selectedDevice.value.key} 添加 ${addSelectedIds.value.length} 个变量实例`, 'info');
  for (const mvId of addSelectedIds.value) {
    try {
      await createDeviceVariable({ deviceId, modelVariableId: mvId, isEnabled: true });
      ok++;
    } catch (e: any) {
      addLog('设备变量', `创建模板[ID:${mvId}]失败: ${e.message}`, 'warning');
    }
  }
  addLog('设备变量', `批量添加完成：成功 ${ok}/${addSelectedIds.value.length}`, ok > 0 ? 'normal' : 'warning');
  showAddModal.value = false;
  // 新建实例后 Address 为空，提示用户补充地址（需要地址的协议如 S7/OPCUA 空地址采集会失败）
  if (ok > 0 && needsAddress.value) {
    addLog('设备变量', `提示：请为新增实例补充${fieldConfig.value.addressLabel}（地址为空时采集将失败）`, 'warning');
  }
  await refreshAll();
};

// ---------- 编辑实例 ----------
const showEditModal = ref<boolean>(false);
const editingForm = ref<DeviceVariable | null>(null);
const editingCfg = ref<AddressConfig | null>(null); // 结构化地址（权威编辑对象）

const openEditModal = (v: DeviceVariable) => {
  // 浅拷贝到可编辑副本；覆盖字段保留 null（null 表示"用模板值"，见下方提示文案）
  // isReadOnlyOverride 归一化：undefined → null，保证三态下拉"继承"项能正确选中。
  editingForm.value = { ...v, isReadOnlyOverride: v.isReadOnlyOverride ?? null };
  // 结构化地址（JSON 权威）：优先解析已有 JSON，否则按当前设备协议给默认骨架。
  editingCfg.value = parseAddressConfig(v.addressConfigJson) ?? newAddressConfig(selectedDevice.value?.type || 'Virtual');
  showEditModal.value = true;
};

// 协议 → 实例字段需求：虚拟设备无地址/位偏移，无需采集属性配置
const emptyFieldConfig = (): ProtocolFieldConfig => ({
  addressLabel: undefined, addressPlaceholder: undefined, addressRequired: false,
  needsBitOffset: false, addressFields: []
});
const fieldConfig = computed<ProtocolFieldConfig>(() =>
  (selectedDevice.value?.type && PROTOCOL_FIELD_CONFIG[selectedDevice.value.type]) ?? emptyFieldConfig());
const needsAddress = computed(() => !!fieldConfig.value.addressLabel);
const needsBitOffset = computed(() => !!fieldConfig.value.needsBitOffset);
// 结构化字段是否已含位信息（S7/Modbus）；若含则隐藏独立的"位偏移"输入，避免重复编辑
const structuredHasBit = computed(() => !!fieldConfig.value.addressFields?.some(f => f.key === 'bitOffset' || f.key === 'bitIndex'));
const tableColspan = computed(() => 7 + (needsAddress.value ? 1 : 0) + (needsBitOffset.value ? 1 : 0));

// 编辑弹窗内地址展示串预览（仅预览，最终展示串由后端权威生成）
const displayPreview = computed(() =>
  editingForm.value && (fieldConfig.value.addressFields?.length ?? 0) > 0
    ? buildAddressDisplay(editingCfg.value)
    : editingForm.value?.address || '');

/** 校验编辑表单，返回错误文案；空串表示通过。 */
const validateEditForm = (): string => {
  const cfg = editingCfg.value;
  if (!editingForm.value || !cfg) return '地址配置缺失';
  for (const f of fieldConfig.value.addressFields || []) {
    const val = (cfg as any)[f.key];
    if (f.required && (val == null || val === '')) return `请填写地址字段：${f.label}`;
    if (f.validate) {
      const err = f.validate(cfg);
      if (err) return `地址字段[${f.label}]：${err}`;
    }
  }
  return '';
};

/** 提交前归一化：前端清空产生空串（''），而后端 null 才是"未配置/继承模板"语义。
  * 统一把空串转为 null 或语义默认值，避免 "" 提交后与后端 `??` 回退逻辑错位（400 / 模板失效）。
  * 参数 <paramref name="nullableDefault"/>：可空数字字段的空串归一化为 null。 */
const normNullableNum = (v: unknown): number | null =>
  v === '' || v == null ? null : (v as number);

const saveEdit = async () => {
  if (!editingForm.value || !selectedDevice.value) return;

  // ---- 顶层实例配置字段：空 → null（回退设备/模板默认）----
  editingForm.value.pollingIntervalMs = normNullableNum(editingForm.value.pollingIntervalMs);
  editingForm.value.deadBandOverride = normNullableNum(editingForm.value.deadBandOverride);
  editingForm.value.bitOffset = normNullableNum(editingForm.value.bitOffset);
  // ---- 换算表达式覆盖：空白 → null（继承模板），否则去首尾空格 ----
  const expr = editingForm.value.scaleExpressionOverride;
  editingForm.value.scaleExpressionOverride =
    expr == null || String(expr).trim() === '' ? null : String(expr).trim();

  if ((fieldConfig.value.addressFields?.length ?? 0) > 0 && editingCfg.value) {
    // ---- 结构化地址非必填数字字段：空串回填语义默认值（位地址 sentinel -1 / 寄存器数 1 / DB 号 0）----
    const cfg = editingCfg.value as any;
    for (const f of fieldConfig.value.addressFields || []) {
      if (f.type === 'number' && !f.required && cfg[f.key] === '') {
        cfg[f.key] = f.key === 'bitOffset' || f.key === 'bitIndex' ? -1
          : f.key === 'registerCount' ? 1
          : f.key === 'dbNumber' ? 0
          : null;
      }
    }
    const err = validateEditForm();
    if (err) {
      addLog('设备变量', err, 'warning');
      return;
    }
    // 地址以 JSON 为权威：写回 addressConfigJson，展示串提交供参考（后端会权威重算）
    editingForm.value.addressConfigJson = stringifyAddressConfig(editingCfg.value);
    editingForm.value.address = buildAddressDisplay(editingCfg.value) || editingForm.value.address;

  } else if (needsAddress.value) {
    // 非结构化协议（MQTT/BACnet/DNP3）：地址为纯文本（Address）。
    // 清掉历史/残留的 AddressConfigJson，避免旧 JSON 提交后被后端白名单校验拒绝。
    editingForm.value.addressConfigJson = null;
    if (!editingForm.value.address?.trim()) {
      addLog('设备变量', `${fieldConfig.value.addressLabel || '地址'}不能为空`, 'warning');
      return;
    }
  }
  try {
    await updateDeviceVariable(editingForm.value);
    addLog('设备变量', `已保存设备变量实例 [${editingForm.value.key}]`, 'normal');
    showEditModal.value = false;
    await refreshAll();
  } catch (e: any) {
    addLog('设备变量', `保存失败: ${e.message}`, 'warning');
  }
};

// 行内启用开关
const toggleEnabled = async (v: DeviceVariable) => {
  const next = { ...v, isEnabled: !v.isEnabled };
  try {
    await updateDeviceVariable(next);
    deviceVariables.value = deviceVariables.value.map(x => x.id === next.id ? next : x);
    addLog('设备变量', `已${next.isEnabled ? '启用' : '停用'}采集 [${v.key}]`, 'normal');
  } catch (e: any) {
    addLog('设备变量', `切换启用状态失败 [${v.key}]: ${e.message}`, 'warning');
  }
};

// ---------- 删除实例 ----------
const confirmDelete = async (v: DeviceVariable) => {
  if (!confirm(`确认删除设备变量实例 [${v.key}]？删除后该设备将停止采集此变量。`)) return;
  try {
    await deleteDeviceVariable(v.id, v.key);
    await refreshAll();
  } catch (e: any) {
    addLog('设备变量', `删除失败 [${v.key}]: ${e.message}`, 'warning');
  }
};

// 覆盖值可见性：仅布尔/位类型需要位偏移
const isBitType = (t?: string) => ['BOOL', 'BIT'].includes(String(t || '').toUpperCase());

// ---- 阶段 4：读写权限改由 AccessMode（Read/Write/ReadWrite）回显，旧 bool 字段 deprecated 兼容 ----
const accessLabel = (m?: string): string =>
  m === 'ReadWrite' ? '读写' : m === 'Write' ? '只写' : '只读';
/** 实例有效访问模式 = 覆盖 ?? 模板；旧后端无 effectiveAccessMode 时按 effectiveIsReadOnly 推导 */
const effectiveAccessOf = (v: DeviceVariable): string =>
  v.effectiveAccessMode ?? (v.effectiveIsReadOnly ? 'Read' : 'ReadWrite');
/** 模板定义的访问模式；旧后端无 templateAccessMode 时按 templateIsReadOnly 推导 */
const templateAccessOf = (v: DeviceVariable): string =>
  v.templateAccessMode ?? (v.templateIsReadOnly ? 'Read' : 'ReadWrite');
/** 列表徽章样式：Read 灰 / Write 琥珀 / ReadWrite 绿 */
const accessBadgeClass = (v: DeviceVariable): string => {
  const mode = effectiveAccessOf(v);
  return mode === 'Read'
    ? 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700'
    : mode === 'Write'
      ? 'bg-amber-50 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400 border-amber-200 dark:border-amber-800'
      : 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-800';
};

// ---------- 初始化 ----------
onMounted(async () => {
  if (systemConfig.value.isSimulationActive) return;
  await Promise.all([syncDevices(), fetchDataModelsFromBackend()]);
  const qId = Number(route.query.deviceId);
  if (qId && devices.value.some(d => d.id === qId)) {
    selectedDevId.value = qId;
  }
  await loadVariables();
});
</script>

<template>
  <div
    class="h-full flex flex-col md:flex-row text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">

    <!-- Mobile Device Switcher Header (移动端紧凑切换条) -->
    <div
      class="md:hidden bg-sky-50/80 dark:bg-slate-900 border-b border-sky-100 dark:border-slate-800 px-4 py-2.5 flex items-center justify-between gap-2 shrink-0">
      <button id="btn-open-device-drawer" @click="isMobileDeviceDrawerOpen = true"
        class="flex-1 flex items-center justify-between bg-white dark:bg-slate-800 border border-sky-200/70 dark:border-slate-700 rounded-lg px-3 py-2 text-left shadow-2xs active:scale-[0.99] transition-transform">
        <div class="flex items-center gap-2 min-w-0">
          <Database class="w-4 h-4 text-[#1890ff] shrink-0" />
          <div class="min-w-0">
            <div class="text-xs font-bold text-slate-800 dark:text-white truncate">
              {{ selectedDevice?.name || '选择设备' }}
            </div>
            <div class="text-[10px] text-slate-500 dark:text-slate-400 flex items-center gap-1.5 mt-0.5">
              <span class="bg-slate-100 dark:bg-slate-700 px-1 rounded text-slate-600 dark:text-slate-300 font-mono">{{
                selectedDevice?.type || 'DEV' }}</span>
              <span>•</span>
              <span class="truncate">{{ currentModel?.name || '未配置模型' }}</span>
            </div>
          </div>
        </div>
        <div class="flex items-center gap-1 text-slate-400 pl-2">
          <ChevronDown class="w-4 h-4 text-[#1890ff]" />
        </div>
      </button>
      <button id="btn-mobile-add-inst" @click="openAddModal" :disabled="uninstancedTemplates.length === 0"
        class="bg-[#1890ff] hover:bg-sky-600 disabled:opacity-40 disabled:cursor-not-allowed text-white p-2.5 rounded-lg flex items-center justify-center shrink-0 shadow-2xs cursor-pointer"
        title="添加变量实例">
        <Plus class="w-4 h-4" />
      </button>
    </div>

    <!-- LEFT PANEL: Desktop Devices list (md 以上屏幕显示) -->
    <div
      class="hidden md:flex w-80 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex-col shrink-0 transition-colors">
      <div class="p-4 border-b border-slate-100 dark:border-slate-800 space-y-3">
        <div class="flex items-center gap-1.5 font-bold text-sm text-[#0f172a] dark:text-white">
          <Database class="w-4 h-4 text-[#1890ff]" />
          <span>设备列表</span>
        </div>
        <div class="relative">
          <Search class="absolute left-2.5 top-2.5 w-4 h-4 text-slate-400" />
          <input v-model="searchQuery" type="text" placeholder="搜索设备名称或编码"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:bg-white dark:focus:bg-slate-900 rounded-lg pl-9 pr-3 py-1.5 text-xs text-[#262626] dark:text-white focus:outline-none focus:border-[#1890ff]" />
        </div>
        <div class="flex flex-wrap gap-1">
          <button @click="selectedTypeFilter = 'ALL'"
            class="text-[9px] font-bold px-2 py-0.5 rounded transition-all cursor-pointer"
            :class="selectedTypeFilter === 'ALL' ? 'bg-slate-900 dark:bg-sky-600 text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 hover:bg-slate-200 dark:hover:bg-slate-700'">全部</button>
          <button v-for="opt in DEVICE_TYPES" :key="opt.value" @click="selectedTypeFilter = opt.value"
            class="text-[9px] font-bold px-2 py-0.5 rounded transition-all cursor-pointer"
            :class="selectedTypeFilter === opt.value ? 'bg-slate-900 dark:bg-sky-600 text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 hover:bg-slate-200 dark:hover:bg-slate-700'">{{
              opt.label }}</button>
        </div>
      </div>

      <div class="flex-1 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800">
        <div v-for="dev in filteredDevices" :key="dev.id" @click="selectedDevId = dev.id"
          class="p-3.5 cursor-pointer hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-all text-left flex items-start gap-2.5 relative"
          :class="selectedDevId === dev.id ? 'bg-sky-50/50 dark:bg-sky-950/30 border-r-4 border-r-[#1890ff]' : ''">
          <span class="w-2 h-2 rounded-full mt-1 shrink-0"
            :class="dev.status === 1 || dev.status === 'online' ? 'bg-emerald-500 shadow-[0_0_6px_#10b981]' : 'bg-slate-300 dark:bg-slate-600'" />
          <div class="space-y-1 overflow-hidden flex-1">
            <h4 class="font-bold text-xs text-slate-800 dark:text-white truncate leading-snug">{{ dev.name }}</h4>
            <div class="flex items-center gap-2 text-[9px] font-mono text-slate-500 dark:text-slate-400">
              <span
                class="bg-slate-100 dark:bg-slate-800 px-1 rounded text-slate-600 dark:text-slate-300 leading-none py-0.5">{{
                dev.type }}</span>
              <span class="truncate">{{ dev.key || dev.code }}</span>
            </div>
          </div>
        </div>
        <div v-if="filteredDevices.length === 0" class="p-8 text-center text-xs text-slate-400 font-mono">未找到匹配的设备</div>
      </div>
    </div>

    <!-- RIGHT PANEL: Variable instances -->
    <div class="flex-1 flex flex-col min-w-0 bg-slate-50/50 dark:bg-transparent overflow-hidden">

      <!-- Header -->
      <div v-if="selectedDevice"
        class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm flex flex-col sm:flex-row sm:items-center justify-between gap-4 shrink-0 font-sans transition-colors">
        <div class="space-y-1.5 text-left">
          <div class="flex items-center gap-2">
            <span
              class="text-[10px] font-bold px-2 py-0.5 bg-slate-100 dark:bg-slate-800 border border-slate-200/50 dark:border-slate-700 rounded-full font-mono uppercase text-slate-500 dark:text-slate-400">{{
              selectedDevice.type }}</span>
            <span class="text-xs font-mono text-slate-400 dark:text-slate-500">模型: {{ currentModel?.name || '未配置'
              }}</span>
          </div>
          <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight">{{ selectedDevice.name }}</h2>
        </div>
        <div class="flex items-center gap-2 shrink-0 self-start sm:self-center">
          <span
            class="text-xs font-mono text-slate-400 dark:text-slate-400 bg-slate-50 dark:bg-slate-950 px-2 py-1 rounded border border-slate-200/40 dark:border-slate-800">
            已配置 <b class="text-emerald-600 dark:text-emerald-400">{{ deviceVariables.length }}</b> /
            {{ modelTemplates.length }} 个模板变量
          </span>
        </div>
      </div>

      <!-- Toolbar -->
      <div v-if="selectedDevice" class="flex flex-wrap items-center gap-2 p-3 sm:px-6 shrink-0">
        <button @click="openAddModal" :disabled="uninstancedTemplates.length === 0"
          class="inline-flex items-center gap-1 text-xs font-bold bg-[#1890ff] text-white hover:bg-sky-600 disabled:opacity-40 disabled:cursor-not-allowed px-3 py-1.5 rounded-lg cursor-pointer">
          <Plus class="w-3.5 h-3.5" /> 添加实例
        </button>
        <button @click="addAllMissing" :disabled="uninstancedTemplates.length === 0"
          class="inline-flex items-center gap-1 text-xs font-bold border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed px-3 py-1.5 rounded-lg cursor-pointer">
          <Braces class="w-3.5 h-3.5" /> 一键补齐 ({{ uninstancedTemplates.length }})
        </button>
        <button @click="refreshAll"
          class="inline-flex items-center gap-1 text-xs font-bold border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-800 px-3 py-1.5 rounded-lg cursor-pointer">
          <RefreshCw class="w-3.5 h-3.5" :class="isLoading ? 'animate-spin' : ''" /> 刷新
        </button>
      </div>

      <!-- Simulation notice -->
      <div v-if="systemConfig.isSimulationActive"
        class="mx-6 my-3 bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-800 text-amber-800 dark:text-amber-300 rounded-xl p-4 text-xs leading-relaxed text-left flex gap-3">
        <AlertTriangle class="w-4 h-4 text-amber-500 shrink-0 mt-0.5" />
        <div>
          <h5 class="font-bold">当前处于模拟模式</h5>
          <p class="mt-0.5 opacity-90">设备变量实例由后端管理，模拟模式下不加载/不提交真实数据。</p>
        </div>
      </div>

      <!-- Table -->
      <div v-if="selectedDevice" class="flex-1 p-3 sm:px-6 overflow-y-auto">
        <div
          class="bg-white dark:bg-slate-900 border border-slate-200/80 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm">
          <div class="overflow-x-auto hidden md:block">
            <table class="w-full text-left text-xs font-mono divide-y divide-slate-100 dark:divide-slate-800">
              <thead>
                <tr
                  class="bg-slate-50/50 dark:bg-slate-950/60 text-slate-400 font-bold text-[10px] uppercase tracking-wider">
                  <th class="px-4 py-3.5">变量标识</th>
                  <th class="px-4 py-3.5">名称 / 单位</th>
                  <th class="px-4 py-3.5">类型</th>
                  <th class="px-4 py-3.5">读写</th>
                  <th v-if="needsAddress" class="px-4 py-3.5">{{ fieldConfig.addressLabel }}</th>
                  <th v-if="needsBitOffset" class="px-4 py-3.5">位偏移</th>
                  <th class="px-4 py-3.5">轮询(ms)</th>
                  <th class="px-4 py-3.5">启用</th>
                  <th class="px-4 py-3.5 text-right">操作</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-100 dark:divide-slate-800 bg-white dark:bg-slate-900">
                <tr v-for="v in deviceVariables" :key="v.id"
                  class="hover:bg-slate-50/40 dark:hover:bg-slate-800/40 transition-all font-mono">
                  <td class="px-4 py-3.5">
                    <span class="flex items-center gap-1.5 font-bold text-slate-600 dark:text-slate-300">
                      <Binary class="w-3 h-3 text-slate-400" /> {{ v.key }}
                    </span>
                  </td>
                  <td class="px-4 py-3.5 text-slate-800 dark:text-slate-200 font-sans font-medium">
                    {{ v.name }}<span v-if="v.unit" class="text-[10px] text-slate-400 ml-1 font-mono">{{ v.unit
                      }}</span>
                  </td>
                  <td class="px-4 py-3.5">
                    <span class="inline-block px-1.5 py-0.5 text-[9px] font-bold rounded border uppercase"
                      :class="isBitType(v.dataType) ? 'bg-indigo-50 dark:bg-indigo-950/60 text-indigo-700 dark:text-indigo-300 border-indigo-200 dark:border-indigo-800' : 'bg-sky-50 dark:bg-sky-950/60 text-sky-700 dark:text-sky-300 border-sky-200 dark:border-sky-800'">{{
                      v.dataType }}</span>
                  </td>
                  <td class="px-4 py-3.5">
                    <span class="inline-block px-1.5 py-0.5 text-[9px] font-bold rounded border"
                      :class="accessBadgeClass(v)" :title="v.isReadOnlyOverride != null
                        ? '该设备实例覆盖模板 → ' + accessLabel(effectiveAccessOf(v))
                        : '继承模板：' + accessLabel(templateAccessOf(v))">
                      {{ accessLabel(effectiveAccessOf(v)) }}<span v-if="v.isReadOnlyOverride != null"
                        class="ml-0.5 opacity-70">·覆盖</span>
                    </span>
                  </td>
                  <td v-if="needsAddress" class="px-4 py-3.5 text-[11px]">
                    <span v-if="v.address"
                      class="bg-slate-100 dark:bg-slate-800 font-bold px-1.5 py-0.5 rounded text-slate-600 dark:text-slate-300">{{
                      v.address }}</span>
                    <span v-else class="text-rose-500 dark:text-rose-400 font-bold text-[10px]">未配置地址</span>
                  </td>
                  <td v-if="needsBitOffset" class="px-4 py-3.5 text-slate-500 dark:text-slate-400 text-[11px]">{{
                    isBitType(v.dataType) ? (v.bitOffset ?? '—') : '—' }}</td>
                  <td class="px-4 py-3.5 text-slate-500 dark:text-slate-400 text-[11px]">{{ v.pollingIntervalMs ?? 1000
                    }}</td>
                  <td class="px-4 py-3.5">
                    <button @click="toggleEnabled(v)"
                      class="relative w-9 h-5 rounded-full transition-colors cursor-pointer"
                      :class="v.isEnabled ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-600'">
                      <span class="absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-all"
                        :class="v.isEnabled ? 'left-[18px]' : 'left-0.5'" />
                    </button>
                  </td>
                  <td class="px-4 py-3.5 text-right">
                    <div class="flex items-center justify-end gap-2">
                      <button @click="openEditModal(v)"
                        class="text-[11px] font-sans font-bold text-[#1890ff] hover:text-sky-600 border border-slate-200 dark:border-slate-700 px-2 py-1 rounded hover:bg-slate-50 dark:hover:bg-slate-800 inline-flex items-center gap-1 transition-all cursor-pointer">
                        <Settings class="w-3 h-3" /> 编辑
                      </button>
                      <button @click="confirmDelete(v)"
                        class="text-[11px] font-sans font-bold text-rose-500 hover:text-rose-700 border border-slate-200 dark:border-slate-700 px-2 py-1 rounded hover:bg-slate-50 dark:hover:bg-slate-800 inline-flex items-center gap-1 transition-all cursor-pointer">
                        <Trash2 class="w-3 h-3" /> 删除
                      </button>
                    </div>
                  </td>
                </tr>
                <tr v-if="!isLoading && deviceVariables.length === 0">
                  <td :colspan="tableColspan"
                    class="p-10 text-center text-slate-400 dark:text-slate-500 text-xs font-sans">
                    <Database class="w-8 h-8 mx-auto mb-2 opacity-30" />
                    该设备尚未配置变量实例，请点击“添加实例”或“一键补齐”。
                    <template v-if="modelTemplates.length > 0">（模型共有 {{ modelTemplates.length }} 个模板变量）</template>
                  </td>
                </tr>
                <tr v-if="loadError">
                  <td :colspan="tableColspan" class="p-6 text-center text-rose-500 text-xs font-sans">加载失败: {{ loadError
                    }}</td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Mobile list -->
          <div class="block md:hidden divide-y divide-slate-100 dark:divide-slate-800 max-h-[500px] overflow-y-auto">
            <div v-for="v in deviceVariables" :key="v.id" class="p-4 space-y-2 text-left">
              <div class="flex items-start justify-between gap-2">
                <div class="min-w-0">
                  <div class="flex items-center gap-1 font-bold font-mono text-xs">
                    <Binary class="w-3 h-3 text-slate-400" />
                    <span class="truncate">{{ v.key }}</span>
                  </div>
                  <div class="text-[10px] text-slate-500 mt-0.5 font-sans">{{ v.name }}{{ v.unit ? ' (' + v.unit + ')' :
                    '' }}
                  </div>
                </div>
                <span v-if="needsAddress && v.address"
                  class="text-[9px] bg-slate-100 dark:bg-slate-800 px-1.5 py-0.5 rounded text-slate-600 dark:text-slate-300 font-mono">{{
                  v.address }}</span>
                <span v-else-if="needsAddress" class="text-[9px] text-rose-500 font-bold">未配置地址</span>
              </div>
              <div class="flex items-center justify-between text-[9px] text-slate-400 font-mono">
                <span class="flex items-center gap-1.5">
                  <span>{{ v.dataType }} · 轮询 {{ v.pollingIntervalMs ?? 1000 }}ms</span>
                  <span class="inline-block px-1 py-px rounded border font-bold"
                    :class="accessBadgeClass(v)">{{ accessLabel(effectiveAccessOf(v))
                    }}{{ v.isReadOnlyOverride != null ? '·覆盖' : '' }}</span>
                </span>
                <button @click="toggleEnabled(v)" class="text-[10px] font-bold"
                  :class="v.isEnabled ? 'text-emerald-500' : 'text-slate-400'">{{ v.isEnabled ? '启用' : '停用' }}</button>
              </div>
              <div class="flex gap-2">
                <button @click="openEditModal(v)"
                  class="flex-1 text-[10px] font-bold text-[#1890ff] border border-slate-200 dark:border-slate-700 px-2 py-1 rounded text-center cursor-pointer">编辑</button>
                <button @click="confirmDelete(v)"
                  class="flex-1 text-[10px] font-bold text-rose-500 border border-slate-200 dark:border-slate-700 px-2 py-1 rounded text-center cursor-pointer">删除</button>
              </div>
            </div>
            <div v-if="!isLoading && deviceVariables.length === 0" class="p-8 text-center text-slate-400 text-xs">
              该设备尚未配置变量实例
            </div>
          </div>
        </div>
      </div>

      <div v-else class="h-64 flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 gap-2">
        <Database class="w-8 h-8 text-slate-300 dark:text-slate-700" />
        <p class="text-xs">请选择设备查看变量实例</p>
      </div>
    </div>

    <!-- Mobile Device Selection Bottom Drawer (移动端设备选择抽屉) -->
    <div v-if="isMobileDeviceDrawerOpen"
      class="fixed inset-0 z-50 md:hidden bg-slate-900/60 backdrop-blur-xs flex flex-col justify-end"
      @click.self="isMobileDeviceDrawerOpen = false">
      <div
        class="bg-white dark:bg-slate-900 rounded-t-2xl max-h-[80vh] flex flex-col shadow-2xl border-t border-slate-200 dark:border-slate-800 animate-in slide-in-from-bottom duration-200">
        <div class="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
          <div class="flex items-center gap-2">
            <Database class="w-5 h-5 text-[#1890ff]" />
            <span class="font-bold text-sm text-slate-800 dark:text-white">选择设备 ({{ devices.length }})</span>
          </div>
          <button @click="isMobileDeviceDrawerOpen = false"
            class="p-1 rounded-md text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">
            <X class="w-4 h-4" />
          </button>
        </div>

        <div class="p-3 border-b border-slate-100 dark:border-slate-800 space-y-2">
          <div class="relative">
            <Search class="absolute left-2.5 top-2.5 w-4 h-4 text-slate-400" />
            <input v-model="searchQuery" type="text" placeholder="搜索设备名称或编码"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg pl-9 pr-3 py-2 text-xs text-[#262626] dark:text-white focus:outline-none focus:border-[#1890ff]" />
          </div>
          <div class="flex flex-wrap gap-1">
            <button @click="selectedTypeFilter = 'ALL'" class="text-[10px] font-bold px-2 py-0.5 rounded transition-all"
              :class="selectedTypeFilter === 'ALL' ? 'bg-[#1890ff] text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400'">全部</button>
            <button v-for="opt in DEVICE_TYPES" :key="opt.value" @click="selectedTypeFilter = opt.value"
              class="text-[10px] font-bold px-2 py-0.5 rounded transition-all"
              :class="selectedTypeFilter === opt.value ? 'bg-[#1890ff] text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400'">{{
              opt.label }}</button>
          </div>
        </div>

        <div class="flex-1 overflow-y-auto p-3 space-y-2 max-h-96">
          <div v-for="dev in filteredDevices" :key="dev.id"
            @click="selectedDevId = dev.id; isMobileDeviceDrawerOpen = false"
            class="p-3 rounded-xl border text-left flex items-center justify-between gap-3 cursor-pointer transition-all"
            :class="selectedDevId === dev.id ? 'bg-sky-50/70 dark:bg-sky-950/40 border-sky-300 dark:border-sky-700' : 'bg-white dark:bg-slate-800 border-slate-200 dark:border-slate-800'">
            <div class="min-w-0 flex-1">
              <div class="flex items-center gap-2">
                <span class="w-2 h-2 rounded-full shrink-0"
                  :class="dev.status === 1 || dev.status === 'online' ? 'bg-emerald-500 shadow-[0_0_6px_#10b981]' : 'bg-slate-300 dark:bg-slate-600'" />
                <div class="font-bold text-xs text-slate-800 dark:text-white truncate">
                  {{ dev.name }}
                </div>
                <span
                  class="bg-slate-100 dark:bg-slate-700 px-1 rounded text-[10px] text-slate-600 dark:text-slate-300 font-mono">
                  {{ dev.type }}
                </span>
              </div>
              <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-1 truncate">
                编码: {{ dev.key || dev.code }}
              </div>
            </div>
            <div v-if="selectedDevId === dev.id"
              class="w-5 h-5 rounded-full bg-[#1890ff] text-white flex items-center justify-center shrink-0">
              <Check class="w-3 h-3" />
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ADD Modal -->
    <div v-if="showAddModal" class="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4"
      @click.self="showAddModal = false">
      <div
        class="bg-white dark:bg-slate-900 rounded-xl shadow-xl w-full max-w-lg max-h-[80vh] flex flex-col overflow-hidden">
        <div class="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
          <h3 class="font-bold text-sm text-slate-900 dark:text-white flex items-center gap-2">
            <Plus class="w-4 h-4 text-[#1890ff]" /> 添加变量实例 — {{ selectedDevice?.name }}
          </h3>
          <button @click="showAddModal = false" class="text-slate-400 hover:text-slate-600 cursor-pointer">
            <X class="w-4 h-4" />
          </button>
        </div>
        <div v-if="uninstancedTemplates.length === 0" class="p-8 text-center text-slate-400 text-xs font-sans">
          该设备已实例化其模型下的全部模板变量。
        </div>
        <div v-else class="p-4 overflow-y-auto space-y-1.5">
          <label v-for="mv in uninstancedTemplates" :key="mv.id"
            class="flex items-start gap-2 p-2.5 rounded-lg border border-slate-100 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800/40 cursor-pointer">
            <input type="checkbox" :value="mv.id" v-model="addSelectedIds" class="mt-0.5 cursor-pointer" />
            <div class="min-w-0">
              <div class="text-xs font-bold text-slate-700 dark:text-slate-200 font-mono">{{ mv.key }}</div>
              <div class="text-[10px] text-slate-400 font-sans">{{ mv.name }} · {{ mv.dataType }}{{ mv.unit ? ' (' +
                mv.unit
                + ')' : '' }}</div>
            </div>
          </label>
        </div>
        <div class="p-4 border-t border-slate-100 dark:border-slate-800 flex justify-end gap-2">
          <button @click="showAddModal = false"
            class="px-3 py-1.5 text-xs font-bold border border-slate-200 dark:border-slate-700 rounded-lg text-slate-600 dark:text-slate-300 cursor-pointer">取消</button>
          <button @click="confirmAdd" :disabled="addSelectedIds.length === 0"
            class="px-3 py-1.5 text-xs font-bold bg-[#1890ff] text-white rounded-lg hover:bg-sky-600 disabled:opacity-40 cursor-pointer">
            添加 ({{ addSelectedIds.length }})
          </button>
        </div>
      </div>
    </div>

    <!-- EDIT Modal -->
    <div v-if="showEditModal && editingForm" class="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4"
      @click.self="showEditModal = false">
      <div
        class="bg-white dark:bg-slate-900 rounded-xl shadow-xl w-full max-w-lg max-h-[80vh] flex flex-col overflow-hidden">
        <div class="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
          <h3 class="font-bold text-sm text-slate-900 dark:text-white flex items-center gap-2">
            <Settings class="w-4 h-4 text-[#1890ff]" /> 编辑变量实例 — <span class="font-mono">{{ editingForm.key }}</span>
          </h3>
          <button @click="showEditModal = false" class="text-slate-400 hover:text-slate-600 cursor-pointer">
            <X class="w-4 h-4" />
          </button>
        </div>
        <div class="p-4 overflow-y-auto space-y-3">
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">变量标识（只读）</label>
              <input :value="editingForm.key" disabled
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-xs text-slate-500" />
            </div>
            <div>
              <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">数据类型（只读）</label>
              <input :value="editingForm.dataType" disabled
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-xs text-slate-500" />
            </div>
          </div>
          <div v-if="needsAddress && (fieldConfig.addressFields?.length ?? 0) > 0">
            <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">
              {{ fieldConfig.addressLabel }} <span class="text-rose-400"
                v-if="fieldConfig.addressRequired && !displayPreview">（必填，空地址采集失败）</span>
            </label>
            <!-- 展示串预览（只读，最终由后端权威生成） -->
            <input :value="displayPreview" disabled
              :placeholder="fieldConfig.addressPlaceholder"
              class="w-full bg-slate-100 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-xs font-mono text-slate-600 dark:text-slate-300" />
            <!-- 结构化地址字段（按协议渲染，JSON 权威） -->
            <div class="mt-2 grid grid-cols-2 gap-2">
              <div v-for="f in fieldConfig.addressFields" :key="f.key">
                <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">{{ f.label
                  }}<span class="text-rose-400" v-if="f.required"> *</span></label>
                <select v-if="f.type === 'select'" v-model="(editingCfg as any)[f.key]"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs focus:outline-none">
                  <option v-for="opt in f.options" :key="String(opt.value)" :value="opt.value">{{ opt.label }}</option>
                </select>
                <input v-else :type="f.type === 'number' ? 'number' : 'text'"
                  v-model.number="(editingCfg as any)[f.key]" :min="f.min" :max="f.max"
                  :placeholder="f.placeholder" :disabled="f.key === 'dbNumber' && editingCfg.area !== 'DB'"
                  class="w-full disabled:opacity-40 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs font-mono focus:outline-none" />
              </div>
            </div>
            <p v-if="displayPreview" class="mt-1 text-[10px] text-emerald-600 dark:text-emerald-400 font-mono">生成地址：
              {{ displayPreview }}</p>
          </div>
          <div v-else-if="needsAddress">
            <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">
              {{ fieldConfig.addressLabel }} <span class="text-rose-400"
                v-if="fieldConfig.addressRequired && !editingForm.address">（必填，空地址采集失败）</span>
            </label>
            <input v-model="editingForm.address" type="text" :placeholder="fieldConfig.addressPlaceholder"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs font-mono focus:outline-none" />
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div v-if="needsBitOffset && !structuredHasBit">
              <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">位偏移（BOOL/BIT）</label>
              <input v-model.number="editingForm.bitOffset" type="number" min="0" max="7"
                :disabled="!isBitType(editingForm.dataType)"
                class="w-full disabled:opacity-40 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs font-mono focus:outline-none" />
            </div>
            <div :class="needsBitOffset && !structuredHasBit ? '' : 'col-span-2'">
              <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">轮询间隔（ms）</label>
              <input v-model.number="editingForm.pollingIntervalMs" type="number" min="100"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs font-mono focus:outline-none" />
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">换算表达式（覆盖）</label>
              <input v-model="editingForm.scaleExpressionOverride" type="text" placeholder="留空=继承模板；例：x*0.1"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs font-mono focus:outline-none" />
            </div>
            <div>
              <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">死区</label>
              <input v-model.number="editingForm.deadBandOverride" type="number" step="0.1"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs font-mono focus:outline-none" />
            </div>
          </div>
          <div>
            <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">读写权限</label>
            <select v-model="editingForm.isReadOnlyOverride"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs focus:outline-none">
              <option :value="null">继承模板（当前：{{ editingForm.templateIsReadOnly ? '只读' : '可写' }}）</option>
              <option :value="true">强制只读</option>
              <option :value="false">强制可写</option>
            </select>
          </div>
          <div class="flex items-center justify-between text-xs text-slate-500 dark:text-slate-400 font-sans">
            <span>启用采集</span>
            <button @click="editingForm.isEnabled = !editingForm.isEnabled"
              class="relative w-9 h-5 rounded-full transition-colors cursor-pointer"
              :class="editingForm.isEnabled ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-600'">
              <span class="absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-all"
                :class="editingForm.isEnabled ? 'left-[18px]' : 'left-0.5'" />
            </button>
          </div>
          <p class="text-[10px] text-slate-400 dark:text-slate-500 font-sans leading-relaxed">
            <template v-if="needsAddress && needsBitOffset">注：缩放/死区留空时使用模板值；位偏移仅对 BOOL/BIT
              有效；读写权限默认继承模板，可按设备强制覆盖。</template>
            <template v-else-if="needsAddress">注：缩放/死区留空时使用模板值；读写权限默认继承模板，可按设备强制覆盖。</template>
            <template v-else>虚拟设备由驱动按数据类型自动生成模拟值，无需配置地址等采集属性；读写权限默认继承模板。</template>
          </p>
        </div>
        <div class="p-4 border-t border-slate-100 dark:border-slate-800 flex justify-end gap-2">
          <button @click="showEditModal = false"
            class="px-3 py-1.5 text-xs font-bold border border-slate-200 dark:border-slate-700 rounded-lg text-slate-600 dark:text-slate-300 cursor-pointer">取消</button>
          <button @click="saveEdit"
            class="px-3 py-1.5 text-xs font-bold bg-[#1890ff] text-white rounded-lg hover:bg-sky-600 cursor-pointer">保存</button>
        </div>
      </div>
    </div>
  </div>
</template>