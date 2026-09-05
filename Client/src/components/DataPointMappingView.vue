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
  Check,
  ArrowUpDown,
  LayoutGrid,
  List,
  Sliders,
  Filter
} from 'lucide-vue-next';
import { devices } from '../store/deviceStore';
import { dataModels, addLog, systemConfig } from '../store/index';
import { DEVICE_TYPES, PROTOCOL_FIELD_CONFIG, ProtocolFieldConfig, DataPointMapping, DataPoint, DeviceModelBinding, AddressConfig, newAddressConfig, parseAddressConfig, stringifyAddressConfig, buildAddressDisplay } from '../types';
import { syncDevices } from '../services/deviceService';
import { fetchDataModelsFromBackend } from '../api/modelApi';
import { extractApiError } from '../api/http';
import {
  fetchDataPointMappings,
  createDataPointMapping,
  updateDataPointMapping,
  deleteDataPointMapping
} from '../api/dataPointMappingApi';

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

// 阶段 5：选中设备的模型绑定摘要（后端 DeviceDto.Models），顶栏只读展示主模型 Code/Version。
const deviceBindingModels = computed<DeviceModelBinding[]>(() => selectedDevice.value?.models ?? []);
const primaryBindingModel = computed(() => deviceBindingModels.value.find(b => b.isPrimary) ?? null);

/** 顶栏只读展示的主模型摘要：Code/Version 以绑定行（含绑定快照）为权威，缺失时回退模板 dataModels。 */
const headerModelMeta = computed(() => {
  const b = primaryBindingModel.value;
  const m = currentModel.value;
  return {
    name: b?.name || m?.name || '',
    code: b?.code || m?.code || '',
    // 绑定行 Version 为绑定时刻快照；模板行 Version 为模型当前版本。优先绑定快照。
    version: b?.version || m?.version || '1.0'
  };
});

// ---------- 变量实例表格（右栏） ----------
const dataPointMappings = ref<DataPointMapping[]>([]);
const isLoading = ref<boolean>(false);
const loadError = ref<string>('');

// 该设备模型下未实例化的模板（用于“添加/一键补齐”去重）
const modelTemplates = computed<DataPoint[]>(() => currentModel.value?.variables || []);
const instancedTemplateIds = computed(() => new Set(dataPointMappings.value.map(v => v.dataPointId)));
const uninstancedTemplates = computed(() => modelTemplates.value.filter(mv => !instancedTemplateIds.value.has(mv.id)));

