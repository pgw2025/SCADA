<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import {
  devices,
  dataModels,
  addLog
} from '../store/index';
import {
  Network,
  Plus,
  Trash2,
  Copy,
  X,
  Send,
  Globe,
  Code,
  Server,
  Pencil,
  Power
} from 'lucide-vue-next';
import { ExposedDataInterface } from '../types';
import {
  fetchExposedInterfaces,
  createExposedInterface,
  updateExposedInterface,
  deleteExposedInterface,
  setExposedInterfaceEnabled
} from '../api/exposedInterfaceApi';

const apis = ref<ExposedDataInterface[]>([]);
const isLoading = ref(false);
const loadError = ref('');
const showAddModal = ref(false);
const editingId = ref<number | null>(null); // null = 新建
const selectedApiId = ref<number | null>(null);
const isSaving = ref(false);

// Form bindings
const newApiName = ref('');
const apiMethod = ref<'GET' | 'POST'>('GET');
const routePathInput = ref('');
const targetDeviceId = ref(0);
const targetVarKey = ref('');

// Test state
const isTesting = ref(false);
const testResult = ref<any>(null);
const testError = ref('');
const testDurationMs = ref<number | null>(null);
const activeTabTest = ref<'headers' | 'payload'>('payload');

const displayOrigin = window.location.origin;

const currentApiObj = computed(
  () => apis.value.find((a) => a.id === selectedApiId.value) || apis.value[0] || null
);

// 变量联动：仅列出所选设备所属数据模型的变量，避免跨设备同名变量混淆。
const variablesOfSelectedDevice = computed<Array<{ key: string; name: string }>>(() => {
  const dev = devices.value.find((d) => d.id === targetDeviceId.value);
  if (!dev) return [];
  const model = dataModels.value.find((m) => m.id === String(dev.modelId));
  return (model?.variables ?? []).map((v) => ({ key: v.key, name: `${v.name} (${v.key})` }));
});

onMounted(loadApis);

async function loadApis() {
  isLoading.value = true;
  loadError.value = '';
  try {
    apis.value = (await fetchExposedInterfaces()).data;
    if (!selectedApiId.value && apis.value.length) {
      selectedApiId.value = apis.value[0].id;
    }
  } catch (e: any) {
    loadError.value = e?.response?.data?.message || e?.message || '加载接口列表失败';
  } finally {
    isLoading.value = false;
  }
}

function openCreate() {
  editingId.value = null;
  newApiName.value = '';
  apiMethod.value = 'GET';
  routePathInput.value = '';
  targetDeviceId.value = devices.value[0]?.id ?? 0;
  targetVarKey.value = '';
  showAddModal.value = true;
}

function openEdit(api: ExposedDataInterface) {
  editingId.value = api.id;
  newApiName.value = api.name;
  apiMethod.value = api.requestMethod;
  routePathInput.value = api.routeUrl;
  targetDeviceId.value = api.deviceId;
  targetVarKey.value = api.exposedKey;
  showAddModal.value = true;
}

async function handleSave() {
  if (!newApiName.value.trim() || !routePathInput.value.trim() || !targetVarKey.value || !targetDeviceId.value) {
    alert('请将必填信息补齐再进行接口注册发布！');
    return;
  }
  let path = routePathInput.value.trim();
  if (!path.startsWith('/')) path = '/' + path;

  isSaving.value = true;
  try {
    if (editingId.value != null) {
      const existing = apis.value.find((a) => a.id === editingId.value);
      await updateExposedInterface({
        ...existing!,
        id: editingId.value,
        name: newApiName.value.trim(),
        requestMethod: apiMethod.value,
        routeUrl: path,
        deviceId: targetDeviceId.value,
        exposedKey: targetVarKey.value
      });
      addLog('API开放接口', `更新API数据接口: [${apiMethod.value}] -> ${path}`, 'info');
    } else {
      await createExposedInterface({
        name: newApiName.value.trim(),
        requestMethod: apiMethod.value,
        routeUrl: path,
        deviceId: targetDeviceId.value,
        exposedKey: targetVarKey.value,
        active: true
      });
      addLog('API开放接口', `发布新HTTP数据接口: [${apiMethod.value}] -> ${path}`, 'normal');
    }
    await loadApis();
    showAddModal.value = false;
  } catch {
    // 失败由 http 拦截器统一提示，保持弹窗不关闭以便修正。
  } finally {
    isSaving.value = false;
  }
}

