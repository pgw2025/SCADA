<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import {
  Terminal,
  Search,
  Trash2,
  Download,
  RefreshCw,
  Radio,
  Pause,
  ChevronLeft,
  ChevronRight,
  ChevronDown,
  ChevronUp,
  X,
  LayoutList,
  AlignJustify,
  Copy,
  Check,
  Eye
} from 'lucide-vue-next';
import { systemConfig, addLog } from '../store/index';
import { SystemLogRecord, SystemLogQuery } from '../types';
import { fetchSystemLogs, clearSystemLogs } from '../api/systemLogApi';
import { extractApiError, TOKEN_KEY } from '../api/http';
import { showToast } from '../services/toastService';

// ================= 过滤条件 =================
const categories = [
  { value: '', label: '全部' },
  { value: 'Runtime', label: '运行日志' },
  { value: 'Operation', label: '操作日志' },
  { value: 'Security', label: '安全日志' }
];
const levelOptions = [
  { value: 'Information', label: '信息' },
  { value: 'Warning', label: '警告' },
  { value: 'Error', label: '错误' },
  { value: 'Critical', label: '致命' }
];

const category = ref<string>('');
const selectedLevels = ref<string[]>([]);
const keyword = ref<string>('');
const startDate = ref<string>('');
const endDate = ref<string>('');

// ================= 移动端视图切换与抽屉 =================
// 'card': 方案1 自适应流式卡片；'compact': 方案2 紧凑列表+底部抽屉
const mobileViewMode = ref<'card' | 'compact'>(
  (localStorage.getItem('scada_log_mobile_view') as 'card' | 'compact') || 'card'
);
const setMobileViewMode = (mode: 'card' | 'compact') => {
  mobileViewMode.value = mode;
  localStorage.setItem('scada_log_mobile_view', mode);
};

// 选中的日志（用于底部抽屉详情）
const selectedLogDetail = ref<SystemLogRecord | null>(null);
const openLogDetail = (log: SystemLogRecord) => {
  selectedLogDetail.value = log;
};
const closeLogDetail = () => {
  selectedLogDetail.value = null;
};

// 卡片视图长文本展开/收起集合
const expandedLogIds = ref<Set<number>>(new Set());
const toggleExpandLog = (id: number) => {
  if (expandedLogIds.value.has(id)) expandedLogIds.value.delete(id);
  else expandedLogIds.value.add(id);
};
const isLogExpanded = (id: number) => expandedLogIds.value.has(id);

// 复制日志内容
const copySuccess = ref(false);
const copyLogContent = async (log: SystemLogRecord) => {
  const text = `[${fmtTime(log.timestamp)}] [${log.category}] [${log.level.toUpperCase()}] [${log.source}]\n` +
    `${log.operator ? `用户: ${log.operator} | IP: ${log.ipAddress || '-'} | 操作: ${log.operation || '-'}\n` : ''}` +
    `内容: ${log.content}`;
  try {
    await navigator.clipboard.writeText(text);
    copySuccess.value = true;
    showToast('日志信息已复制到剪贴板', 'success');
    setTimeout(() => { copySuccess.value = false; }, 2000);
  } catch {
    showToast('复制失败，请手动选择复制', 'warning');
  }
};

// ================= 查询与分页 =================
const items = ref<SystemLogRecord[]>([]);
const total = ref(0);
const pageIndex = ref(1);
const pageSize = ref(50);
const isLoading = ref(false);
const loadError = ref('');

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)));

const buildQuery = (): SystemLogQuery => {
  const q: SystemLogQuery = {
    pageIndex: pageIndex.value,
    pageSize: pageSize.value
  };
  if (category.value) q.category = category.value;
  if (selectedLevels.value.length) q.levels = [...selectedLevels.value];
  if (keyword.value.trim()) q.keyword = keyword.value.trim();
  if (startDate.value) q.startTime = `${startDate.value}T00:00:00`;
  if (endDate.value) q.endTime = `${endDate.value}T23:59:59`;
  return q;
};

