<script setup lang="ts">
import { ref, computed } from 'vue';
import { mqttServers, devices, addLog } from '../store/index';
import { MqttServer } from '../types';
import { 
  Plus, 
  Trash2, 
  Edit3, 
  Search, 
  Settings, 
  X, 
  Rss, 
  Check, 
  Activity, 
  Unlink, 
  Grid 
} from 'lucide-vue-next';

// View selection
const selectedServerId = ref<string>(mqttServers.value[0]?.id || '');
const showServerModal = ref(false);
const isEditing = ref(false);

const activeServer = computed(() => {
  return mqttServers.value.find(s => s.id === selectedServerId.value) || mqttServers.value[0];
});

// Server modal form fields
const sName = ref('');
const sUrl = ref('');
const sPort = ref(1883);
const sClientId = ref('');
const sUsername = ref('');
const sPassword = ref('');
const sPrefix = ref('');

// Search queries
const varSearchQuery = ref('');
const addVarSearchQuery = ref('');

// Association addition modal
const showAssociateModal = ref(false);

// Gather all system variables
const allAvailableVariables = computed(() => {
  const list: { deviceId: string; deviceName: string; variableKey: string }[] = [];
  devices.value.forEach(dev => {
    Object.keys(dev.variables).forEach(vKey => {
      list.push({
        deviceId: dev.id,
        deviceName: dev.name,
        variableKey: vKey
      });
    });
  });
  return list;
});

// Associated variables of the active server, filtered by search
const filteredAssociatedVariables = computed(() => {
  if (!activeServer.value) return [];
  return activeServer.value.associatedVariables.filter(v => {
    const dev = devices.value.find(d => d.id === v.deviceId);
    const dName = dev ? dev.name : '';
    const term = varSearchQuery.value.toLowerCase();
    return v.variableKey.toLowerCase().includes(term) || dName.toLowerCase().includes(term);
  });
});

// Unassociated variables available for addition
const unassociatedVariables = computed(() => {
  if (!activeServer.value) return [];
  const associatedKeys = new Set(
    activeServer.value.associatedVariables.map(av => `${av.deviceId}:${av.variableKey}`)
  );
  
  return allAvailableVariables.value.filter(v => {
    const isAssociated = associatedKeys.has(`${v.deviceId}:${v.variableKey}`);
    if (isAssociated) return false;
    
    // Filter by search terms
    const term = addVarSearchQuery.value.toLowerCase();
    return v.variableKey.toLowerCase().includes(term) || v.deviceName.toLowerCase().includes(term);
  });
});

const openNewServerModal = () => {
  isEditing.value = false;
  sName.value = '';
  sUrl.value = 'mqtt://broker.emqx.io';
  sPort.value = 1883;
  sClientId.value = `scada_edge_${Date.now().toString().slice(-4)}`;
  sUsername.value = '';
  sPassword.value = '';
  sPrefix.value = 'factory/scada/telemetry';
  showServerModal.value = true;
};

const openEditServerModal = (srv: MqttServer) => {
  isEditing.value = true;
  sName.value = srv.name;
  sUrl.value = srv.brokerUrl;
  sPort.value = srv.port;
  sClientId.value = srv.clientId;
  sUsername.value = srv.username || '';
  sPassword.value = srv.password || '';
  sPrefix.value = srv.topicPrefix || '';
  showServerModal.value = true;
};

const handleSaveServer = () => {
  if (!sName.value.trim() || !sUrl.value.trim()) return;

  if (isEditing.value && activeServer.value) {
    const srv = activeServer.value;
    srv.name = sName.value;
    srv.brokerUrl = sUrl.value;
    srv.port = sPort.value;
    srv.clientId = sClientId.value;
    srv.username = sUsername.value;
    srv.password = sPassword.value;
    srv.topicPrefix = sPrefix.value;
    addLog('MQTT服务', `更新了MQTT服务器通道 [${sName.value}]`, 'normal');
  } else {
    const newId = `mqtt-${Date.now()}`;
    mqttServers.value.push({
      id: newId,
      name: sName.value,
      brokerUrl: sUrl.value,
      port: sPort.value,
      clientId: sClientId.value,
      username: sUsername.value,
      password: sPassword.value,
      topicPrefix: sPrefix.value,
      status: 'disconnected',
      associatedVariables: []
    });
    selectedServerId.value = newId;
    addLog('MQTT服务', `注册了新MQTT云转发通道 [${sName.value}]`, 'normal');
  }

  showServerModal.value = false;
};

