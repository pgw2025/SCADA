<script setup lang="ts">
import { ref, computed, onMounted, watch, onUnmounted } from 'vue';
import {
  devices,
  addLog,
  scriptExecutionEvents
} from '../store/index';
import {
  loadSystemScripts,
  saveNewScript,
  persistScript,
  removeScript,
  validateScript,
  runScript,
  testScript,
  resetScriptTripped,
  queryScriptRecords
} from '../services/scriptService';
import {
  FileCode,
  Plus,
  Trash2,
  Play,
  Clock,
  Terminal,
  Save,
  X,
  RotateCcw,
  Check,
  AlertTriangle,
  Shield,
  ShieldCheck,
  Zap
} from 'lucide-vue-next';
import { SystemScript, ScriptExecutionRecord } from '../types';

// ========== 列表与编辑态 ==========
const scripts = ref<SystemScript[]>([]);
const selectedId = ref<number | null>(null);
const loading = ref(false);

// 可编辑副本：选中列表项时深拷贝，编辑不直接污染列表，保存时才写库
const form = ref<SystemScript>(blankScript());
const isNew = computed(() => currentFormId.value === 0);

const currentFormId = computed(() => form.value.id ?? 0);
const selectedScript = computed(() =>
  scripts.value.find(s => s.id === selectedId.value) || null
);

const triggerTypeLabel = (t: string) =>
  t === 'Periodic' ? '周期' : t === 'Schedule' ? '定时' : t === 'OnChange' ? '变量变化' : '手动';

function blankScript(): SystemScript {
  return {
    id: 0,
    name: '',
    code: '// 模板：服务端 Jint 沙箱执行\n' +
          '// 可选钩子 run()（手动/周期/定时）与 onChange(ev)（变量变化）\n' +
          'log("hello scada");',
    triggerType: 'Manual',
    intervalSeconds: null,
    cronExpression: '',
    watchDeviceKey: null,
    watchVariableKey: null,
    deadBand: null,
    cooldownMs: 500,
    timeoutMs: 2000,
    scopeRead: '',
    scopeWrite: '',
    active: true,
    version: 0,
    failureCount: 0,
    tripped: false,
    lastError: null,
    lastExecutedAt: null,
    lastDurationMs: null
  };
}

const loadAll = async () => {
  loading.value = true;
  try {
    scripts.value = await loadSystemScripts();
    if (selectedId.value == null || !scripts.value.some(s => s.id === selectedId.value)) {
      selectedId.value = scripts.value[0]?.id ?? null;
    }
    if (selectedId.value != null) {
      const cur = scripts.value.find(s => s.id === selectedId.value);
      if (cur) form.value = { ...cur };
    } else {
      form.value = blankScript();
    }
  } finally {
    loading.value = false;
  }
};

// ========== 选中与新建 ==========
const selectScript = (id: number) => {
  selectedId.value = id;
  const cur = scripts.value.find(s => s.id === id);
  if (cur) form.value = { ...cur };
  loadRecords(1);
};

const beginNew = () => {
  form.value = blankScript();
  selectedId.value = null;
  consoleLines.value = [];
  clearRecords();
};

const handleDelete = async () => {
  if (selectedId.value == null) return;
  const name = form.value.name;
  if (!confirm(`确定删除脚本 [${name}] 吗？`)) return;
  await removeScript(selectedId.value);
  selectedId.value = null;
  form.value = blankScript();
  addLog('系统脚本', `已删除脚本 [${name}]`, 'warning');
  await loadAll();
};

// ========== 校验 / 保存 ==========
const consoleLines = ref<{ text: string; tone: string }[]>([]);
const pushConsole = (text: string, tone = 'neutral') => {
  consoleLines.value.push({ text, tone });
  if (consoleLines.value.length > 500) consoleLines.value.shift();
};
const validateTone = 'bg-emerald-500/90';

const buildPayload = (): SystemScript => ({
  ...form.value,
  scopeRead: (form.value.scopeRead || '').trim(),
  scopeWrite: (form.value.scopeWrite || '').trim()
});