async function handleDeleteApi(id: number, name: string) {
  if (confirm(`确定要注销下架该 API 数据接口 [${name}] 吗？`)) {
    try {
      await deleteExposedInterface(id);
      if (selectedApiId.value === id) {
        selectedApiId.value = apis.value[0]?.id ?? null;
      }
      addLog('API开放接口', `注销并冷备了 API 接口: [${name}]`, 'warning');
      await loadApis();
    } catch {
      // 拦截器已提示
    }
  }
}

// 启停开关：乐观更新，失败回滚。
async function handleToggleActive(api: ExposedDataInterface) {
  const origin = api.active;
  api.active = !api.active;
  try {
    await setExposedInterfaceEnabled(api.id, api.active);
    addLog('API开放接口', `${api.active ? '启用' : '停用'} API 接口: [${api.name}]`, 'info');
  } catch {
    api.active = origin;
  }
}

// 真实接口测试：用底层 fetch 以外部调用者视角（不带 JWT）请求 /open/*。
async function handleTestRequest() {
  const api = currentApiObj.value;
  if (!api) return;
  isTesting.value = true;
  testResult.value = null;
  testError.value = '';
  testDurationMs.value = null;

  const url = displayOrigin + api.routeUrl;
  const start = performance.now();
  try {
    const resp = await fetch(url, { method: api.requestMethod });
    testDurationMs.value = Math.round(performance.now() - start);
    let body: any = null;
    try {
      body = await resp.json();
    } catch {
      body = await resp.text();
    }
    testResult.value = {
      status: resp.status,
      statusText: resp.statusText,
      headers: Object.fromEntries(resp.headers.entries()),
      body
    };
  } catch (e: any) {
    testError.value = e?.message || '无法连接网关';
  } finally {
    isTesting.value = false;
  }
}

function handleCopyUrl(url: string) {
  navigator.clipboard.writeText(displayOrigin + url);
  alert('API 地址已成功复制到剪贴板！');
}

function statusClass(code: number) {
  if (code >= 500) return 'bg-rose-900/30 text-rose-400 border-rose-800/40';
  if (code >= 400) return 'bg-amber-900/30 text-amber-400 border-amber-800/40';
  return 'bg-emerald-900/30 text-emerald-400 border-emerald-800/40';
}