const handleDeleteServer = (id: string, name: string) => {
  if (confirm(`确定注销MQTT网关通道 [${name}] 吗？`)) {
    mqttServers.value = mqttServers.value.filter(s => s.id !== id);
    addLog('MQTT服务', `注销了MQTT转发服务通道 [${name}]`, 'warning');
    selectedServerId.value = mqttServers.value[0]?.id || '';
  }
};

const toggleServerConnection = (srv: MqttServer) => {
  srv.status = srv.status === 'connected' ? 'disconnected' : 'connected';
  addLog(
    'MQTT服务', 
    `通道 [${srv.name}] 电气通路切换为 ${srv.status === 'connected' ? '已连接 (ONLINE)' : '断开 (OFFLINE)'}`,
    srv.status === 'connected' ? 'normal' : 'warning'
  );
};

// Add association mapping
const associateVariable = (deviceId: string, variableKey: string) => {
  if (!activeServer.value) return;
  activeServer.value.associatedVariables.push({ deviceId, variableKey });
  
  const dev = devices.value.find(d => d.id === deviceId);
  const devName = dev ? dev.name : '未知设备';
  addLog('MQTT服务', `联动转发变量: [${activeServer.value.name}] 挂载了 [${devName}] - ${variableKey}`, 'normal');
};

// Unassociate variable
const disassociateVariable = (deviceId: string, variableKey: string) => {
  if (!activeServer.value) return;
  activeServer.value.associatedVariables = activeServer.value.associatedVariables.filter(
    av => !(av.deviceId === deviceId && av.variableKey === variableKey)
  );
  const dev = devices.value.find(d => d.id === deviceId);
  const devName = dev ? dev.name : '未知设备';
  addLog('MQTT服务', `断开转发映射: [${activeServer.value.name}] 移除了 [${devName}] - ${variableKey}`, 'warning');
};
</script>

