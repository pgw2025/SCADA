<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import {
  Plus,
  Edit3,
  Trash2,
  Search,
  RefreshCw,
  ChevronLeft,
  ChevronRight,
  X,
  Power,
  Filter,
  Link2,
  Lock,
  Code2
} from 'lucide-vue-next';
import { systemConfig, addLog } from '../store/index';
import { fetchControllerOptions } from '../api/controllerApi';
import { fetchProtocols } from '../api/protocolApi';
import {
  fetchDeviceConnections,
  createDeviceConnection,
  updateDeviceConnection,
  deleteDeviceConnection
} from '../api/connectionApi';
import { extractApiError } from '../api/http';
import { showToast } from '../services/toastService';
import { devices } from '../store/deviceStore';
import { syncDevices } from '../services/deviceService';
import RefDevicesPanel from './RefDevicesPanel.vue';
import { DeviceConnection, DeviceConnectionRequest, ControllerOption, Protocol } from '../types';

// ================= 列表（接口返回全量，前端本地筛选 + 分页） =================
const all = ref<DeviceConnection[]>([]);
const loading = ref(false);

const controllerOptions = ref<ControllerOption[]>([]);
const protocols = ref<Protocol[]>([]);

const filterControllerId = ref<number | null>(null);
const keyword = ref('');

const pageIndex = ref(1);
const pageSize = ref(20);

const filtered = computed<DeviceConnection[]>(() =>
  all.value.filter(c =>
    (filterControllerId.value == null || c.controllerId === filterControllerId.value) &&
    (!keyword.value.trim() ||
      [c.name, c.host ?? '', String(c.port ?? ''), c.controllerName ?? '']
        .join(' ').toLowerCase().includes(keyword.value.trim().toLowerCase()))
  )
);
const totalPages = computed(() => Math.max(1, Math.ceil(filtered.value.length / pageSize.value)));
const paged = computed<DeviceConnection[]>(() =>
  filtered.value.slice((pageIndex.value - 1) * pageSize.value, pageIndex.value * pageSize.value)
);

const loadControllerOptions = async () => {
  controllerOptions.value = await fetchControllerOptions();
};
const loadProtocols = async () => {
  protocols.value = await fetchProtocols();
};

const loadList = async () => {
  if (systemConfig.value.isSimulationActive) { all.value = []; return; }
  loading.value = true;
  try {
    all.value = await fetchDeviceConnections();
  } catch (e: any) {
    addLog('连接管理', `获取连接列表失败: ${e?.message}`, 'warning');
    all.value = [];
  } finally {
    loading.value = false;
  }
};

const applyFilter = () => { pageIndex.value = 1; };
const resetFilter = () => {
  filterControllerId.value = null;
  keyword.value = '';
  applyFilter();
};
const changePage = (delta: number) => {
  const next = pageIndex.value + delta;
  if (next < 1 || next > totalPages.value) return;
  pageIndex.value = next;
};

// ================= 表单（新增/编辑共用） =================
const showModal = ref(false);
const editingId = ref<number | null>(null);
const editingConn = ref<DeviceConnection | null>(null);
const formError = ref('');
const saving = ref(false);

// 被引用的连接允许编辑参数字段；仅 ControllerId/ProtocolId（结构性绑定）冻结，须到设备页变更。
const isEditingReferenced = computed(() => editingConn.value?.inUseByDevice === true);

// 当前选择协议 key（用于渲染结构化配置面板）
const selectedProtocolKey = computed(() => {
  const p = protocols.value.find(p => p.id === form.value.ProtocolId);
  return (p?.key || '').toUpperCase();
});

const form = ref<DeviceConnectionRequest>({
  ControllerId: 0,
  Name: '',
  ProtocolId: 0,
  ConfigJson: null,
  ReconnectIntervalMs: 5000,
  IsEnabled: true
});

