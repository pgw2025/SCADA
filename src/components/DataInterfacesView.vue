<script setup lang="ts">
import { ref, computed } from 'vue';
import { 
  exposedApis, 
  devices, 
  dataModels, 
  addLog, 
  getDeviceVariableValue 
} from '../store/index';
import { 
  Network, 
  Plus, 
  Trash2, 
  Copy, 
  X, 
  Play, 
  Send, 
  Globe, 
  Code, 
  Check, 
  AlertTriangle,
  Server
} from 'lucide-vue-next';
import { ExposedDataInterface } from '../types';

const showAddModal = ref(false);
const selectedApiId = ref<string | null>(null);

// Form bindings
const newApiName = ref('');
const apiMethod = ref<'GET' | 'POST'>('GET');
const targetVarKey = ref('');
const targetDeviceId = ref('');
const routePathInput = ref('');

// Test status simulation
const isSendingRequest = ref(false);
const showTestResult = ref(false);
const simulatedResponse = ref<any>(null);
const activeTabTest = ref<'headers' | 'payload'>('payload');

// All unique variable keys
const uniqueVariableKeys = computed(() => {
  const keys: Array<{ key: string; name: string }> = [];
  dataModels.value.forEach(m => {
    m.variables.forEach(v => {
      if (!keys.some(k => k.key === v.key)) {
        keys.push({ key: v.key, name: `${v.name} (${v.key})` });
      }
    });
  });
  return keys;
});

// Set initial selection
if (exposedApis.value.length > 0 && !selectedApiId.value) {
  selectedApiId.value = exposedApis.value[0].id;
}

const currentApiObj = computed(() => {
  return exposedApis.value.find(a => a.id === selectedApiId.value) || exposedApis.value[0] || null;
});

const handleCreateApi = () => {
  if (!newApiName.value.trim() || !routePathInput.value.trim() || !targetVarKey.value || !targetDeviceId.value) {
    alert('请将必填信息补齐再进行 API 注册发布！');
    return;
  }

  // Format path url to guarantee start with /
  let path = routePathInput.value.trim();
  if (!path.startsWith('/')) path = '/' + path;

  const newApi: ExposedDataInterface = {
    id: `api-${Date.now()}`,
    name: newApiName.value.trim(),
    exposedKey: targetVarKey.value,
    deviceId: targetDeviceId.value,
    active: true,
    routeUrl: path,
    requestMethod: apiMethod.value
  };

  exposedApis.value.push(newApi);
  selectedApiId.value = newApi.id;
  addLog('API开放接口', `发布新HTTP数据接口: [${newApi.requestMethod}] -> ${newApi.routeUrl}`, 'normal');

  // Reset
  newApiName.value = '';
  routePathInput.value = '';
  showAddModal.value = false;
};

const handleDeleteApi = (id: string, name: string) => {
  if (confirm(`确定要注销下架该 API 数据接口 [${name}] 吗？`)) {
    exposedApis.value = exposedApis.value.filter(a => a.id !== id);
    if (selectedApiId.value === id) {
      selectedApiId.value = exposedApis.value[0]?.id || null;
    }
    addLog('API开放接口', `注销并冷备了 API 接口: [${name}]`, 'warning');
  }
};

const handleTestRequest = () => {
  const api = currentApiObj.value;
  if (!api) return;

  isSendingRequest.value = true;
  showTestResult.value = false;

  // Simulate server latency
  setTimeout(() => {
    const dev = devices.value.find(d => d.id === api.deviceId);
    const rawVal = dev ? dev.variables[api.exposedKey] : 'offline';
    const d = new Date();
    const timestampStr = d.toISOString().replace('T', ' ').slice(0, 19);

    simulatedResponse.value = {
      status: 200,
      statusText: "OK",
      headers: {
        "content-type": "application/json; charset=utf-8",
        "cache-control": "no-store, no-cache, must-revalidate",
        "x-scada-gateway": "IOTA-SCADA-CORE-v6.0",
        "access-control-allow-origin": "*"
      },
      data: {
        system: "IOTA-SCADA M2M GATEWAY",
        version: "V6.0 企业级",
        api_name: api.name,
        endpoint: api.routeUrl,
        device: {
          id: dev?.id || "unknown",
          code: dev?.code || "MOCK-PLC-ERR",
          name: dev?.name || "未知绑定设备",
          status: dev?.status || "offline"
        },
        payload: {
          variable_key: api.exposedKey,
          current_value: rawVal,
          timestamp: timestampStr,
          data_quality: dev?.status === 'online' ? "GOOD (0x0)" : "BAD (0x1F)"
        }
      }
    };

    isSendingRequest.value = false;
    showTestResult.value = true;
    addLog('API开发测试', `客户端请求触发 API 接口成功: ${api.routeUrl}`, 'info');
  }, 800);
};