const handleValidate = async () => {
  const res = await validateScript(buildPayload());
  pushConsole(`—— 脚本校验 ——`);
  if (!res.valid) {
    pushConsole('校验未通过（存在 Error 级问题）:', 'error');
    res.issues.forEach(i => pushConsole(`  [${i.level}] ${i.message}`, i.level === 'Error' ? 'error' : 'warn'));
  } else {
    pushConsole('校验通过 ✓（语法与元数据合法）', 'ok');
    res.issues.forEach(i => pushConsole(`  [${i.level}] ${i.message}`, 'warn'));
  }
};

const handleSave = async () => {
  if (!form.value.name.trim()) { pushConsole('脚本名称不能为空', 'error'); return; }
  const res = await validateScript(buildPayload()).catch(() => null);
  if (!res) return;
  if (!res.valid) {
    pushConsole('保存被阻止：存在 Error 级校验问题。', 'error');
    res.issues.forEach(i => pushConsole(`  [${i.level}] ${i.message}`, 'error'));
    return;
  }
  const payload = buildPayload();
  if (isNew.value) {
    await saveNewScript(payload);
    pushConsole(`已创建脚本 [${payload.name}]`, 'ok');
  } else {
    await persistScript(payload);
    pushConsole(`已保存脚本 [${payload.name}]（版本 +1，熔断已复位）`, 'ok');
  }
  await loadAll();
};

// ========== 运行 / 试运行 ==========
const running = ref(false);
const testing = ref(false);

const handleRun = async () => {
  if (currentFormId.value <= 0) { pushConsole('请先保存脚本后再运行', 'error'); return; }
  running.value = true;
  try {
    const res = await runScript(currentFormId.value);
    pushConsole(`—— 手动运行 结果: ${res?.result} ——`, res?.result === 'Success' ? 'ok' : 'error');
    if (res?.durationMs != null) pushConsole(`耗时: ${res.durationMs} ms`);
    if (res?.error) pushConsole(`错误: ${res.error}`, 'error');
    if (res?.output) {
      (res.output as string).split('\n').filter(Boolean).forEach(l => pushConsole(`  | ${l}`));
    }
    await loadAll();
  } finally {
    running.value = false;
  }
};

const handleTest = async () => {
  testing.value = true;
  try {
    const payload = buildPayload();
    const isOnChange = payload.triggerType === 'OnChange';
    const ctxDevice = isOnChange ? payload.watchDeviceKey : null;
    const ctxVariable = isOnChange ? payload.watchVariableKey : null;
    const res = await testScript(payload, ctxDevice, ctxVariable);
    pushConsole(`—— 试运行(dry-run) 结果: ${res?.result} ——`, res?.result === 'Success' ? 'ok' : 'error');
    if (res?.durationMs != null) pushConsole(`耗时: ${res.durationMs} ms`);
    if (res?.error) pushConsole(`错误: ${res.error}`, 'error');
    if (res?.output) {
      (res.output as string).split('\n').filter(Boolean).forEach(l => pushConsole(`  | ${l}`));
    }
  } finally {
    testing.value = false;
  }
};

const handleResetTripped = async () => {
  if (currentFormId.value <= 0) return;
  await resetScriptTripped(currentFormId.value);
  pushConsole('已复位熔断状态（FailureCount=0, Tripped=false）', 'ok');
  await loadAll();
};

// ========== scope 选择器 ==========
const readSet = computed(() =>
  new Set((form.value.scopeRead || '').split(';').filter(Boolean))
);

const toggleReadScope = (dk: string) => {
  const set = readSet.value;
  set.has(dk) ? set.delete(dk) : set.add(dk);
  form.value.scopeRead = [...set].join(';');
};

// 写授权：先选设备，再勾变量（存 "设备键.变量键"）
const writeDeviceKey = ref<string>('');
const writeSet = computed(() =>
  new Set((form.value.scopeWrite || '').split(';').filter(Boolean))
);
const writeDeviceVars = computed(() => {
  const dev = devices.value.find(d => d.key === writeDeviceKey.value);
  return dev ? Object.keys(dev.variables || {}).filter(k => k) : [];
});
const toggleWriteScope = (varkey: string) => {
  const entry = `${writeDeviceKey.value}.${varkey}`;
  const set = writeSet.value;
  set.has(entry) ? set.delete(entry) : set.add(entry);
  form.value.scopeWrite = [...set].join(';');
};

const deviceVariableOpts = (devKey: string) => {
  const dev = devices.value.find(d => d.key === devKey);
  return dev ? Object.keys(dev.variables || {}).filter(k => k) : [];
};

