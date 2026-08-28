<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import {
  Plus, Trash2, Edit3, X, Rss, Unlink, Grid, RefreshCw, Power, PowerOff,
  PlugZap, Settings, Check, ChevronLeft, Server
} from 'lucide-vue-next';
import { MqttServer, MqttServerStatus, MqttVariableConfig } from '../types';
import { devices, addLog } from '../store/index';
import { syncDevices } from '../services/deviceService';
import {
  fetchMqttServers, fetchMqttServerStatuses, createMqttServer, updateMqttServer,
  deleteMqttServer, setMqttServerEnabled, testMqttServerConnection
} from '../api/mqttServerApi';
import {
  fetchMqttVariableConfigs, addMqttVariableConfig, updateMqttVariableConfig,
  deleteMqttVariableConfig
} from '../api/mqttVariableConfigApi';

// ===== 数据 =====
const servers = ref<MqttServer[]>([]);
const statuses = ref<Record<number, MqttServerStatus>>({});
const selectedServerId = ref<number | null>(null);
const variables = ref<MqttVariableConfig[]>([]);
const loadingServers = ref(true);
const loadingVariables = ref(false);
const activeTab = ref<'cards' | 'detail'>('cards');

const selectedServer = computed(() =>
  servers.value.find(s => s.id === selectedServerId.value) || null
);
const statusOf = (srv: MqttServer): MqttServerStatus | undefined => statuses.value[srv.id];

// ===== 候选变量（全部设备变量） =====
const allDeviceVariables = computed(() => {
  const list: { deviceId: number; deviceName: string; variableKey: string; variableName: string }[] = [];
  devices.value.forEach(dev => {
    const meta = dev.variableMeta || {};
    Object.keys(dev.variables || {}).forEach(k => {
      list.push({
        deviceId: dev.id,
        deviceName: dev.name,
        variableKey: k,
        variableName: (meta[k] as any)?.name || k
      });
    });
  });
  return list;
});

const associatedKeys = computed(() => {
  const set = new Set<string>();
  variables.value.forEach(v => set.add(`${v.deviceId}:${v.variableKey}`));
  return set;
});

const unassociatedVariables = computed(() =>
  allDeviceVariables.value.filter(v => !associatedKeys.value.has(`${v.deviceId}:${v.variableKey}`))
);

// ===== 加载 =====
const loadServers = async () => {
  try {
    const { data } = await fetchMqttServers();
    servers.value = data;
    // 选中第一个可用服务器
    if (!selectedServerId.value && servers.value.length > 0) {
      selectedServerId.value = servers.value[0].id;
      await loadVariables(selectedServerId.value);
    }
  } finally {
    loadingServers.value = false;
  }
};

const loadStatuses = async () => {
  try {
    const { data } = await fetchMqttServerStatuses();
    const map: Record<number, MqttServerStatus> = {};
    data.forEach(s => { map[s.id] = s; });
    statuses.value = map;
  } catch {
    /* 状态接口失败不影响主体功能 */
  }
};

const loadVariables = async (serverId: number) => {
  loadingVariables.value = true;
  try {
    const { data } = await fetchMqttVariableConfigs(serverId);
    variables.value = data;
  } finally {
    loadingVariables.value = false;
  }
};

const refreshAll = async () => {
  await loadServers();
  await loadStatuses();
};

const selectServer = (srv: MqttServer) => {
  selectedServerId.value = srv.id;
  activeTab.value = 'detail';
  loadVariables(srv.id);
  loadStatuses();
};

// ===== 服务器表单 =====
const showServerModal = ref(false);
const isEditing = ref(false);
const testing = ref(false);
const sName = ref('');
const sUrl = ref('mqtt://broker.emqx.io');
const sPort = ref(1883);
const sClientId = ref('');
const sUsername = ref('');
const sPassword = ref('');
const sPrefix = ref('scada');

