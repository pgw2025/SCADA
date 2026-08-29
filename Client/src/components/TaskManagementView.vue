<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { systemScripts, dataModels, devices, addLog } from '../store/index';
import {
  Calendar, Plus, Trash2, Play, ToggleLeft, ToggleRight,
  Database, FileCode, X, CheckCircle2, Loader2, History,
  Clock, Pencil, AlertTriangle, SkipForward, Moon
} from 'lucide-vue-next';
import { ScheduledTask, ScheduledTaskRunResult } from '../types';
import {
  fetchScheduledTasks, createScheduledTask, updateScheduledTask,
  deleteScheduledTask, executeScheduledTask
} from '../api/taskApi';
import { fetchDataModelsFromBackend } from '../api/modelApi';
import { syncDevices } from '../services/deviceService';
import { loadSystemScripts } from '../services/scriptService';
import { extractApiError } from '../api/http';

type TaskType = ScheduledTask['type'];

// ===== 列表状态（后端轮询） =====
const tasks = ref<ScheduledTask[]>([]);
const loading = ref(false);
let pollHandle: ReturnType<typeof setInterval> | null = null;

// 手动执行结果横幅
const lastRunResult = ref<ScheduledTaskRunResult & { taskName?: string } | null>(null);

const refreshTasks = async () => {
  try {
    const { data } = await fetchScheduledTasks();
    tasks.value = data ?? [];
  } catch (err: any) {
    addLog('任务调度', `同步任务列表失败: ${extractApiError(err)}`, 'warning');
  }
};

onMounted(async () => {
  loading.value = true;
  // 依赖数据：设备（变量写入）、模型（变量下拉）、脚本（脚本执行）
  await Promise.all([
    refreshTasks(),
    devices.value.length ? Promise.resolve() : syncDevices(),
    dataModels.value.length ? Promise.resolve() : fetchDataModelsFromBackend(),
    systemScripts.value.length ? Promise.resolve() : loadSystemScripts()
  ]);
  loading.value = false;
  // D3-A：前端 5 秒轮询执行状态/下次触发时间
  pollHandle = setInterval(refreshTasks, 5000);
});

onUnmounted(() => {
  if (pollHandle) clearInterval(pollHandle);
});

// ===== 新建 / 编辑弹窗 =====
const showModal = ref(false);
const editingId = ref<number | null>(null); // null = 新建
const saving = ref(false);
const form = ref({
  name: '',
  type: 'backup' as TaskType,
  cronExpression: '0 2 * * *',
  active: true,
  // set_value 参数
  deviceId: null as number | null,
  variableKey: '',
  newValue: 0,
  // execute_script 参数
  scriptId: null as number | null,
  // clear_history 参数
  retentionDays: 30
});

const isEdit = computed(() => editingId.value != null);

// Cron 快捷预设（真实 Cron：5 段分钟级 / 6 段秒级）
const cronPresets = [
  { label: '每 5 秒', value: '*/5 * * * * *' },
  { label: '每分钟', value: '0 * * * *' },
  { label: '每 15 分钟', value: '*/15 * * * *' },
  { label: '每天 02:00', value: '0 2 * * *' },
  { label: '每周日 00:00', value: '0 0 * * 0' }
];

// 选中设备对应模型下的变量（变量唯一身份 = DeviceKey + VariableKey）
const deviceVariables = computed(() => {
  const dev = devices.value.find(d => d.id === form.value.deviceId);
  if (!dev) return [];
  const model = dataModels.value.find(m => String(m.id) === String(dev.modelId));
  return model?.variables ?? [];
});

const openCreate = () => {
  editingId.value = null;
  form.value = {
    name: '', type: 'backup', cronExpression: '0 2 * * *', active: true,
    deviceId: null, variableKey: '', newValue: 0,
    scriptId: systemScripts.value.length ? systemScripts.value[0].id : null,
    retentionDays: 30
  };
  showModal.value = true;
};