// ========== 执行记录控制台 ==========
const records = ref<ScriptExecutionRecord[]>([]);
const recordsTotal = ref(0);
const recordsPage = ref(1);
const recordsPageSize = 20;

const clearRecords = () => { records.value = []; recordsTotal.value = 0; recordsPage.value = 1; };

const loadRecords = async (page = recordsPage.value) => {
  if (selectedId.value == null) { clearRecords(); return; }
  const res = await queryScriptRecords(selectedId.value, undefined, page, recordsPageSize);
  records.value = res.items;
  recordsTotal.value = res.total;
  recordsPage.value = page;
};

// SignalR 实时执行事件：匹配当前脚本时写入控制台
const unwatch = watch(scriptExecutionEvents, (list) => {
  if (selectedId.value == null) return;
  const evt = list[0];
  if (!evt || evt.scriptId !== selectedId.value) return;
  const src = evt.triggerSource ?? 'Auto';
  const tone = evt.result === 'Success' ? 'ok' : evt.result === 'Skipped' ? 'warn' : 'error';
  pushConsole(`[实时] ${src} 执行 ${evt.result}${evt.durationMs != null ? ` (${evt.durationMs}ms)` : ''}`, tone);
  if (evt.error) pushConsole(`  错误: ${evt.error}`, 'error');
});

const fmtTime = (iso?: string | null) => {
  if (!iso) return '未执行';
  const d = new Date(iso);
  return isNaN(d.getTime()) ? '未执行' : d.toLocaleString();
};

const fmtDuration = (ms?: number | null) => (ms == null ? '-' : `${ms} ms`);