const loadVariables = async () => {
  if (!selectedDevice.value || systemConfig.value.isSimulationActive) {
    dataPointMappings.value = [];
    return;
  }
  isLoading.value = true;
  loadError.value = '';
  try {
    dataPointMappings.value = await fetchDataPointMappings(selectedDevice.value.id);
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
      await createDataPointMapping({ deviceId, dataPointId: mvId, isEnabled: true });
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
const editingForm = ref<DataPointMapping | null>(null);
const editingCfg = ref<AddressConfig | null>(null); // 结构化地址（权威编辑对象）

const openEditModal = (v: DataPointMapping) => {
  // 浅拷贝到可编辑副本；覆盖字段保留 null（null 表示"用模板值"，见下方提示文案）
  // accessModeOverride 归一化：undefined → null，保证下拉"继承"项能正确选中。
  // updateMode 归一化：undefined → 'Polling'（与后端 DTO 默认值对齐，保证下拉有确定选中项）。
  editingForm.value = { ...v, accessModeOverride: v.accessModeOverride ?? null, updateMode: v.updateMode ?? 'Polling' };
  // 结构化地址（JSON 权威）：优先解析已有 JSON，否则按当前设备协议给默认骨架。
  editingCfg.value = parseAddressConfig(v.addressConfigJson) ?? newAddressConfig(selectedDevice.value?.type || 'Virtual');
  showEditModal.value = true;
};

// 访问宽度联动：S7 切到 BIT 时回填位地址位偏移(0-7)让预览进入位地址分支；切出 BIT 时复位非位地址哨兵 -1
const onAddressFieldWidthChange = () => {
  const cfg = editingCfg.value;
  if (!cfg || (cfg.protocol || '').toUpperCase() !== 'S7') return;
  if (cfg.width === 'BIT') {
    if (cfg.bitOffset == null || cfg.bitOffset < 0 || cfg.bitOffset > 7) cfg.bitOffset = 0;
  } else if (cfg.bitOffset != null && cfg.bitOffset >= 0 && cfg.bitOffset <= 7) {
    cfg.bitOffset = -1;
  }
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
// 仅当访问宽度为 BIT 时才显示 S7 位偏移字段；否则隐藏（S7 位地址专属字段，Modbus 等其他协议不受影响）
const visibleAddressFields = computed(() => {
  const isS7 = (editingCfg.value as any)?.protocol?.toUpperCase() === 'S7';
  return (fieldConfig.value.addressFields || []).filter(f =>
    !(isS7 && (f.key === 'bitOffset') && (editingCfg.value as any)?.width !== 'BIT'));
});
const tableColspan = computed(() => 8 + (needsAddress.value ? 1 : 0) + (needsBitOffset.value ? 1 : 0));

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
    await updateDataPointMapping(editingForm.value);
    addLog('设备变量', `已保存设备变量实例 [${editingForm.value.key}]`, 'normal');
    showEditModal.value = false;
    await refreshAll();
  } catch (e: any) {
    addLog('设备变量', `保存失败: ${e.message}`, 'warning');
  }
};

// 行内启用开关
const toggleEnabled = async (v: DataPointMapping) => {
  const next = { ...v, isEnabled: !v.isEnabled };
  try {
    await updateDataPointMapping(next);
    dataPointMappings.value = dataPointMappings.value.map(x => x.id === next.id ? next : x);
    addLog('设备变量', `已${next.isEnabled ? '启用' : '停用'}采集 [${v.key}]`, 'normal');
  } catch (e: any) {
    addLog('设备变量', `切换启用状态失败 [${v.key}]: ${e.message}`, 'warning');
  }
};

// ---------- 删除实例 ----------
const confirmDelete = async (v: DataPointMapping) => {
  if (!confirm(`确认删除设备变量实例 [${v.key}]？删除后该设备将停止采集此变量。`)) return;
  try {
    await deleteDataPointMapping(v.id, v.key);
    await refreshAll();
  } catch (e: any) {
    addLog('设备变量', `删除失败 [${v.key}]: ${e.message}`, 'warning');
  }
};

// 覆盖值可见性：仅布尔/位类型需要位偏移
const isBitType = (t?: string) => ['BOOL', 'BIT'].includes(String(t || '').toUpperCase());

// ---- 阶段 6：读写权限以 AccessMode（Read/Write/ReadWrite）为唯一回显，旧 bool 字段已删除 ----
const accessLabel = (m?: string): string =>
  m === 'ReadWrite' ? '读写' : m === 'Write' ? '只写' : '只读';
/** 实例有效访问模式 = 覆盖(accessModeOverride) ?? 模板（后端恒回显，缺省 Read 兜底） */
const effectiveAccessOf = (v: DataPointMapping): string =>
  v.effectiveAccessMode ?? v.templateAccessMode ?? 'Read';
/** 模板定义的访问模式（后端恒回显，缺省 Read 兜底） */
const templateAccessOf = (v: DataPointMapping): string =>
  v.templateAccessMode ?? 'Read';
/** 列表徽章样式：Read 灰 / Write 琥珀 / ReadWrite 绿 */
const accessBadgeClass = (v: DataPointMapping): string => {
  const mode = effectiveAccessOf(v);
  return mode === 'Read'
    ? 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700'
    : mode === 'Write'
      ? 'bg-amber-50 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400 border-amber-200 dark:border-amber-800'
      : 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-800';
};

// ---- 阶段六：更新方式（Polling / Subscription）----
/** 实例更新方式（缺省 undefined 视为 Polling，与后端 DTO 默认值对齐） */
const updateModeOf = (v: DataPointMapping): 'Polling' | 'Subscription' => v.updateMode ?? 'Polling';
const updateModeLabel = (v: DataPointMapping): string => updateModeOf(v) === 'Subscription' ? '订阅' : '轮询';
/** 列表徽章样式：Polling 蓝 / Subscription 紫 */
const updateModeBadgeClass = (v: DataPointMapping): string =>
  updateModeOf(v) === 'Subscription'
    ? 'bg-purple-50 dark:bg-purple-950/60 text-purple-700 dark:text-purple-300 border-purple-200 dark:border-purple-800'
    : 'bg-blue-50 dark:bg-blue-950/60 text-blue-700 dark:text-blue-300 border-blue-200 dark:border-blue-800';
/** 设备协议是否支持订阅推送（当前仅 OPC UA；与后端 ValidateSubscriptionCapabilityAsync 同源判定，用于下拉禁用兜底） */
const supportsSubscription = computed(() => (selectedDevice.value?.type ?? '').toUpperCase() === 'OPCUA');
/** 列表存在订阅变量 → 列头显示"间隔(ms)"（订阅语义=服务端采样/发布间隔），否则"轮询(ms)" */
const hasSubscriptionVariables = computed(() => dataPointMappings.value.some(v => updateModeOf(v) === 'Subscription'));

// ---------- 变量搜索、分类过滤与排序（方案一 双行紧凑流） ----------
const varSearchQuery = ref<string>('');
const varCategoryFilter = ref<string>('ALL');
const varSortBy = ref<'default' | 'key' | 'name' | 'address' | 'interval'>('default');
const varSortMenuOpen = ref<boolean>(false);
const varViewMode = ref<'card' | 'list'>('card');

const varSortOptions = [
  { value: 'default', label: '默认顺序', mobileLabel: '默认' },
  { value: 'key', label: '标识 (A-Z)', mobileLabel: '标识' },
  { value: 'name', label: '名称 (A-Z)', mobileLabel: '名称' },
  { value: 'address', label: '地址排序', mobileLabel: '地址' },
  { value: 'interval', label: '采集间隔', mobileLabel: '间隔' },
] as const;

const varSortMobileLabel = computed(() => {
  const found = varSortOptions.find(o => o.value === varSortBy.value);
  return found ? found.mobileLabel : '排序';
});

// 分类胶囊与计数
const varCategories = computed(() => {
  const list = dataPointMappings.value;
  return [
    { id: 'ALL', name: '全部', count: list.length },
    { id: 'ANALOG', name: '模拟量', count: list.filter(v => !isBitType(v.dataType)).length },
    { id: 'BOOL', name: '开关量', count: list.filter(v => isBitType(v.dataType)).length },
    { id: 'SUBSCRIPTION', name: '订阅', count: list.filter(v => updateModeOf(v) === 'Subscription').length },
    { id: 'ENABLED', name: '已启用', count: list.filter(v => v.isEnabled).length },
    { id: 'UNCONFIGURED', name: '待配地址', count: list.filter(v => needsAddress.value && !v.address).length }
  ];
});

// 过滤和排序后的变量列表
const filteredDataPointMappings = computed(() => {
  let list = dataPointMappings.value;
  const q = varSearchQuery.value.trim().toLowerCase();
  if (q) {
    list = list.filter(v =>
      v.key.toLowerCase().includes(q) ||
      (v.name && v.name.toLowerCase().includes(q)) ||
      (v.address && v.address.toLowerCase().includes(q))
    );
  }

  // 分类过滤
  if (varCategoryFilter.value === 'ANALOG') {
    list = list.filter(v => !isBitType(v.dataType));
  } else if (varCategoryFilter.value === 'BOOL') {
    list = list.filter(v => isBitType(v.dataType));
  } else if (varCategoryFilter.value === 'SUBSCRIPTION') {
    list = list.filter(v => updateModeOf(v) === 'Subscription');
  } else if (varCategoryFilter.value === 'ENABLED') {
    list = list.filter(v => v.isEnabled);
  } else if (varCategoryFilter.value === 'UNCONFIGURED') {
    list = list.filter(v => needsAddress.value && !v.address);
  }

  // 排序
  if (varSortBy.value === 'key') {
    list = [...list].sort((a, b) => a.key.localeCompare(b.key));
  } else if (varSortBy.value === 'name') {
    list = [...list].sort((a, b) => (a.name || '').localeCompare(b.name || ''));
  } else if (varSortBy.value === 'address') {
    list = [...list].sort((a, b) => (a.address || '').localeCompare(b.address || ''));
  } else if (varSortBy.value === 'interval') {
    list = [...list].sort((a, b) => (a.pollingIntervalMs ?? 1000) - (b.pollingIntervalMs ?? 1000));
  }

  return list;
});

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
      class="md:hidden bg-sky-50/80 dark:bg-slate-900 border-b border-sky-100 dark:border-slate-800 px-3.5 py-2 flex items-center justify-between gap-2 shrink-0">
      <button id="btn-open-device-drawer" @click="isMobileDeviceDrawerOpen = true"
        class="flex-1 flex items-center justify-between bg-white dark:bg-slate-800 border border-sky-200/70 dark:border-slate-700 rounded-lg px-3 py-1.5 text-left shadow-2xs active:scale-[0.99] transition-transform cursor-pointer">
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
              <span class="truncate">{{ headerModelMeta.name || '未配置模型' }}<template v-if="headerModelMeta.code"> · {{ headerModelMeta.code }}</template></span>
            </div>
          </div>
        </div>
        <div class="flex items-center gap-1 text-slate-400 pl-2">
          <ChevronDown class="w-4 h-4 text-[#1890ff]" />
        </div>
      </button>

      <div class="flex items-center gap-1.5 shrink-0">
        <button id="btn-mobile-refresh" @click="refreshAll"
          class="p-2 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 rounded-lg shadow-2xs cursor-pointer active:scale-95"
          title="刷新变量">
          <RefreshCw class="w-3.5 h-3.5" :class="isLoading ? 'animate-spin' : ''" />
        </button>
        <button id="btn-mobile-add-inst" @click="openAddModal" :disabled="uninstancedTemplates.length === 0"
          class="bg-[#1890ff] hover:bg-sky-600 disabled:opacity-40 disabled:cursor-not-allowed text-white p-2 rounded-lg flex items-center justify-center shadow-2xs cursor-pointer active:scale-95"
          title="添加变量实例">
          <Plus class="w-3.5 h-3.5" />
        </button>
      </div>
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

      <!-- Desktop Header (md 及以上展示完整设备与模型信息) -->
      <div v-if="selectedDevice"
        class="hidden md:flex bg-white dark:bg-slate-900 p-4 lg:p-5 border-b border-slate-200 dark:border-slate-800 shadow-2xs flex-row items-center justify-between gap-4 shrink-0 font-sans transition-colors">
        <div class="space-y-1 text-left">
          <div class="flex items-center gap-2">
            <span
              class="text-[10px] font-bold px-2 py-0.5 bg-slate-100 dark:bg-slate-800 border border-slate-200/50 dark:border-slate-700 rounded-full font-mono uppercase text-slate-500 dark:text-slate-400">{{
              selectedDevice.type }}</span>
            <span class="text-xs font-mono text-slate-400 dark:text-slate-500">
              模型: {{ headerModelMeta.name || '未配置' }}
              <template v-if="headerModelMeta.code"> · {{ headerModelMeta.code }}</template>
              <template v-if="headerModelMeta.version"> · v{{ headerModelMeta.version }}</template>
              <template v-if="deviceBindingModels.length > 1">（另有 {{ deviceBindingModels.length - 1 }} 个附加模型）</template>
            </span>
          </div>
          <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight">{{ selectedDevice.name }}</h2>
        </div>
        <div class="flex items-center gap-2 shrink-0">
          <span
            class="text-xs font-mono text-slate-500 dark:text-slate-400 bg-slate-50 dark:bg-slate-950 px-2.5 py-1 rounded border border-slate-200/60 dark:border-slate-800">
            已配置 <b class="text-emerald-600 dark:text-emerald-400">{{ dataPointMappings.length }}</b> /
            {{ modelTemplates.length }} 个模板变量
          </span>
        </div>
      </div>

      <!-- 检索与分类控制栏 (Workbench Toolbar - 方案一 双行紧凑流) -->
      <div v-if="selectedDevice" class="bg-white dark:bg-slate-900/95 border-b border-slate-200 dark:border-slate-800 px-3.5 sm:px-6 py-2 sm:py-2.5 flex flex-col md:flex-row md:items-center md:justify-between gap-2 md:gap-3 shrink-0 transition-colors shadow-2xs">
        <!-- 第 1 行：分类过滤横向滑动轨 -->
        <div class="relative w-full md:w-auto min-w-0">
          <div class="flex items-center gap-1.5 overflow-x-auto no-scrollbar py-0.5 max-w-full -mx-3.5 px-3.5 md:mx-0 md:px-0">
            <button
              v-for="cat in varCategories"
              :key="cat.id"
              @click="varCategoryFilter = cat.id"
              class="inline-flex items-center gap-1 px-2.5 py-1 md:px-3 md:py-1.5 rounded-lg text-[11px] md:text-xs font-medium transition-all whitespace-nowrap cursor-pointer active:scale-95 shrink-0"
              :class="varCategoryFilter === cat.id
                ? 'bg-sky-600 text-white shadow-xs'
                : 'bg-slate-50 dark:bg-slate-800/90 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 border border-slate-200/80 dark:border-slate-700/80'"
            >
              <span>{{ cat.name }}</span>
              <span class="text-[10px] font-mono px-1 py-0.2 rounded-full"
                :class="varCategoryFilter === cat.id ? 'bg-white/20 text-white' : 'bg-slate-200/70 text-slate-500 dark:bg-slate-700 dark:text-slate-400'">
                {{ cat.count }}
              </span>
            </button>
            <button
              v-if="uninstancedTemplates.length > 0"
              @click="addAllMissing"
              class="inline-flex items-center gap-1 px-2.5 py-1 md:px-3 md:py-1.5 rounded-lg text-[11px] md:text-xs font-bold border border-emerald-300 dark:border-emerald-800/80 bg-emerald-50 dark:bg-emerald-950/40 text-emerald-700 dark:text-emerald-300 hover:bg-emerald-100 dark:hover:bg-emerald-900/50 transition-all whitespace-nowrap cursor-pointer active:scale-95 shrink-0"
            >
              <Braces class="w-3 h-3" />
              <span>补齐待配 ({{ uninstancedTemplates.length }})</span>
            </button>
          </div>
        </div>

        <!-- 第 2 行（移动端）/ 右侧控制组（桌面端）：搜索 + 排序 + 视图切换 + 桌面操作 -->
        <div class="flex items-center gap-2 w-full md:w-auto md:ml-auto">
          <!-- 搜索输入框：移动端 flex-1 自适应伸缩 -->
          <div class="relative flex-1 md:w-48 lg:w-56 min-w-0">
            <Search class="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500 pointer-events-none" />
            <input
              v-model="varSearchQuery"
              placeholder="搜索标识、名称或地址..."
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg pl-8 pr-7 py-1.5 text-xs text-slate-800 dark:text-slate-200 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:border-sky-500 focus:bg-white dark:focus:bg-slate-900 transition-colors"
            />
            <button
              v-if="varSearchQuery"
              @click="varSearchQuery = ''"
              class="absolute right-2 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-700 dark:text-slate-500 dark:hover:text-slate-300 cursor-pointer"
            >
              <X class="w-3.5 h-3.5" />
            </button>
          </div>

          <!-- 紧凑排序按钮及弹层 -->
          <div class="relative shrink-0">
            <button
              type="button"
              @click="varSortMenuOpen = !varSortMenuOpen"
              class="flex items-center gap-1 px-2.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-300 text-xs font-medium cursor-pointer shadow-2xs active:scale-95"
              title="选择排序方式"
            >
              <ArrowUpDown class="w-3 h-3 text-sky-600 dark:text-sky-400" />
              <span class="text-[11px]">{{ varSortMobileLabel }}</span>
            </button>

            <!-- 排序遮罩与浮层 -->
            <div
              v-if="varSortMenuOpen"
              class="fixed inset-0 z-40"
              @click="varSortMenuOpen = false"
            />
            <div
              v-if="varSortMenuOpen"
              class="absolute right-0 top-full mt-1.5 z-50 min-w-[130px] py-1 bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-200 dark:border-slate-800 text-xs animate-in fade-in zoom-in-95 duration-150"
            >
              <button
                v-for="opt in varSortOptions"
                :key="opt.value"
                type="button"
                @click="varSortBy = opt.value; varSortMenuOpen = false"
                class="w-full text-left px-3 py-1.5 text-[11px] font-medium transition-colors flex items-center justify-between cursor-pointer"
                :class="varSortBy === opt.value ? 'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 font-bold' : 'text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800'"
              >
                <span>{{ opt.label }}</span>
                <Check v-if="varSortBy === opt.value" class="w-3 h-3 text-sky-600 dark:text-sky-400" />
              </button>
            </div>
          </div>

          <!-- 移动端视图模式切换（卡片 / 列表） -->
          <div class="flex md:hidden items-center bg-slate-100 dark:bg-slate-800 p-0.5 rounded-lg border border-slate-200 dark:border-slate-700 shrink-0">
            <button
              @click="varViewMode = 'card'"
              class="p-1 rounded-md transition-all cursor-pointer"
              :class="varViewMode === 'card' ? 'bg-white dark:bg-slate-900 text-sky-600 dark:text-sky-400 shadow-2xs' : 'text-slate-400 hover:text-slate-600 dark:hover:text-slate-300'"
              title="卡片视图"
            >
              <LayoutGrid class="w-3.5 h-3.5" />
            </button>
            <button
              @click="varViewMode = 'list'"
              class="p-1 rounded-md transition-all cursor-pointer"
              :class="varViewMode === 'list' ? 'bg-white dark:bg-slate-900 text-sky-600 dark:text-sky-400 shadow-2xs' : 'text-slate-400 hover:text-slate-600 dark:hover:text-slate-300'"
              title="紧凑列表"
            >
              <List class="w-3.5 h-3.5" />
            </button>
          </div>

          <!-- 桌面端专属操作按钮 -->
          <div class="hidden md:flex items-center gap-1.5 shrink-0 pl-1 border-l border-slate-200 dark:border-slate-800">
            <button @click="openAddModal" :disabled="uninstancedTemplates.length === 0"
              class="inline-flex items-center gap-1 text-xs font-bold bg-[#1890ff] text-white hover:bg-sky-600 disabled:opacity-40 disabled:cursor-not-allowed px-2.5 py-1.5 rounded-lg cursor-pointer transition-colors shadow-2xs">
              <Plus class="w-3.5 h-3.5" /> 添加
            </button>
            <button @click="addAllMissing" :disabled="uninstancedTemplates.length === 0"
              class="inline-flex items-center gap-1 text-xs font-bold border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed px-2.5 py-1.5 rounded-lg cursor-pointer transition-colors">
              <Braces class="w-3.5 h-3.5" /> 一键补齐 ({{ uninstancedTemplates.length }})
            </button>
            <button @click="refreshAll"
              class="inline-flex items-center gap-1 text-xs font-bold border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 px-2 py-1.5 rounded-lg cursor-pointer transition-colors">
              <RefreshCw class="w-3.5 h-3.5" :class="isLoading ? 'animate-spin' : ''" />
            </button>
          </div>
        </div>
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

      <!-- Table & Mobile List Container -->
      <div v-if="selectedDevice" class="flex-1 flex flex-col min-h-0 overflow-hidden">
        <!-- Desktop Table Container (md 及以上) -->
        <div class="hidden md:block flex-1 p-3 sm:px-6 overflow-y-auto">
          <div
            class="bg-white dark:bg-slate-900 border border-slate-200/80 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm">
            <div class="overflow-x-auto">
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
                    <th class="px-4 py-3.5">{{ hasSubscriptionVariables ? '间隔(ms)' : '轮询(ms)' }}</th>
                    <th class="px-4 py-3.5">更新方式</th>
                    <th class="px-4 py-3.5">启用</th>
                    <th class="px-4 py-3.5 text-right">操作</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-slate-100 dark:divide-slate-800 bg-white dark:bg-slate-900">
                  <tr v-for="v in filteredDataPointMappings" :key="v.id"
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
                        :class="accessBadgeClass(v)" :title="v.accessModeOverride != null
                          ? '该设备实例覆盖模板 → ' + accessLabel(effectiveAccessOf(v))
                          : '继承模板：' + accessLabel(templateAccessOf(v))">
                        {{ accessLabel(effectiveAccessOf(v)) }}<span v-if="v.accessModeOverride != null"
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
                      <span class="inline-block px-1.5 py-0.5 text-[9px] font-bold rounded border"
                        :class="updateModeBadgeClass(v)"
                        :title="updateModeOf(v) === 'Subscription' ? '订阅推送：值变化由服务器即时推送，间隔=采样/发布' : '自主轮询：按间隔主动读取'">
                        {{ updateModeLabel(v) }}
                      </span>
                    </td>
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
                  <tr v-if="!isLoading && filteredDataPointMappings.length === 0">
                    <td :colspan="tableColspan"
                      class="p-10 text-center text-slate-400 dark:text-slate-500 text-xs font-sans">
                      <Database class="w-8 h-8 mx-auto mb-2 opacity-30" />
                      {{ varSearchQuery || varCategoryFilter !== 'ALL' ? '未找到匹配的变量实例' : '该设备尚未配置变量实例，请点击“添加实例”或“一键补齐”。' }}
                      <template v-if="modelTemplates.length > 0 && !varSearchQuery && varCategoryFilter === 'ALL'">（模型共有 {{ modelTemplates.length }} 个模板变量）</template>
                    </td>
                  </tr>
                  <tr v-if="loadError">
                    <td :colspan="tableColspan" class="p-6 text-center text-rose-500 text-xs font-sans">加载失败: {{ loadError
                      }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>

        <!-- Mobile list & cards (方案一 移动端卡片/紧凑列表切换) -->
        <div class="block md:hidden flex-1 overflow-y-auto p-3 sm:p-4">
          <!-- 卡片视图模式 (Card Mode) -->
          <div v-if="varViewMode === 'card'" class="space-y-2.5">
            <div
              v-for="v in filteredDataPointMappings"
              :key="v.id"
              class="bg-white dark:bg-slate-900 rounded-xl p-3.5 border border-slate-200/80 dark:border-slate-800 shadow-2xs text-left transition-all"
            >
              <!-- 顶部：标识、名称与启用切换 -->
              <div class="flex items-start justify-between gap-2">
                <div class="min-w-0 flex-1">
                  <div class="flex items-center gap-1.5 font-bold font-mono text-xs text-slate-900 dark:text-white">
                    <Binary class="w-3.5 h-3.5 text-sky-500 shrink-0" />
                    <span class="truncate">{{ v.key }}</span>
                  </div>
                  <div class="text-[11px] text-slate-600 dark:text-slate-300 mt-1 font-sans font-medium truncate">
                    {{ v.name }}<span v-if="v.unit" class="text-[10px] text-slate-400 font-mono ml-1">({{ v.unit }})</span>
                  </div>
                </div>
                <!-- 启用开关 -->
                <div class="flex items-center gap-1.5 shrink-0">
                  <span class="text-[10px] font-mono" :class="v.isEnabled ? 'text-emerald-600 dark:text-emerald-400 font-bold' : 'text-slate-400'">
                    {{ v.isEnabled ? '启用' : '停用' }}
                  </span>
                  <button
                    @click="toggleEnabled(v)"
                    class="relative w-8 h-4.5 rounded-full transition-colors cursor-pointer"
                    :class="v.isEnabled ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-700'"
                  >
                    <span
                      class="absolute top-0.5 w-3.5 h-3.5 bg-white rounded-full shadow-xs transition-all"
                      :class="v.isEnabled ? 'left-[16px]' : 'left-0.5'"
                    />
                  </button>
                </div>
              </div>

              <!-- 中部参数徽章：类型、读写、更新方式、采样周期、协议地址 -->
              <div class="mt-2 pt-2 border-t border-slate-100 dark:border-slate-800/80 flex flex-wrap items-center gap-1.5 text-[10px] font-mono">
                <span
                  class="px-1.5 py-0.5 rounded border font-bold uppercase"
                  :class="isBitType(v.dataType) ? 'bg-indigo-50 dark:bg-indigo-950/60 text-indigo-700 dark:text-indigo-300 border-indigo-200 dark:border-indigo-800' : 'bg-sky-50 dark:bg-sky-950/60 text-sky-700 dark:text-sky-300 border-sky-200 dark:border-sky-800'"
                >
                  {{ v.dataType }}
                </span>
                <span class="px-1.5 py-0.5 rounded border font-bold" :class="accessBadgeClass(v)">
                  {{ accessLabel(effectiveAccessOf(v)) }}
                </span>
                <span class="px-1.5 py-0.5 rounded border font-bold" :class="updateModeBadgeClass(v)">
                  {{ updateModeLabel(v) }} · {{ v.pollingIntervalMs ?? 1000 }}ms
                </span>
                <span
                  v-if="needsAddress && v.address"
                  class="bg-slate-100 dark:bg-slate-800 px-1.5 py-0.5 rounded text-slate-700 dark:text-slate-300 border border-slate-200/60 dark:border-slate-700 font-bold ml-auto"
                >
                  {{ v.address }}
                </span>
                <span
                  v-else-if="needsAddress"
                  class="bg-rose-50 dark:bg-rose-950/50 text-rose-600 dark:text-rose-400 border border-rose-200 dark:border-rose-900 px-1.5 py-0.5 rounded font-bold ml-auto"
                >
                  未配置地址
                </span>
              </div>

              <!-- 底部操作按钮 -->
              <div class="mt-2.5 pt-2 border-t border-slate-100 dark:border-slate-800/80 flex items-center gap-2">
                <button
                  @click="openEditModal(v)"
                  class="flex-1 py-1.5 bg-slate-50 dark:bg-slate-800/80 hover:bg-sky-50 dark:hover:bg-sky-950/40 text-[#1890ff] text-xs font-medium rounded-lg border border-slate-200 dark:border-slate-700 flex items-center justify-center gap-1 cursor-pointer transition-colors active:scale-[0.98]"
                >
                  <Settings class="w-3.5 h-3.5" />
                  <span>配置参数</span>
                </button>
                <button
                  @click="confirmDelete(v)"
                  class="py-1.5 px-3 bg-rose-50/60 dark:bg-rose-950/30 hover:bg-rose-100 dark:hover:bg-rose-900/50 text-rose-600 dark:text-rose-400 text-xs font-medium rounded-lg border border-rose-200 dark:border-rose-900 flex items-center justify-center gap-1 cursor-pointer transition-colors active:scale-[0.98]"
                >
                  <Trash2 class="w-3.5 h-3.5" />
                  <span>删除</span>
                </button>
              </div>
            </div>
          </div>

          <!-- 紧凑列表模式 (Compact List Mode) -->
          <div v-else class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200/80 dark:border-slate-800 divide-y divide-slate-100 dark:divide-slate-800 overflow-hidden shadow-2xs">
            <div
              v-for="v in filteredDataPointMappings"
              :key="v.id"
              class="p-2.5 flex items-center justify-between gap-2 text-left hover:bg-slate-50 dark:hover:bg-slate-800/50 transition-colors"
            >
              <div class="min-w-0 flex-1">
                <div class="flex items-center gap-1.5">
                  <span class="w-1.5 h-1.5 rounded-full shrink-0" :class="v.isEnabled ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-600'" />
                  <span class="font-bold font-mono text-xs text-slate-800 dark:text-slate-200 truncate">{{ v.key }}</span>
                  <span class="text-[10px] text-slate-400 dark:text-slate-500 truncate">{{ v.name }}</span>
                </div>
                <div class="flex items-center gap-1.5 mt-1 text-[10px] font-mono text-slate-500 dark:text-slate-400">
                  <span class="px-1 rounded bg-slate-100 dark:bg-slate-800 font-bold uppercase">{{ v.dataType }}</span>
                  <span v-if="needsAddress" class="truncate font-bold" :class="v.address ? 'text-slate-600 dark:text-slate-300' : 'text-rose-500'">
                    {{ v.address || '无地址' }}
                  </span>
                  <span>· {{ updateModeLabel(v) }}</span>
                </div>
              </div>

              <div class="flex items-center gap-1.5 shrink-0">
                <button
                  @click="toggleEnabled(v)"
                  class="p-1 rounded text-[10px] font-bold border transition-colors cursor-pointer"
                  :class="v.isEnabled ? 'text-emerald-600 bg-emerald-50 border-emerald-200 dark:bg-emerald-950/60 dark:border-emerald-800' : 'text-slate-400 bg-slate-50 border-slate-200 dark:bg-slate-800 dark:border-slate-700'"
                >
                  {{ v.isEnabled ? '启用' : '停用' }}
                </button>
                <button
                  @click="openEditModal(v)"
                  class="p-1 text-slate-400 hover:text-sky-600 dark:hover:text-sky-400 rounded hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
                  title="配置"
                >
                  <Settings class="w-3.5 h-3.5" />
                </button>
                <button
                  @click="confirmDelete(v)"
                  class="p-1 text-slate-400 hover:text-rose-600 dark:hover:text-rose-400 rounded hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
                  title="删除"
                >
                  <Trash2 class="w-3.5 h-3.5" />
                </button>
              </div>
            </div>
          </div>

          <!-- 空状态 -->
          <div v-if="!isLoading && filteredDataPointMappings.length === 0" class="py-12 px-4 text-center text-slate-400 dark:text-slate-500">
            <Database class="w-8 h-8 mx-auto mb-2 opacity-30" />
            <p class="text-xs">
              {{ varSearchQuery || varCategoryFilter !== 'ALL' ? '未找到匹配的变量实例' : '该设备尚未配置变量实例' }}
            </p>
            <button
              v-if="varSearchQuery || varCategoryFilter !== 'ALL'"
              @click="varSearchQuery = ''; varCategoryFilter = 'ALL'"
              class="mt-2 text-[11px] text-sky-600 dark:text-sky-400 underline cursor-pointer"
            >
              重置过滤条件
            </button>
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
              <div v-for="f in visibleAddressFields" :key="f.key">
                <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">{{ f.label
                  }}<span class="text-rose-400" v-if="f.required"> *</span></label>
                <select v-if="f.type === 'select'" v-model="(editingCfg as any)[f.key]"
                  @change="onAddressFieldWidthChange"
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
          <div>
            <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">更新方式</label>
            <select v-model="editingForm.updateMode" :disabled="!supportsSubscription"
              class="w-full disabled:opacity-40 disabled:cursor-not-allowed bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs focus:outline-none">
              <option value="Polling">自主轮询（按间隔主动读取）</option>
              <option value="Subscription">订阅推送（值变化即时推送）</option>
            </select>
            <p class="mt-1 text-[9px] text-slate-400 dark:text-slate-500 font-sans leading-relaxed"
              v-if="!supportsSubscription">当前协议驱动不支持订阅更新（仅 OPC UA 支持）。</p>
            <p class="mt-1 text-[9px] text-slate-400 dark:text-slate-500 font-sans leading-relaxed" v-else>订阅模式由服务器在值变化时推送；下方间隔语义变为服务端采样/发布间隔。</p>
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div v-if="needsBitOffset && !structuredHasBit">
              <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">位偏移（BOOL/BIT）</label>
              <input v-model.number="editingForm.bitOffset" type="number" min="0" max="7"
                :disabled="!isBitType(editingForm.dataType)"
                class="w-full disabled:opacity-40 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs font-mono focus:outline-none" />
            </div>
            <div :class="needsBitOffset && !structuredHasBit ? '' : 'col-span-2'">
              <label class="block text-[10px] font-bold text-slate-400 uppercase mb-1">{{
                editingForm.updateMode === 'Subscription' ? '采样间隔（ms）' : '轮询间隔（ms）' }}</label>
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
            <select v-model="editingForm.accessModeOverride"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:border-[#1890ff] rounded-lg px-2.5 py-1.5 text-xs focus:outline-none">
              <option :value="null">继承模板（当前：{{ accessLabel(templateAccessOf(editingForm)) }}）</option>
              <option value="Read">强制只读（Read）</option>
              <option value="Write">强制只写（Write）</option>
              <option value="ReadWrite">强制读写（ReadWrite）</option>
            </select>
            <p class="mt-1 text-[9px] text-slate-400 dark:text-slate-500 font-sans leading-relaxed">实例级覆盖优先于模板：Read=只读、Write=只写、ReadWrite=读写（留空继承模板权限）。</p>
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