<template>
  <div class="h-full overflow-y-auto md:overflow-y-hidden flex flex-col md:flex-row text-[#1e293b] select-none bg-slate-50">
    
    <!-- LEFT LIST: Broker channels -->
    <div class="w-full md:w-80 bg-white border-r border-slate-200 flex flex-col shrink-0 flex-1 md:flex-none">
      <div class="p-4 border-b border-slate-100 flex items-center justify-between">
        <div class="flex items-center gap-1.5 font-bold text-sm text-slate-900">
          <Rss class="w-4 h-4 text-sky-500" />
          <span>MQTT 服务器管理</span>
        </div>
        <button 
          @click="openNewServerModal"
          class="p-1 rounded bg-[#1890ff] hover:bg-sky-600 text-white cursor-pointer"
          title="新建服务器"
        >
          <Plus class="w-4.5 h-4.5" />
        </button>
      </div>

      <div class="flex-1 overflow-y-auto divide-y divide-slate-100 max-h-[220px] md:max-h-none text-left">
        <div 
          v-for="s in mqttServers" 
          :key="s.id"
          @click="selectedServerId = s.id"
          class="p-4 cursor-pointer hover:bg-slate-50/50 transition-all space-y-2 relative"
          :class="selectedServerId === s.id ? 'bg-sky-50/40 border-r-4 border-r-[#1890ff]' : ''"
        >
          <div class="flex items-center justify-between">
            <span class="text-[9px] font-mono font-bold bg-slate-100 text-slate-600 px-1.5 py-0.5 rounded">
              PORT: {{ s.port }}
            </span>
            <button 
              @click.stop="toggleServerConnection(s)"
              class="text-[9px] font-bold px-1.5 py-0.5 rounded-full flex items-center gap-0.5 border"
              :class="s.status === 'connected' ? 'bg-emerald-50 text-emerald-600 border-emerald-200' : 'bg-slate-50 text-slate-400 border-slate-200'"
            >
              <span class="w-1.5 h-1.5 rounded-full" :class="s.status === 'connected' ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'" />
              {{ s.status === 'connected' ? '已连接' : '断开' }}
            </button>
          </div>
          <div>
            <h4 class="font-bold text-xs text-slate-800 leading-snug">{{ s.name }}</h4>
            <p class="text-[10px] text-slate-400 font-mono mt-0.5 truncate">{{ s.brokerUrl }}</p>
          </div>
          <div class="flex justify-between items-center text-[9px] font-mono text-slate-400">
            <span>Client ID: {{ s.clientId }}</span>
            <span class="text-sky-600 font-sans font-bold">{{ s.associatedVariables.length }} 个变量</span>
          </div>
        </div>
      </div>
    </div>

    <!-- RIGHT PANEL: Detail, mappings list and search -->
    <div class="flex-1 flex flex-col bg-slate-50/50 text-left min-w-0">
      
      <div v-if="activeServer" class="bg-white p-5 border-b border-slate-200 shadow-sm space-y-4">
        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div class="space-y-1">
            <div class="flex items-center gap-2">
              <h2 class="font-bold text-base text-slate-900 font-sans tracking-tight">{{ activeServer.name }}</h2>
              <span 
                class="text-[9px] px-1.5 py-0.5 rounded font-bold border"
                :class="activeServer.status === 'connected' ? 'bg-emerald-50 text-emerald-600 border-emerald-200' : 'bg-slate-50 text-slate-400 border-slate-200'"
              >
                {{ activeServer.status === 'connected' ? '运行中' : '已停用' }}
              </span>
            </div>
            <p class="text-xs text-slate-500 font-mono truncate max-w-lg">
              地址 Endpoint: <b class="text-slate-700">{{ activeServer.brokerUrl }}:{{ activeServer.port }}</b>
            </p>
          </div>

          <div class="flex items-center gap-2 shrink-0">
            <button 
              @click="openEditServerModal(activeServer)"
              class="border border-slate-200 hover:bg-slate-50 font-bold text-xs text-slate-600 px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all"
            >
              <Edit3 class="w-4 h-4 text-slate-400" />
              编辑配置
            </button>
            <button 
              @click="showAssociateModal = true"
              class="bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all shadow-sm"
            >
              <Plus class="w-4 h-4" />
              关联变量
            </button>
            <button 
              @click="handleDeleteServer(activeServer.id, activeServer.name)"
              class="text-rose-600 hover:text-rose-800 border border-rose-100 font-bold text-xs p-1.5 rounded-lg bg-rose-50 cursor-pointer"
            >
              <Trash2 class="w-4 h-4" />
            </button>
          </div>
        </div>

        <!-- Connection Details strip -->
        <div class="grid grid-cols-2 sm:grid-cols-4 gap-4 p-3.5 bg-slate-50 rounded-xl border border-slate-150 text-[11px] font-mono text-slate-600">
          <div>
            <span class="text-slate-400">客户端ID:</span>
            <span class="block font-bold text-slate-800 truncate">{{ activeServer.clientId }}</span>
          </div>
          <div>
            <span class="text-slate-400">用户名:</span>
            <span class="block font-bold text-slate-800 truncate">{{ activeServer.username || '匿名' }}</span>
          </div>
          <div>
            <span class="text-slate-400">主题前缀:</span>
            <span class="block font-bold text-[#1890ff] truncate">{{ activeServer.topicPrefix || '无' }}</span>
          </div>
          <div>
            <span class="text-slate-400">状态:</span>
            <span class="block font-bold" :class="activeServer.status === 'connected' ? 'text-emerald-500' : 'text-slate-400'">
              {{ activeServer.status === 'connected' ? '传输中' : '等待连接' }}
            </span>
          </div>
        </div>
      </div>

      <!-- Variables section with searching -->
      <div class="flex-1 p-5 md:overflow-y-auto overflow-y-visible space-y-4">
        <div v-if="activeServer" class="space-y-3">
          <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 mb-1.5">
            <h3 class="text-xs font-bold tracking-widest uppercase text-slate-500 inline-flex items-center gap-1">
            <Grid class="w-4 h-4 text-[#1890ff]" />
            <span>关联变量 ({{ filteredAssociatedVariables.length }})</span>
          </h3>

            <!-- Search box for variable association list -->
            <div class="relative w-full sm:w-64 select-none">
              <input 
                v-model="varSearchQuery"
                type="text"
                placeholder="搜索变量..."
                class="w-full bg-white border border-slate-200 rounded-lg py-1.5 pl-8 pr-3 text-xs placeholder-slate-400 focus:outline-none focus:border-[#1890ff] focus:ring-1 focus:ring-[#1890ff]"
              />
              <Search class="absolute left-2.5 top-2 w-3.5 h-3.5 text-slate-400" />
              <button 
                v-if="varSearchQuery" 
                @click="varSearchQuery = ''" 
                class="absolute right-2 top-2 text-slate-400 hover:text-slate-600 focus:outline-none"
              >
                <X class="w-3.5 h-3.5" />
              </button>
            </div>
          </div>

          <!-- Variable association card grid -->
          <div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
            <div 
              v-for="av in filteredAssociatedVariables" 
              :key="`${av.deviceId}:${av.variableKey}`"
              class="bg-white border border-slate-200 rounded-xl p-4 flex flex-col justify-between hover:shadow-md transition-all text-left relative overflow-hidden"
            >
              <div class="space-y-1.5">
                <div class="flex items-center justify-between text-[10px]">
                  <span class="text-slate-400 font-bold font-mono">
                    {{ devices.find(d => d.id === av.deviceId)?.name || '未知设备' }}
                  </span>
                  <span class="text-[9px] font-mono text-slate-400 font-bold bg-slate-50 px-1.5 py-0.5 rounded">
                    设备ID: {{ av.deviceId }}
                  </span>
                </div>
                <div>
                  <h4 class="font-mono font-bold text-xs text-sky-600 truncate">{{ av.variableKey }}</h4>
                  <p class="text-[10px] text-slate-400 mt-0.5">
                    实时值: <span class="text-slate-800 font-mono font-bold">
                      {{ devices.find(d => d.id === av.deviceId)?.variables[av.variableKey] }}
                    </span>
                  </p>
                </div>
              </div>

              <div class="border-t border-slate-100 pt-3.5 mt-3.5 flex items-center justify-between">
                <span class="text-[9px] text-slate-400 font-mono">
                  推送路径: {{ activeServer.topicPrefix || 'raw' }}/{{ av.variableKey }}
                </span>
                <button 
              @click="disassociateVariable(av.deviceId, av.variableKey)"
              class="text-rose-500 hover:text-rose-700 text-[10px] font-sans font-semibold inline-flex items-center gap-0.5 cursor-pointer"
              title="取消关联"
            >
              <Unlink class="w-3.5 h-3.5" />
              取消关联
            </button>
              </div>
            </div>

            <div 
              v-if="filteredAssociatedVariables.length === 0" 
              class="col-span-full py-16 bg-white border border-dashed border-slate-200 rounded-xl flex flex-col items-center justify-center text-slate-400 text-center space-y-2"
            >
              <Rss class="w-8 h-8 text-slate-350" />
              <div class="text-xs">
                <p class="font-bold text-slate-500">暂无关联变量</p>
                <p class="text-slate-400 text-[11px] mt-1">点击右上角 "关联变量" 添加变量</p>
              </div>
            </div>
          </div>
        </div>

        <div v-else class="h-64 flex flex-col items-center justify-center text-slate-400">
          <Rss class="w-8 h-8 text-slate-300 mb-2 animate-pulse" />
          <p class="text-xs font-bold text-slate-500">请添加或选择一个有效的 MQTT 云转发通道</p>
        </div>
      </div>
    </div>

    <!-- MODAL: ADD / EDIT DIRECT LINK CHANNEL -->
    <div v-if="showServerModal" class="fixed inset-0 bg-slate-900/40 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white rounded-xl shadow-xl border border-slate-100 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 text-white p-4 flex items-center justify-between">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Settings class="w-4 h-4 text-sky-400 animate-spin" />
            <span>{{ isEditing ? '编辑MQTT服务器' : '新建MQTT服务器' }}</span>
          </div>
          <button @click="showServerModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div>
            <label class="text-slate-500 font-bold block mb-1">服务器名称</label>
            <input 
              v-model="sName"
              type="text"
              placeholder="如: 阿里云MQTT"
              class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 font-sans focus:bg-white text-slate-900 focus:outline-none focus:border-[#1890ff]"
            />
          </div>

          <div class="grid grid-cols-3 gap-2">
            <div class="col-span-2">
              <label class="text-slate-500 font-bold block mb-1">Broker地址</label>
              <input 
                v-model="sUrl"
                type="text"
                placeholder="e.g. mqtt://broker.emqx.io"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 font-mono text-[11px] focus:bg-white text-slate-900 focus:outline-none focus:border-[#1890ff]"
              />
            </div>
            <div>
              <label class="text-slate-500 font-bold block mb-1">端口</label>
              <input 
                v-model="sPort"
                type="number"
                placeholder="1883"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 font-mono text-[11px] focus:bg-white text-slate-900 focus:outline-none focus:border-[#1890ff]"
              />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-2">
            <div>
              <label class="text-slate-500 font-bold block mb-1">客户端ID</label>
              <input 
                v-model="sClientId"
                type="text"
                placeholder="scada_edge"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 font-mono focus:bg-white text-slate-900 focus:outline-none focus:border-[#1890ff]"
              />
            </div>
            <div>
              <label class="text-slate-500 font-bold block mb-1">主题前缀</label>
              <input 
                v-model="sPrefix"
                type="text"
                placeholder="factory/telemetry"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 font-mono focus:bg-white text-slate-900 focus:outline-none focus:border-[#1890ff]"
              />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-2 p-3 bg-slate-50 rounded-xl border border-slate-100">
            <div>
              <label class="text-slate-400 font-bold block mb-1">用户名 (选填)</label>
              <input 
                v-model="sUsername"
                type="text"
                class="w-full bg-white border border-slate-200 rounded p-1.5 font-mono focus:outline-none text-[11px]"
              />
            </div>
            <div>
              <label class="text-slate-400 font-bold block mb-1">密码 (选填)</label>
              <input 
                v-model="sPassword"
                type="password"
                class="w-full bg-white border border-slate-200 rounded p-1.5 font-mono focus:outline-none text-[11px]"
              />
            </div>
          </div>
        </div>

        <div class="bg-slate-50 p-3 flex justify-end gap-2 border-t border-slate-100">
          <button 
            @click="showServerModal = false"
            class="px-3.5 py-1.5 border border-slate-200 bg-white hover:bg-slate-50 font-bold text-xs rounded-lg text-slate-600 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleSaveServer"
            class="px-4 py-1.5 bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white rounded-lg cursor-pointer"
          >
            保存
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: ASSOCIATE VARIABLES -->
    <div v-if="showAssociateModal" class="fixed inset-0 bg-slate-900/40 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white rounded-xl shadow-xl border border-slate-100 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150 flex flex-col max-h-[500px]">
        <div class="bg-slate-900 text-white p-4 flex items-center justify-between shrink-0">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Rss class="w-4 h-4 text-[#1890ff]" />
            <span>关联变量</span>
          </div>
          <button @click="showAssociateModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <!-- Search bar inside variables injection list -->
        <div class="p-3 bg-slate-50 border-b border-slate-200 shrink-0 select-none">
          <div class="relative">
            <input 
              v-model="addVarSearchQuery"
              type="text"
              placeholder="搜索变量..."
              class="w-full bg-white border border-slate-200 rounded-lg py-1.5 pl-8 pr-3 text-xs placeholder-slate-400 focus:outline-none focus:border-[#1890ff]"
            />
            <Search class="absolute left-2.5 top-2.5 w-3.5 h-3.5 text-slate-400" />
            <button 
              v-if="addVarSearchQuery" 
              @click="addVarSearchQuery = ''" 
              class="absolute right-2 top-2.5 text-slate-400 hover:text-slate-600 focus:outline-none"
            >
              <X class="w-3.5 h-3.5" />
            </button>
          </div>
        </div>

        <div class="flex-1 p-3 overflow-y-auto min-h-[250px] space-y-2">
          <div 
            v-for="v in unassociatedVariables" 
            :key="`${v.deviceId}:${v.variableKey}`"
            class="p-3 bg-white border border-slate-200 rounded-xl hover:bg-slate-50/50 flex items-center justify-between text-xs text-left"
          >
            <div>
              <div class="flex items-center gap-2">
                <span class="font-mono font-bold text-slate-700 text-xs">{{ v.variableKey }}</span>
                <span class="text-[9px] bg-slate-100 text-slate-500 px-1 rounded uppercase font-mono">{{ v.deviceName }}</span>
              </div>
              <p class="text-[10px] text-slate-400 font-sans mt-0.5">
                端网寻址: ID: {{ v.deviceId }} · 默认转发: {{ activeServer.topicPrefix || 'factory' }}/{{ v.variableKey }}
              </p>
            </div>
            
            <button 
              @click="associateVariable(v.deviceId, v.variableKey)"
              class="border border-[#1890ff]/30 text-[#1890ff] hover:bg-[#1890ff] hover:text-white font-bold px-2.5 py-1 rounded text-[10px] flex items-center gap-1 cursor-pointer transition-colors"
            >
              <Plus class="w-3.5 h-3.5" />
              关联
            </button>
          </div>

          <div v-if="unassociatedVariables.length === 0" class="py-12 text-center text-slate-400">
            <Check class="w-8 h-8 text-emerald-400 mx-auto mb-2" />
            <p class="text-xs font-bold text-slate-500">无可关联变量</p>
            <p class="text-[10px] text-slate-400 mt-1">所有变量已关联到该服务器</p>
          </div>
        </div>

        <div class="bg-slate-50 p-3 flex justify-end border-t border-slate-100 shrink-0">
          <button 
            @click="showAssociateModal = false"
            class="px-5 py-1.5 rounded-lg bg-slate-900 border border-slate-900 hover:bg-slate-800 text-white font-bold text-xs cursor-pointer shadow-sm"
          >
            完成
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