// 结构化配置（按协议 key 驱动面板渲染；字段命名与后端 *Config DTO 对齐），
// configMode 为 raw 时退化为原始 JSON 编辑（非受支持协议/解析失败走此模式）。
const cfgStructured = ref({
  // S7
  ipAddress: '', port: 102, rack: 0, slot: 1, cpuType: 'S71500', ioTimeoutMs: 5000, connectTimeoutMs: 5000,
  // OPC UA
  endpointUrl: '', securityPolicy: 'None', username: '', password: '',
  // Modbus TCP
  modbusIp: '', modbusPort: 502, unitId: 1,
  // MQTT
  broker: '', mqttPort: 1883, mqttUsername: '', mqttPassword: '', topic: '', clientId: '',
  // Virtual
  intervalMs: 1000, randomValues: true
});
const cfgRaw = ref('{}');
const configMode = ref<'structured' | 'raw'>('structured');

const structuredDefault = {
  ipAddress: '', port: 102, rack: 0, slot: 1, cpuType: 'S71500', ioTimeoutMs: 5000, connectTimeoutMs: 5000,
  endpointUrl: '', securityPolicy: 'None', username: '', password: '',
  modbusIp: '', modbusPort: 502, unitId: 1,
  broker: '', mqttPort: 1883, mqttUsername: '', mqttPassword: '', topic: '', clientId: '',
  intervalMs: 1000, randomValues: true
};

const numberOr = (v: any, fallback: number): number => {
  const n = Number(v);
  return Number.isFinite(n) ? n : fallback;
};

// 把已有配置原文回填到结构化字段；解析失败则切到 raw 编辑。
const applyConfigToStructured = (json: string | null | undefined) => {
  cfgRaw.value = json || '{}';
  configMode.value = 'structured';
  try {
    const o = json ? JSON.parse(json) : {};
    cfgStructured.value = {
      ...structuredDefault,
      ipAddress: o.IpAddress ?? o.ip ?? '',
      port: numberOr(o.Port ?? o.port, 102),
      rack: numberOr(o.Rack ?? o.rack, 0),
      slot: numberOr(o.Slot ?? o.slot, 1),
      cpuType: o.CpuType ?? o.cpuType ?? 'S71500',
      ioTimeoutMs: numberOr(o.IoTimeoutMs ?? o.ioTimeoutMs, 5000),
      connectTimeoutMs: numberOr(o.ConnectTimeoutMs ?? o.connectTimeoutMs, 5000),
      endpointUrl: o.EndpointUrl ?? o.endpointUrl ?? '',
      securityPolicy: o.SecurityPolicy ?? o.securityPolicy ?? 'None',
      username: o.Username ?? o.username ?? '',
      password: o.Password ?? o.password ?? '',
      modbusIp: o.IpAddress ?? o.ip ?? '',
      modbusPort: numberOr(o.Port ?? o.port, 502),
      unitId: numberOr(o.UnitId ?? o.unitId, 1),
      broker: o.Broker ?? o.broker ?? '',
      mqttPort: numberOr(o.Port ?? o.port, 1883),
      mqttUsername: o.Username ?? o.username ?? '',
      mqttPassword: o.Password ?? o.password ?? '',
      topic: o.Topic ?? o.topic ?? '',
      clientId: o.ClientId ?? o.clientId ?? '',
      intervalMs: numberOr(o.IntervalMs ?? o.intervalMs, 1000),
      randomValues: o.RandomValues ?? o.randomValues ?? true
    };
  } catch {
    configMode.value = 'raw';
  }
};