const openEdit = (task: ScheduledTask) => {
  let params: any = {};
  try { params = task.paramsJson ? JSON.parse(task.paramsJson) : {}; } catch { /* 忽略坏 JSON */ }
  editingId.value = task.id;
  form.value = {
    name: task.name,
    type: task.type,
    cronExpression: task.cronExpression,
    active: task.active,
    deviceId: params.deviceId ?? null,
    variableKey: params.variableKey ?? '',
    newValue: typeof params.newValue === 'boolean' ? (params.newValue ? 1 : 0) : Number(params.newValue ?? 0),
    scriptId: params.scriptId ?? null,
    retentionDays: Number(params.retentionDays ?? 30)
  };
  showModal.value = true;
};

// 按任务类型组装 paramsJson（与后端 ValidateAndNormalize 对齐）
const buildParamsJson = (): string => {
  switch (form.value.type) {
    case 'set_value':
      return JSON.stringify({
        deviceId: form.value.deviceId,
        variableKey: form.value.variableKey,
        newValue: form.value.newValue
      });
    case 'execute_script':
      return JSON.stringify({ scriptId: form.value.scriptId });
    case 'clear_history':
      return JSON.stringify({ retentionDays: form.value.retentionDays });
    default:
      return '{}';
  }
};

const handleSave = async () => {
  if (!form.value.name.trim()) { alert('请填写任务名称！'); return; }
  if (!form.value.cronExpression.trim()) { alert('请填写 Cron 表达式！'); return; }
  if (form.value.type === 'set_value') {
    if (form.value.deviceId == null) { alert('变量写入任务必须选择目标设备！'); return; }
    if (!form.value.variableKey) { alert('变量写入任务必须选择目标变量！'); return; }
  }
  if (form.value.type === 'execute_script' && form.value.scriptId == null) {
    alert('脚本执行任务必须选择目标脚本！'); return;
  }
  if (form.value.type === 'clear_history' && (!form.value.retentionDays || form.value.retentionDays < 1)) {
    alert('历史清理任务的保留天数必须 ≥ 1！'); return;
  }

  const dto: ScheduledTask = {
    id: editingId.value ?? 0,
    name: form.value.name.trim(),
    type: form.value.type,
    cronExpression: form.value.cronExpression.trim(),
    paramsJson: buildParamsJson(),
    active: form.value.active
  } as ScheduledTask;

  saving.value = true;
  try {
    if (isEdit.value) {
      await updateScheduledTask(dto);
      addLog('任务调度', `已更新任务: [${dto.name}]`, 'normal');
    } else {
      await createScheduledTask(dto);
      addLog('任务调度', `已创建任务: [${dto.name}]`, 'normal');
    }
    showModal.value = false;
    await refreshTasks();
  } catch (err: any) {
    alert(`保存失败: ${extractApiError(err)}`);
  } finally {
    saving.value = false;
  }
};

// ===== 列表操作 =====
const handleDelete = async (task: ScheduledTask) => {
  if (!confirm(`确定要删除任务 [${task.name}] 吗？`)) return;
  try {
    await deleteScheduledTask(task.id);
    addLog('任务调度', `已删除任务: [${task.name}]`, 'warning');
    await refreshTasks();
  } catch (err: any) {
    alert(`删除失败: ${extractApiError(err)}`);
  }
};

const executingId = ref<number | null>(null);
const handleExecute = async (task: ScheduledTask) => {
  executingId.value = task.id;
  try {
    const { data } = await executeScheduledTask(task.id);
    lastRunResult.value = { ...(data as ScheduledTaskRunResult), taskName: task.name };
    addLog('任务调度',
      `手动执行 [${task.name}]: ${data?.status === 'Success' ? '成功' : data?.status === 'Skipped' ? '跳过（上一次仍在执行）' : '失败'}${data?.error ? ` - ${data.error}` : ''}`,
      data?.status === 'Success' ? 'normal' : 'warning');
    await refreshTasks();
  } catch (err: any) {
    alert(`执行失败: ${extractApiError(err)}`);
  } finally {
    executingId.value = null;
  }
};

