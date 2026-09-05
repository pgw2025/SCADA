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
  ListChecks,
  LayoutList,
  AlignJustify,
  Eye,
  Copy,
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

// ================= 移动端双模与详情抽屉 =================
const mobileViewMode = ref<'card' | 'compact'>(
  (localStorage.getItem('scada_alarm_mobile_view') as 'card' | 'compact') || 'card'
);
const setMobileViewMode = (mode: 'card' | 'compact') => {
  mobileViewMode.value = mode;
  localStorage.setItem('scada_alarm_mobile_view', mode);
};

const selectedRuleDetail = ref<AlarmRule | null>(null);
const openRuleDetail = (r: AlarmRule) => {
  selectedRuleDetail.value = r;
};
const closeRuleDetail = () => {
  selectedRuleDetail.value = null;
};

const selectedRecordDetail = ref<AlarmRecord | null>(null);
const openRecordDetail = (r: AlarmRecord) => {
  selectedRecordDetail.value = r;
};
const closeRecordDetail = () => {
  selectedRecordDetail.value = null;
};

const copyRecordDetail = async (r: AlarmRecord) => {
  const content = [
    `【SCADA 报警记录】ID: #${r.id}`,
    `级别: ${LEVEL_OPTS.find(l => l.value === r.level)?.label ?? r.level}`,
    `设备: ${deviceName(r.deviceId)}`,
    `变量: ${r.variableKey}`,
    `来源: ${SOURCE_LABEL[r.source] ?? r.source}`,
    `触发时间: ${fmtTime(r.triggeredAt)}`,
    `恢复时间: ${r.recoveredAt ? fmtTime(r.recoveredAt) : '未恢复'}`,
    `确认状态: ${r.acked ? `已确认 (${r.ackedBy || '操作员'} @ ${fmtTime(r.ackedAt)})` : '未确认'}`,
    `报警文案: ${r.message}`,
    r.actualValue != null ? `实际测量值: ${r.actualValue}` : '',
  ].filter(Boolean).join('\n');

  try {
    await navigator.clipboard.writeText(content);
    showToast('已复制报警详情至剪贴板', 'success');
  } catch {
    showToast('已生成文本，长按可复制', 'warning');
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
    <div
      class="bg-white dark:bg-slate-900 p-4 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-3 transition-colors">
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
        <div v-if="activeTab === 'records' && activeAlarms.length"
          class="flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs">
          <ListChecks class="w-4 h-4 text-slate-400" />
          <span class="text-slate-500 dark:text-slate-400">未恢复 {{ activeAlarms.length }} / 未确认 {{ unackedCount }}</span>
        </div>
        <span class="px-2.5 py-1.5 rounded-full text-[11px] font-bold flex items-center gap-1.5" :class="unackedCount > 0
          ? 'bg-rose-100 text-rose-700 dark:bg-rose-900/50 dark:text-rose-300'
          : 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300'">
          <Bell class="w-3.5 h-3.5" />
          未确认 {{ unackedCount }}
        </span>
      </div>
    </div>

    <!-- Tabs + Rules actions -->
    <div
      class="bg-white dark:bg-slate-900 px-4 py-2 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between gap-2 shrink-0 flex-wrap transition-colors">
      <div class="flex items-center gap-2">
        <button @click="activeTab = 'rules'"
          class="px-3 py-1.5 rounded-lg text-xs font-bold transition-colors cursor-pointer border"
          :class="activeTab === 'rules'
            ? 'bg-indigo-600 text-white border-indigo-600 shadow-sm'
            : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-700 dark:hover:text-slate-200'">规则配置</button>
        <button @click="activeTab = 'records'"
          class="px-3 py-1.5 rounded-lg text-xs font-bold transition-colors cursor-pointer border"
          :class="activeTab === 'records'
            ? 'bg-indigo-600 text-white border-indigo-600 shadow-sm'
            : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-700 dark:hover:text-slate-200'">报警记录</button>

        <!-- 移动端双模切换开关 (方案一卡片 / 方案二紧凑) -->
        <div
          class="flex md:hidden items-center p-0.5 bg-slate-100 dark:bg-slate-800 rounded-lg border border-slate-200 dark:border-slate-700">
          <button @click="setMobileViewMode('card')"
            class="flex items-center gap-1 px-2 py-1 text-[11px] font-bold rounded-md transition-all cursor-pointer"
            :class="mobileViewMode === 'card'
              ? 'bg-white dark:bg-slate-700 text-indigo-600 dark:text-indigo-400 shadow-xs'
              : 'text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200'" title="方案一：卡片模式">
            <LayoutList class="w-3.5 h-3.5" />
            卡片
          </button>
          <button @click="setMobileViewMode('compact')"
            class="flex items-center gap-1 px-2 py-1 text-[11px] font-bold rounded-md transition-all cursor-pointer"
            :class="mobileViewMode === 'compact'
              ? 'bg-white dark:bg-slate-700 text-indigo-600 dark:text-indigo-400 shadow-xs'
              : 'text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200'"
            title="方案二：紧凑列表模式">
            <AlignJustify class="w-3.5 h-3.5" />
            紧凑
          </button>
        </div>
      </div>

      <div class="flex items-center gap-2" v-if="activeTab === 'rules'">
        <button @click="loadRules"
          class="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-400 hover:text-indigo-500 hover:bg-slate-50 dark:hover:bg-slate-800 cursor-pointer transition-colors"
          title="刷新规则">
          <RefreshCw class="w-3.5 h-3.5" />
        </button>
        <button @click="openCreate"
          class="font-bold text-xs bg-indigo-600 hover:bg-indigo-700 text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors">
          <Plus class="w-3.5 h-3.5" />
          新建规则
        </button>
      </div>
    </div>

    <!-- ============ 规则配置 ============ -->
    <div v-if="activeTab === 'rules'" class="flex-1 p-3 md:p-4 overflow-auto">
      <div
        class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 overflow-hidden shadow-sm">
        <div
          class="bg-slate-50 dark:bg-slate-950 px-4 py-2 border-b border-slate-200 dark:border-slate-800 text-xs text-slate-500 dark:text-slate-400 font-mono flex items-center justify-between">
          <span class="flex items-center gap-2">
            <ShieldAlert class="w-3.5 h-3.5" />报警规则（共 {{ rules.length }} 条）
          </span>
          <span v-if="rulesLoading" class="text-indigo-400 animate-pulse">加载中...</span>
        </div>

        <!-- 桌面端表格视图 -->
        <div class="hidden md:block overflow-x-auto">
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
              <tr v-for="r in rules" :key="r.id"
                class="align-middle hover:bg-slate-50 dark:hover:bg-slate-800/40 transition-colors">
                <td class="px-3 py-2 font-semibold text-slate-800 dark:text-slate-100" :title="r.message || ''">{{
                  r.name }}</td>
                <td class="px-3 py-2 text-slate-500 dark:text-slate-400 whitespace-nowrap">{{ deviceName(r.deviceId) }}
                </td>
                <td class="px-3 py-2 text-slate-600 dark:text-slate-300 font-mono whitespace-nowrap">{{ r.variableKey }}
                </td>
                <td class="px-3 py-2 text-slate-600 dark:text-slate-300 whitespace-nowrap">{{
                  CONDITION_LABEL[r.condition] }}</td>
                <td class="px-3 py-2 text-slate-600 dark:text-slate-300 font-mono whitespace-nowrap">{{ r.threshold }}
                </td>
                <td class="px-3 py-2 whitespace-nowrap">
                  <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border"
                    :class="levelBadge(r.level)">
                    {{LEVEL_OPTS.find(l => l.value === r.level)?.label ?? r.level}}
                  </span>
                </td>
                <td class="px-3 py-2 text-slate-500 dark:text-slate-400 font-mono whitespace-nowrap">{{
                  r.debounceSeconds }}</td>
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

        <!-- 移动端双模视图 (方案一卡片 / 方案二紧凑) -->
        <div class="block md:hidden">
          <div v-if="!rulesLoading && rules.length === 0"
            class="flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 py-16 gap-2">
            <ShieldAlert class="w-8 h-8 text-slate-300 dark:text-slate-600 animate-pulse" />
            <p class="text-xs font-sans">暂无报警规则，点击右上角「新建规则」添加</p>
          </div>

          <!-- 方案一：完整卡片视图 (Card Mode) -->
          <div v-else-if="mobileViewMode === 'card'" class="p-3 space-y-3">
            <div v-for="r in rules" :key="'m-rule-card-' + r.id"
              class="bg-slate-50/70 dark:bg-slate-800/50 rounded-xl p-3.5 border border-slate-200/80 dark:border-slate-700/80 space-y-2.5 transition-colors shadow-xs">
              <div class="flex items-start justify-between gap-2">
                <div class="min-w-0 flex-1">
                  <div class="flex items-center gap-1.5">
                    <span class="w-2 h-2 rounded-full shrink-0" :class="r.active ? 'bg-emerald-500' : 'bg-slate-400'" />
                    <h3 class="font-bold text-xs text-slate-900 dark:text-white truncate">{{ r.name }}</h3>
                  </div>
                  <p class="text-[11px] text-slate-500 dark:text-slate-400 truncate mt-0.5">
                    {{ deviceName(r.deviceId) }} · <span class="font-mono text-indigo-600 dark:text-indigo-400">{{
                      r.variableKey }}</span>
                  </p>
                </div>
                <div class="flex items-center gap-1.5 shrink-0">
                  <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border"
                    :class="levelBadge(r.level)">
                    {{LEVEL_OPTS.find(l => l.value === r.level)?.label ?? r.level}}
                  </span>
                  <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border"
                    :class="r.active ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400 border-slate-200 dark:border-slate-700'">
                    {{ r.active ? '启用' : '停用' }}
                  </span>
                </div>
              </div>

              <!-- 条件与防抖 -->
              <div class="flex flex-wrap items-center gap-1.5 text-[10px]">
                <span
                  class="px-2 py-0.5 rounded-md bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-300 font-mono">
                  条件: {{ CONDITION_LABEL[r.condition] }} {{ r.threshold }}
                </span>
                <span
                  class="px-2 py-0.5 rounded-md bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-slate-500 dark:text-slate-400 font-mono">
                  防抖: {{ r.debounceSeconds }}s
                </span>
              </div>

              <!-- 报警文案 -->
              <p v-if="r.message"
                class="text-[11px] text-slate-600 dark:text-slate-300 bg-white dark:bg-slate-900/60 rounded-lg p-2 border border-slate-100 dark:border-slate-800 break-words leading-relaxed">
                {{ r.message }}
              </p>

              <!-- 底部操作条 -->
              <div
                class="pt-2 border-t border-slate-200/60 dark:border-slate-800/80 flex items-center justify-between text-xs">
                <button @click="openRuleDetail(r)"
                  class="inline-flex items-center gap-1 text-[11px] font-bold text-slate-500 dark:text-slate-400 hover:text-indigo-600 dark:hover:text-indigo-400 cursor-pointer">
                  <Eye class="w-3.5 h-3.5" />
                  详情
                </button>
                <div class="flex items-center gap-1.5">
                  <button @click="toggleActive(r)"
                    class="px-2 py-1 rounded-lg border border-slate-200 dark:border-slate-700 font-bold text-[11px] inline-flex items-center gap-1 cursor-pointer transition-colors"
                    :class="r.active ? 'text-amber-600 dark:text-amber-400 hover:bg-amber-50 dark:hover:bg-amber-950/40' : 'text-emerald-600 dark:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-950/40'">
                    <Power class="w-3 h-3" />
                    {{ r.active ? '停用' : '启用' }}
                  </button>
                  <button @click="openEdit(r)"
                    class="px-2.5 py-1 rounded-lg border border-indigo-200 dark:border-indigo-800 hover:bg-indigo-50 dark:hover:bg-indigo-950/40 font-bold text-[11px] text-indigo-600 dark:text-indigo-400 inline-flex items-center gap-1 cursor-pointer">
                    <Pencil class="w-3 h-3" />
                    编辑
                  </button>
                  <button @click="removeRule(r)"
                    class="px-2 py-1 rounded-lg border border-rose-200 dark:border-rose-900 hover:bg-rose-50 dark:hover:bg-rose-950/40 font-bold text-[11px] text-rose-600 dark:text-rose-400 inline-flex items-center gap-1 cursor-pointer">
                    <Trash2 class="w-3 h-3" />
                  </button>
                </div>
              </div>
            </div>
          </div>

          <!-- 方案二：高密度紧凑视图 (Compact Mode) -->
          <div v-else-if="mobileViewMode === 'compact'" class="divide-y divide-slate-100 dark:divide-slate-800">
            <div v-for="r in rules" :key="'m-rule-compact-' + r.id" @click="openRuleDetail(r)"
              class="px-3 py-2.5 flex items-center gap-2 hover:bg-slate-50 dark:hover:bg-slate-800/40 active:bg-slate-100 dark:active:bg-slate-800 transition-colors cursor-pointer">
              <span class="w-2 h-2 rounded-full shrink-0" :class="r.active ? 'bg-emerald-500' : 'bg-slate-400'" />
              <span class="inline-block px-1.5 py-0.2 rounded text-[9px] font-bold border shrink-0"
                :class="levelBadge(r.level)">
                {{LEVEL_OPTS.find(l => l.value === r.level)?.label ?? r.level}}
              </span>
              <div class="flex-1 min-w-0">
                <div class="flex items-center gap-1">
                  <span class="font-bold text-xs text-slate-800 dark:text-white truncate">{{ r.name }}</span>
                </div>
                <p class="text-[10px] text-slate-400 dark:text-slate-500 truncate font-mono mt-0.5">
                  {{ deviceName(r.deviceId) }} · {{ r.variableKey }} {{ CONDITION_LABEL[r.condition] }} {{ r.threshold
                  }}
                </p>
              </div>
              <div class="text-right shrink-0 flex items-center gap-1">
                <span class="text-[10px] font-mono text-slate-400">{{ r.debounceSeconds }}s</span>
                <ChevronRight class="w-3.5 h-3.5 text-slate-300 dark:text-slate-600" />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ============ 报警记录 ============ -->
    <div v-else class="flex-1 flex flex-col min-h-0">
      <!-- 记录过滤条 -->
      <div
        class="bg-white dark:bg-slate-900 px-4 py-3 border-b border-slate-200 dark:border-slate-800 shrink-0 transition-colors flex flex-wrap items-center gap-2">
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
      <div v-if="recentEvents.length"
        class="bg-slate-950 px-4 py-2 border-b border-slate-800 shrink-0 max-h-36 overflow-y-auto">
        <div class="flex items-center gap-1.5 text-[10px] text-slate-400 font-mono mb-1">
          <span class="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
          <span class="font-bold text-emerald-400">LIVE</span>
          <span>实时报警事件（最近 {{ recentEvents.length }} 条）</span>
          <button @click="clearRecentEvents" class="ml-auto text-slate-400 hover:text-slate-200 cursor-pointer"
            title="清空">
            <X class="w-3.5 h-3.5" />
          </button>
        </div>
        <div class="space-y-1 font-mono text-[11px] leading-relaxed">
          <div v-for="(e, i) in recentEvents" :key="'e-' + i" class="flex items-start gap-2 text-slate-300">
            <span class="shrink-0 text-slate-500">{{ fmtTime(e.triggeredAt) }}</span>
            <span class="shrink-0 px-1.5 py-0.5 rounded text-[9px] font-bold uppercase" :class="levelBadge(e.level)">{{
              LEVEL_OPTS.find(l => l.value === e.level)?.label ?? e.level }}</span>
            <span class="shrink-0 text-sky-300/80">{{ deviceName(e.deviceId) }}</span>
            <span class="shrink-0 text-slate-400">[{{ e.variableKey }}]</span>
            <p class="flex-1 break-all">{{ e.recoveredAt ? '已恢复' : e.message }}</p>
          </div>
        </div>
      </div>

      <!-- 记录表格 -->
      <div class="flex-1 p-3 md:p-4 overflow-auto">
        <div
          class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 overflow-hidden shadow-sm flex flex-col min-h-[300px]">
          <div
            class="bg-slate-50 dark:bg-slate-950 px-4 py-2 flex items-center justify-between border-b border-slate-200 dark:border-slate-800 text-xs text-slate-500 dark:text-slate-400 font-mono">
            <span class="flex items-center gap-2">
              <ListChecks class="w-3.5 h-3.5" />报警记录（共 {{ total }} 条）<span v-if="recordsLoading"
                class="text-indigo-400 animate-pulse">加载中...</span>
            </span>
            <button @click="loadRecords"
              class="inline-flex items-center gap-1 text-slate-400 hover:text-indigo-500 cursor-pointer transition-colors"
              title="刷新">
              <RefreshCw class="w-3.5 h-3.5" />刷新
            </button>
          </div>

          <!-- 桌面端表格视图 -->
          <div class="hidden md:block overflow-x-auto">
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
                <tr v-for="r in records" :key="r.id"
                  class="align-top hover:bg-slate-50 dark:hover:bg-slate-800/40 transition-colors">
                  <td class="px-3 py-2 text-slate-500 dark:text-slate-400 whitespace-nowrap font-mono">{{
                    fmtTime(r.triggeredAt) }}</td>
                  <td class="px-3 py-2 text-slate-600 dark:text-slate-300 whitespace-nowrap">{{ deviceName(r.deviceId)
                    }}</td>
                  <td class="px-3 py-2 font-mono text-slate-600 dark:text-slate-300 whitespace-nowrap">{{ r.variableKey
                    }}</td>
                  <td class="px-3 py-2 whitespace-nowrap">
                    <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border"
                      :class="levelBadge(r.level)">{{LEVEL_OPTS.find(l => l.value === r.level)?.label ?? r.level
                      }}</span>
                  </td>
                  <td class="px-3 py-2 text-slate-500 dark:text-slate-400 whitespace-nowrap">{{ SOURCE_LABEL[r.source]
                    ?? r.source }}</td>
                  <td class="px-3 py-2 text-slate-700 dark:text-slate-200 break-all min-w-[180px]"
                    :title="`实际值: ${r.actualValue ?? '-'}${r.ruleName ? ` · 规则: ${r.ruleName}` : ''}`">
                    {{ r.message }}
                  </td>
                  <td class="px-3 py-2 text-slate-500 dark:text-slate-400 whitespace-nowrap font-mono">{{ r.recoveredAt
                    ? fmtTime(r.recoveredAt) : '未恢复' }}</td>
                  <td class="px-3 py-2 whitespace-nowrap">
                    <span v-if="r.acked"
                      class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800">
                      {{ r.ackedBy || '已确认' }}
                    </span>
                    <span v-else
                      class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border bg-amber-100 text-amber-700 dark:bg-amber-900/50 dark:text-amber-300 border-amber-200 dark:border-amber-800">未确认</span>
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
                    <div
                      class="flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 py-16 gap-2">
                      <ListChecks class="w-8 h-8 text-slate-300 dark:text-slate-600 animate-pulse" />
                      <p class="text-xs font-sans">暂无匹配的报警记录</p>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- 移动端双模视图 (方案一卡片 / 方案二紧凑) -->
          <div class="block md:hidden">
            <div v-if="!recordsLoading && records.length === 0"
              class="flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 py-16 gap-2">
              <ListChecks class="w-8 h-8 text-slate-300 dark:text-slate-600 animate-pulse" />
              <p class="text-xs font-sans">暂无匹配的报警记录</p>
            </div>

            <!-- 方案一：完整卡片视图 (Card Mode) -->
            <div v-else-if="mobileViewMode === 'card'" class="p-3 space-y-3">
              <div v-for="r in records" :key="'m-record-card-' + r.id"
                class="bg-slate-50/70 dark:bg-slate-800/50 rounded-xl p-3.5 border border-slate-200/80 dark:border-slate-700/80 space-y-2.5 transition-colors shadow-xs">
                <!-- 头部：级别 + 来源 + 触发时间 + 确认状态 -->
                <div class="flex items-center justify-between gap-2 text-xs">
                  <div class="flex items-center gap-1.5 flex-wrap">
                    <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border"
                      :class="levelBadge(r.level)">
                      {{LEVEL_OPTS.find(l => l.value === r.level)?.label ?? r.level}}
                    </span>
                    <span
                      class="px-1.5 py-0.5 rounded bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-slate-500 dark:text-slate-400 text-[10px]">
                      {{ SOURCE_LABEL[r.source] ?? r.source }}
                    </span>
                    <span class="font-mono text-[10px] text-slate-400 dark:text-slate-500">
                      {{ fmtTime(r.triggeredAt) }}
                    </span>
                  </div>
                  <span v-if="r.acked"
                    class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800 shrink-0">
                    已确认
                  </span>
                  <span v-else
                    class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border bg-amber-100 text-amber-700 dark:bg-amber-900/50 dark:text-amber-300 border-amber-200 dark:border-amber-800 shrink-0">
                    未确认
                  </span>
                </div>

                <!-- 设备与变量 -->
                <div class="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1.5">
                  <span class="font-bold text-slate-800 dark:text-slate-200">{{ deviceName(r.deviceId) }}</span>
                  <span>·</span>
                  <span class="font-mono text-indigo-600 dark:text-indigo-400">{{ r.variableKey }}</span>
                </div>

                <!-- 完整报警文案 (手机端完整折行无省略) -->
                <div
                  class="bg-white dark:bg-slate-900/70 p-2.5 rounded-lg border border-slate-200/70 dark:border-slate-700/70 text-xs text-slate-800 dark:text-slate-100 break-words leading-relaxed">
                  {{ r.message }}
                </div>

                <!-- 恢复与测量值摘要 -->
                <div
                  class="flex flex-wrap items-center justify-between gap-2 text-[10px] text-slate-500 dark:text-slate-400">
                  <span class="font-mono">
                    恢复: {{ r.recoveredAt ? fmtTime(r.recoveredAt) : '持续中 (未恢复)' }}
                  </span>
                  <span v-if="r.actualValue != null"
                    class="font-mono bg-white dark:bg-slate-900 px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-300">
                    测量值: {{ r.actualValue }}
                  </span>
                </div>

                <!-- 底部操作条 -->
                <div
                  class="pt-2 border-t border-slate-200/60 dark:border-slate-800/80 flex items-center justify-between text-xs">
                  <button @click="openRecordDetail(r)"
                    class="inline-flex items-center gap-1 text-[11px] font-bold text-slate-500 dark:text-slate-400 hover:text-indigo-600 dark:hover:text-indigo-400 cursor-pointer">
                    <Eye class="w-3.5 h-3.5" />
                    查看详情
                  </button>
                  <div class="flex items-center gap-1.5">
                    <button @click="copyRecordDetail(r)"
                      class="px-2 py-1 rounded-lg border border-slate-200 dark:border-slate-700 font-bold text-[11px] text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 inline-flex items-center gap-1 cursor-pointer"
                      title="复制报警内容">
                      <Copy class="w-3 h-3" />
                      复制
                    </button>
                    <button v-if="!r.acked" @click="doAck(r)"
                      class="px-2.5 py-1 rounded-lg bg-emerald-600 hover:bg-emerald-700 text-white font-bold text-[11px] inline-flex items-center gap-1 cursor-pointer shadow-xs transition-colors">
                      <Check class="w-3 h-3" />
                      确认报警
                    </button>
                  </div>
                </div>
              </div>
            </div>

            <!-- 方案二：高密度紧凑列表 (Compact Mode) -->
            <div v-else-if="mobileViewMode === 'compact'" class="divide-y divide-slate-100 dark:divide-slate-800">
              <div v-for="r in records" :key="'m-record-compact-' + r.id" @click="openRecordDetail(r)"
                class="px-3 py-2.5 flex items-center gap-2 hover:bg-slate-50 dark:hover:bg-slate-800/40 active:bg-slate-100 dark:active:bg-slate-800 transition-colors cursor-pointer">
                <!-- 状态呼吸灯/小圆点 -->
                <span class="w-2 h-2 rounded-full shrink-0"
                  :class="r.acked ? 'bg-slate-300 dark:bg-slate-600' : 'bg-rose-500 animate-pulse'" />
                <span class="inline-block px-1.5 py-0.2 rounded text-[9px] font-bold border shrink-0"
                  :class="levelBadge(r.level)">
                  {{LEVEL_OPTS.find(l => l.value === r.level)?.label ?? r.level}}
                </span>
                <div class="flex-1 min-w-0">
                  <div class="flex items-center gap-1.5">
                    <span class="font-bold text-xs text-slate-800 dark:text-white truncate">{{ deviceName(r.deviceId)
                      }}</span>
                    <span class="text-[11px] text-slate-500 dark:text-slate-400 truncate">{{ r.message }}</span>
                  </div>
                  <div class="flex items-center gap-2 text-[10px] text-slate-400 dark:text-slate-500 font-mono mt-0.5">
                    <span>{{ fmtTime(r.triggeredAt) }}</span>
                    <span>·</span>
                    <span :class="r.recoveredAt ? 'text-emerald-500' : 'text-amber-500'">
                      {{ r.recoveredAt ? '已恢复' : '持续中' }}
                    </span>
                  </div>
                </div>
                <div class="shrink-0 flex items-center gap-1.5">
                  <button v-if="!r.acked" @click.stop="doAck(r)"
                    class="px-2 py-0.5 rounded text-[10px] font-bold border border-emerald-300 dark:border-emerald-800 text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/40 hover:bg-emerald-100 cursor-pointer">
                    确认
                  </button>
                  <span v-else class="text-[10px] text-slate-400">已认</span>
                  <ChevronRight class="w-3.5 h-3.5 text-slate-300 dark:text-slate-600" />
                </div>
              </div>
            </div>
          </div>

          <!-- 分页 -->
          <div
            class="px-4 py-2.5 border-t border-slate-200 dark:border-slate-800 flex items-center justify-between gap-3 bg-slate-50 dark:bg-slate-950">
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
      <div
        class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 shadow-2xl w-[560px] max-w-[92vw] max-h-[90vh] overflow-y-auto p-5">
        <div class="flex items-center justify-between mb-4">
          <h3 class="font-bold text-sm text-slate-900 dark:text-white">{{ editingRuleId != null ? '编辑规则' : '新建规则' }}
          </h3>
          <button @click="showRuleModal = false"
            class="p-1 rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer">
            <X class="w-4 h-4" />
          </button>
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
              class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-sm px-3 py-2 rounded-lg outline-none text-slate-800 dark:text-white"
              :disabled="!variableOptions.length">
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
          <label
            class="col-span-2 flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400 cursor-pointer select-none">
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

    <!-- 移动端规则详情抽屉 (Bottom Sheet) -->
    <div v-if="selectedRuleDetail"
      class="fixed inset-0 z-50 flex flex-col justify-end bg-black/50 backdrop-blur-xs md:hidden"
      @click.self="closeRuleDetail">
      <div
        class="bg-white dark:bg-slate-900 rounded-t-2xl max-h-[85vh] flex flex-col shadow-2xl border-t border-slate-200 dark:border-slate-800 animate-in slide-in-from-bottom duration-200 text-left">
        <!-- 顶部拖拽把手 -->
        <div class="w-10 h-1 bg-slate-300 dark:bg-slate-700 rounded-full mx-auto mt-2.5 shrink-0" />

        <!-- 抽屉头部 -->
        <div class="px-4 py-3 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
          <div class="flex items-center gap-2 min-w-0">
            <div
              class="w-8 h-8 rounded-lg bg-indigo-50 dark:bg-indigo-950/60 border border-indigo-200 dark:border-indigo-800 flex items-center justify-center shrink-0">
              <ShieldAlert class="w-4 h-4 text-indigo-600 dark:text-indigo-400" />
            </div>
            <div class="min-w-0">
              <h3 class="font-bold text-sm text-slate-900 dark:text-white truncate">{{ selectedRuleDetail.name }}</h3>
              <p class="text-[10px] text-slate-400 dark:text-slate-500 truncate">报警规则详情</p>
            </div>
          </div>
          <button @click="closeRuleDetail"
            class="p-1.5 rounded-lg text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer shrink-0">
            <X class="w-4 h-4" />
          </button>
        </div>

        <!-- 抽屉内容 -->
        <div class="p-4 overflow-y-auto space-y-4 text-xs">
          <!-- 核心属性 -->
          <div
            class="grid grid-cols-2 gap-2.5 bg-slate-50 dark:bg-slate-950/60 p-3 rounded-xl border border-slate-200/80 dark:border-slate-800">
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">监控设备</span>
              <span class="font-bold text-slate-800 dark:text-slate-200 truncate block">{{
                deviceName(selectedRuleDetail.deviceId) }}</span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">测点变量</span>
              <span class="font-mono font-bold text-indigo-600 dark:text-indigo-400 truncate block">{{
                selectedRuleDetail.variableKey }}</span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">判定条件</span>
              <span class="font-bold text-slate-800 dark:text-slate-200">
                {{ CONDITION_LABEL[selectedRuleDetail.condition] }}
              </span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">判定阈值</span>
              <span class="font-mono font-bold text-slate-800 dark:text-slate-200">
                {{ selectedRuleDetail.threshold }}
              </span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">报警级别</span>
              <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border"
                :class="levelBadge(selectedRuleDetail.level)">
                {{LEVEL_OPTS.find(l => l.value === selectedRuleDetail.level)?.label ?? selectedRuleDetail.level}}
              </span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">防抖延时</span>
              <span class="font-mono text-slate-800 dark:text-slate-200">
                {{ selectedRuleDetail.debounceSeconds }} 秒
              </span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">启停状态</span>
              <span class="font-bold"
                :class="selectedRuleDetail.active ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'">
                {{ selectedRuleDetail.active ? '● 运行中 (已启用)' : '○ 已停用' }}
              </span>
            </div>
          </div>

          <!-- 报警文案 -->
          <div>
            <span class="text-[11px] font-bold text-slate-500 dark:text-slate-400 block mb-1">报警消息模板</span>
            <div
              class="bg-slate-50 dark:bg-slate-950 p-3 rounded-lg border border-slate-200 dark:border-slate-800 text-slate-800 dark:text-slate-200 leading-relaxed text-xs break-words">
              {{ selectedRuleDetail.message || '（未设置独立模板，触发时自动生成）' }}
            </div>
          </div>
        </div>

        <!-- 底部操作条 -->
        <div
          class="p-3 border-t border-slate-100 dark:border-slate-800 bg-slate-50 dark:bg-slate-950/80 flex items-center gap-2">
          <button @click="toggleActive(selectedRuleDetail)"
            class="flex-1 py-2 rounded-lg border font-bold text-xs inline-flex items-center justify-center gap-1.5 cursor-pointer shadow-xs"
            :class="selectedRuleDetail.active
              ? 'border-amber-300 dark:border-amber-700 bg-amber-50 dark:bg-amber-950/40 text-amber-700 dark:text-amber-300'
              : 'border-emerald-300 dark:border-emerald-700 bg-emerald-50 dark:bg-emerald-950/40 text-emerald-700 dark:text-emerald-300'">
            <Power class="w-3.5 h-3.5" />
            {{ selectedRuleDetail.active ? '停用规则' : '启用规则' }}
          </button>
          <button @click="openEdit(selectedRuleDetail); closeRuleDetail()"
            class="flex-1 py-2 rounded-lg bg-indigo-600 hover:bg-indigo-700 font-bold text-xs text-white inline-flex items-center justify-center gap-1.5 cursor-pointer shadow-xs">
            <Pencil class="w-3.5 h-3.5" />
            编辑规则
          </button>
          <button @click="removeRule(selectedRuleDetail); closeRuleDetail()"
            class="py-2 px-3 rounded-lg border border-rose-200 dark:border-rose-900 bg-rose-50 dark:bg-rose-950/40 font-bold text-xs text-rose-600 dark:text-rose-400 inline-flex items-center justify-center gap-1 cursor-pointer"
            title="删除规则">
            <Trash2 class="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </div>

    <!-- 移动端报警记录详情抽屉 (Bottom Sheet) -->
    <div v-if="selectedRecordDetail"
      class="fixed inset-0 z-50 flex flex-col justify-end bg-black/50 backdrop-blur-xs md:hidden"
      @click.self="closeRecordDetail">
      <div
        class="bg-white dark:bg-slate-900 rounded-t-2xl max-h-[85vh] flex flex-col shadow-2xl border-t border-slate-200 dark:border-slate-800 animate-in slide-in-from-bottom duration-200 text-left">
        <!-- 顶部拖拽把手 -->
        <div class="w-10 h-1 bg-slate-300 dark:bg-slate-700 rounded-full mx-auto mt-2.5 shrink-0" />

        <!-- 抽屉头部 -->
        <div class="px-4 py-3 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
          <div class="flex items-center gap-2 min-w-0">
            <span class="inline-block px-2 py-0.5 rounded text-xs font-bold border shrink-0"
              :class="levelBadge(selectedRecordDetail.level)">
              {{LEVEL_OPTS.find(l => l.value === selectedRecordDetail.level)?.label ?? selectedRecordDetail.level}}
            </span>
            <div class="min-w-0">
              <h3 class="font-bold text-sm text-slate-900 dark:text-white truncate">报警记录 #{{ selectedRecordDetail.id }}
              </h3>
              <p class="font-mono text-[10px] text-slate-400 dark:text-slate-500 truncate">{{
                fmtTime(selectedRecordDetail.triggeredAt) }}</p>
            </div>
          </div>
          <button @click="closeRecordDetail"
            class="p-1.5 rounded-lg text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer shrink-0">
            <X class="w-4 h-4" />
          </button>
        </div>

        <!-- 抽屉内容 -->
        <div class="p-4 overflow-y-auto space-y-4 text-xs">
          <!-- 报警文案核心区域 (高亮提示) -->
          <div>
            <span class="text-[11px] font-bold text-slate-500 dark:text-slate-400 block mb-1">报警消息内容</span>
            <div
              class="bg-slate-900 text-slate-100 p-3.5 rounded-xl border border-slate-800 font-sans text-xs leading-relaxed break-words shadow-xs">
              {{ selectedRecordDetail.message }}
            </div>
          </div>

          <!-- 字段参数网格 -->
          <div
            class="grid grid-cols-2 gap-2.5 bg-slate-50 dark:bg-slate-950/60 p-3 rounded-xl border border-slate-200/80 dark:border-slate-800">
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">目标设备</span>
              <span class="font-bold text-slate-800 dark:text-slate-200 truncate block">
                {{ deviceName(selectedRecordDetail.deviceId) }}
              </span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">测点变量</span>
              <span class="font-mono font-bold text-indigo-600 dark:text-indigo-400 truncate block">
                {{ selectedRecordDetail.variableKey }}
              </span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">报警来源</span>
              <span class="font-bold text-slate-800 dark:text-slate-200">
                {{ SOURCE_LABEL[selectedRecordDetail.source] ?? selectedRecordDetail.source }}
              </span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">实际测量值</span>
              <span class="font-mono font-bold text-slate-800 dark:text-slate-200">
                {{ selectedRecordDetail.actualValue ?? '-' }}
              </span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">恢复时间</span>
              <span class="font-mono text-slate-800 dark:text-slate-200">
                {{ selectedRecordDetail.recoveredAt ? fmtTime(selectedRecordDetail.recoveredAt) : '持续中 (未恢复)' }}
              </span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">确认状态</span>
              <span class="font-bold"
                :class="selectedRecordDetail.acked ? 'text-emerald-600 dark:text-emerald-400' : 'text-amber-600 dark:text-amber-400'">
                {{ selectedRecordDetail.acked ? `已确认 (${selectedRecordDetail.ackedBy || '操作员'})` : '待确认' }}
              </span>
            </div>
            <div v-if="selectedRecordDetail.acked && selectedRecordDetail.ackedAt" class="col-span-2">
              <span class="text-[10px] text-slate-400 block mb-0.5">确认时间</span>
              <span class="font-mono text-slate-600 dark:text-slate-300">
                {{ fmtTime(selectedRecordDetail.ackedAt) }}
              </span>
            </div>
          </div>
        </div>

        <!-- 底部操作条 -->
        <div
          class="p-3 border-t border-slate-100 dark:border-slate-800 bg-slate-50 dark:bg-slate-950/80 flex items-center gap-2">
          <button @click="copyRecordDetail(selectedRecordDetail)"
            class="flex-1 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 font-bold text-xs text-slate-700 dark:text-slate-200 inline-flex items-center justify-center gap-1.5 cursor-pointer shadow-xs">
            <Copy class="w-3.5 h-3.5" />
            复制记录
          </button>
          <button v-if="!selectedRecordDetail.acked" @click="doAck(selectedRecordDetail)"
            class="flex-1 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-700 font-bold text-xs text-white inline-flex items-center justify-center gap-1.5 cursor-pointer shadow-xs">
            <Check class="w-3.5 h-3.5" />
            确认报警
          </button>
          <button @click="closeRecordDetail"
            class="py-2 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer">
            关闭
          </button>
        </div>
      </div>
    </div>
  </div>
</template>