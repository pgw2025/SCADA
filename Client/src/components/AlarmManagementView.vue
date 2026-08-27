<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue';
import {
  ShieldAlert,
  Plus,
  Pencil,
  Trash2,
  Power,
  Search,
  RefreshCw,
  Bell,
  Check,
  ChevronLeft,
  ChevronRight,
  X,
  ListChecks
} from 'lucide-vue-next';
import { systemConfig, addLog } from '../store/index';
import { devices } from '../store/deviceStore';
import { syncDevices } from '../services/deviceService';
import {
  activeAlarms, unackedCount, recentEvents, clearRecentEvents, refreshActiveAlarms
} from '../store/alarmStore';
import {
  AlarmRule, AlarmLevel, TriggerCondition, AlarmSource,
  AlarmRecord, AlarmRecordQuery
} from '../types';
import {
  fetchAlarmRules, createAlarmRule, updateAlarmRule, deleteAlarmRule, toggleAlarmRule,
  fetchAlarmRecords, ackAlarmRecord
} from '../api/alarmApi';
import { extractApiError } from '../api/http';
import { showToast } from '../services/toastService';

type TabKey = 'rules' | 'records';
const activeTab = ref<TabKey>('rules');

// ================= 展示映射 =================
const LEVEL_OPTS: { value: AlarmLevel; label: string }[] = [
  { value: 'Low', label: '低' },
  { value: 'Medium', label: '中' },
  { value: 'High', label: '高' },
  { value: 'Critical', label: '紧急' }
];
const CONDITION_LABEL: Record<TriggerCondition, string> = {
  GreaterThan: '大于', GreaterOrEqual: '大于等于',
  LessThan: '小于', LessOrEqual: '小于等于',
  EqualTo: '等于', NotEqualTo: '不等于'
};
const SOURCE_LABEL: Record<AlarmSource, string> = {
  Rule: '规则', MinMaxLimit: '上下限', System: '系统'
};
const levelBadge = (lv: AlarmLevel) => {
  switch (lv) {
    case 'Critical': return 'bg-rose-100 text-rose-700 dark:bg-rose-900/50 dark:text-rose-300 border-rose-200 dark:border-rose-800';
    case 'High': return 'bg-red-100 text-red-700 dark:bg-red-900/50 dark:text-red-300 border-red-200 dark:border-red-800';
    case 'Medium': return 'bg-amber-100 text-amber-700 dark:bg-amber-900/50 dark:text-amber-300 border-amber-200 dark:border-amber-800';
    default: return 'bg-sky-100 text-sky-700 dark:bg-sky-900/40 dark:text-sky-300 border-sky-200 dark:border-sky-800';
  }
};
const fmtTime = (ts?: string | null) => {
  if (!ts) return '';
  const d = new Date(ts);
  if (isNaN(d.getTime())) return ts;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
};
const deviceName = (id: number | null | undefined) => {
  if (id == null) return '-';
  return devices.value.find(d => d.id === id)?.name ?? `#${id}`;
};

// ================= 规则配置 =================
const rules = ref<AlarmRule[]>([]);
const rulesLoading = ref(false);

const loadRules = async () => {
  if (systemConfig.value.isSimulationActive) { rules.value = []; return; }
  rulesLoading.value = true;
  try {
    const { data } = await fetchAlarmRules();
    rules.value = data ?? [];
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  } finally {
    rulesLoading.value = false;
  }
};

// 规则表单（新增/编辑共用）
const showRuleModal = ref(false);
const editingRuleId = ref<number | null>(null);
const ruleForm = ref<AlarmRule>({
  id: 0, name: '', deviceId: 0, variableKey: '', condition: 'GreaterThan',
  threshold: 0, level: 'Medium', active: true, message: '', debounceSeconds: 0
});

const deviceOptions = computed(() => devices.value.map(d => ({ id: d.id, name: d.name, key: d.key })));

const variableOptions = computed<{ key: string; name: string }[]>(() => {
  const dev = devices.value.find(d => d.id === ruleForm.value.deviceId);
  if (!dev) return [];
  const keys = Object.keys(dev.variables ?? {});
  if (keys.length === 0 && dev.variableMeta) return Object.keys(dev.variableMeta).map(k => ({ key: k, name: dev.variableMeta![k]?.name || k }));
  return keys.map(k => ({ key: k, name: dev.variableMeta?.[k]?.name || k }));
});