const togglingId = ref<number | null>(null);
const handleToggleActive = async (task: ScheduledTask) => {
  togglingId.value = task.id;
  try {
    await updateScheduledTask({ ...task, active: !task.active });
    addLog('任务调度', `任务 [${task.name}] 已${task.active ? '停用' : '启用'}`, task.active ? 'warning' : 'info');
    await refreshTasks();
  } catch (err: any) {
    alert(`操作失败: ${extractApiError(err)}`);
  } finally {
    togglingId.value = null;
  }
};

// ===== 展示辅助 =====
const statusMeta: Record<string, { label: string; cls: string }> = {
  Idle: { label: '空闲', cls: 'text-slate-500 dark:text-slate-400' },
  Running: { label: '执行中', cls: 'text-indigo-600 dark:text-indigo-400' },
  Success: { label: '成功', cls: 'text-emerald-600 dark:text-emerald-400' },
  Failed: { label: '失败', cls: 'text-rose-600 dark:text-rose-400' },
  Skipped: { label: '跳过', cls: 'text-amber-600 dark:text-amber-400' }
};

const fmtTime = (iso?: string | null) => {
  if (!iso) return '从未执行';
  const d = new Date(iso);
  return isNaN(d.getTime()) ? String(iso) : d.toLocaleString();
};

const fmtDuration = (ms?: number | null) => {
  if (ms == null) return '';
  return ms >= 1000 ? `${(ms / 1000).toFixed(1)}s` : `${ms}ms`;
};

const typeLabel = (t: TaskType) =>
  t === 'backup' ? '数据备份' : t === 'set_value' ? '变量写入' : t === 'execute_script' ? '脚本执行' : '历史清理';

const paramsText = (task: ScheduledTask) => {
  let params: any = {};
  try { params = task.paramsJson ? JSON.parse(task.paramsJson) : {}; } catch { return task.paramsJson; }
  switch (task.type) {
    case 'set_value':
      return `设备ID ${params.deviceId} · 变量 ${params.variableKey} → 值 ${params.newValue}`;
    case 'execute_script': {
      const scr = systemScripts.value.find(s => s.id === params.scriptId);
      return `脚本: ${scr ? scr.name : `ID ${params.scriptId}`}`;
    }
    case 'clear_history':
      return `保留 ${params.retentionDays} 天内数据，超出部分自动清理`;
    default:
      return '导出系统配置（MySQL）与时序数据（InfluxDB）到备份压缩包';
  }
};