const openNewServerModal = () => {
  isEditing.value = false;
  sName.value = ''; sUrl.value = 'mqtt://broker.emqx.io'; sPort.value = 1883;
  sClientId.value = `scada_${Date.now().toString().slice(-4)}`;
  sUsername.value = ''; sPassword.value = ''; sPrefix.value = 'scada';
  showServerModal.value = true;
};

const openEditServerModal = (srv: MqttServer) => {
  isEditing.value = true;
  sName.value = srv.name; sUrl.value = srv.brokerUrl; sPort.value = srv.port;
  sClientId.value = srv.clientId; sUsername.value = srv.username || '';
  sPassword.value = ''; sPrefix.value = srv.topicPrefix || '';
  showServerModal.value = true;
};

const testConnection = async () => {
  if (!sUrl.value.trim()) { addLog('MQTT服务', '请先填写 Broker 地址后再测试', 'warning'); return; }
  testing.value = true;
  try {
    const { data } = await testMqttServerConnection({
      brokerUrl: sUrl.value.trim(),
      port: Number(sPort.value),
      clientId: sClientId.value,
      username: sUsername.value || undefined,
      password: sPassword.value || undefined
    });
    if (data.success) {
      addLog('MQTT服务', `连接测试成功 [${sUrl.value}:${sPort.value}]`, 'normal');
      alert('连接测试成功');
    } else {
      addLog('MQTT服务', `连接测试失败: ${data.errorMessage}`, 'warning');
      alert(`连接测试失败: ${data.errorMessage}`);
    }
  } finally {
    testing.value = false;
  }
};

const saveServer = async () => {
  if (!sName.value.trim() || !sUrl.value.trim()) {
    addLog('MQTT服务', '服务器名称与 Broker 地址不能为空', 'warning');
    return;
  }
  const dto = {
    name: sName.value.trim(),
    brokerUrl: sUrl.value.trim(),
    port: Number(sPort.value),
    clientId: sClientId.value.trim(),
    username: sUsername.value,
    password: sPassword.value, // 编辑时留空表示保持原密码（后端处理）
    topicPrefix: sPrefix.value.trim()
  };
  try {
    if (isEditing.value && selectedServer.value) {
      await updateMqttServer({ ...dto, id: selectedServer.value.id, isEnabled: selectedServer.value.isEnabled } as MqttServer);
      addLog('MQTT服务', `更新了 MQTT 服务器 [${dto.name}]`, 'normal');
    } else {
      const res = await createMqttServer(dto);
      selectedServerId.value = res.data.id;
      activeTab.value = 'detail';
      addLog('MQTT服务', `注册了 MQTT 服务器 [${dto.name}]`, 'normal');
    }
    showServerModal.value = false;
    await loadServers();
    if (selectedServerId.value) loadVariables(selectedServerId.value);
    loadStatuses();
  } catch {
    /* 错误由 http 拦截器统一提示 */
  }
};

const toggleServerEnabled = async (srv: MqttServer) => {
  try {
    await setMqttServerEnabled(srv.id, !srv.isEnabled);
    addLog('MQTT服务', `服务器 [${srv.name}] 已${srv.isEnabled ? '停用' : '启用'}`, srv.isEnabled ? 'warning' : 'normal');
    await loadServers();
    loadStatuses();
  } catch { /* 拦截器已提示 */ }
};

const deleteServer = async (srv: MqttServer) => {
  if (!confirm(`确定删除 MQTT 服务器 [${srv.name}] 及其全部关联变量吗？`)) return;
  try {
    await deleteMqttServer(srv.id);
    addLog('MQTT服务', `删除 MQTT 服务器 [${srv.name}]`, 'warning');
    if (srv.id === selectedServerId.value) {
      selectedServerId.value = servers.value.find(s => s.id !== srv.id)?.id ?? null;
      if (selectedServerId.value) loadVariables(selectedServerId.value);
      else variables.value = [];
    }
    await loadServers();
    loadStatuses();
  } catch { /* 拦截器已提示 */ }
};

// ===== 变量映射 =====
const showAssociateModal = ref(false);
const addAlias = ref('');
const addCustomTopic = ref('');