// 按协议 key 组装 ConfigJson 原文（字段命名与后端 *Config DTO 一致）。
const buildConfigJson = (key: string): string => {
  const s = cfgStructured.value;
  switch (key) {
    case 'S7':
      return JSON.stringify({
        IpAddress: s.ipAddress || '127.0.0.1',
        Port: numberOr(s.port, 102),
        Rack: numberOr(s.rack, 0),
        Slot: numberOr(s.slot, 1),
        CpuType: s.cpuType || 'S71500',
        IoTimeoutMs: numberOr(s.ioTimeoutMs, 5000),
        ConnectTimeoutMs: numberOr(s.connectTimeoutMs, 5000)
      });
    case 'OPCUA':
      return JSON.stringify({
        EndpointUrl: s.endpointUrl || 'opc.tcp://127.0.0.1:4840',
        SecurityPolicy: s.securityPolicy || 'None',
        Username: s.username || null,
        Password: s.password || null
      });
    case 'MODBUSTCP':
      return JSON.stringify({
        IpAddress: s.modbusIp || '127.0.0.1',
        Port: numberOr(s.modbusPort, 502),
        UnitId: numberOr(s.unitId, 1)
      });
    case 'MQTT':
      return JSON.stringify({
        Broker: s.broker || '127.0.0.1',
        Port: numberOr(s.mqttPort, 1883),
        Username: s.mqttUsername || null,
        Password: s.mqttPassword || null,
        Topic: s.topic || '',
        ClientId: s.clientId || `scada-${Date.now()}`
      });
    case 'VIRTUAL':
      return JSON.stringify({ IntervalMs: numberOr(s.intervalMs, 1000), RandomValues: s.randomValues });
    default:
      return '{}';
  }
};

const supportedProtocolKeys = ['S7', 'OPCUA', 'MODBUSTCP', 'MQTT', 'VIRTUAL'];

// 用户主动切换协议时，重置为所选协议的默认结构化参数，避免残留上一协议的输入值。
const onProtocolChange = () => {
  cfgStructured.value = { ...structuredDefault };
  cfgRaw.value = '{}';
};

const openCreate = () => {
  editingId.value = null;
  editingConn.value = null;
  formError.value = '';
  form.value = {
    ControllerId: controllerOptions.value[0]?.id || 0,
    Name: '',
    ProtocolId: protocols.value[0]?.id || 0,
    ConfigJson: '{}',
    TimeoutMs: 5000,
    ReconnectIntervalMs: 5000,
    IsEnabled: true
  };
  applyConfigToStructured('{}');
  showModal.value = true;
};

const openEdit = (c: DeviceConnection) => {
  editingId.value = c.id;
  editingConn.value = c;
  formError.value = '';
  form.value = {
    ControllerId: c.controllerId,
    Name: c.name,
    ProtocolId: c.protocolId,
    ConfigJson: c.configJson ?? null,
    TimeoutMs: c.timeoutMs,
    ReconnectIntervalMs: c.reconnectIntervalMs,
    IsEnabled: c.isEnabled
  };
  applyConfigToStructured(c.configJson);
  showModal.value = true;
};

const save = async () => {
  formError.value = '';
  if (!form.value.ControllerId) { showToast('请选择所属控制器', 'warning'); return; }
  if (!form.value.Name.trim()) { showToast('请输入连接名称', 'warning'); return; }
  if (!form.value.ProtocolId) { showToast('请选择协议', 'warning'); return; }

  const key = selectedProtocolKey.value;
  let configJson: string;
  if (configMode.value === 'raw' || !supportedProtocolKeys.includes(key)) {
    const trimmed = cfgRaw.value.trim();
    configJson = trimmed && trimmed !== '{}' ? trimmed : '{}';
  } else {
    configJson = buildConfigJson(key);
  }

  saving.value = true;
  try {
    const dto: DeviceConnectionRequest = {
      ControllerId: form.value.ControllerId,
      Name: form.value.Name.trim(),
      ProtocolId: form.value.ProtocolId,
      ConfigJson: configJson,
      ReconnectIntervalMs: form.value.ReconnectIntervalMs,
      IsEnabled: form.value.IsEnabled
    };
    if (editingId.value != null) {
      await updateDeviceConnection(editingId.value, dto);
      addLog('连接管理', `更新了连接 [${dto.Name}]`, 'normal');
    } else {
      await createDeviceConnection(dto);
      addLog('连接管理', `新增连接 [${dto.Name}]`, 'normal');
    }
    showModal.value = false;
    showToast('保存成功', 'success');
    loadList();
  } catch (e: any) {
    formError.value = extractApiError(e);
  } finally {
    saving.value = false;
  }
};