function previewBody(body: any) {
  if (typeof body === 'string') return body;
  return JSON.stringify(body, null, 2);
}
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">

    <!-- Title Section -->
    <div class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Network class="w-5 h-5 text-sky-500 animate-pulse" />
          数据接口管理
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          将设备数据转换为标准 RESTful API，经 /open 网关供外部系统实时调用。
        </p>
      </div>

      <button
        @click="openCreate"
        class="font-bold text-xs bg-slate-900 dark:bg-sky-600 text-white hover:bg-slate-800 dark:hover:bg-sky-500 px-4 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-sm active:translate-y-0.5"
      >
        <Plus class="w-4 h-4" />
        新建接口
      </button>
    </div>

    <!-- Interface Workspace split -->
    <div class="flex-1 flex flex-col lg:flex-row min-h-0 overflow-hidden">

      <!-- Left side: List of active routes -->
      <div class="w-full lg:w-96 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex flex-col shrink-0 transition-colors">
        <div class="p-4 border-b border-slate-100 dark:border-slate-800 font-bold text-[10px] text-slate-400 dark:text-slate-500 uppercase tracking-widest text-left">
          API 接口列表 ({{ apis.length }})
        </div>

        <div v-if="isLoading" class="flex-1 flex items-center justify-center text-xs text-slate-400 py-10">
          加载中...
        </div>
        <div v-else-if="loadError" class="flex-1 flex flex-col items-center gap-3 py-10 px-4">
          <p class="text-xs text-rose-500 text-center">{{ loadError }}</p>
          <button @click="loadApis" class="text-xs font-bold text-sky-500 cursor-pointer hover:underline">重试</button>
        </div>

        <div v-else class="flex-1 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800 text-left">
          <div
            v-for="api in apis"
            :key="api.id"
            @click="selectedApiId = api.id"
            class="p-4 cursor-pointer hover:bg-slate-50/50 dark:hover:bg-slate-800/40 transition-all space-y-2 relative"
            :class="selectedApiId === api.id ? 'bg-sky-50/30 dark:bg-sky-950/30 text-sky-600 dark:text-sky-400 border-r-4 border-r-sky-500' : 'text-slate-700 dark:text-slate-300'"
          >
            <div class="flex items-start justify-between gap-1">
              <div class="min-w-0">
                <span class="font-bold text-xs leading-snug tracking-tight block max-w-[200px] break-words text-slate-900 dark:text-slate-100">
                  {{ api.name }}
                </span>

                <div class="flex items-center gap-1.5 mt-1.5 font-mono text-[10px] bg-slate-100 dark:bg-slate-800 p-1 px-2 rounded-md border border-slate-200 dark:border-slate-700 w-fit">
                  <span class="font-bold text-[#1890ff] uppercase text-[9px]">{{ api.requestMethod }}</span>
                  <span class="text-slate-500 dark:text-slate-400 font-bold truncate block max-w-[170px]">{{ api.routeUrl }}</span>
                </div>
              </div>

              <div class="flex items-center gap-1 shrink-0">
                <button
                  @click.stop="handleToggleActive(api)"
                  class="p-1 rounded cursor-pointer transition-colors"
                  :class="api.active ? 'text-emerald-500 hover:text-emerald-600' : 'text-slate-400 hover:text-slate-600'"
                  :title="api.active ? '停用接口' : '启用接口'"
                >
                  <Power class="w-3.5 h-3.5" />
                </button>
                <button
                  @click.stop="openEdit(api)"
                  class="p-1 text-slate-400 hover:text-sky-600 cursor-pointer transition-colors"
                  title="编辑接口"
                >
                  <Pencil class="w-3.5 h-3.5" />
                </button>
                <button
                  @click.stop="handleDeleteApi(api.id, api.name)"
                  class="p-1 text-slate-400 hover:text-rose-600 dark:hover:text-rose-400 transition-colors cursor-pointer"
                  title="注销此接口"
                >
                  <Trash2 class="w-3.5 h-3.5" />
                </button>
              </div>
            </div>

            <div class="flex items-center gap-2">
              <span
                class="text-[9px] px-1.5 py-0.5 rounded border font-bold"
                :class="api.active
                  ? 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400 border-emerald-100 dark:border-emerald-800'
                  : 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700'"
              >
                {{ api.active ? '已启用' : '已停用' }}
              </span>
              <span class="text-[9px] bg-sky-50 dark:bg-sky-950/60 text-[#1890ff] dark:text-sky-400 font-bold px-1.5 py-0.5 rounded border border-sky-100 dark:border-sky-800 truncate">
                变量: {{ api.exposedKey }}
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- Right side: REST Client Sandbox -->
      <div v-if="currentApiObj" class="flex-1 flex flex-col min-w-0 bg-[#0f172a] dark:bg-slate-950 border-l border-slate-950 dark:border-slate-800 relative text-slate-300">

        <!-- REST client top panel bar -->
        <div class="bg-indigo-950/40 dark:bg-slate-900/60 p-4 border-b border-indigo-950 dark:border-slate-800 flex flex-col sm:flex-row sm:items-center justify-between gap-3 text-left">
          <div class="space-y-1">
            <span class="text-[9px] font-bold text-indigo-400 dark:text-sky-400 uppercase tracking-widest font-mono">API TESTER</span>
            <h3 class="font-bold text-sm text-white flex items-center gap-1">
              <Server class="w-4 h-4 text-sky-400" />
              接口测试
            </h3>
          </div>

          <button
            @click="handleTestRequest"
            :disabled="isTesting || !currentApiObj.active"
            :title="currentApiObj.active ? '' : '接口已停用，无法测试'"
            class="px-4 py-2 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 disabled:cursor-not-allowed text-white font-bold text-xs rounded-lg inline-flex items-center gap-1.5 cursor-pointer shadow-md tracking-wide max-w-fit active:translate-y-0.5 transition-all"
          >
            <Send class="w-3.5 h-3.5" />
            {{ isTesting ? '请求处理中...' : '测试接口' }}
          </button>
        </div>

        <!-- Request block details -->
        <div class="p-5 border-b border-slate-800 text-left space-y-3 font-mono text-xs">
          <div class="flex flex-col sm:flex-row sm:items-center gap-2">
            <span class="text-amber-500 font-bold uppercase w-14">HTTP URL:</span>
            <div class="flex-1 bg-slate-900 dark:bg-slate-900/80 border border-slate-800 p-2 rounded-lg flex items-center justify-between overflow-hidden">
              <span class="text-slate-400 select-all truncate">
                {{ displayOrigin }}<span class="text-emerald-400 font-bold">{{ currentApiObj.routeUrl }}</span>
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

          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4 text-slate-400 bg-slate-950/40 rounded-xl p-4 border border-slate-800/60">
            <div>
              <span class="text-slate-500 block text-[10px] font-bold">API NAME / 接口用途</span>
              <span class="text-slate-200 mt-0.5 block font-bold font-sans">{{ currentApiObj.name }}</span>
            </div>
            <div>
              <span class="text-slate-500 block text-[10px] font-bold">TRIGGER FIELD / 监测变量</span>
              <span class="text-[#1890ff] mt-0.5 block font-bold font-mono truncate">{{ currentApiObj.exposedKey }}</span>
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

          <div class="flex-1 overflow-y-auto p-5 font-mono text-[11px] leading-relaxed bg-[#0b0f19]">
            <div v-if="testError" class="flex items-center gap-2 text-xs font-sans text-rose-400">
              <span class="font-bold">请求失败：</span><span>{{ testError }}</span>
            </div>

            <div v-else-if="testResult" class="space-y-4">
              <div class="flex items-center gap-2 text-xs font-bold font-sans flex-wrap">
                <span class="text-slate-500">Status Code:</span>
                <span class="px-2 py-0.5 rounded border" :class="statusClass(testResult.status)">
                  {{ testResult.status }} {{ testResult.statusText }}
                </span>
                <span v-if="testDurationMs != null" class="text-slate-500 font-mono font-normal">Served in {{ testDurationMs }}ms</span>
              </div>

              <pre v-if="activeTabTest === 'payload'" class="text-emerald-400 select-all font-mono whitespace-pre overflow-x-auto">{{ previewBody(testResult.body) }}</pre>
              <pre v-else class="text-amber-400 select-all font-mono whitespace-pre overflow-x-auto">{{ JSON.stringify(testResult.headers, null, 2) }}</pre>
            </div>

            <div v-else-if="isTesting" class="flex flex-col items-center justify-center py-16 gap-3 text-slate-500 font-sans">
              <Code class="w-10 h-10 animate-spin text-sky-400" />
              <span>正在处理请求...</span>
            </div>

            <div v-else class="flex flex-col items-center justify-center py-20 gap-3 text-slate-600 font-sans text-center">
              <Code class="w-8 h-8 text-slate-700 animate-pulse" />
              <p class="text-xs">
                就绪。点击右上角 <b class="text-slate-400">测试接口</b> 发送真实请求。
              </p>
            </div>
          </div>
        </div>
      </div>

      <div v-else class="flex-1 flex flex-col items-center justify-center text-slate-400 dark:text-slate-600 py-16 gap-3">
        <Network class="w-10 h-10 text-slate-300 dark:text-slate-600 animate-pulse" />
        <span v-if="!isLoading && !loadError">暂无接口。点击右上角按钮创建第一个接口。</span>
      </div>
    </div>

    <!-- NEW / EDIT ENDPOINT DIALOG MODAL -->
    <div v-if="showAddModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-2 font-bold text-xs uppercase tracking-widest text-[#1890ff]">
            <Globe class="w-4 h-4" />
            <span>{{ editingId != null ? '编辑数据接口' : '新建数据接口' }}</span>
          </div>
          <button @click="showAddModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4.5 h-4.5" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs font-sans">

          <div>
            <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">接口名称</label>
            <input
              v-model="newApiName"
              type="text"
              placeholder="如: 储水罐液位接口"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white outline-none focus:border-[#1890ff]"
            />
          </div>

          <div class="grid grid-cols-3 gap-3">
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">请求方法</label>
              <select
                v-model="apiMethod"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white focus:outline-none font-bold"
              >
                <option value="GET">GET</option>
                <option value="POST">POST</option>
              </select>
            </div>

            <div class="col-span-2">
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">路由路径</label>
              <input
                v-model="routePathInput"
                type="text"
                placeholder="/open/factory/levels"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white outline-none font-mono focus:border-[#1890ff]"
              />
              <p class="text-[10px] text-slate-400 mt-1">须以 <code class="font-mono text-[#1890ff]">/open/</code> 开头</p>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3 border-t border-slate-100 dark:border-slate-800 pt-3">
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">源设备</label>
              <select
                v-model="targetDeviceId"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-bold focus:outline-none"
              >
                <option v-for="d in devices" :key="d.id" :value="d.id">
                  {{ d.name }}
                </option>
              </select>
            </div>

            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">映射变量</label>
              <select
                v-model="targetVarKey"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-mono font-bold focus:outline-none"
              >
                <option v-for="v in variablesOfSelectedDevice" :key="v.key" :value="v.key">
                  {{ v.name }}
                </option>
              </select>
              <p v-if="variablesOfSelectedDevice.length === 0" class="text-[10px] text-amber-500 mt-1">该设备模型暂无可用变量</p>
            </div>
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800">
          <button
            @click="showAddModal = false"
            class="px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button
            @click="handleSave"
            :disabled="isSaving"
            class="px-4 py-1.5 rounded-lg bg-slate-900 dark:bg-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white cursor-pointer disabled:opacity-50"
          >
            {{ isSaving ? '保存中...' : (editingId != null ? '保存修改' : '保存接口') }}
          </button>
        </div>
      </div>
    </div>

  </div>
</template>