const openCreate = () => {
  editingRuleId.value = null;
  ruleForm.value = {
    id: 0, name: '', deviceId: deviceOptions.value[0]?.id || 0, variableKey: '',
    condition: 'GreaterThan', threshold: 0, level: 'Medium', active: true,
    message: '', debounceSeconds: 0
  };
  showRuleModal.value = true;
};
const openEdit = (r: AlarmRule) => {
  editingRuleId.value = r.id;
  ruleForm.value = { ...r };
  showRuleModal.value = true;
};

const saveRule = async () => {
  const f = ruleForm.value;
  if (!f.name.trim()) { showToast('请输入规则名称', 'warning'); return; }
  if (!f.deviceId) { showToast('请选择设备', 'warning'); return; }
  if (!f.variableKey.trim()) { showToast('请选择变量', 'warning'); return; }
  try {
    if (editingRuleId.value != null) {
      await updateAlarmRule(editingRuleId.value, { ...f, id: editingRuleId.value });
      addLog('报警管理', `已更新报警规则: ${f.name}`, 'normal');
    } else {
      const { data } = await createAlarmRule(f as any);
      f.id = data?.id ?? 0;
      addLog('报警管理', `已新建报警规则: ${f.name}`, 'normal');
    }
    showRuleModal.value = false;
    showToast('保存成功', 'success');
    loadRules();
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  }
};

const toggleActive = async (r: AlarmRule) => {
  try {
    await toggleAlarmRule(r.id, !r.active);
    r.active = !r.active;
    addLog('报警管理', `报警规则「${r.name}」${r.active ? '启用' : '停用'}`, 'normal');
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  }
};

const removeRule = async (r: AlarmRule) => {
  if (!confirm(`确定删除报警规则「${r.name}」吗？`)) return;
  try {
    await deleteAlarmRule(r.id);
    rules.value = rules.value.filter(x => x.id !== r.id);
    addLog('报警管理', `已删除报警规则: ${r.name}`, 'warning');
    showToast('已删除', 'success');
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  }
};

// ================= 报警记录 =================
const records = ref<AlarmRecord[]>([]);
const total = ref(0);
const pageIndex = ref(1);
const pageSize = ref(50);
const recordsLoading = ref(false);

const filterDeviceId = ref<number | null>(null);
const filterLevel = ref<AlarmLevel | null>(null);
const filterUnacked = ref<boolean | null>(null);
const filterUnrecovered = ref<boolean | null>(null);
const startDate = ref<string>('');
const endDate = ref<string>('');

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)));

const buildQuery = (): AlarmRecordQuery => {
  const q: AlarmRecordQuery = { pageIndex: pageIndex.value, pageSize: pageSize.value };
  if (filterDeviceId.value != null) q.deviceId = filterDeviceId.value;
  if (filterLevel.value) q.level = filterLevel.value;
  if (filterUnacked.value != null) q.unacked = filterUnacked.value;
  if (filterUnrecovered.value != null) q.unrecovered = filterUnrecovered.value;
  if (startDate.value) q.startTime = `${startDate.value}T00:00:00`;
  if (endDate.value) q.endTime = `${endDate.value}T23:59:59`;
  return q;
};

const loadRecords = async () => {
  if (systemConfig.value.isSimulationActive) { records.value = []; total.value = 0; return; }
  recordsLoading.value = true;
  try {
    const { data } = await fetchAlarmRecords(buildQuery());
    records.value = data?.items ?? [];
    total.value = data?.total ?? 0;
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  } finally {
    recordsLoading.value = false;
  }
};

const applyRecordQuery = () => {
  pageIndex.value = 1;
  loadRecords();
};
const changePage = (delta: number) => {
  const next = pageIndex.value + delta;
  if (next < 1 || next > totalPages.value) return;
  pageIndex.value = next;
  loadRecords();
};
const resetRecordFilters = () => {
  filterDeviceId.value = null; filterLevel.value = null;
  filterUnacked.value = null; filterUnrecovered.value = null;
  startDate.value = ''; endDate.value = '';
  applyRecordQuery();
};

const doAck = async (r: AlarmRecord) => {
  if (r.acked) return;
  try {
    await ackAlarmRecord(r.id);
    r.acked = true;
    r.ackedAt = new Date().toISOString();
    showToast('已确认', 'success');
    refreshActiveAlarms();
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  }
};