const fetchLogs = async () => {
  if (systemConfig.value.isSimulationActive) {
    items.value = [];
    total.value = 0;
    return;
  }
  isLoading.value = true;
  loadError.value = '';
  try {
    const { data } = await fetchSystemLogs(buildQuery());
    items.value = data?.items ?? [];
    total.value = data?.total ?? 0;
  } catch (e: any) {
    loadError.value = extractApiError(e);
    showToast(loadError.value, 'error');
  } finally {
    isLoading.value = false;
  }
};

const applyQuery = () => {
  pageIndex.value = 1;
  fetchLogs();
};

const resetFilters = () => {
  category.value = '';
  selectedLevels.value = [];
  keyword.value = '';
  startDate.value = '';
  endDate.value = '';
  applyQuery();
};

const selectCategory = (v: string) => {
  category.value = v;
  applyQuery();
};

const toggleLevel = (lv: string) => {
  const idx = selectedLevels.value.indexOf(lv);
  if (idx >= 0) selectedLevels.value.splice(idx, 1);
  else selectedLevels.value.push(lv);
  applyQuery();
};

const changePage = (delta: number) => {
  const next = pageIndex.value + delta;
  if (next < 1 || next > totalPages.value) return;
  pageIndex.value = next;
  fetchLogs();
};

// ================= 实时推送（P6） =================
const liveEnabled = ref(false);
const liveLogs = ref<SystemLogRecord[]>([]);
const liveConnecting = ref(false);
let liveConnection: HubConnection | null = null;

const startLive = async () => {
  if (systemConfig.value.isSimulationActive) {
    showToast('仿真模式下不启用实时日志推送', 'warning');
    return;
  }
  liveConnecting.value = true;
  try {
    const conn = new HubConnectionBuilder()
      .withUrl(`${systemConfig.value.backendApiUrl}/hubs/systemlog`, {
        accessTokenFactory: () => localStorage.getItem(TOKEN_KEY) || ''
      })
      .withAutomaticReconnect()
      .build();

    conn.on('ReceiveLog', (log: SystemLogRecord) => {
      if (!log || log.category !== 'Runtime') return;
      liveLogs.value.unshift({ ...log, timestamp: new Date(log.timestamp).toLocaleString() });
      if (liveLogs.value.length > 300) liveLogs.value.length = 300;
    });

    conn.onclose(() => {
      liveEnabled.value = false;
      liveConnecting.value = false;
      addLog('系统日志', '实时日志推送已断开', 'warning');
    });

    await conn.start();
    liveConnection = conn;
    liveEnabled.value = true;
    liveConnecting.value = false;
    addLog('系统日志', '实时日志推送已连接（/hubs/systemlog）', 'normal');
  } catch (e: any) {
    liveConnecting.value = false;
    showToast(`实时日志连接失败：${e.message}`, 'error');
  }
};

const stopLive = async () => {
  if (liveConnection) {
    try { await liveConnection.stop(); } catch { /* 忽略 */ }
    liveConnection = null;
  }
  liveEnabled.value = false;
  liveConnecting.value = false;
};

const toggleLive = () => {
  if (liveEnabled.value) stopLive();
  else startLive();
};