const triggerTypeLabel = (t: string) =>
  t === 'Periodic' ? '周期' : t === 'Schedule' ? '定时' : t === 'OnChange' ? '变量变化' : '手动';
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">

    <!-- Top info row -->
    <div class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Calendar class="w-5 h-5 text-indigo-500 dark:text-indigo-400" />
          任务调度管理
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          配置 Cron 定时执行的自动化任务，支持数据备份、变量写入、脚本执行和历史清理。状态每 5 秒自动刷新。
        </p>
      </div>

      <button
        @click="openCreate"
        class="font-bold text-xs bg-indigo-600 dark:bg-indigo-500 text-white hover:bg-indigo-700 dark:hover:bg-indigo-600 px-4 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-sm active:translate-y-0.5"
      >
        <Plus class="w-4 h-4" />
        新建任务
      </button>
    </div>

    <!-- 手动执行结果横幅 -->
    <div v-if="lastRunResult" class="px-6 pt-4 shrink-0">
      <div
        class="rounded-lg border p-3 text-xs flex items-start justify-between gap-3"
        :class="lastRunResult.status === 'Success'
          ? 'bg-emerald-50 dark:bg-emerald-950/40 border-emerald-200 dark:border-emerald-900 text-emerald-700 dark:text-emerald-400'
          : lastRunResult.status === 'Skipped'
            ? 'bg-amber-50 dark:bg-amber-950/40 border-amber-200 dark:border-amber-900 text-amber-700 dark:text-amber-400'
            : 'bg-rose-50 dark:bg-rose-950/40 border-rose-200 dark:border-rose-900 text-rose-700 dark:text-rose-400'"
      >
        <div class="flex items-start gap-2">
          <CheckCircle2 v-if="lastRunResult.status === 'Success'" class="w-4 h-4 shrink-0 mt-0.5" />
          <SkipForward v-else-if="lastRunResult.status === 'Skipped'" class="w-4 h-4 shrink-0 mt-0.5" />
          <AlertTriangle v-else class="w-4 h-4 shrink-0 mt-0.5" />
          <div>
            <b>[{{ lastRunResult.taskName }}]</b> 手动执行{{ lastRunResult.status === 'Success' ? '成功' : lastRunResult.status === 'Skipped' ? '跳过（上一次仍在执行）' : '失败' }}
            （耗时 {{ fmtDuration(lastRunResult.durationMs) }}）
            <div v-if="lastRunResult.output" class="mt-1 font-mono break-all opacity-80">{{ lastRunResult.output }}</div>
            <div v-if="lastRunResult.error" class="mt-1 font-mono break-all opacity-80">错误: {{ lastRunResult.error }}</div>
          </div>
        </div>
        <button @click="lastRunResult = null" class="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer shrink-0">
          <X class="w-4 h-4" />
        </button>
      </div>
    </div>

    <!-- Task grid list -->
    <div class="flex-1 p-6 overflow-y-auto space-y-4 text-left">
      <div v-if="loading" class="flex items-center justify-center text-slate-400 gap-2 text-xs py-16">
        <Loader2 class="w-4 h-4 animate-spin" /> 正在加载任务列表...
      </div>

      <div v-else-if="tasks.length === 0" class="flex flex-col items-center justify-center text-slate-400 gap-3 py-16 text-xs">
        <Calendar class="w-10 h-10 opacity-40" />
        暂无调度任务，点击右上角「新建任务」创建。
      </div>

      <div v-else class="grid grid-cols-1 xl:grid-cols-2 gap-4">

        <div
          v-for="task in tasks"
          :key="task.id"
          class="bg-white dark:bg-slate-900 border rounded-xl p-5 shadow-xs flex flex-col justify-between gap-4 transition-all hover:shadow-md"
          :class="task.active ? 'border-slate-200/95 dark:border-slate-800' : 'border-slate-200/40 dark:border-slate-800/40 opacity-70 bg-slate-50/50 dark:bg-slate-900/50'"
        >
          <!-- Task Header -->
          <div class="flex items-start justify-between gap-4">
            <div class="flex items-start gap-3">
              <div
                class="w-10 h-10 rounded-lg flex items-center justify-center shrink-0"
                :class="{
                  'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400': task.type === 'set_value',
                  'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400': task.type === 'backup',
                  'bg-indigo-50 dark:bg-indigo-950/60 text-indigo-600 dark:text-indigo-400': task.type === 'execute_script',
                  'bg-rose-50 dark:bg-rose-950/60 text-rose-600 dark:text-rose-400': task.type === 'clear_history'
                }"
              >
                <Clock v-if="task.type === 'set_value'" class="w-5 h-5" />
                <Database v-else-if="task.type === 'backup'" class="w-5 h-5" />
                <FileCode v-else-if="task.type === 'execute_script'" class="w-5 h-5" />
                <History v-else class="w-5 h-5" />
              </div>

              <div>
                <h3 class="font-bold text-xs text-slate-900 dark:text-white leading-tight">{{ task.name }}</h3>
                <div class="flex items-center gap-2 mt-1.5 flex-wrap">
                  <span class="inline-flex items-center gap-1 bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 text-[10px] font-bold font-mono px-2 py-0.5 rounded-md" :title="task.cronExpression">
                    <Clock class="w-3 h-3" />
                    {{ task.cronExpression }}
                  </span>

                  <span class="text-[9px] uppercase tracking-wider font-bold px-1.5 py-0.5 rounded border"
                    :class="{
                      'border-sky-200 dark:border-sky-800 text-sky-600 dark:text-sky-400 bg-sky-50/20 dark:bg-sky-950/20': task.type === 'set_value',
                      'border-emerald-200 dark:border-emerald-800 text-emerald-600 dark:text-emerald-400 bg-emerald-50/20 dark:bg-emerald-950/20': task.type === 'backup',
                      'border-indigo-200 dark:border-indigo-800 text-indigo-600 dark:text-indigo-400 bg-indigo-50/20 dark:bg-indigo-950/20': task.type === 'execute_script',
                      'border-rose-200 dark:border-rose-800 text-rose-600 dark:text-rose-400 bg-rose-50/20 dark:bg-rose-950/20': task.type === 'clear_history'
                    }"
                  >
                    {{ typeLabel(task.type) }}
                  </span>
                </div>
              </div>
            </div>

            <!-- Enable Toggle Button -->
            <button
              @click="handleToggleActive(task)"
              :disabled="togglingId === task.id"
              class="focus:outline-none cursor-pointer transition-transform active:scale-95 text-slate-500 disabled:opacity-50"
              :title="task.active ? '点击停用' : '点击启用'"
            >
              <Loader2 v-if="togglingId === task.id" class="w-8 h-8 text-slate-400 animate-spin" />
              <ToggleRight v-else-if="task.active" class="w-8 h-8 text-emerald-500" />
              <ToggleLeft v-else class="w-8 h-8 text-slate-300 dark:text-slate-600" />
            </button>
          </div>

          <!-- Parameter values print -->
          <div class="text-[11px] bg-slate-50 dark:bg-slate-950 border border-slate-100 dark:border-slate-800 p-2.5 rounded-lg text-slate-600 dark:text-slate-300">
            <span class="font-bold block mb-1 text-slate-400 dark:text-slate-500">任务配置:</span>
            <div class="font-mono break-all">{{ paramsText(task) }}</div>
          </div>

          <!-- Footer run actions -->
          <div class="flex items-center justify-between border-t border-slate-100 dark:border-slate-800 pt-3 text-[10px] gap-2">
            <div class="text-slate-400 dark:text-slate-500 font-mono flex flex-col gap-0.5 min-w-0">
              <span class="flex items-center gap-1">
                <Clock class="w-3 h-3" />
                <span>上次:</span>
                <span class="font-bold text-slate-600 dark:text-slate-300">{{ fmtTime(task.lastRunAt) }}</span>
                <span v-if="task.lastDurationMs != null" class="text-slate-400">({{ fmtDuration(task.lastDurationMs) }})</span>
              </span>
              <span class="flex items-center gap-1">
                <Moon class="w-3 h-3" />
                <span>下次:</span>
                <span class="font-bold text-slate-600 dark:text-slate-300">{{ task.active ? fmtTime(task.nextRunAt) : '已停用' }}</span>
              </span>
            </div>

            <div class="flex items-center gap-2 shrink-0">
              <!-- Execution status badge -->
              <div
                v-if="task.lastStatus && statusMeta[task.lastStatus]"
                class="inline-flex items-center gap-1 font-bold font-sans"
                :class="statusMeta[task.lastStatus].cls"
                :title="task.lastError || ''"
              >
                <Loader2 v-if="task.lastStatus === 'Running'" class="w-3.5 h-3.5 animate-spin" />
                <CheckCircle2 v-else-if="task.lastStatus === 'Success'" class="w-3.5 h-3.5" />
                <AlertTriangle v-else-if="task.lastStatus === 'Failed'" class="w-3.5 h-3.5" />
                <SkipForward v-else-if="task.lastStatus === 'Skipped'" class="w-3.5 h-3.5" />
                <span>{{ statusMeta[task.lastStatus].label }}</span>
              </div>

              <!-- Manual bypass trigger -->
              <button
                @click="handleExecute(task)"
                :disabled="executingId === task.id || task.lastStatus === 'Running'"
                class="px-2.5 py-1 text-[10px] font-bold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 bg-white dark:bg-slate-900 text-slate-700 dark:text-slate-300 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all disabled:opacity-45 disabled:cursor-not-allowed select-none"
              >
                <Loader2 v-if="executingId === task.id" class="w-3 h-3 animate-spin" />
                <Play v-else class="w-3 h-3 text-slate-500 dark:text-slate-400" />
                立即执行
              </button>

              <!-- Edit -->
              <button
                @click="openEdit(task)"
                class="p-1 hover:bg-sky-50 dark:hover:bg-sky-950/40 text-slate-400 hover:text-sky-500 rounded-lg cursor-pointer transition-colors"
                title="编辑任务"
              >
                <Pencil class="w-3.5 h-3.5" />
              </button>

              <!-- Deletion -->
              <button
                @click="handleDelete(task)"
                class="p-1 hover:bg-rose-50 dark:hover:bg-rose-950/40 text-slate-400 hover:text-rose-500 rounded-lg cursor-pointer transition-colors"
                title="删除定时任务"
              >
                <Trash2 class="w-3.5 h-3.5" />
              </button>
            </div>
          </div>

          <!-- Last error detail -->
          <div v-if="task.lastStatus === 'Failed' && task.lastError"
            class="text-[10px] bg-rose-50 dark:bg-rose-950/30 border border-rose-100 dark:border-rose-900/40 text-rose-600 dark:text-rose-400 rounded-lg p-2 font-mono break-all">
            {{ task.lastError }}
          </div>

        </div>

      </div>
    </div>

    <!-- CREATE / EDIT DIALOG MODAL -->
    <div v-if="showModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150 max-h-[90vh] flex flex-col">
        <!-- Header -->
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800 shrink-0">
          <div class="flex items-center gap-2 font-bold text-xs uppercase tracking-widest text-indigo-400">
            <Clock class="w-4 h-4" />
            <span>{{ isEdit ? '编辑调度任务' : '新建调度任务' }}</span>
          </div>
          <button @click="showModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4.5 h-4.5" /></button>
        </div>

        <!-- Content -->
        <div class="p-5 space-y-4 text-xs font-sans overflow-y-auto">

          <!-- Task Name -->
          <div>
            <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">任务名称</label>
            <input
              v-model="form.name"
              type="text"
              placeholder="如: 每日数据备份"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white outline-none focus:border-[#1890ff]"
            />
          </div>

          <!-- Cron expression + type -->
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">任务类型</label>
              <select
                v-model="form.type"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white focus:outline-none font-sans font-bold"
              >
                <option value="backup">数据备份</option>
                <option value="set_value">变量写入</option>
                <option value="execute_script">脚本执行</option>
                <option value="clear_history">历史清理</option>
              </select>
            </div>
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">启用状态</label>
              <select
                v-model="form.active"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white focus:outline-none font-sans font-bold"
              >
                <option :value="true">启用</option>
                <option :value="false">停用</option>
              </select>
            </div>
          </div>

          <!-- Cron expression -->
          <div>
            <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">
              Cron 表达式
              <span class="font-normal text-slate-400">（5 段分钟级或 6 段秒级，如 <code class="font-mono">0 2 * * *</code> / <code class="font-mono">*/5 * * * * *</code>）</span>
            </label>
            <input
              v-model="form.cronExpression"
              type="text"
              placeholder="0 2 * * *"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono text-slate-800 dark:text-white outline-none focus:border-[#1890ff]"
            />
            <div class="flex flex-wrap gap-1.5 mt-2">
              <button
                v-for="p in cronPresets"
                :key="p.value"
                @click="form.cronExpression = p.value"
                class="px-2 py-0.5 text-[10px] font-bold rounded-md border cursor-pointer transition-colors"
                :class="form.cronExpression === p.value
                  ? 'bg-indigo-600 text-white border-indigo-600'
                  : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:border-indigo-400'"
              >
                {{ p.label }}
              </button>
            </div>
          </div>

          <!-- Conditional param fields based on type selected -->
          <div v-if="form.type === 'set_value'" class="bg-slate-50 dark:bg-slate-950 rounded-lg p-3 border border-slate-150 dark:border-slate-800 space-y-2">
            <h4 class="font-bold text-slate-600 dark:text-slate-300 block mb-1">变量配置</h4>
            <div class="space-y-2">
              <div>
                <label class="text-[10px] text-slate-400 dark:text-slate-500 block mb-0.5">目标设备 *</label>
                <select
                  v-model="form.deviceId"
                  class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-slate-800 dark:text-white p-1.5 rounded font-mono"
                >
                  <option :value="null">-- 请选择设备（必填）--</option>
                  <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }} ({{ d.key }})</option>
                </select>
              </div>
              <div class="grid grid-cols-2 gap-2">
                <div>
                  <label class="text-[10px] text-slate-400 dark:text-slate-500 block mb-0.5">目标变量 *</label>
                  <select
                    v-model="form.variableKey"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-slate-800 dark:text-white p-1.5 rounded font-mono"
                    :disabled="form.deviceId == null"
                  >
                    <option value="">{{ form.deviceId == null ? '请先选择设备' : '-- 请选择变量 --' }}</option>
                    <option v-for="v in deviceVariables" :key="v.key" :value="v.key">{{ v.key }}（{{ v.name }}）</option>
                  </select>
                </div>
                <div>
                  <label class="text-[10px] text-slate-400 dark:text-slate-500 block mb-0.5">写入值 *</label>
                  <input
                    v-model.number="form.newValue"
                    type="number"
                    step="any"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-slate-800 dark:text-white p-1.5 rounded font-bold"
                  />
                </div>
              </div>
            </div>
          </div>

          <div v-else-if="form.type === 'execute_script'" class="bg-indigo-50/50 dark:bg-indigo-950/40 rounded-lg p-3 border border-indigo-100 dark:border-indigo-900/40">
            <label class="font-bold text-indigo-700 dark:text-indigo-400 block mb-1">选择脚本</label>
            <select
              v-model="form.scriptId"
              class="w-full bg-white dark:bg-slate-900 border border-indigo-200 dark:border-indigo-800 text-slate-800 dark:text-white p-2 rounded-lg font-bold outline-none"
            >
              <option :value="null">-- 请选择脚本（必填）--</option>
              <option v-for="scr in systemScripts" :key="scr.id" :value="scr.id">
                {{ scr.name }} ({{ triggerTypeLabel(scr.triggerType) }})
              </option>
            </select>
            <p v-if="systemScripts.length === 0" class="text-[10px] text-slate-400 mt-1.5">暂无可用脚本，请先在「系统脚本」中创建。</p>
          </div>

          <div v-else-if="form.type === 'clear_history'" class="bg-rose-50/50 dark:bg-rose-950/40 rounded-lg p-3 border border-rose-100 dark:border-rose-900/40 flex items-center justify-between">
            <span class="font-bold text-rose-800 dark:text-rose-300">数据保留期限</span>
            <div class="flex items-center gap-1 text-slate-700 dark:text-slate-300 font-bold">
              <span>保留</span>
              <input
                v-model.number="form.retentionDays"
                type="number"
                min="1"
                class="w-14 bg-white dark:bg-slate-900 border border-rose-200 dark:border-rose-800 text-slate-800 dark:text-white rounded p-1 text-center font-mono font-bold"
              />
              <span>天</span>
            </div>
          </div>

          <div v-else-if="form.type === 'backup'" class="text-[11px] leading-relaxed text-emerald-700 dark:text-emerald-400 bg-emerald-50/50 dark:bg-emerald-950/30 border border-emerald-100 dark:border-emerald-900/40 rounded-lg p-3">
            <b>备份内容：</b>MySQL 业务数据全量导出 + InfluxDB 时序历史（variable_history），打包为 zip 存储到服务器备份目录。
          </div>

        </div>

        <!-- Footer -->
        <div class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800 shrink-0">
          <button
            @click="showModal = false"
            class="px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button
            @click="handleSave"
            :disabled="saving"
            class="px-4 py-1.5 rounded-lg bg-slate-900 dark:bg-indigo-600 hover:bg-slate-800 dark:hover:bg-indigo-500 font-bold text-xs text-white cursor-pointer disabled:opacity-60 inline-flex items-center gap-1.5"
          >
            <Loader2 v-if="saving" class="w-3.5 h-3.5 animate-spin" />
            {{ isEdit ? '保存修改' : '创建任务' }}
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