// ===== 初始化 / 清理 =====
let refreshTimer: number | null = null;
onMounted(async () => {
  if (!systemConfig.value.isSimulationActive) {
    await syncDevices();
  }
  loadRules();
  loadRecords();
  refreshActiveAlarms();
  // 记录页定时刷新未确认角标计数（SignalR 实时更新为主，轮询兜底）
  refreshTimer = window.setInterval(() => refreshActiveAlarms(), 60000);
});
onBeforeUnmount(() => {
  if (refreshTimer != null) window.clearInterval(refreshTimer);
});
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">
    <!-- Header -->
    <div class="bg-white dark:bg-slate-900 p-4 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-3 transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <ShieldAlert class="w-4 h-4 text-slate-600 dark:text-slate-300" />
          报警管理
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          报警规则配置与报警记录查询，支持实时事件推送、未确认与未恢复过滤、单条确认。
        </p>
      </div>
      <div class="flex items-center gap-2 self-end md:self-center">
        <div v-if="activeTab === 'records' && activeAlarms.length" class="flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs">
          <ListChecks class="w-4 h-4 text-slate-400" />
          <span class="text-slate-500 dark:text-slate-400">未恢复 {{ activeAlarms.length }} / 未确认 {{ unackedCount }}</span>
        </div>
        <span class="px-2.5 py-1.5 rounded-full text-[11px] font-bold flex items-center gap-1.5"
          :class="unackedCount > 0
            ? 'bg-rose-100 text-rose-700 dark:bg-rose-900/50 dark:text-rose-300'
            : 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300'">
          <Bell class="w-3.5 h-3.5" />
          未确认 {{ unackedCount }}
        </span>
      </div>
    </div>

    <!-- Tabs + Rules actions -->
    <div class="bg-white dark:bg-slate-900 px-4 py-2 border-b border-slate-200 dark:border-slate-800 flex items-center gap-2 shrink-0 flex-wrap transition-colors">
      <button
        @click="activeTab = 'rules'"
        class="px-3 py-1.5 rounded-lg text-xs font-bold transition-colors cursor-pointer border"
        :class="activeTab === 'rules'
          ? 'bg-indigo-600 text-white border-indigo-600 shadow-sm'
          : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-700 dark:hover:text-slate-200'"
      >规则配置</button>
      <button
        @click="activeTab = 'records'"
        class="px-3 py-1.5 rounded-lg text-xs font-bold transition-colors cursor-pointer border"
        :class="activeTab === 'records'
          ? 'bg-indigo-600 text-white border-indigo-600 shadow-sm'
          : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-700 dark:hover:text-slate-200'"
      >报警记录</button>

      <div class="ml-auto flex items-center gap-2" v-if="activeTab === 'rules'">
        <button
          @click="loadRules"
          class="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-400 hover:text-indigo-500 hover:bg-slate-50 dark:hover:bg-slate-800 cursor-pointer transition-colors" title="刷新规则">
          <RefreshCw class="w-3.5 h-3.5" />
        </button>
        <button
          @click="openCreate"
          class="font-bold text-xs bg-indigo-600 hover:bg-indigo-700 text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors">
          <Plus class="w-3.5 h-3.5" />
          新建规则
        </button>
      </div>
    </div>

    <!-- ============ 规则配置 ============ -->
    <div v-if="activeTab === 'rules'" class="flex-1 p-4 overflow-auto">
      <div class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 overflow-hidden shadow-sm">
        <div class="bg-slate-50 dark:bg-slate-950 px-4 py-2 border-b border-slate-200 dark:border-slate-800 text-xs text-slate-500 dark:text-slate-400 font-mono flex items-center justify-between">
          <span class="flex items-center gap-2"><ShieldAlert class="w-3.5 h-3.5" />报警规则（共 {{ rules.length }} 条）</span>
          <span v-if="rulesLoading" class="text-indigo-400 animate-pulse">加载中...</span>
        </div>
        <table class="w-full text-xs">
          <thead class="bg-slate-50 dark:bg-slate-950 text-slate-500 dark:text-slate-400">
            <tr class="text-left border-b border-slate-200 dark:border-slate-800">
              <th class="px-3 py-2 font-bold whitespace-nowrap">名称</th>
              <th class="px-3 py-2 font-bold whitespace-nowrap">设备</th>
              <th class="px-3 py-2 font-bold whitespace-nowrap">变量</th>
              <th class="px-3 py-2 font-bold whitespace-nowrap">条件</th>
              <th class="px-3 py-2 font-bold whitespace-nowrap">阈值</th>
              <th class="px-3 py-2 font-bold whitespace-nowrap">级别</th>
              <th class="px-3 py-2 font-bold whitespace-nowrap">防抖(s)</th>
              <th class="px-3 py-2 font-bold whitespace-nowrap">状态</th>
              <th class="px-3 py-2 font-bold text-right">操作</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100 dark:divide-slate-800/60">
            <tr v-for="r in rules" :key="r.id" class="align-middle hover:bg-slate-50 dark:hover:bg-slate-800/40 transition-colors">
              <td class="px-3 py-2 font-semibold text-slate-800 dark:text-slate-100" :title="r.message || ''">{{ r.name }}</td>
              <td class="px-3 py-2 text-slate-500 dark:text-slate-400 whitespace-nowrap">{{ deviceName(r.deviceId) }}</td>
              <td class="px-3 py-2 text-slate-600 dark:text-slate-300 font-mono whitespace-nowrap">{{ r.variableKey }}</td>
              <td class="px-3 py-2 text-slate-600 dark:text-slate-300 whitespace-nowrap">{{ CONDITION_LABEL[r.condition] }}</td>
              <td class="px-3 py-2 text-slate-600 dark:text-slate-300 font-mono whitespace-nowrap">{{ r.threshold }}</td>
              <td class="px-3 py-2 whitespace-nowrap">
                <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border" :class="levelBadge(r.level)">
                  {{ LEVEL_OPTS.find(l => l.value === r.level)?.label ?? r.level }}
                </span>
              </td>
              <td class="px-3 py-2 text-slate-500 dark:text-slate-400 font-mono whitespace-nowrap">{{ r.debounceSeconds }}</td>
              <td class="px-3 py-2 whitespace-nowrap">
                <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border"
                  :class="r.active ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400 border-slate-200 dark:border-slate-700'">
                  {{ r.active ? '启用' : '停用' }}
                </span>
              </td>
              <td class="px-3 py-2">
                <div class="flex items-center justify-end gap-1">
                  <button @click="toggleActive(r)" :title="r.active ? '停用' : '启用'"
                    class="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-400 hover:text-emerald-500 hover:bg-emerald-50 dark:hover:bg-emerald-950/40 cursor-pointer transition-colors">
                    <Power class="w-3.5 h-3.5" />
                  </button>
                  <button @click="openEdit(r)" title="编辑"
                    class="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-400 hover:text-sky-500 hover:bg-sky-50 dark:hover:bg-sky-950/40 cursor-pointer transition-colors">
                    <Pencil class="w-3.5 h-3.5" />
                  </button>
                  <button @click="removeRule(r)" title="删除"
                    class="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-400 hover:text-rose-500 hover:bg-rose-50 dark:hover:bg-rose-950/40 cursor-pointer transition-colors">
                    <Trash2 class="w-3.5 h-3.5" />
                  </button>
                </div>
              </td>
            </tr>
            <tr v-if="!rulesLoading && rules.length === 0">
              <td colspan="9">
                <div class="flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 py-16 gap-2">
                  <ShieldAlert class="w-8 h-8 text-slate-300 dark:text-slate-600 animate-pulse" />
                  <p class="text-xs font-sans">暂无报警规则，点击右上角「新建规则」添加</p>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- ============ 报警记录 ============ -->
    <div v-else class="flex-1 flex flex-col min-h-0">
      <!-- 记录过滤条 -->
      <div class="bg-white dark:bg-slate-900 px-4 py-3 border-b border-slate-200 dark:border-slate-800 shrink-0 transition-colors flex flex-wrap items-center gap-2">
        <select v-model.number="filterDeviceId" @change="applyRecordQuery"
          class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-xs px-2 py-1.5 rounded-lg outline-none text-slate-800 dark:text-white">
          <option :value="null">全部设备</option>
          <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
        </select>
        <select v-model="filterLevel" @change="applyRecordQuery"
          class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-xs px-2 py-1.5 rounded-lg outline-none text-slate-800 dark:text-white">
          <option :value="null">全部级别</option>
          <option v-for="l in LEVEL_OPTS" :key="l.value" :value="l.value">{{ l.label }}</option>
        </select>
        <button @click="filterUnacked = filterUnacked === true ? null : true"
          class="px-2.5 py-1.5 rounded-lg text-xs font-bold border cursor-pointer transition-colors"
          :class="filterUnacked ? 'bg-amber-500 text-white border-amber-500' : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700'">
          仅未确认
        </button>
        <button @click="filterUnrecovered = filterUnrecovered === true ? null : true"
          class="px-2.5 py-1.5 rounded-lg text-xs font-bold border cursor-pointer transition-colors"
          :class="filterUnrecovered ? 'bg-rose-500 text-white border-rose-500' : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700'">
          仅未恢复
        </button>
        <label class="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 shrink-0">
          起
          <input v-model="startDate" type="date"
            class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-xs px-2 py-1.5 rounded-lg outline-none text-slate-800 dark:text-white" />
        </label>
        <label class="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 shrink-0">
          止
          <input v-model="endDate" type="date"
            class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-xs px-2 py-1.5 rounded-lg outline-none text-slate-800 dark:text-white" />
        </label>
        <button @click="applyRecordQuery"
          class="font-bold text-xs bg-indigo-600 hover:bg-indigo-700 text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors">
          <Search class="w-3.5 h-3.5" />查询
        </button>
        <button @click="resetRecordFilters"
          class="font-bold text-xs text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 bg-white dark:bg-slate-900 px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors">
          <RefreshCw class="w-3.5 h-3.5" />重置
        </button>
      </div>

      <!-- 最近实时事件 -->
      <div v-if="recentEvents.length" class="bg-slate-950 px-4 py-2 border-b border-slate-800 shrink-0 max-h-36 overflow-y-auto">
        <div class="flex items-center gap-1.5 text-[10px] text-slate-400 font-mono mb-1">
          <span class="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
          <span class="font-bold text-emerald-400">LIVE</span>
          <span>实时报警事件（最近 {{ recentEvents.length }} 条）</span>
          <button @click="clearRecentEvents" class="ml-auto text-slate-400 hover:text-slate-200 cursor-pointer" title="清空"><X class="w-3.5 h-3.5" /></button>
        </div>
        <div class="space-y-1 font-mono text-[11px] leading-relaxed">
          <div v-for="(e, i) in recentEvents" :key="'e-' + i" class="flex items-start gap-2 text-slate-300">
            <span class="shrink-0 text-slate-500">{{ fmtTime(e.triggeredAt) }}</span>
            <span class="shrink-0 px-1.5 py-0.5 rounded text-[9px] font-bold uppercase" :class="levelBadge(e.level)">{{ LEVEL_OPTS.find(l => l.value === e.level)?.label ?? e.level }}</span>
            <span class="shrink-0 text-sky-300/80">{{ deviceName(e.deviceId) }}</span>
            <span class="shrink-0 text-slate-400">[{{ e.variableKey }}]</span>
            <p class="flex-1 break-all">{{ e.recoveredAt ? '已恢复' : e.message }}</p>
          </div>
        </div>
      </div>

      <!-- 记录表格 -->
      <div class="flex-1 p-4 overflow-auto">
        <div class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 overflow-hidden shadow-sm flex flex-col min-h-[300px]">
          <div class="bg-slate-50 dark:bg-slate-950 px-4 py-2 flex items-center justify-between border-b border-slate-200 dark:border-slate-800 text-xs text-slate-500 dark:text-slate-400 font-mono">
            <span class="flex items-center gap-2"><ListChecks class="w-3.5 h-3.5" />报警记录（共 {{ total }} 条）<span v-if="recordsLoading" class="text-indigo-400 animate-pulse">加载中...</span></span>
            <button @click="loadRecords" class="inline-flex items-center gap-1 text-slate-400 hover:text-indigo-500 cursor-pointer transition-colors" title="刷新"><RefreshCw class="w-3.5 h-3.5" />刷新</button>
          </div>
          <table class="w-full text-xs">
            <thead class="bg-slate-50 dark:bg-slate-950 text-slate-500 dark:text-slate-400">
              <tr class="text-left border-b border-slate-200 dark:border-slate-800">
                <th class="px-3 py-2 font-bold whitespace-nowrap">触发时间</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">设备</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">变量</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">级别</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">来源</th>
                <th class="px-3 py-2 font-bold">文案</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">恢复时间</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">确认</th>
                <th class="px-3 py-2 font-bold text-right">操作</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100 dark:divide-slate-800/60">
              <tr v-for="r in records" :key="r.id" class="align-top hover:bg-slate-50 dark:hover:bg-slate-800/40 transition-colors">
                <td class="px-3 py-2 text-slate-500 dark:text-slate-400 whitespace-nowrap font-mono">{{ fmtTime(r.triggeredAt) }}</td>
                <td class="px-3 py-2 text-slate-600 dark:text-slate-300 whitespace-nowrap">{{ deviceName(r.deviceId) }}</td>
                <td class="px-3 py-2 font-mono text-slate-600 dark:text-slate-300 whitespace-nowrap">{{ r.variableKey }}</td>
                <td class="px-3 py-2 whitespace-nowrap">
                  <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border" :class="levelBadge(r.level)">{{ LEVEL_OPTS.find(l => l.value === r.level)?.label ?? r.level }}</span>
                </td>
                <td class="px-3 py-2 text-slate-500 dark:text-slate-400 whitespace-nowrap">{{ SOURCE_LABEL[r.source] ?? r.source }}</td>
                <td class="px-3 py-2 text-slate-700 dark:text-slate-200 break-all min-w-[180px]" :title="`实际值: ${r.actualValue ?? '-'}${r.ruleName ? ` · 规则: ${r.ruleName}` : ''}`">
                  {{ r.message }}
                </td>
                <td class="px-3 py-2 text-slate-500 dark:text-slate-400 whitespace-nowrap font-mono">{{ r.recoveredAt ? fmtTime(r.recoveredAt) : '未恢复' }}</td>
                <td class="px-3 py-2 whitespace-nowrap">
                  <span v-if="r.acked" class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800">
                    {{ r.ackedBy || '已确认' }}
                  </span>
                  <span v-else class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border bg-amber-100 text-amber-700 dark:bg-amber-900/50 dark:text-amber-300 border-amber-200 dark:border-amber-800">未确认</span>
                </td>
                <td class="px-3 py-2">
                  <div class="flex items-center justify-end">
                    <button v-if="!r.acked" @click="doAck(r)"
                      class="inline-flex items-center gap-1 px-2 py-1 rounded-lg text-[10px] font-bold border border-emerald-200 dark:border-emerald-800 text-emerald-600 dark:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-950/40 cursor-pointer transition-colors">
                      <Check class="w-3 h-3" />确认
                    </button>
                    <span v-else class="text-slate-300 dark:text-slate-600 text-[10px]">-</span>
                  </div>
                </td>
              </tr>
              <tr v-if="!recordsLoading && records.length === 0">
                <td colspan="9">
                  <div class="flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 py-16 gap-2">
                    <ListChecks class="w-8 h-8 text-slate-300 dark:text-slate-600 animate-pulse" />
                    <p class="text-xs font-sans">暂无匹配的报警记录</p>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>

          <!-- 分页 -->
          <div class="px-4 py-2.5 border-t border-slate-200 dark:border-slate-800 flex items-center justify-between gap-3 bg-slate-50 dark:bg-slate-950">
            <div class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
              <select v-model.number="pageSize" @change="applyRecordQuery"
                class="bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-xs px-1.5 py-1 rounded-lg outline-none cursor-pointer">
                <option :value="20">20 / 页</option>
                <option :value="50">50 / 页</option>
                <option :value="100">100 / 页</option>
              </select>
              <span>第 {{ pageIndex }} / {{ totalPages }} 页</span>
            </div>
            <div class="flex items-center gap-1.5">
              <button @click="changePage(-1)" :disabled="pageIndex <= 1"
                class="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-white dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer">
                <ChevronLeft class="w-4 h-4" />
              </button>
              <button @click="changePage(1)" :disabled="pageIndex >= totalPages"
                class="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-white dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer">
                <ChevronRight class="w-4 h-4" />
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 规则编辑弹窗 -->
    <div v-if="showRuleModal" class="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
      @click.self="showRuleModal = false">
      <div class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 shadow-2xl w-[560px] max-w-[92vw] max-h-[90vh] overflow-y-auto p-5">
        <div class="flex items-center justify-between mb-4">
          <h3 class="font-bold text-sm text-slate-900 dark:text-white">{{ editingRuleId != null ? '编辑规则' : '新建规则' }}</h3>
          <button @click="showRuleModal = false" class="p-1 rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"><X class="w-4 h-4" /></button>
        </div>
        <div class="grid grid-cols-2 gap-3">
          <label class="col-span-2 flex flex-col gap-1 text-xs text-slate-500 dark:text-slate-400">
            规则名称 *
            <input v-model="ruleForm.name" type="text" placeholder="如：锅炉压力超高报警"
              class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-sm px-3 py-2 rounded-lg outline-none text-slate-800 dark:text-white focus:border-[#1890ff]" />
          </label>
          <label class="flex flex-col gap-1 text-xs text-slate-500 dark:text-slate-400">
            设备 *
            <select v-model.number="ruleForm.deviceId" @change="ruleForm.variableKey = ''"
              class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-sm px-3 py-2 rounded-lg outline-none text-slate-800 dark:text-white">
              <option v-for="d in deviceOptions" :key="d.id" :value="d.id">{{ d.name }}</option>
            </select>
          </label>
          <label class="flex flex-col gap-1 text-xs text-slate-500 dark:text-slate-400">
            变量 *
            <select v-model="ruleForm.variableKey"
              class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-sm px-3 py-2 rounded-lg outline-none text-slate-800 dark:text-white" :disabled="!variableOptions.length">
              <option value="" disabled>{{ variableOptions.length ? '请选择变量' : '该设备无变量' }}</option>
              <option v-for="v in variableOptions" :key="v.key" :value="v.key">{{ v.name }}</option>
            </select>
          </label>
          <label class="flex flex-col gap-1 text-xs text-slate-500 dark:text-slate-400">
            条件 *
            <select v-model="ruleForm.condition"
              class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-sm px-3 py-2 rounded-lg outline-none text-slate-800 dark:text-white">
              <option v-for="(label, val) in CONDITION_LABEL" :key="val" :value="val">{{ label }}</option>
            </select>
          </label>
          <label class="flex flex-col gap-1 text-xs text-slate-500 dark:text-slate-400">
            阈值 *
            <input v-model.number="ruleForm.threshold" type="number" step="any"
              class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-sm px-3 py-2 rounded-lg outline-none text-slate-800 dark:text-white focus:border-[#1890ff]" />
          </label>
          <label class="flex flex-col gap-1 text-xs text-slate-500 dark:text-slate-400">
            级别 *
            <select v-model="ruleForm.level"
              class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-sm px-3 py-2 rounded-lg outline-none text-slate-800 dark:text-white">
              <option v-for="l in LEVEL_OPTS" :key="l.value" :value="l.value">{{ l.label }}</option>
            </select>
          </label>
          <label class="flex flex-col gap-1 text-xs text-slate-500 dark:text-slate-400">
            防抖秒数
            <input v-model.number="ruleForm.debounceSeconds" type="number" min="0" max="86400"
              class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-sm px-3 py-2 rounded-lg outline-none text-slate-800 dark:text-white focus:border-[#1890ff]" />
          </label>
          <label class="col-span-2 flex flex-col gap-1 text-xs text-slate-500 dark:text-slate-400">
            报警文案（留空则用默认模板）
            <input v-model="ruleForm.message" type="text" placeholder="如：锅炉压力 {{threshold}} 超过阈值 {{threshold}}"
              class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-sm px-3 py-2 rounded-lg outline-none text-slate-800 dark:text-white focus:border-[#1890ff]" />
          </label>
          <label class="col-span-2 flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400 cursor-pointer select-none">
            <input v-model="ruleForm.active" type="checkbox" class="accent-indigo-600 w-4 h-4" />
            启用该规则
          </label>
        </div>
        <div class="flex justify-end gap-2 mt-5">
          <button @click="showRuleModal = false"
            class="px-4 py-2 rounded-lg text-xs font-bold text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 cursor-pointer">取消</button>
          <button @click="saveRule"
            class="px-4 py-2 rounded-lg text-xs font-bold bg-indigo-600 hover:bg-indigo-700 text-white cursor-pointer">保存</button>
        </div>
      </div>
    </div>
  </div>
</template>