// ================= 导出 =================
const exportLogs = () => {
  const rows = items.value.map(l =>
    `[${fmtTime(l.timestamp)}] [${l.category}] [${l.level.toUpperCase()}] [${l.source}]` +
    `${l.operator ? ` [操作:${l.operation ?? ''}] [用户:${l.operator}] [IP:${l.ipAddress ?? ''}]` : ''} ${l.content}`
  );
  if (rows.length === 0) {
    showToast('当前没有可导出的日志', 'warning');
    return;
  }
  const blob = new Blob([rows.join('\n')], { type: 'text/plain;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `scada_system_logs_${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}.log`;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
};

// ================= 清理 =================
const clearLogs = async () => {
  if (!startDate.value && !endDate.value) {
    showToast('请先选择时间范围后再清理，防止误删全部日志', 'warning');
    return;
  }
  if (!confirm(`确定清理「${category.value ? category.value : '全部'}」分类在所选时间范围内的日志吗？该操作不可恢复！`)) return;
  try {
    const { data } = await clearSystemLogs({
      category: category.value || undefined,
      startTime: startDate.value ? `${startDate.value}T00:00:00` : null,
      endTime: endDate.value ? `${endDate.value}T23:59:59` : null
    });
    showToast(data?.message ?? '清理完成', 'success');
    fetchLogs();
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  }
};

// ================= 展示辅助 =================
const fmtTime = (ts: string) => {
  if (!ts) return '';
  const d = new Date(ts);
  if (isNaN(d.getTime())) return ts;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
};

const categoryBadge = (cat: string) => {
  switch (cat) {
    case 'Runtime': return 'bg-sky-100 text-sky-700 dark:bg-sky-900/40 dark:text-sky-300 border-sky-200 dark:border-sky-800';
    case 'Operation': return 'bg-violet-100 text-violet-700 dark:bg-violet-900/40 dark:text-violet-300 border-violet-200 dark:border-violet-800';
    case 'Security': return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800';
    default: return 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300 border-slate-200 dark:border-slate-700';
  }
};

const levelBadge = (lvl: string) => {
  switch (lvl) {
    case 'Critical': return 'bg-rose-100 text-rose-700 dark:bg-rose-900/50 dark:text-rose-300 border-rose-200 dark:border-rose-800';
    case 'Error': return 'bg-red-100 text-red-700 dark:bg-red-900/50 dark:text-red-300 border-red-200 dark:border-red-800';
    case 'Warning': return 'bg-amber-100 text-amber-700 dark:bg-amber-900/50 dark:text-amber-300 border-amber-200 dark:border-amber-800';
    case 'Information': return 'bg-sky-100 text-sky-700 dark:bg-sky-900/40 dark:text-sky-300 border-sky-200 dark:border-sky-800';
    default: return 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300 border-slate-200 dark:border-slate-700';
  }
};

onMounted(() => { fetchLogs(); });
onBeforeUnmount(() => { stopLive(); });
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">

    <!-- Header -->
    <div
      class="bg-white dark:bg-slate-900 p-3.5 sm:p-4 border-b border-slate-200 dark:border-slate-800 shadow-2xs shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-3 transition-colors">
      <div class="flex items-center justify-between gap-2">
        <div class="space-y-0.5">
          <h2
            class="font-bold text-sm sm:text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
            <Terminal class="w-4 h-4 text-slate-600 dark:text-slate-300" />
            系统日志
          </h2>
          <p class="text-[11px] sm:text-xs text-slate-500 dark:text-slate-400 font-sans line-clamp-1">
            统一展示运行 / 操作 / 安全日志，支持分级、搜索、时间段查询与实时推送。
          </p>
        </div>

        <!-- 手机端视图模式切换按钮（仅在移动端小屏显示） -->
        <div
          class="flex md:hidden items-center p-0.5 rounded-lg bg-slate-100 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shrink-0">
          <button type="button" @click="setMobileViewMode('card')"
            class="px-2 py-1 rounded-md text-xs font-bold flex items-center gap-1 transition-all cursor-pointer" :class="mobileViewMode === 'card'
              ? 'bg-white dark:bg-slate-900 text-[#1890ff] shadow-xs'
              : 'text-slate-400 hover:text-slate-600 dark:hover:text-slate-200'" title="方案1：自适应卡片流">
            <LayoutList class="w-3.5 h-3.5" />
            <span>卡片</span>
          </button>
          <button type="button" @click="setMobileViewMode('compact')"
            class="px-2 py-1 rounded-md text-xs font-bold flex items-center gap-1 transition-all cursor-pointer" :class="mobileViewMode === 'compact'
              ? 'bg-white dark:bg-slate-900 text-[#1890ff] shadow-xs'
              : 'text-slate-400 hover:text-slate-600 dark:hover:text-slate-200'" title="方案2：高密度摘要行">
            <AlignJustify class="w-3.5 h-3.5" />
            <span>紧凑</span>
          </button>
        </div>
      </div>

      <div class="flex items-center gap-2 flex-wrap self-end md:self-center">
        <!-- 实时推送开关 -->
        <button @click="toggleLive" :disabled="liveConnecting"
          class="font-bold text-xs px-2.5 sm:px-3 py-1.5 rounded-lg inline-flex items-center gap-1.5 border cursor-pointer transition-colors disabled:opacity-50"
          :class="liveEnabled
            ? 'text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-900 bg-emerald-50 dark:bg-emerald-950/30'
            : 'text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 bg-white dark:bg-slate-900'">
          <Radio class="w-3.5 h-3.5" :class="liveEnabled ? 'animate-pulse' : ''" />
          {{ liveConnecting ? '连接中…' : (liveEnabled ? '实时推送中' : '开启实时推送') }}
        </button>

        <button @click="exportLogs"
          class="font-bold text-xs text-indigo-600 dark:text-indigo-400 border border-indigo-100 dark:border-indigo-900/40 hover:bg-indigo-50 dark:hover:bg-indigo-950/40 bg-indigo-50/50 dark:bg-indigo-950/20 px-2.5 sm:px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors">
          <Download class="w-3.5 h-3.5" />
          导出
        </button>
        <button @click="clearLogs"
          class="font-bold text-xs text-rose-600 dark:text-rose-400 border border-rose-100 dark:border-rose-900/40 hover:bg-rose-50 dark:hover:bg-rose-950/40 bg-rose-50/50 dark:bg-rose-950/20 px-2.5 sm:px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors">
          <Trash2 class="w-3.5 h-3.5" />
          清理
        </button>
      </div>
    </div>

    <!-- Filter bar -->
    <div
      class="bg-white dark:bg-slate-900 px-3.5 sm:px-4 py-2.5 sm:py-3 border-b border-slate-100 dark:border-slate-800 shadow-2xs shrink-0 flex flex-col gap-2.5 sm:gap-3 transition-colors">
      <!-- 分类 Tab + 级别多选 -->
      <div class="flex items-center gap-1.5 overflow-x-auto pb-1 scrollbar-none">
        <button v-for="c in categories" :key="c.value" @click="selectCategory(c.value)"
          class="px-2.5 py-1 rounded-lg text-xs font-bold transition-all cursor-pointer border shrink-0 whitespace-nowrap"
          :class="category === c.value
            ? 'bg-indigo-600 text-white border-indigo-600 shadow-sm'
            : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-700 dark:hover:text-slate-200'">
          {{ c.label }}
        </button>
        <div class="mx-1.5 w-px h-4 bg-slate-200 dark:bg-slate-700 shrink-0" />
        <!-- 级别多选 -->
        <button v-for="lv in levelOptions" :key="lv.value" @click="toggleLevel(lv.value)"
          class="px-2.5 py-1 rounded-lg text-xs font-bold transition-all cursor-pointer border shrink-0 whitespace-nowrap"
          :class="selectedLevels.includes(lv.value)
            ? 'bg-sky-600 text-white border-sky-600 shadow-sm'
            : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-700 dark:hover:text-slate-200'">
          {{ lv.label }}
        </button>
      </div>

      <!-- 搜索 + 时间段 + 操作 -->
      <div class="flex flex-col sm:flex-row items-stretch sm:items-center gap-2">
        <div class="relative flex-1 min-w-0">
          <Search class="absolute left-2.5 top-2 ml-0.5 w-4 h-4 text-slate-400" />
          <input v-model="keyword" type="text" placeholder="搜索日志内容 / 来源 / 操作人..." @keyup.enter="applyQuery"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:bg-white dark:focus:bg-slate-900 text-xs pl-9 pr-3 py-1.5 rounded-lg outline-none text-slate-800 dark:text-white focus:border-[#1890ff]" />
        </div>

        <div class="flex items-center gap-2 overflow-x-auto">
          <label class="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 shrink-0">
            起
            <input v-model="startDate" type="date"
              class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-xs px-2 py-1.5 rounded-lg outline-none text-slate-800 dark:text-white focus:border-[#1890ff]" />
          </label>
          <label class="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 shrink-0">
            止
            <input v-model="endDate" type="date"
              class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-xs px-2 py-1.5 rounded-lg outline-none text-slate-800 dark:text-white focus:border-[#1890ff]" />
          </label>

          <button @click="applyQuery"
            class="font-bold text-xs bg-indigo-600 hover:bg-indigo-700 text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors shrink-0">
            <Search class="w-3.5 h-3.5" />
            查询
          </button>
          <button @click="resetFilters"
            class="font-bold text-xs text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 bg-white dark:bg-slate-900 px-2.5 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors shrink-0">
            <RefreshCw class="w-3.5 h-3.5" />
            重置
          </button>
        </div>
      </div>
    </div>

    <!-- 实时推送流 -->
    <div v-if="liveEnabled" class="bg-slate-950 px-4 py-2 border-b border-slate-800 shrink-0 max-h-48 overflow-y-auto">
      <div class="flex items-center gap-1.5 text-[10px] text-slate-400 font-mono mb-1">
        <span class="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
        <span class="font-bold text-emerald-400">LIVE</span>
        <span>实时运行日志推送（/hubs/systemlog）</span>
        <button @click="stopLive" class="ml-auto text-slate-400 hover:text-slate-200 cursor-pointer" title="停止实时推送">
          <Pause class="w-3.5 h-3.5" />
        </button>
      </div>
      <div class="space-y-1 font-mono text-[11px] leading-relaxed">
        <div v-for="(l, i) in liveLogs" :key="'live-' + i" class="flex items-start gap-2 text-slate-300">
          <span class="shrink-0 text-slate-500">{{ fmtTime(l.timestamp) }}</span>
          <span class="shrink-0 px-1.5 py-0.5 rounded text-[9px] font-bold uppercase tracking-wide"
            :class="levelBadge(l.level)">{{ l.level }}</span>
          <span class="shrink-0 text-sky-300/80">{{ l.source }}</span>
          <p class="flex-1 break-all">{{ l.content }}</p>
        </div>
        <div v-if="liveLogs.length === 0" class="text-slate-500 text-xs">等待日志推送...</div>
      </div>
    </div>

    <!-- 主展示区 -->
    <div class="flex-1 p-3 sm:p-4 overflow-auto">
      <div
        class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 overflow-hidden shadow-sm flex flex-col min-h-[300px]">

        <!-- 表格工具栏 -->
        <div
          class="bg-slate-50 dark:bg-slate-950 px-3.5 sm:px-4 py-2 flex items-center justify-between border-b border-slate-200 dark:border-slate-800 text-xs text-slate-500 dark:text-slate-400 font-mono">
          <div class="flex items-center gap-2">
            <Terminal class="w-3.5 h-3.5" />
            <span>日志记录（共 {{ total }} 条）</span>
            <span v-if="isLoading" class="text-indigo-400 animate-pulse">加载中...</span>
          </div>
          <div class="flex items-center gap-2">
            <button @click="fetchLogs"
              class="inline-flex items-center gap-1 text-slate-400 hover:text-indigo-500 cursor-pointer transition-colors"
              title="刷新">
              <RefreshCw class="w-3.5 h-3.5" />
              <span class="hidden sm:inline">刷新</span>
            </button>
          </div>
        </div>

        <div v-if="loadError"
          class="px-4 py-3 text-xs text-rose-600 dark:text-rose-400 bg-rose-50 dark:bg-rose-950/40 border-b border-rose-100 dark:border-rose-900">
          {{ loadError }}
        </div>

        <!-- ================= 1. 桌面端表格视图（>= md） ================= -->
        <div class="hidden md:block overflow-x-auto">
          <table class="w-full text-xs">
            <thead class="bg-slate-50 dark:bg-slate-950 text-slate-500 dark:text-slate-400">
              <tr class="text-left border-b border-slate-200 dark:border-slate-800">
                <th class="px-3 py-2 font-bold whitespace-nowrap">时间</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">分类</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">级别</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">来源</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">操作</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">操作人</th>
                <th class="px-3 py-2 font-bold whitespace-nowrap">IP</th>
                <th class="px-3 py-2 font-bold">内容</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100 dark:divide-slate-800/60">
              <tr v-for="l in items" :key="l.id"
                class="align-top hover:bg-slate-50 dark:hover:bg-slate-800/40 transition-colors">
                <td class="px-3 py-2 text-slate-500 dark:text-slate-400 whitespace-nowrap font-mono">{{
                  fmtTime(l.timestamp) }}</td>
                <td class="px-3 py-2 whitespace-nowrap">
                  <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border"
                    :class="categoryBadge(l.category)">{{ l.category }}</span>
                </td>
                <td class="px-3 py-2 whitespace-nowrap">
                  <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border"
                    :class="levelBadge(l.level)">{{ l.level }}</span>
                </td>
                <td class="px-3 py-2 whitespace-nowrap text-slate-600 dark:text-slate-300 max-w-[160px] truncate"
                  :title="l.source">{{ l.source }}</td>
                <td class="px-3 py-2 whitespace-nowrap text-slate-500 dark:text-slate-400">{{ l.operation || '-' }}</td>
                <td class="px-3 py-2 whitespace-nowrap text-slate-600 dark:text-slate-300">{{ l.operator || '-' }}</td>
                <td class="px-3 py-2 whitespace-nowrap text-slate-500 dark:text-slate-400 font-mono">{{ l.ipAddress ||
                  '-' }}</td>
                <td class="px-3 py-2 text-slate-700 dark:text-slate-200 break-all min-w-[240px]">{{ l.content }}</td>
              </tr>

              <tr v-if="!isLoading && items.length === 0">
                <td colspan="8">
                  <div class="flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 py-16 gap-2">
                    <Terminal class="w-8 h-8 text-slate-300 dark:text-slate-600 animate-pulse" />
                    <p class="text-xs font-sans">暂无匹配的日志记录</p>
                    <button v-if="category || selectedLevels.length || keyword || startDate || endDate"
                      @click="resetFilters"
                      class="inline-flex items-center gap-1 text-xs text-indigo-500 hover:text-indigo-600 cursor-pointer">
                      <X class="w-3.5 h-3.5" />
                      清除筛选条件
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- ================= 2. 移动端自适应视图（< md） ================= -->
        <div class="block md:hidden flex-1 overflow-y-auto">
          <!-- 空状态 -->
          <div v-if="!isLoading && items.length === 0"
            class="flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 py-16 gap-2">
            <Terminal class="w-8 h-8 text-slate-300 dark:text-slate-600 animate-pulse" />
            <p class="text-xs font-sans">暂无匹配的日志记录</p>
            <button v-if="category || selectedLevels.length || keyword || startDate || endDate" @click="resetFilters"
              class="inline-flex items-center gap-1 text-xs text-indigo-500 hover:text-indigo-600 cursor-pointer">
              <X class="w-3.5 h-3.5" />
              清除筛选条件
            </button>
          </div>

          <!-- 模式一：自适应流式卡片 (Card Feed) -->
          <div v-else-if="mobileViewMode === 'card'" class="p-3 space-y-2.5">
            <div v-for="l in items" :key="'card-' + l.id"
              class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-3 shadow-2xs space-y-2 text-left transition-all">
              <!-- 卡片头：级别 + 分类 + 时间 -->
              <div class="flex items-center justify-between gap-2">
                <div class="flex items-center gap-1.5 flex-wrap">
                  <span class="px-1.5 py-0.5 rounded text-[10px] font-bold border shrink-0"
                    :class="levelBadge(l.level)">{{ l.level }}</span>
                  <span class="px-1.5 py-0.5 rounded text-[10px] font-bold border shrink-0"
                    :class="categoryBadge(l.category)">{{ l.category }}</span>
                  <span class="text-[11px] font-semibold text-slate-700 dark:text-slate-200 truncate max-w-[130px]">{{
                    l.source }}</span>
                </div>
                <span class="text-[10px] font-mono text-slate-400 shrink-0">{{ fmtTime(l.timestamp).slice(5) }}</span>
              </div>

              <!-- 卡片正文：直接自适应完整展示或折叠 -->
              <div class="text-xs text-slate-800 dark:text-slate-100 break-words leading-relaxed font-sans">
                <p :class="isLogExpanded(l.id) ? '' : 'line-clamp-3'">
                  {{ l.content }}
                </p>
                <button v-if="l.content.length > 90" type="button" @click="toggleExpandLog(l.id)"
                  class="mt-1 text-[11px] text-indigo-600 dark:text-indigo-400 hover:underline font-bold inline-flex items-center gap-0.5 cursor-pointer">
                  <span>{{ isLogExpanded(l.id) ? '收起内容' : '展开完整内容' }}</span>
                  <ChevronUp v-if="isLogExpanded(l.id)" class="w-3 h-3" />
                  <ChevronDown v-else class="w-3 h-3" />
                </button>
              </div>

              <!-- 卡片底部元数据与操作 -->
              <div
                class="pt-2 border-t border-slate-100 dark:border-slate-800/80 flex items-center justify-between text-[11px] text-slate-500 dark:text-slate-400">
                <div class="flex items-center gap-2 truncate text-[10px]">
                  <span v-if="l.operator">用户: <strong class="text-slate-700 dark:text-slate-300 font-medium">{{
                      l.operator }}</strong></span>
                  <span v-if="l.ipAddress" class="font-mono">IP: {{ l.ipAddress }}</span>
                </div>
                <div class="flex items-center gap-2 shrink-0">
                  <button type="button" @click="copyLogContent(l)"
                    class="text-[11px] text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 font-medium inline-flex items-center gap-0.5 cursor-pointer"
                    title="复制日志文本">
                    <Copy class="w-3 h-3" />
                    <span>复制</span>
                  </button>
                  <button type="button" @click="openLogDetail(l)"
                    class="text-xs text-indigo-600 dark:text-indigo-400 hover:underline font-bold inline-flex items-center gap-0.5 cursor-pointer">
                    <Eye class="w-3.5 h-3.5" />
                    <span>详情</span>
                  </button>
                </div>
              </div>
            </div>
          </div>

          <!-- 模式二：高密度摘要行 (Compact Rows) -->
          <div v-else class="p-2 space-y-1.5">
            <div v-for="l in items" :key="'compact-' + l.id" @click="openLogDetail(l)"
              class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl px-3 py-2 shadow-2xs flex items-center justify-between gap-2.5 active:bg-slate-50 dark:active:bg-slate-800/80 cursor-pointer transition-colors text-left">
              <div class="flex items-center gap-2 min-w-0 flex-1">
                <!-- 呼吸标示灯 -->
                <span class="w-2 h-2 rounded-full shrink-0" :class="{
                  'bg-rose-500': l.level === 'Critical',
                  'bg-red-500': l.level === 'Error',
                  'bg-amber-500': l.level === 'Warning',
                  'bg-sky-500': l.level === 'Information'
                }"></span>
                <span class="px-1.5 py-0.5 rounded text-[10px] font-bold border shrink-0"
                  :class="levelBadge(l.level)">{{ l.level }}</span>
                <div class="min-w-0 flex-1">
                  <div class="text-xs text-slate-800 dark:text-slate-100 truncate font-medium">
                    {{ l.content }}
                  </div>
                  <div class="text-[10px] text-slate-400 flex items-center gap-1.5 truncate mt-0.5">
                    <span class="text-sky-600 dark:text-sky-400 font-semibold">{{ l.source }}</span>
                    <span v-if="l.operator">· {{ l.operator }}</span>
                    <span v-if="l.operation">· {{ l.operation }}</span>
                  </div>
                </div>
              </div>
              <div class="text-right shrink-0 flex items-center gap-1">
                <div class="text-[10px] font-mono text-slate-400">{{ fmtTime(l.timestamp).slice(11, 19) }}</div>
                <ChevronRight class="w-3.5 h-3.5 text-slate-300 dark:text-slate-600" />
              </div>
            </div>
          </div>
        </div>

        <!-- 分页 -->
        <div
          class="px-3.5 sm:px-4 py-2.5 border-t border-slate-200 dark:border-slate-800 flex items-center justify-between gap-3 bg-slate-50 dark:bg-slate-950 mt-auto shrink-0">
          <div class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
            <select v-model.number="pageSize" @change="applyQuery"
              class="bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-xs px-1.5 py-1 rounded-lg outline-none cursor-pointer">
              <option :value="20">20 / 页</option>
              <option :value="50">50 / 页</option>
              <option :value="100">100 / 页</option>
            </select>
            <span class="text-[11px] sm:text-xs">第 {{ pageIndex }} / {{ totalPages }} 页</span>
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

    <!-- ================= 移动端底部详情抽屉 (Bottom Sheet) ================= -->
    <div v-if="selectedLogDetail" class="fixed inset-0 z-50 overflow-hidden select-none">
      <!-- 背景遮罩 -->
      <div class="absolute inset-0 bg-slate-900/60 backdrop-blur-xs transition-opacity" @click="closeLogDetail" />

      <!-- 抽屉内容容器 -->
      <div
        class="fixed inset-x-0 bottom-0 z-50 max-h-[88vh] bg-white dark:bg-slate-900 rounded-t-2xl shadow-2xl flex flex-col overflow-hidden text-left border-t border-slate-200 dark:border-slate-800 animate-in slide-in-from-bottom duration-200">
        <!-- 抽屉顶部拖动条 -->
        <div class="w-12 h-1 bg-slate-300 dark:bg-slate-700 rounded-full mx-auto mt-2.5 mb-1" />

        <!-- 抽屉标题栏 -->
        <div
          class="px-4 py-3 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between shrink-0">
          <div class="flex items-center gap-2">
            <span class="px-2 py-0.5 rounded text-xs font-bold border" :class="levelBadge(selectedLogDetail.level)">
              {{ selectedLogDetail.level }}
            </span>
            <span class="px-2 py-0.5 rounded text-xs font-bold border"
              :class="categoryBadge(selectedLogDetail.category)">
              {{ selectedLogDetail.category }}
            </span>
            <span class="text-xs font-bold text-slate-900 dark:text-white">日志明细 #{{ selectedLogDetail.id }}</span>
          </div>
          <button type="button" @click="closeLogDetail"
            class="p-1.5 rounded-lg text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer">
            <X class="w-5 h-5" />
          </button>
        </div>

        <!-- 抽屉主体内容 -->
        <div class="p-4 overflow-y-auto space-y-3.5 text-xs">
          <!-- 结构化关键信息表格 -->
          <div
            class="grid grid-cols-2 gap-2.5 bg-slate-50 dark:bg-slate-950 p-3 rounded-xl border border-slate-200/80 dark:border-slate-800 text-[11px]">
            <div>
              <span class="text-slate-400 block text-[10px]">记录时间</span>
              <span class="font-mono font-medium text-slate-800 dark:text-slate-200">{{
                fmtTime(selectedLogDetail.timestamp) }}</span>
            </div>
            <div>
              <span class="text-slate-400 block text-[10px]">来源模块</span>
              <span class="font-medium text-slate-800 dark:text-slate-200">{{ selectedLogDetail.source }}</span>
            </div>
            <div>
              <span class="text-slate-400 block text-[10px]">操作类型</span>
              <span class="font-medium text-slate-800 dark:text-slate-200">{{ selectedLogDetail.operation || '—'
                }}</span>
            </div>
            <div>
              <span class="text-slate-400 block text-[10px]">操作人 / IP</span>
              <span class="font-mono text-slate-800 dark:text-slate-200">
                {{ selectedLogDetail.operator || '—' }} {{ selectedLogDetail.ipAddress ?
                  `(${selectedLogDetail.ipAddress})` : '' }}
              </span>
            </div>
          </div>

          <!-- 完整正文 / 堆栈区域 -->
          <div class="space-y-1.5">
            <div class="flex items-center justify-between">
              <span class="font-bold text-slate-700 dark:text-slate-300">日志正文与调用堆栈</span>
              <button type="button" @click="copyLogContent(selectedLogDetail)"
                class="text-[11px] text-indigo-600 dark:text-indigo-400 hover:underline font-bold inline-flex items-center gap-1 cursor-pointer">
                <Check v-if="copySuccess" class="w-3.5 h-3.5 text-emerald-500" />
                <Copy v-else class="w-3.5 h-3.5" />
                <span>{{ copySuccess ? '已复制' : '复制内容' }}</span>
              </button>
            </div>
            <div
              class="p-3 bg-slate-950 text-slate-200 rounded-xl font-mono text-[11px] leading-relaxed break-all max-h-56 overflow-y-auto select-text border border-slate-800 shadow-inner">
              {{ selectedLogDetail.content }}
            </div>
          </div>
        </div>

        <!-- 抽屉操作底栏 -->
        <div
          class="p-3 bg-slate-50 dark:bg-slate-950 border-t border-slate-200 dark:border-slate-800 flex gap-2 shrink-0">
          <button type="button" @click="copyLogContent(selectedLogDetail)"
            class="flex-1 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-700 text-white font-bold text-xs inline-flex items-center justify-center gap-1.5 cursor-pointer shadow-xs transition-colors">
            <Copy class="w-4 h-4" />
            <span>复制日志信息</span>
          </button>
          <button type="button" @click="closeLogDetail"
            class="px-4 py-2.5 rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-slate-700 dark:text-slate-200 font-bold text-xs cursor-pointer">
            关闭
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