const openAssociateModal = () => {
  addAlias.value = '';
  addCustomTopic.value = '';
  showAssociateModal.value = true;
};

const addVar = async (v: { deviceId: number; variableKey: string }) => {
  if (!selectedServer.value) return;
  try {
    await addMqttVariableConfig(selectedServer.value.id, {
      deviceId: v.deviceId,
      variableKey: v.variableKey,
      alias: addAlias.value.trim() || v.variableKey,
      customTopic: addCustomTopic.value.trim() || undefined
    });
    addLog('MQTT服务', `服务器 [${selectedServer.value.name}] 关联变量 [${v.variableKey}]`, 'normal');
    await loadVariables(selectedServer.value.id);
  } catch { /* 拦截器已提示 */ }
};

const toggleVariable = async (cfg: MqttVariableConfig) => {
  if (!selectedServer.value) return;
  try {
    await updateMqttVariableConfig(cfg.id, {
      alias: cfg.alias,
      customTopic: cfg.customTopic || undefined,
      isEnabled: !cfg.isEnabled
    });
    await loadVariables(selectedServer.value.id);
  } catch { /* 拦截器已提示 */ }
};

const removeVariable = async (cfg: MqttVariableConfig) => {
  if (!selectedServer.value) return;
  if (!confirm(`确定取消变量 [${cfg.variableKey}] 的关联吗？`)) return;
  try {
    await deleteMqttVariableConfig(cfg.id);
    addLog('MQTT服务', `服务器 [${selectedServer.value.name}] 移除关联变量 [${cfg.variableKey}]`, 'warning');
    await loadVariables(selectedServer.value.id);
  } catch { /* 拦截器已提示 */ }
};

// ===== 状态辅助 =====
const statusBadge = (status?: MqttServerStatus) => {
  const s = status?.status || 'Disconnected';
  if (s === 'Connected') return { cls: 'bg-emerald-50 text-emerald-600 border-emerald-200 dark:bg-emerald-950/60 dark:text-emerald-400 dark:border-emerald-800', dot: 'bg-emerald-500', text: '已连接' };
  if (s === 'Connecting') return { cls: 'bg-amber-50 text-amber-600 border-amber-200 dark:bg-amber-950/60 dark:text-amber-400 dark:border-amber-800', dot: 'bg-amber-500 animate-pulse', text: '连接中' };
  if (s === 'Error') return { cls: 'bg-rose-50 text-rose-600 border-rose-200 dark:bg-rose-950/60 dark:text-rose-400 dark:border-rose-800', dot: 'bg-rose-500', text: '异常' };
  if (s === 'Disabled') return { cls: 'bg-slate-100 text-slate-500 border-slate-200 dark:bg-slate-800 dark:text-slate-400 dark:border-slate-700', dot: 'bg-slate-400', text: '已停用' };
  return { cls: 'bg-slate-50 text-slate-500 border-slate-200 dark:bg-slate-800 dark:text-slate-400 dark:border-slate-700', dot: 'bg-slate-400', text: '断开' };
};

// ===== 启动 =====
let timer: number | undefined;
onMounted(async () => {
  if (devices.value.length === 0) await syncDevices();
  await refreshAll();
  timer = window.setInterval(() => loadStatuses(), 5000);
});
onUnmounted(() => { if (timer) window.clearInterval(timer); });
</script>