onMounted(async () => {
  await loadAll();
  if (selectedId.value != null) loadRecords(1);
});
onUnmounted(() => unwatch());
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">

    <!-- Header banner -->
    <div class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <FileCode class="w-5 h-5 text-indigo-600 dark:text-indigo-400" />
          系统脚本管理
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          服务端沙箱执行。支持手动 / 周期 / Cron 定时 / 变量变化触发，自带熔断、执行记录与权限白名单。
        </p>
      </div>
      <button
        @click="beginNew"
        class="font-bold text-xs bg-slate-900 dark:bg-sky-600 text-white hover:bg-slate-800 dark:hover:bg-sky-500 px-4 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-sm active:translate-y-0.5"
      >
        <Plus class="w-4 h-4" />
        新建脚本
      </button>
    </div>

    <div class="flex-1 flex flex-col lg:flex-row min-h-0 overflow-hidden">

      <!-- Left sidebar selector -->
      <div class="w-full lg:w-72 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex flex-col shrink-0 transition-colors">
        <div class="p-4 border-b border-slate-100 dark:border-slate-800 font-bold text-[10px] text-slate-400 dark:text-slate-500 uppercase tracking-widest text-left">
          脚本列表 ({{ scripts.length }})
        </div>
        <div class="flex-1 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800 text-left">
          <div
            v-for="scr in scripts"
            :key="scr.id"
            @click="selectScript(scr.id)"
            class="p-4 cursor-pointer hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-all space-y-2 relative"
            :class="scr.id === selectedId ? 'bg-indigo-50/20 dark:bg-indigo-950/40 text-indigo-600 dark:text-indigo-400 border-r-4 border-r-indigo-600 dark:border-r-indigo-400' : 'text-slate-700 dark:text-slate-300'"
          >
            <span class="font-bold text-xs leading-snug tracking-tight block max-w-[180px] break-words pr-6">{{ scr.name }}</span>
            <div class="flex items-center gap-2 flex-wrap">
              <span class="text-[9px] font-bold px-1.5 py-0.5 rounded border"
                :class="scr.triggerType === 'Periodic' || scr.triggerType === 'Schedule' ? 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400 border-emerald-100 dark:border-emerald-800' : scr.triggerType === 'OnChange' ? 'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 border-sky-100 dark:border-sky-800' : 'bg-slate-50 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-150 dark:border-slate-700'"
              >
                {{ triggerTypeLabel(scr.triggerType) }}
              </span>
              <span v-if="scr.tripped" class="text-[9px] font-bold px-1.5 py-0.5 rounded bg-rose-500 text-white border border-rose-500">熔断</span>
              <span v-if="!scr.active" class="text-[9px] font-bold px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-400 dark:text-slate-500 border border-slate-200 dark:border-slate-700">停用</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Right panel -->
      <div class="flex-1 flex flex-col min-w-0 bg-slate-900 dark:bg-slate-950 border-l border-slate-950 dark:border-slate-800 relative overflow-hidden">

        <!-- Editor header -->
        <div class="bg-slate-950/80 px-5 py-3 border-b border-slate-950 dark:border-slate-800 flex items-center justify-between text-white text-xs gap-2">
          <div class="flex items-center gap-2 min-w-0">
            <span class="w-2.5 h-2.5 rounded-full bg-rose-500 shrink-0" />
            <b class="font-sans text-slate-400">脚本:</b>
            <input
              v-model="form.name"
              placeholder="脚本名称（必填）"
              class="bg-transparent font-bold text-[#1890ff] font-mono outline-none border-b border-transparent focus:border-slate-600 min-w-0 flex-1"
            />
            <span class="text-[10px] text-slate-500 font-mono hidden md:inline-block">v{{ form.version }}</span>
            <span v-if="form.tripped" class="text-[10px] font-bold px-1.5 py-0.5 rounded bg-rose-500 text-white">熔断</span>
            <span class="text-[10px] text-slate-500 font-mono hidden md:inline-block" title="最近执行开始时间">{{ isNew ? '未保存' : fmtTime(form.lastExecutedAt) }}</span>
          </div>

          <div class="flex items-center gap-1.5 flex-wrap justify-end">
            <label class="flex items-center gap-1 text-[10px] text-slate-400 font-sans cursor-pointer">
              <input v-model="form.active" type="checkbox" class="accent-sky-500" />
              启用
            </label>
            <button @click="handleValidate"
              class="px-2.5 py-1 rounded bg-slate-700 hover:bg-slate-600 text-white font-bold inline-flex items-center gap-1 cursor-pointer text-xs">
              <Check class="w-3.5 h-3.5" /> 校验
            </button>
            <button @click="handleTest" :disabled="testing"
              class="px-2.5 py-1 rounded bg-slate-700 hover:bg-slate-600 text-white font-bold inline-flex items-center gap-1 cursor-pointer text-xs disabled:opacity-40">
              <RotateCcw class="w-3.5 h-3.5" /> 试运行
            </button>
            <button @click="handleRun" :disabled="running || isNew"
              class="px-3 py-1 rounded bg-indigo-600 hover:bg-indigo-500 text-white font-bold inline-flex items-center gap-1 cursor-pointer text-xs disabled:opacity-40">
              <Play class="w-3.5 h-3.5" /> 运行
            </button>
            <button @click="handleSave"
              class="px-3 py-1 rounded bg-emerald-600 hover:bg-emerald-500 text-white font-bold inline-flex items-center gap-1 cursor-pointer text-xs">
              <Save class="w-3.5 h-3.5" /> {{ isNew ? '创建' : '保存' }}
            </button>
            <button v-if="form.tripped" @click="handleResetTripped"
              class="px-2.5 py-1 rounded bg-amber-600 hover:bg-amber-500 text-white font-bold inline-flex items-center gap-1 cursor-pointer text-xs">
              <Zap class="w-3.5 h-3.5" /> 复位熔断
            </button>
            <button v-if="!isNew" @click="handleDelete"
              class="px-2 py-1 rounded bg-slate-800 hover:bg-rose-600 text-slate-300 hover:text-white font-bold inline-flex items-center gap-1 cursor-pointer text-xs">
              <Trash2 class="w-3.5 h-3.5" /> 删除
            </button>
          </div>
        </div>

        <!-- Main scrollable content: metadata + editor + console -->
        <div class="flex-1 overflow-y-auto min-h-0 bg-slate-900 dark:bg-slate-950">

          <!-- 触发与执行参数表单 -->
          <div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3 px-4 py-4 border-b border-slate-800/60 bg-slate-800/30">
            <!-- 触发类型 -->
            <div>
              <label class="block text-[10px] font-bold text-slate-400 mb-1 uppercase tracking-wider">触发类型</label>
              <select v-model="form.triggerType"
                class="w-full bg-slate-950 border border-slate-700 rounded p-1.5 text-xs text-white font-mono outline-none focus:border-sky-500">
                <option value="Manual">手动触发</option>
                <option value="Periodic">周期执行</option>
                <option value="Schedule">Cron 定时</option>
                <option value="OnChange">变量变化</option>
              </select>
            </div>

            <div v-if="form.triggerType === 'Periodic'">
              <label class="block text-[10px] font-bold text-slate-400 mb-1 uppercase tracking-wider">执行间隔（秒）</label>
              <input v-model.number="form.intervalSeconds" type="number" min="1" placeholder="≥1"
                class="w-full bg-slate-950 border border-slate-700 rounded p-1.5 text-xs text-white font-mono outline-none focus:border-sky-500" />
            </div>

            <div v-if="form.triggerType === 'Schedule'" class="md:col-span-2 xl:col-span-1">
              <label class="block text-[10px] font-bold text-slate-400 mb-1 uppercase tracking-wider">Cron 表达式（Asia/Shanghai）</label>
              <input v-model="form.cronExpression" placeholder="如 0 */5 * * * ?"
                class="w-full bg-slate-950 border border-slate-700 rounded p-1.5 text-xs text-white font-mono outline-none focus:border-sky-500" />
            </div>

            <template v-if="form.triggerType === 'OnChange'">
              <div>
                <label class="block text-[10px] font-bold text-slate-400 mb-1 uppercase tracking-wider">监听设备键</label>
                <select v-model="form.watchDeviceKey"
                  class="w-full bg-slate-950 border border-slate-700 rounded p-1.5 text-xs text-white font-mono outline-none focus:border-sky-500">
                  <option :value="null">-- 选择设备 --</option>
                  <option v-for="d in devices" :key="d.key" :value="d.key">{{ d.name }} ({{ d.key }})</option>
                </select>
              </div>
              <div>
                <label class="block text-[10px] font-bold text-slate-400 mb-1 uppercase tracking-wider">监听变量键</label>
                <select v-model="form.watchVariableKey"
                  class="w-full bg-slate-950 border border-slate-700 rounded p-1.5 text-xs text-white font-mono outline-none focus:border-sky-500">
                  <option :value="null">-- 选择变量 --</option>
                  <option v-for="k in deviceVariableOpts(form.watchDeviceKey || '')" :key="k" :value="k">{{ k }}</option>
                </select>
              </div>
              <div>
                <label class="block text-[10px] font-bold text-slate-400 mb-1 uppercase tracking-wider">死区阈值（空=任意变化）</label>
                <input v-model.number="form.deadBand" type="number" step="any" placeholder="≥0"
                  class="w-full bg-slate-950 border border-slate-700 rounded p-1.5 text-xs text-white font-mono outline-none focus:border-sky-500" />
              </div>
              <div>
                <label class="block text-[10px] font-bold text-slate-400 mb-1 uppercase tracking-wider">冷却时间（ms）</label>
                <input v-model.number="form.cooldownMs" type="number" min="100" max="60000"
                  class="w-full bg-slate-950 border border-slate-700 rounded p-1.5 text-xs text-white font-mono outline-none focus:border-sky-500" />
              </div>
            </template>

            <div>
              <label class="block text-[10px] font-bold text-slate-400 mb-1 uppercase tracking-wider">执行超时（ms）</label>
              <input v-model.number="form.timeoutMs" type="number" min="500" max="30000"
                class="w-full bg-slate-950 border border-slate-700 rounded p-1.5 text-xs text-white font-mono outline-none focus:border-sky-500" />
            </div>

            <div v-if="form.lastError" class="md:col-span-2 xl:col-span-1 flex items-center gap-1.5 text-amber-300 text-[10px] font-mono">
              <AlertTriangle class="w-3.5 h-3.5 shrink-0" />
              <span class="break-all">{{ form.lastError }}</span>
            </div>
          </div>

          <!-- 权限 scope 选择器 -->
          <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 px-4 py-4 border-b border-slate-800/60 bg-slate-800/20">
            <!-- 读授权（设备级） -->
            <div>
              <div class="flex items-center gap-1.5 text-[10px] font-bold text-slate-400 mb-1.5 uppercase tracking-wider">
                <ShieldCheck class="w-3.5 h-3.5" /> 读授权（设备级）
              </div>
              <div class="flex flex-wrap gap-1.5 border border-slate-700 rounded p-2 min-h-[34px]">
                <button v-for="d in devices" :key="d.key"
                  @click="toggleReadScope(d.key)"
                  class="text-[10px] px-2 py-0.5 rounded-full border font-bold cursor-pointer transition-colors"
                  :class="readSet.has(d.key) ? 'bg-emerald-500/20 text-emerald-300 border-emerald-600' : 'bg-slate-900 text-slate-400 border-slate-700 hover:border-slate-500'">
                  {{ d.key }}
                </button>
                <span v-if="devices.length === 0" class="text-[10px] text-slate-600">暂无设备</span>
              </div>
            </div>

            <!-- 写授权（变量级） -->
            <div>
              <div class="flex items-center gap-1.5 text-[10px] font-bold text-slate-400 mb-1.5 uppercase tracking-wider">
                <Shield class="w-3.5 h-3.5" /> 写授权（变量级）
              </div>
              <div class="flex items-center gap-1.5 mb-1.5">
                <select v-model="writeDeviceKey"
                  class="bg-slate-950 border border-slate-700 rounded p-1 text-[10px] text-white font-mono outline-none focus:border-sky-500 flex-1">
                  <option value="">-- 选择设备 --</option>
                  <option v-for="d in devices" :key="d.key" :value="d.key">{{ d.name }} ({{ d.key }})</option>
                </select>
              </div>
              <div class="flex flex-wrap gap-1.5 border border-slate-700 rounded p-2 min-h-[34px]">
                <button v-for="k in writeDeviceVars" :key="k"
                  @click="toggleWriteScope(k)"
                  class="text-[10px] px-2 py-0.5 rounded-full border font-bold cursor-pointer transition-colors"
                  :class="writeSet.has(`${writeDeviceKey}.${k}`) ? 'bg-amber-500/20 text-amber-300 border-amber-600' : 'bg-slate-900 text-slate-400 border-slate-700 hover:border-slate-500'">
                  {{ k }}
                </button>
                <span v-if="writeDeviceVars.length === 0" class="text-[10px] text-slate-600">先选设备查看可选变量</span>
              </div>
            </div>
          </div>

          <!-- Code editor -->
          <div class="flex-1 flex flex-col min-h-[260px] relative">
            <span class="absolute top-2.5 right-4 pointer-events-none select-none uppercase tracking-widest text-[#1890ff] font-bold text-[9px] font-mono">
              SCRIPT CODE (SERVER SANDBOX)
            </span>
            <div class="absolute left-0 top-0 bottom-0 w-11 bg-slate-950 text-slate-600 font-mono text-[10px] text-right pr-2 py-4 select-none border-r border-slate-800">
              <div v-for="n in 24" :key="n" class="leading-relaxed h-5">{{ n }}</div>
            </div>
            <textarea
              v-model="form.code"
              spellcheck="false"
              placeholder="// 输入控制代码...（run() 或 onChange(ev) 钩子）"
              class="flex-1 w-full bg-[#181824] pl-14 pr-4 py-4 font-mono text-[11.5px] leading-relaxed text-emerald-400 outline-none resize-none overflow-y-auto"
            />
          </div>

          <!-- Console + execution records -->
          <div class="border-t border-slate-950 dark:border-slate-800 bg-slate-950 text-left">
            <div class="bg-slate-900 border-b border-slate-950 px-4 py-1.5 flex items-center justify-between text-slate-400 font-mono text-[10px] select-none">
              <div class="flex items-center gap-1 text-[#1890ff] font-bold">
                <Terminal class="w-3.5 h-3.5" />
                <span>控制台输出</span>
              </div>
              <div class="flex items-center gap-3">
                <span>最近执行: {{ isNew ? '—' : fmtTime(form.lastExecutedAt) }} / {{ isNew ? '—' : fmtDuration(form.lastDurationMs) }}</span>
                <button v-if="!isNew" @click="loadRecords(1)" class="text-slate-500 hover:text-sky-400 cursor-pointer">刷新记录</button>
              </div>
            </div>

            <div class="h-44 overflow-y-auto p-4 space-y-1 font-mono text-[10.5px] text-slate-300 leading-relaxed break-all select-all">
              <div v-for="(line, idx) in consoleLines" :key="idx" class="whitespace-pre-wrap"
                :class="line.tone === 'ok' ? 'text-emerald-400' : line.tone === 'error' ? 'text-rose-400' : line.tone === 'warn' ? 'text-amber-300' : 'text-slate-300'">
                <span class="text-indigo-400 font-bold mr-1.5">>>></span>{{ line.text }}
              </div>
              <div v-if="consoleLines.length === 0" class="text-slate-600 text-center py-6">
                点击 "校验 / 试运行 / 运行" 查看输出；变量变化等自动触发会实时推送到此。
              </div>
            </div>

            <!-- 执行记录表 -->
            <div v-if="!isNew && records.length > 0" class="border-t border-slate-800/60">
              <div class="px-4 py-1.5 text-slate-400 font-mono text-[10px] flex items-center justify-between">
                <span>执行记录（共 {{ recordsTotal }} 条）</span>
                <div class="flex items-center gap-1.5">
                  <button :disabled="recordsPage <= 1" @click="loadRecords(recordsPage - 1)" class="text-slate-500 hover:text-sky-400 disabled:opacity-30 cursor-pointer">上一页</button>
                  <span class="text-slate-500">第 {{ recordsPage }} 页</span>
                  <button :disabled="recordsPage * recordsPageSize >= recordsTotal" @click="loadRecords(recordsPage + 1)" class="text-slate-500 hover:text-sky-400 disabled:opacity-30 cursor-pointer">下一页</button>
                </div>
              </div>
              <div class="overflow-x-auto">
                <table class="w-full text-left font-mono text-[10px] text-slate-300">
                  <thead>
                    <tr class="text-slate-500 border-b border-slate-800/60">
                      <th class="px-4 py-1.5 font-bold">时间</th>
                      <th class="px-2 py-1.5 font-bold">触发</th>
                      <th class="px-2 py-1.5 font-bold">结果</th>
                      <th class="px-2 py-1.5 font-bold">耗时</th>
                      <th class="px-2 py-1.5 font-bold">执行人</th>
                      <th class="px-4 py-1.5 font-bold">错误 / 输出</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="r in records" :key="r.id" class="border-b border-slate-800/40 align-top">
                      <td class="px-4 py-1.5 whitespace-nowrap text-slate-400">{{ fmtTime(r.startedAt) }}</td>
                      <td class="px-2 py-1.5">{{ r.triggerSource }}</td>
                      <td class="px-2 py-1.5">
                        <span class="px-1.5 py-0.5 rounded text-[9px] font-bold"
                          :class="r.result === 'Success' ? 'bg-emerald-500/20 text-emerald-300' : 'bg-rose-500/20 text-rose-300'">{{ r.result }}</span>
                      </td>
                      <td class="px-2 py-1.5">{{ fmtDuration(r.durationMs) }}</td>
                      <td class="px-2 py-1.5">{{ r.executedBy || '—' }}</td>
                      <td class="px-4 py-1.5 text-slate-400 max-w-[300px]">
                        <span v-if="r.error" class="text-rose-300 break-all block">{{ r.error }}</span>
                        <span v-if="r.output" class="break-all whitespace-pre-wrap">{{ r.output.slice(0, 300) }}</span>
                        <span v-if="!r.error && !r.output">—</span>
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>

        </div>

        <!-- Float Help Panel -->
        <div class="absolute top-14 right-4 bg-slate-950/90 border border-slate-800/80 rounded-lg p-3 text-left max-w-xs space-y-2 pointer-events-auto select-none mt-2 shadow-lg text-[9px] font-mono leading-relaxed text-slate-400">
          <b class="text-slate-200 block border-b border-slate-800 pb-1 text-[10px] font-sans">服务端 API（Jint 沙箱白名单）</b>
          <div class="space-y-1">
            <p><b class="text-yellow-400">read(devKey, varKey)</b>: 读取变量当前值。权限=读授权。</p>
            <p><b class="text-yellow-400">write(devKey, varKey, val)</b>: 写入变量，返回 true/false。权限=写授权。</p>
            <p><b class="text-yellow-400">getQuality(devKey, varKey)</b>: 读取变量质量（Good/Bad/…）。</p>
            <p><b class="text-yellow-400">log(...)</b>: 输出到控制台与执行记录。</p>
          </div>
        </div>

      </div>
    </div>
  </div>
</template>