const handleCopyUrl = (url: string) => {
  navigator.clipboard.writeText(`http://127.0.0.1:3000${url}`);
  alert('API 相对物理网关地址已成功复制到剪贴板！');
};
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] select-none bg-slate-50">
    
    <!-- Title Section -->
    <div class="bg-white p-5 border-b border-slate-200 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 tracking-tight flex items-center gap-2">
          <Network class="w-5 h-5 text-sky-500 animate-pulse" />
          数据接口管理
        </h2>
        <p class="text-xs text-slate-500 font-sans">
          将设备数据转换为标准 RESTful API，供外部系统调用。
        </p>
      </div>

      <button 
        @click="showAddModal = true; if(uniqueVariableKeys.length) targetVarKey = uniqueVariableKeys[0].key; if(devices.length) targetDeviceId = devices[0].id;"
        class="font-bold text-xs bg-slate-900 text-white hover:bg-slate-800 px-4 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-sm active:translate-y-0.5"
      >
        <Plus class="w-4 h-4" />
        新建接口
      </button>
    </div>

    <!-- Interface Workspace split -->
    <div class="flex-1 flex flex-col lg:flex-row min-h-0 overflow-hidden">
      
      <!-- Left side: List of active routes -->
      <div class="w-full lg:w-96 bg-white border-r border-slate-200 flex flex-col shrink-0">
        <div class="p-4 border-b border-slate-100 font-bold text-[10px] text-slate-400 uppercase tracking-widest text-left">
          API 接口列表 ({{ exposedApis.length }})
        </div>

        <div class="flex-1 overflow-y-auto divide-y divide-slate-100 text-left">
          <div 
            v-for="api in exposedApis" 
            :key="api.id"
            @click="selectedApiId = api.id"
            class="p-4 cursor-pointer hover:bg-slate-50/50 transition-all space-y-2 relative"
            :class="selectedApiId === api.id ? 'bg-sky-50/20 text-sky-600 border-r-4 border-r-sky-550' : 'text-slate-700'"
          >
            <div class="flex items-start justify-between gap-1">
              <div>
                <span class="font-bold text-xs leading-snug tracking-tight block max-w-[240px] break-words text-slate-900">
                  {{ api.name }}
                </span>
                
                <!-- URL endpoint display -->
                <div class="flex items-center gap-1.5 mt-1.5 font-mono text-[10px] bg-slate-150 p-1 px-2 rounded-md border border-slate-200 w-fit">
                  <span class="font-bold text-[#1890ff] uppercase text-[9px]">{{ api.requestMethod }}</span>
                  <span class="text-slate-500 font-bold truncate block max-w-[190px]">{{ api.routeUrl }}</span>
                </div>
              </div>

              <!-- Delete api -->
              <button 
                @click.stop="handleDeleteApi(api.id, api.name)"
                class="text-slate-400 hover:text-rose-600 p-0.5"
                title="注销此接口"
              >
                <Trash2 class="w-3.5 h-3.5" />
              </button>
            </div>

            <div class="flex items-center gap-2">
              <span class="text-[9px] bg-sky-50 text-[#1890ff] font-bold px-1.5 py-0.5 rounded border border-sky-100">
                变量: {{ api.exposedKey }}
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- Right side: REST Client Mock Sandbox -->
      <div v-if="currentApiObj" class="flex-1 flex flex-col min-w-0 bg-[#0f172a] border-l border-slate-950 relative text-slate-300">
        
        <!-- REST client top panel bar -->
        <div class="bg-indigo-950/40 p-4 border-b border-indigo-950 flex flex-col sm:flex-row sm:items-center justify-between gap-3 text-left">
          <div class="space-y-1">
            <span class="text-[9px] font-bold text-indigo-400 uppercase tracking-widest font-mono">API TESTER</span>
            <h3 class="font-bold text-sm text-white flex items-center gap-1">
              <Server class="w-4 h-4 text-sky-400" />
              接口测试
            </h3>
          </div>

          <button 
            @click="handleTestRequest"
            :disabled="isSendingRequest"
            class="px-4 py-2 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-bold text-xs rounded-lg inline-flex items-center gap-1.5 cursor-pointer shadow-md tracking-wide max-w-fit active:translate-y-0.5 transition-all"
          >
            <Send class="w-3.5 h-3.5 sm:animate-bounce" />
            {{ isSendingRequest ? '请求处理中...' : '测试接口' }}
          </button>
        </div>

        <!-- Simulated Request block details -->
        <div class="p-5 border-b border-slate-800 text-left space-y-3 font-mono text-xs">
          <!-- Raw URL -->
          <div class="flex flex-col sm:flex-row sm:items-center gap-2">
            <span class="text-amber-500 font-bold uppercase w-14">HTTP URL:</span>
            <div class="flex-1 bg-slate-900 border border-slate-800 p-2 rounded-lg flex items-center justify-between overflow-hidden">
              <span class="text-slate-400 select-all truncate">
                http://127.0.0.1:3000<span class="text-emerald-400 font-bold">{{ currentApiObj.routeUrl }}</span>
              </span>
              <button 
                @click="handleCopyUrl(currentApiObj.routeUrl)"
                class="text-slate-500 hover:text-white cursor-pointer ml-2 shrink-0"
                title="复制完整路径"
              >
                <Copy class="w-4 h-4" />
              </button>
            </div>
          </div>

          <!-- Sandbox details specs -->
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4 text-slate-400 bg-slate-950/40 rounded-xl p-4 border border-slate-800/60">
            <div>
              <span class="text-slate-500 block text-[10px] font-bold">API NAME / 接口用途</span>
              <span class="text-slate-200 mt-0.5 block font-bold font-sans">{{ currentApiObj.name }}</span>
            </div>
            <div>
              <span class="text-slate-500 block text-[10px] font-bold">TRIGGER FIELD / 监测寄存器通道</span>
              <span class="text-[#1890ff] mt-0.5 block font-bold font-mono">{{ currentApiObj.exposedKey }}</span>
            </div>
          </div>
        </div>

        <!-- HTTP RESPONSE PANEL -->
        <div class="flex-1 flex flex-col min-h-0 text-left">
          
          <div class="bg-slate-950/90 px-5 py-2.5 border-b border-slate-900 flex items-center justify-between text-slate-400 font-mono text-[10px]">
            <div class="flex items-center gap-2">
            <Globe class="w-3.5 h-3.5 text-emerald-500 animate-pulse" />
            <span>响应结果</span>
          </div>

          <!-- TABS FOR HEADERS OR BODY -->
          <div class="flex bg-slate-900 p-0.5 rounded gap-1 font-bold text-[9px]">
            <button 
              @click="activeTabTest = 'payload'"
              class="px-2 py-0.5 rounded cursor-pointer"
              :class="activeTabTest === 'payload' ? 'bg-slate-800 text-white' : 'text-slate-500'"
            >
              响应体
            </button>
            <button 
              @click="activeTabTest = 'headers'"
              class="px-2 py-0.5 rounded cursor-pointer"
              :class="activeTabTest === 'headers' ? 'bg-slate-800 text-white' : 'text-slate-500'"
            >
              响应头
            </button>
          </div>
          </div>

          <!-- Response display block -->
          <div class="flex-1 overflow-y-auto p-5 font-mono text-[11px] leading-relaxed bg-[#0b0f19]">
            <div v-if="showTestResult && simulatedResponse" class="space-y-4">
              <!-- HTTP STATUS CODES -->
              <div class="flex items-center gap-2 text-xs font-bold font-sans">
                <span class="text-slate-500">Status Code:</span>
                <span class="bg-emerald-900/30 text-emerald-400 px-2 py-0.5 rounded border border-emerald-800/40">200 OK</span>
                <span class="text-slate-500 font-mono font-normal">Served in 24ms</span>
              </div>

              <!-- Payload/Body format -->
              <pre v-if="activeTabTest === 'payload'" class="text-emerald-400 select-all font-mono whitespace-pre overflow-x-auto">{{ JSON.stringify(simulatedResponse.data, null, 2) }}</pre>
              <pre v-else class="text-amber-400 select-all font-mono whitespace-pre overflow-x-auto">{{ JSON.stringify(simulatedResponse.headers, null, 2) }}</pre>
            </div>

            <div v-else-if="isSendingRequest" class="flex flex-col items-center justify-center py-16 gap-3 text-slate-500 font-sans">
              <Code class="w-10 h-10 animate-spin text-sky-400" />
              <span>正在处理请求...</span>
            </div>

            <div v-else class="flex flex-col items-center justify-center py-20 gap-3 text-slate-600 font-sans text-center">
              <Code class="w-8 h-8 text-slate-700 animate-pulse" />
              <p class="text-xs">
                就绪。点击右上角 <b class="text-slate-400 hover:underline">测试接口</b> 发送请求。
              </p>
            </div>
          </div>

        </div>

      </div>

      <div v-else class="flex-1 flex flex-col items-center justify-center text-slate-400 py-16 gap-3">
        <Network class="w-10 h-10 text-slate-300 animate-pulse" />
        <span>暂无接口。点击右上角按钮创建第一个接口。</span>
      </div>

    </div>

    <!-- PUBLISH NEW ENDPOINT DIALOG MODAL -->
    <div v-if="showAddModal" class="fixed inset-0 bg-slate-900/40 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white rounded-xl shadow-xl border border-slate-100 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <!-- Title -->
        <div class="bg-slate-900 text-white p-4 flex items-center justify-between">
          <div class="flex items-center gap-2 font-bold text-xs uppercase tracking-widest text-[#1890ff]">
            <Globe class="w-4 h-4" />
            <span>新建数据接口</span>
          </div>
          <button @click="showAddModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4.5 h-4.5" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs font-sans">
          
          <div>
            <label class="font-bold text-slate-500 block mb-1">接口名称</label>
            <input 
              v-model="newApiName"
              type="text"
              placeholder="如: 储水罐液位接口"
              class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 outline-none focus:border-[#1890ff]"
            />
          </div>

          <!-- Route Path & Method -->
          <div class="grid grid-cols-3 gap-3">
            <div>
              <label class="font-bold text-slate-500 block mb-1">请求方法</label>
              <select 
                v-model="apiMethod"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 focus:outline-none font-bold"
              >
                <option value="GET">GET</option>
                <option value="POST">POST</option>
              </select>
            </div>

            <div class="col-span-2">
              <label class="font-bold text-slate-500 block mb-1">路由路径</label>
              <input 
                v-model="routePathInput"
                type="text"
                placeholder="/api/v1/factory/levels"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 outline-none font-mono focus:border-[#1890ff]"
              />
            </div>
          </div>

          <!-- Variable choices -->
          <div class="grid grid-cols-2 gap-3 border-t border-slate-100 pt-3">
            <div>
              <label class="font-bold text-slate-500 block mb-1">源设备</label>
              <select 
                v-model="targetDeviceId"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-1.5 focus:bg-white text-slate-800 font-bold focus:outline-none"
              >
                <option v-for="d in devices" :key="d.id" :value="d.id">
                  {{ d.name }}
                </option>
              </select>
            </div>

            <div>
              <label class="font-bold text-slate-500 block mb-1">映射变量</label>
              <select 
                v-model="targetVarKey"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-1.5 focus:bg-white text-slate-800 font-mono font-bold focus:outline-none"
              >
                <option v-for="v in uniqueVariableKeys" :key="v.key" :value="v.key">
                  {{ v.name }}
                </option>
              </select>
            </div>
          </div>

        </div>

        <!-- Footer -->
        <div class="bg-slate-50 p-3 flex justify-end gap-2 border-t border-slate-100">
          <button 
            @click="showAddModal = false"
            class="px-3 py-1.5 rounded-lg border border-slate-200 bg-white hover:bg-slate-50 font-bold text-xs text-slate-600 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleCreateValueApi"
            @click.stop="handleCreateApi"
            class="px-4 py-1.5 rounded-lg bg-slate-900 hover:bg-slate-800 font-bold text-xs text-white cursor-pointer"
          >
            保存接口
          </button>
        </div>

      </div>
    </div>

  </div>
</template>