<template>
  <div class="h-full overflow-y-auto overflow-x-hidden text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent p-4">
    <!-- 页面头 -->
    <div class="flex items-center justify-between mb-4">
      <div class="flex items-center gap-2 font-bold text-sm text-slate-900 dark:text-white">
        <Rss class="w-4 h-4 text-sky-500" />
        <span>MQTT 服务器管理</span>
        <span class="text-[10px] font-mono text-slate-400 ml-1">多通道转发 · 每通道独立别名</span>
      </div>
      <div class="flex items-center gap-2">
        <button @click="refreshAll" class="p-2 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-500 hover:text-sky-500 hover:border-sky-300 cursor-pointer" title="刷新状态">
          <RefreshCw class="w-4 h-4" />
        </button>
        <button @click="activeTab = 'cards'" v-if="activeTab === 'detail'" class="px-3 py-1.5 border border-slate-200 dark:border-slate-700 rounded-lg font-bold text-xs text-slate-500 hover:text-sky-500 cursor-pointer inline-flex items-center gap-1">
          <ChevronLeft class="w-4 h-4" /> 返回列表
        </button>
        <button @click="openNewServerModal" class="bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer">
          <Plus class="w-4 h-4" /> 新建服务器
        </button>
      </div>
    </div>

    <!-- ============ 卡片列表视图 ============ -->
    <div v-if="activeTab === 'cards'">
      <div v-if="loadingServers" class="text-xs text-slate-400 py-10 text-center">加载中...</div>
      <div v-else-if="servers.length === 0" class="bg-white dark:bg-slate-900 border border-dashed border-slate-300 dark:border-slate-700 rounded-xl py-16 flex flex-col items-center justify-center text-slate-400 text-center space-y-2">
        <Server class="w-10 h-10 text-slate-300 dark:text-slate-600" />
        <p class="text-sm font-bold text-slate-500 dark:text-slate-400">暂无 MQTT 服务器</p>
        <p class="text-xs">点击右上角「新建服务器」添加一个转发通道</p>
      </div>
      <div v-else class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
        <div
          v-for="s in servers"
          :key="s.id"
          class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:shadow-md transition-all flex flex-col cursor-pointer"
          @click="selectServer(s)"
        >
          <div class="flex items-start justify-between">
            <div class="flex items-center gap-2 min-w-0">
              <div class="w-8 h-8 rounded-lg bg-sky-50 dark:bg-sky-950/60 flex items-center justify-center shrink-0">
                <Rss class="w-4 h-4 text-sky-500" />
              </div>
              <h4 class="font-bold text-sm text-slate-800 dark:text-slate-100 truncate">{{ s.name }}</h4>
            </div>
            <span
              class="text-[10px] px-2 py-0.5 rounded-full font-bold border flex items-center gap-1 shrink-0"
              :class="statusBadge(statusOf(s)).cls"
            >
              <span class="w-1.5 h-1.5 rounded-full" :class="statusBadge(statusOf(s)).dot" />
              {{ statusBadge(statusOf(s)).text }}
            </span>
          </div>

          <p class="text-[11px] font-mono text-slate-400 dark:text-slate-500 mt-2 truncate">{{ s.brokerUrl }}:{{ s.port }}</p>
          <p class="text-[10px] font-mono text-slate-400 dark:text-slate-500 mt-0.5 truncate">Client: {{ s.clientId || '自动' }}</p>
          <p class="text-[10px] font-mono text-slate-400 dark:text-slate-500 mt-0.5 truncate">前缀: {{ s.topicPrefix || 'scada' }}</p>

          <div v-if="statusOf(s)?.status === 'Error'" class="mt-2 text-[10px] text-rose-500 truncate" :title="statusOf(s)?.lastError">
            {{ statusOf(s)?.lastError }}
          </div>

          <div class="border-t border-slate-100 dark:border-slate-800 mt-3 pt-3 flex items-center justify-between">
            <span class="text-[11px] font-bold text-sky-600 dark:text-sky-400">{{ s.variableCount }} 个变量</span>
            <div class="flex items-center gap-1" @click.stop>
              <button
                @click="toggleServerEnabled(s)"
                class="p-1.5 rounded-lg cursor-pointer"
                :class="s.isEnabled ? 'text-emerald-500 hover:bg-emerald-50 dark:hover:bg-emerald-950/40' : 'text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800'"
                :title="s.isEnabled ? '停用' : '启用'"
              >
                <PowerOff v-if="s.isEnabled" class="w-4 h-4" />
                <Power v-else class="w-4 h-4" />
              </button>
              <button @click="openEditServerModal(s)" class="p-1.5 rounded-lg text-slate-400 hover:text-sky-500 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer" title="编辑"><Edit3 class="w-4 h-4" /></button>
              <button @click="deleteServer(s)" class="p-1.5 rounded-lg text-slate-400 hover:text-rose-500 hover:bg-rose-50 dark:hover:bg-rose-950/40 cursor-pointer" title="删除"><Trash2 class="w-4 h-4" /></button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ============ 服务器详情视图 ============ -->
    <div v-else-if="selectedServer" class="space-y-4">
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div class="space-y-1">
          <div class="flex items-center gap-2">
            <h2 class="font-bold text-base text-slate-900 dark:text-white">{{ selectedServer.name }}</h2>
            <span class="text-[10px] px-2 py-0.5 rounded-full font-bold border" :class="statusBadge(statusOf(selectedServer)).cls">
              {{ statusBadge(statusOf(selectedServer)).text }}
            </span>
          </div>
          <p class="text-xs text-slate-500 dark:text-slate-400 font-mono">{{ selectedServer.brokerUrl }}:{{ selectedServer.port }}</p>
        </div>
        <div class="flex items-center gap-2 shrink-0">
          <button @click="openEditServerModal(selectedServer)" class="border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer">
            <Settings class="w-4 h-4 text-slate-400" />
            编辑配置
          </button>
          <button @click="openAssociateModal" class="bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer">
            <Plus class="w-4 h-4" />
            关联变量
          </button>
          <button @click="deleteServer(selectedServer)" class="text-rose-600 dark:text-rose-400 border border-rose-100 dark:border-rose-900/40 hover:bg-rose-50 dark:hover:bg-rose-950/40 font-bold text-xs p-1.5 rounded-lg cursor-pointer">
            <Trash2 class="w-4 h-4" />
          </button>
        </div>
      </div>

      <!-- 连接信息条 -->
      <div class="grid grid-cols-2 lg:grid-cols-4 gap-3 p-3.5 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl text-[11px] font-mono text-slate-600 dark:text-slate-300">
        <div><span class="text-slate-400 dark:text-slate-500">客户端ID:</span><span class="block font-bold text-slate-800 dark:text-slate-200 truncate">{{ selectedServer.clientId || '自动' }}</span></div>
        <div><span class="text-slate-400 dark:text-slate-500">用户名:</span><span class="block font-bold text-slate-800 dark:text-slate-200 truncate">{{ selectedServer.username || '匿名' }}</span></div>
        <div><span class="text-slate-400 dark:text-slate-500">主题前缀:</span><span class="block font-bold text-sky-600 dark:text-sky-400 truncate">{{ selectedServer.topicPrefix || 'scada' }}</span></div>
        <div>
          <span class="text-slate-400 dark:text-slate-500">状态:</span>
          <span class="block font-bold" :class="statusOf(selectedServer)?.status === 'Connected' ? 'text-emerald-500 dark:text-emerald-400' : 'text-slate-400'">
            {{ statusBadge(statusOf(selectedServer)).text }}
          </span>
        </div>
      </div>

      <!-- 关联变量列表 -->
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 space-y-3">
        <div class="flex items-center justify-between">
          <h3 class="text-xs font-bold tracking-widest uppercase text-slate-500 dark:text-slate-400 inline-flex items-center gap-1">
            <Grid class="w-4 h-4 text-sky-500" />
            关联变量 ({{ variables.length }})
          </h3>
        </div>

        <div v-if="loadingVariables" class="py-10 text-center text-xs text-slate-400">加载中...</div>

        <div v-else-if="variables.length === 0" class="py-14 bg-slate-50 dark:bg-slate-950/40 border border-dashed border-slate-200 dark:border-slate-800 rounded-xl flex flex-col items-center justify-center text-slate-400 text-center space-y-2">
          <PlugZap class="w-8 h-8 text-slate-300 dark:text-slate-600" />
          <p class="text-xs font-bold text-slate-500 dark:text-slate-400">暂无关联变量</p>
          <p class="text-[11px] text-slate-400 dark:text-slate-500">点击右上角「关联变量」添加映射</p>
        </div>

        <div v-else class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          <div
            v-for="cfg in variables"
            :key="cfg.id"
            class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 flex flex-col justify-between hover:shadow-md transition-all"
            :class="cfg.isEnabled ? 'bg-slate-50/60 dark:bg-slate-950/40' : 'bg-slate-50 dark:bg-slate-900 opacity-70'"
          >
            <div class="space-y-1.5">
              <div class="flex items-center justify-between text-[10px]">
                <span class="text-slate-400 dark:text-slate-500 font-bold">{{ cfg.deviceName }}</span>
                <button @click="toggleVariable(cfg)" class="text-[9px] font-bold px-1.5 py-0.5 rounded-full border"
                  :class="cfg.isEnabled ? 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-800' : 'bg-slate-100 dark:bg-slate-800 text-slate-400 border-slate-200 dark:border-slate-700'">
                  {{ cfg.isEnabled ? '启用' : '停用' }}
                </button>
              </div>
              <div>
                <h4 class="font-mono font-bold text-xs text-sky-600 dark:text-sky-400 truncate">{{ cfg.variableKey }}</h4>
                <p class="text-[10px] text-slate-400 dark:text-slate-500 mt-0.5">
                  别名: <span class="text-slate-800 dark:text-slate-200 font-mono font-bold">{{ cfg.alias }}</span>
                </p>
                <p class="text-[10px] text-slate-400 dark:text-slate-500 mt-0.5">
                  实时值: <span class="text-slate-800 dark:text-slate-200 font-mono font-bold">{{ cfg.realtimeValue ?? '-' }}</span>
                </p>
              </div>
            </div>
            <div class="border-t border-slate-100 dark:border-slate-800 pt-3 mt-3 flex items-center justify-between">
              <span class="text-[9px] text-slate-400 dark:text-slate-500 font-mono truncate mr-2">{{ cfg.topicPreview }}</span>
              <button @click="removeVariable(cfg)" class="text-rose-500 hover:text-rose-700 dark:hover:text-rose-400 text-[10px] font-semibold inline-flex items-center gap-0.5 cursor-pointer shrink-0">
                <Unlink class="w-3.5 h-3.5" /> 取消关联
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ============ 服务器新建/编辑弹窗 ============ -->
    <div v-if="showServerModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Rss class="w-4 h-4 text-sky-400" />
            <span>{{ isEditing ? '编辑 MQTT 服务器' : '新建 MQTT 服务器' }}</span>
          </div>
          <button @click="showServerModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">服务器名称</label>
            <input v-model="sName" type="text" placeholder="如: 阿里云MQTT" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
          </div>

          <div class="grid grid-cols-3 gap-2">
            <div class="col-span-2">
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">Broker 地址</label>
              <input v-model="sUrl" type="text" placeholder="e.g. broker.emqx.io" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono text-[11px] text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">端口</label>
              <input v-model="sPort" type="number" placeholder="1883" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono text-[11px] text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
            </div>
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">客户端 ID</label>
            <input v-model="sClientId" type="text" placeholder="scada_edge" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">主题前缀</label>
            <input v-model="sPrefix" type="text" placeholder="scada" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
          </div>

          <div class="grid grid-cols-2 gap-2 p-3 bg-slate-50 dark:bg-slate-950/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <div>
              <label class="text-slate-400 font-bold block mb-1">用户名 (选填)</label>
              <input v-model="sUsername" type="text" class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded p-1.5 font-mono text-[11px] text-slate-900 dark:text-white focus:outline-none" />
            </div>
            <div>
              <label class="text-slate-400 font-bold block mb-1">密码 (选填)</label>
              <input v-model="sPassword" type="password" :placeholder="isEditing ? '留空保持不变' : ''" class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded p-1.5 font-mono text-[11px] text-slate-900 dark:text-white focus:outline-none" />
            </div>
          </div>

          <div class="flex items-center justify-between bg-sky-50 dark:bg-sky-950/40 border border-sky-100 dark:border-sky-900/40 rounded-lg p-2.5">
            <span class="text-[11px] text-sky-700 dark:text-sky-300 font-bold">使用当前参数测试连接</span>
            <button @click="testConnection" :disabled="testing" class="px-3 py-1 bg-sky-500 hover:bg-sky-600 text-white font-bold text-[10px] rounded-md cursor-pointer disabled:opacity-50 inline-flex items-center gap-1">
              <PlugZap class="w-3.5 h-3.5" /> {{ testing ? '测试中...' : '测试连接' }}
            </button>
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800">
          <button @click="showServerModal = false" class="px-3.5 py-1.5 border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs rounded-lg text-slate-600 dark:text-slate-300 cursor-pointer">取消</button>
          <button @click="saveServer" class="px-4 py-1.5 bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white rounded-lg cursor-pointer">保存</button>
        </div>
      </div>
    </div>

    <!-- ============ 关联变量弹窗 ============ -->
    <div v-if="showAssociateModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150 flex flex-col max-h-[560px]">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between shrink-0 border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Rss class="w-4 h-4 text-sky-400" />
            <span>关联变量 · 每通道独立别名</span>
          </div>
          <button @click="showAssociateModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <!-- 别名与自定义主题 -->
        <div class="p-3 bg-slate-50 dark:bg-slate-950 border-b border-slate-200 dark:border-slate-800 shrink-0 space-y-2">
          <div>
            <label class="text-[10px] text-slate-400 font-bold block mb-1">转发别名（默认取变量名）</label>
            <input v-model="addAlias" type="text" placeholder="该服务器上使用的别名" class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg py-1.5 px-2.5 text-xs text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
          </div>
          <div>
            <label class="text-[10px] text-slate-400 font-bold block mb-1">自定义主题（选填，优先级高于 前缀/别名）</label>
            <input v-model="addCustomTopic" type="text" placeholder="如 factory/telemetry/xxx" class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg py-1.5 px-2.5 text-xs font-mono text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]" />
          </div>
        </div>

        <div class="flex-1 p-3 overflow-y-auto space-y-2">
          <div v-if="unassociatedVariables.length === 0" class="py-12 text-center text-slate-400">
            <Check class="w-8 h-8 text-emerald-400 mx-auto mb-2" />
            <p class="text-xs font-bold text-slate-500 dark:text-slate-400">无可关联变量</p>
            <p class="text-[10px] text-slate-400 dark:text-slate-500 mt-1">所有变量已关联到该服务器</p>
          </div>
          <div v-for="v in unassociatedVariables" :key="`${v.deviceId}:${v.variableKey}`"
            class="p-3 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl hover:bg-slate-50/50 dark:hover:bg-slate-800/50 flex items-center justify-between text-xs">
            <div class="min-w-0">
              <div class="flex items-center gap-2">
                <span class="font-mono font-bold text-slate-700 dark:text-slate-200 text-xs">{{ v.variableKey }}</span>
                <span class="text-[9px] bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 px-1 rounded uppercase font-mono">{{ v.deviceName }}</span>
              </div>
              <p class="text-[10px] text-slate-400 dark:text-slate-500 mt-0.5 truncate">{{ v.variableName }}</p>
            </div>
            <button @click="addVar(v)" class="border border-[#1890ff]/30 text-[#1890ff] hover:bg-[#1890ff] hover:text-white font-bold px-2.5 py-1 rounded text-[10px] flex items-center gap-1 cursor-pointer transition-colors shrink-0">
              <Plus class="w-3.5 h-3.5" /> 关联
            </button>
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end border-t border-slate-100 dark:border-slate-800 shrink-0">
          <button @click="showAssociateModal = false" class="px-5 py-1.5 rounded-lg bg-slate-900 dark:bg-sky-600 border border-slate-900 dark:border-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 text-white font-bold text-xs cursor-pointer">完成</button>
        </div>
      </div>
    </div>
  </div>
</template>