const remove = async (c: DeviceConnection) => {
  if (confirm(`确定删除连接 [${c.name}] 吗？`)) {
    try {
      await deleteDeviceConnection(c.id);
      addLog('连接管理', `删除了连接 [${c.name}]`, 'warning');
      showToast('已删除', 'success');
      if (paged.value.length === 1 && pageIndex.value > 1) pageIndex.value -= 1;
      loadList();
    } catch (e: any) {
      showToast(extractApiError(e), 'error');
    }
  }
};

const endpointLabel = (c: DeviceConnection): string =>
  c.host ? (c.port != null ? `${c.host}:${c.port}` : c.host) : '—';

const fmtTime = (ts?: string | null) => {
  if (!ts) return '—';
  const d = new Date(ts);
  if (isNaN(d.getTime())) return ts;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

// ================= 左列表选中 + 右栏关联设备 =================
const selectedId = ref<number | null>(null);
const selectedItem = computed<DeviceConnection | null>(() =>
  selectedId.value != null ? all.value.find(x => x.id === selectedId.value) ?? null : null
);

// 该连接被多少设备引用（来自 devices store 全量）
const connectionDeviceCount = (connectionId: number): number =>
  devices.value.filter(d => Number(d.connectionId) === Number(connectionId)).length;

const selectItem = (c: DeviceConnection) => {
  selectedId.value = c.id;
};

const refreshAll = () => {
  loadList();
  syncDevices();
};

onMounted(async () => {
  await Promise.all([loadControllerOptions(), loadProtocols()]);
  loadList();
  syncDevices();
});
</script>

<template>
  <div class="h-full overflow-y-auto p-4 sm:p-6 bg-slate-50/50 dark:bg-transparent text-[#1e293b] dark:text-slate-100 select-none">
    <!-- Header -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between border-b border-slate-200 dark:border-slate-800 pb-5 gap-4 text-left">
      <div>
        <h1 class="text-xl font-bold font-sans text-slate-900 dark:text-white tracking-tight">连接管理</h1>
        <p class="text-xs text-slate-500 dark:text-slate-400 mt-1">
          登记设备连接资产台账；被设备引用的连接请在「设备管理」页维护
        </p>
      </div>
      <button
        @click="openCreate"
        class="bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 shadow-sm"
      >
        <Plus class="w-4 h-4" />
        添加连接
      </button>
    </div>

    <!-- 左列表 + 右关联设备：左右分栏 -->
    <div class="mt-5 flex flex-col md:flex-row gap-4">
      <!-- 左栏：连接列表（桌面端常显） -->
      <aside class="hidden md:flex flex-col w-80 shrink-0 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden text-left">
        <div class="p-3 border-b border-slate-100 dark:border-slate-800 space-y-2">
          <select
            v-model="filterControllerId"
            @change="applyFilter"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-xs font-bold text-slate-700 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]"
          >
            <option :value="null">全部控制器</option>
            <option v-for="c in controllerOptions" :key="c.id" :value="c.id">{{ c.code }} · {{ c.name }}</option>
          </select>
          <div class="relative">
            <Search class="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400" />
            <input
              v-model="keyword"
              type="text"
              placeholder="名称 / 地址 / 端口 / 控制器"
              @keyup.enter="applyFilter"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg pl-8 pr-2.5 py-1.5 text-xs text-slate-700 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]"
            />
          </div>
          <div class="flex items-center justify-between gap-2">
            <button
              v-if="filterControllerId != null || keyword"
              @click="resetFilter"
              class="text-rose-500 hover:text-rose-700 font-bold cursor-pointer text-xs"
            >
              清除筛选
            </button>
            <button
              @click="refreshAll"
              class="ml-auto text-[10px] text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 font-bold px-2 py-0.5 rounded border border-slate-200 dark:border-slate-700 hover:border-slate-300 transition-all cursor-pointer inline-flex items-center gap-1"
            >
              <RefreshCw class="w-3 h-3" :class="loading ? 'animate-spin' : ''" />
              刷新
            </button>
          </div>
        </div>

        <div class="flex-1 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800">
          <div
            v-for="c in paged"
            :key="c.id"
            role="button"
            @click="selectItem(c)"
            :class="[
              'px-3 py-2.5 cursor-pointer border-l-4 transition-all text-left',
              selectedId === c.id
                ? 'bg-sky-50 dark:bg-slate-800/80 border-l-[#1890ff]'
                : 'border-l-transparent hover:bg-slate-50 dark:hover:bg-slate-800/50'
            ]"
          >
            <div class="flex items-center justify-between gap-2">
              <span class="font-sans font-bold text-slate-800 dark:text-white text-xs inline-flex items-center gap-1.5 min-w-0">
                <Link2 class="w-3.5 h-3.5 text-slate-400 shrink-0" />
                <span class="truncate">{{ c.name }}</span>
              </span>
              <span
                class="shrink-0 text-[10px] font-bold px-1.5 py-0.5 rounded-full border"
                :class="connectionDeviceCount(c.id) > 0
                  ? 'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 border-sky-200 dark:border-sky-800'
                  : 'bg-slate-100 dark:bg-slate-800 text-slate-400 border-slate-200 dark:border-slate-700'"
              >
                {{ connectionDeviceCount(c.id) }} 台设备
              </span>
            </div>
            <div class="mt-1 flex items-center gap-2 text-[10px] text-slate-400 dark:text-slate-500">
              <span class="bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 font-bold px-1.5 py-0.5 rounded-full">{{ c.protocolName || `#${c.protocolId}` }}</span>
              <span class="truncate">{{ c.controllerName || `#${c.controllerId}` }}</span>
            </div>
            <div class="mt-1 flex items-center gap-2 text-[10px] text-slate-400 dark:text-slate-500">
              <span class="font-mono">{{ endpointLabel(c) }}</span>
              <span v-if="c.inUseByDevice" class="inline-flex items-center gap-0.5 text-amber-600 dark:text-amber-400 font-bold">
                <Link2 class="w-3 h-3" />已关联
              </span>
            </div>
          </div>
          <div v-if="paged.length === 0 && !loading" class="py-8 text-center text-slate-400 dark:text-slate-500 text-xs">
            暂无连接
          </div>
        </div>

        <!-- 左栏分页 -->
        <div class="px-3 py-2.5 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between text-[10px] text-slate-400">
          <button
            @click="changePage(-1)"
            :disabled="pageIndex <= 1"
            class="p-1 rounded border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
          >
            <ChevronLeft class="w-3.5 h-3.5" />
          </button>
          <span>第 {{ pageIndex }} / {{ totalPages }} 页（每页 {{ pageSize }}）</span>
          <button
            @click="changePage(1)"
            :disabled="pageIndex >= totalPages"
            class="p-1 rounded border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
          >
            <ChevronRight class="w-3.5 h-3.5" />
          </button>
        </div>
      </aside>

      <!-- 右栏：选中连接 → 摘要 + 关联设备 -->
      <main class="flex-1 min-w-0 flex flex-col gap-4">
        <!-- 移动端选择器 -->
        <div class="md:hidden bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-2">
          <select
            v-model="selectedId"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-xs font-bold text-slate-700 dark:text-slate-200 focus:outline-none"
          >
            <option :value="null" disabled>选择连接</option>
            <option v-for="c in paged" :key="c.id" :value="c.id">{{ c.name }}</option>
          </select>
        </div>

        <div v-if="!selectedItem" class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl py-12 text-center text-slate-400 dark:text-slate-500 text-xs">
          <Link2 class="w-8 h-8 mx-auto mb-2 opacity-20" />
          <span>请从左侧选择一条连接，查看其被哪些设备关联</span>
        </div>

        <template v-else>
          <!-- 选中连接摘要 + 操作 -->
          <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 text-left">
            <div class="flex flex-wrap items-start justify-between gap-3">
              <div class="flex items-start gap-3">
                <div class="w-9 h-9 rounded-lg bg-sky-50 dark:bg-sky-950/60 flex items-center justify-center shrink-0">
                  <Link2 class="w-4 h-4 text-sky-600 dark:text-sky-400" />
                </div>
                <div>
                  <h3 class="text-sm font-bold text-slate-900 dark:text-white inline-flex items-center gap-2 flex-wrap">
                    {{ selectedItem.name }}
                    <span
                      v-if="selectedItem.inUseByDevice"
                      class="inline-flex items-center gap-1 text-[10px] font-bold px-2 py-0.5 rounded-full border bg-amber-50 dark:bg-amber-950/40 text-amber-600 dark:text-amber-400 border-amber-200 dark:border-amber-800"
                    >
                      <Link2 class="w-3 h-3" />
                      已关联设备
                    </span>
                  </h3>
                  <p class="mt-1 text-[10px] text-slate-400 dark:text-slate-500">
                    控制器 {{ selectedItem.controllerName || `#${selectedItem.controllerId}` }}
                    <span class="text-slate-300 dark:text-slate-600"> · </span>
                    <span class="bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 font-bold px-1.5 py-0.5 rounded-full">{{ selectedItem.protocolName || `#${selectedItem.protocolId}` }}</span>
                    <span class="text-slate-300 dark:text-slate-600"> · 地址 </span>
                    <span class="font-mono">{{ endpointLabel(selectedItem) }}</span>
                  </p>
                  <p class="mt-1 text-[10px] text-slate-400 dark:text-slate-500">
                    重连 {{ selectedItem.reconnectIntervalMs }} ms
                    <span class="text-slate-300 dark:text-slate-600"> · </span>
                    IO 超时 {{ selectedItem.timeoutMs ?? '—' }} ms
                    <span class="text-slate-300 dark:text-slate-600"> · </span>
                    更新 {{ fmtTime(selectedItem.updatedAt) }}
                  </p>
                </div>
              </div>
              <div class="flex items-center gap-2 flex-wrap">
                <span
                  class="inline-flex items-center gap-1 text-[10px] font-bold px-2 py-0.5 rounded-full border"
                  :class="selectedItem.isEnabled
                    ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-800'
                    : 'bg-slate-100 dark:bg-slate-800 text-slate-400 border-slate-200 dark:border-slate-700'"
                >
                  <Power class="w-3 h-3" />
                  {{ selectedItem.isEnabled ? '启用' : '停用' }}
                </span>
                <button
                  @click="openEdit(selectedItem)"
                  class="text-[#1890ff] dark:text-sky-400 hover:text-sky-600 cursor-pointer font-sans font-bold inline-flex items-center gap-0.5"
                >
                  <Edit3 class="w-3.5 h-3.5" />
                  编辑
                </button>
                <button
                  @click="remove(selectedItem)"
                  :disabled="selectedItem.inUseByDevice"
                  class="text-rose-500 hover:text-rose-700 cursor-pointer font-sans font-bold inline-flex items-center gap-0.5 disabled:opacity-40 disabled:cursor-not-allowed"
                >
                  <Trash2 class="w-3.5 h-3.5" />
                  删除
                </button>
              </div>
            </div>
          </div>

          <RefDevicesPanel owner-type="connection" :owner-id="selectedId" />
        </template>
      </main>
    </div>

    <!-- MODAL: ADD / EDIT -->
    <div v-if="showModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Link2 class="w-4 h-4 text-[#1890ff]" />
            <span>{{ editingId != null ? '编辑连接' : '添加连接' }}</span>
          </div>
          <button @click="showModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs overflow-y-auto max-h-[420px]">
          <div v-if="formError" class="bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 rounded-lg p-3 text-rose-600 dark:text-rose-400 whitespace-pre-line">
            {{ formError }}
          </div>

          <div v-if="isEditingReferenced" class="bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-800 rounded-lg p-3 text-amber-700 dark:text-amber-400 text-[10px]">
            该连接已被设备使用，可编辑以下所有参数；所属控制器/协议属设备归属绑定，不可在此变更。
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">所属控制器 <span class="text-rose-500">*</span></label>
            <div class="relative">
              <select v-model="form.ControllerId" :disabled="isEditingReferenced"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] disabled:opacity-50 disabled:cursor-not-allowed">
                <option :value="0" disabled>请选择控制器</option>
                <option v-for="c in controllerOptions" :key="c.id" :value="c.id">{{ c.code }} · {{ c.name }}</option>
              </select>
              <Lock v-if="isEditingReferenced" class="w-3.5 h-3.5 absolute right-2.5 top-1/2 -translate-y-1/2 text-amber-500" />
            </div>
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">连接名称 <span class="text-rose-500">*</span></label>
            <input v-model="form.Name" type="text" placeholder="例如: 1# 车间 S7 主连接"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">协议 <span class="text-rose-500">*</span></label>
            <div class="relative">
              <select v-model="form.ProtocolId" @change="onProtocolChange" :disabled="isEditingReferenced"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] disabled:opacity-50 disabled:cursor-not-allowed">
                <option :value="0" disabled>请选择协议</option>
                <option v-for="p in protocols" :key="p.id" :value="p.id">{{ p.name }}（{{ p.key }}）</option>
              </select>
              <Lock v-if="isEditingReferenced" class="w-3.5 h-3.5 absolute right-2.5 top-1/2 -translate-y-1/2 text-amber-500" />
            </div>
          </div>

          <div>
            <div class="flex items-center justify-between mb-1.5">
              <label class="text-slate-500 dark:text-slate-400 font-bold">连接参数</label>
              <button @click="configMode = configMode === 'structured' ? 'raw' : 'structured'"
                class="text-[10px] text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 font-bold px-2 py-0.5 rounded border border-slate-200 dark:border-slate-700 cursor-pointer inline-flex items-center gap-1">
                <Code2 class="w-3 h-3" />
                {{ configMode === 'structured' ? '切换为原始 JSON' : '切换为表单编辑' }}
              </button>
            </div>

            <!-- 原始 JSON 编辑 -->
            <textarea v-if="configMode === 'raw'" v-model="cfgRaw" rows="5"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono text-[10px] focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] leading-relaxed" />
            <p v-if="configMode === 'raw'" class="text-slate-400 dark:text-slate-500 text-[10px] mt-1">驱动配置原文；字段名与后端 *Config DTO 一致（S7=IpAddress/Port/Rack/Slot/CpuType/IoTimeoutMs/ConnectTimeoutMs；OPC UA=EndpointUrl/SecurityPolicy/Username/Password）。</p>

            <!-- S7 -->
            <div v-else-if="selectedProtocolKey === 'S7'" class="space-y-3">
              <div class="grid grid-cols-2 gap-3">
                <div class="col-span-2">
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">PLC IP 地址</label>
                  <input v-model="cfgStructured.ipAddress" type="text" placeholder="192.168.1.60"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">端口</label>
                  <input v-model.number="cfgStructured.port" type="number" min="1" max="65535"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">CPU 类型</label>
                  <input v-model="cfgStructured.cpuType" type="text"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">机架 Rack</label>
                  <input v-model.number="cfgStructured.rack" type="number" min="0"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">槽位 Slot</label>
                  <input v-model.number="cfgStructured.slot" type="number" min="0"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
              </div>
              <div class="grid grid-cols-2 gap-3">
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">IO 超时(ms)</label>
                  <input v-model.number="cfgStructured.ioTimeoutMs" type="number" min="500" max="60000"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">建链超时(ms)</label>
                  <input v-model.number="cfgStructured.connectTimeoutMs" type="number" min="500" max="60000"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
              </div>
            </div>

            <!-- OPC UA -->
            <div v-else-if="selectedProtocolKey === 'OPCUA'" class="space-y-3">
              <div>
                <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">端点 Endpoint URL</label>
                <input v-model="cfgStructured.endpointUrl" type="text" placeholder="opc.tcp://host:4840/MyServer/Instance"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono text-[11px] focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                <p class="text-slate-400 dark:text-slate-500 text-[10px] mt-1">支持完整路径；仅改主机/端口时路径会原样保留。</p>
              </div>
              <div>
                <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">安全策略 SecurityPolicy</label>
                <input v-model="cfgStructured.securityPolicy" type="text" placeholder="None / Basic256Sha256"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
              </div>
              <div class="grid grid-cols-2 gap-3">
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">用户名</label>
                  <input v-model="cfgStructured.username" type="text" autocomplete="off"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">密码</label>
                  <input v-model="cfgStructured.password" type="password" autocomplete="new-password"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
              </div>
            </div>

            <!-- Modbus TCP -->
            <div v-else-if="selectedProtocolKey === 'MODBUSTCP'" class="space-y-3">
              <div>
                <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">设备 IP 地址</label>
                <input v-model="cfgStructured.modbusIp" type="text" placeholder="192.168.1.20"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
              </div>
              <div class="grid grid-cols-2 gap-3">
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">端口</label>
                  <input v-model.number="cfgStructured.modbusPort" type="number" min="1" max="65535"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">从站地址 Unit ID</label>
                  <input v-model.number="cfgStructured.unitId" type="number" min="0" max="255"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
              </div>
            </div>

            <!-- MQTT -->
            <div v-else-if="selectedProtocolKey === 'MQTT'" class="space-y-3">
              <div>
                <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">Broker 地址</label>
                <input v-model="cfgStructured.broker" type="text" placeholder="tcp://192.168.1.50"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
              </div>
              <div class="grid grid-cols-2 gap-3">
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">端口</label>
                  <input v-model.number="cfgStructured.mqttPort" type="number" min="1" max="65535"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">主题 Topic</label>
                  <input v-model="cfgStructured.topic" type="text"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
              </div>
              <div class="grid grid-cols-2 gap-3">
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">Client ID</label>
                  <input v-model="cfgStructured.clientId" type="text"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">用户名</label>
                  <input v-model="cfgStructured.mqttUsername" type="text" autocomplete="off"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
              </div>
              <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">密码</label>
                  <input v-model="cfgStructured.mqttPassword" type="password" autocomplete="new-password"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
              </div>
            </div>

            <!-- Virtual -->
            <div v-else-if="selectedProtocolKey === 'VIRTUAL'" class="space-y-3">
              <div class="grid grid-cols-2 gap-3">
                <div>
                  <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">值更新间隔(ms)</label>
                  <input v-model.number="cfgStructured.intervalMs" type="number" min="100"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
              </div>
              <label class="flex items-center gap-2 font-bold text-slate-600 dark:text-slate-300 cursor-pointer select-none">
                <input type="checkbox" v-model="cfgStructured.randomValues" class="text-[#1890ff] focus:ring-0" />
                随机产生数值
              </label>
            </div>

            <!-- 其它/未支持协议 -->
            <div v-else class="space-y-3">
              <textarea v-model="cfgRaw" rows="5"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono text-[10px] focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] leading-relaxed" />
              <p class="text-slate-400 dark:text-slate-500 text-[10px] mt-1">该协议暂未提供表单，请直接编辑 JSON 原文。</p>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">重连周期(ms)</label>
              <input v-model.number="form.ReconnectIntervalMs" type="number" min="100" max="3600000"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
            </div>
          </div>

          <label class="flex items-center gap-2 font-bold text-slate-600 dark:text-slate-300 cursor-pointer select-none">
            <input type="checkbox" v-model="form.IsEnabled" class="text-[#1890ff] focus:ring-0" />
            启用该连接
          </label>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-4 border-t border-slate-100 dark:border-slate-800 flex justify-end gap-2">
          <button
            @click="showModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button
            @click="save"
            :disabled="saving"
            class="px-4 py-1.5 rounded-lg bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {{ saving ? '保存中...' : '保存' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>