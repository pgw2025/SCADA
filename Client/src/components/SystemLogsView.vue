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
  X
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
  // 日期范围：起始取当天 00:00，结束取当天 23:59:59（含整天）
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

const toggleLevel = (lv: string) => {
  const idx = selectedLevels.value.indexOf(lv);
  if (idx >= 0) selectedLevels.value.splice(idx, 1);
  else selectedLevels.value.push(lv);
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
      // 新日志插到顶部；本地缓冲最多 300 条，避免无限增长
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
    <div class="bg-white dark:bg-slate-900 p-4 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-3 transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Terminal class="w-4 h-4 text-slate-600 dark:text-slate-300" />
          系统日志
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          统一展示运行 / 操作 / 安全日志，支持分级、搜索、时间段查询与实时推送。
        </p>
      </div>

      <div class="flex items-center gap-2 flex-wrap self-end md:self-center">
        <!-- 实时推送开关 -->
        <button
          @click="toggleLive"
          :disabled="liveConnecting"
          class="font-bold text-xs px-3 py-1.5 rounded-lg inline-flex items-center gap-1.5 border cursor-pointer transition-colors disabled:opacity-50"
          :class="liveEnabled
            ? 'text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-900 bg-emerald-50 dark:bg-emerald-950/30'
            : 'text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 bg-white dark:bg-slate-900'"
        >
          <Radio class="w-4 h-4" :class="liveEnabled ? 'animate-pulse' : ''" />
          {{ liveConnecting ? '连接中…' : (liveEnabled ? '实时推送中' : '开启实时推送') }}
        </button>

        <button
          @click="exportLogs"
          class="font-bold text-xs text-indigo-600 dark:text-indigo-400 border border-indigo-100 dark:border-indigo-900/40 hover:bg-indigo-50 dark:hover:bg-indigo-950/40 bg-indigo-50/50 dark:bg-indigo-950/20 px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors"
        >
          <Download class="w-4 h-4" />
          导出
        </button>
        <button
          @click="clearLogs"
          class="font-bold text-xs text-rose-600 dark:text-rose-400 border border-rose-100 dark:border-rose-900/40 hover:bg-rose-50 dark:hover:bg-rose-950/40 bg-rose-50/50 dark:bg-rose-950/20 px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors"
        >
          <Trash2 class="w-4 h-4" />
          清理
        </button>
      </div>
    </div>

    <!-- Filter bar -->
    <div class="bg-white dark:bg-slate-900 px-4 py-3 border-b border-slate-100 dark:border-slate-800 shadow-xs shrink-0 flex flex-col gap-3 transition-colors">
      <!-- 分类 Tab -->
      <div class="flex items-center gap-1.5 flex-wrap">
        <button
          v-for="c in categories"
          :key="c.value"
          @click="category = c.value"
          class="px-2.5 py-1 rounded-lg text-xs font-bold transition-all cursor-pointer border"
          :class="category === c.value
            ? 'bg-indigo-600 text-white border-indigo-600 shadow-sm'
            : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-700 dark:hover:text-slate-200'"
        >
          {{ c.label }}
        </button>
        <div class="mx-2 w-px h-4 bg-slate-200 dark:bg-slate-700" />
        <!-- 级别多选 -->
        <button
          v-for="lv in levelOptions"
          :key="lv.value"
          @click="toggleLevel(lv.value)"
          class="px-2.5 py-1 rounded-lg text-xs font-bold transition-all cursor-pointer border"
          :class="selectedLevels.includes(lv.value)
            ? 'bg-sky-600 text-white border-sky-600 shadow-sm'
            : 'bg-white dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:text-slate-700 dark:hover:text-slate-200'"
        >
          {{ lv.label }}
        </button>
      </div>

      <!-- 搜索 + 时间段 + 操作 -->
      <div class="flex items-center gap-2 flex-wrap">
        <div class="relative flex-1 min-w-[180px]">
          <Search class="absolute left-2.5 top-2 ml-0.5 w-4 h-4 text-slate-400" />
          <input
            v-model="keyword"
            type="text"
            placeholder="搜索日志内容 / 来源 / 操作人..."
            @keyup.enter="applyQuery"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 focus:bg-white dark:focus:bg-slate-900 text-xs pl-9 pr-3 py-1.5 rounded-lg outline-none text-slate-800 dark:text-white focus:border-[#1890ff]"
          />
        </div>

        <label class="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 shrink-0">
          起
          <input
            v-model="startDate"
            type="date"
            class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-xs px-2 py-1.5 rounded-lg outline-none text-slate-800 dark:text-white focus:border-[#1890ff]"
          />
        </label>
        <label class="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 shrink-0">
          止
          <input
            v-model="endDate"
            type="date"
            class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-xs px-2 py-1.5 rounded-lg outline-none text-slate-800 dark:text-white focus:border-[#1890ff]"
          />
        </label>

        <button
          @click="applyQuery"
          class="font-bold text-xs bg-indigo-600 hover:bg-indigo-700 text-white px-4 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors"
        >
          <Search class="w-3.5 h-3.5" />
          查询
        </button>
        <button
          @click="resetFilters"
          class="font-bold text-xs text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 bg-white dark:bg-slate-900 px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-colors"
        >
          <RefreshCw class="w-3.5 h-3.5" />
          重置
        </button>
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
          <span
            class="shrink-0 px-1.5 py-0.5 rounded text-[9px] font-bold uppercase tracking-wide"
            :class="levelBadge(l.level)"
          >{{ l.level }}</span>
          <span class="shrink-0 text-sky-300/80">{{ l.source }}</span>
          <p class="flex-1 break-all">{{ l.content }}</p>
        </div>
        <div v-if="liveLogs.length === 0" class="text-slate-500 text-xs">等待日志推送...</div>
      </div>
    </div>

    <!-- 日志表格 -->
    <div class="flex-1 p-4 overflow-auto">
      <div class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 overflow-hidden shadow-sm flex flex-col min-h-[300px]">
        <div class="bg-slate-50 dark:bg-slate-950 px-4 py-2 flex items-center justify-between border-b border-slate-200 dark:border-slate-800 text-xs text-slate-500 dark:text-slate-400 font-mono">
          <div class="flex items-center gap-2">
            <Terminal class="w-3.5 h-3.5" />
            <span>系统日志记录（共 {{ total }} 条）</span>
            <span v-if="isLoading" class="text-indigo-400 animate-pulse">加载中...</span>
          </div>
          <button
            @click="fetchLogs"
            class="inline-flex items-center gap-1 text-slate-400 hover:text-indigo-500 cursor-pointer transition-colors"
            title="刷新"
          >
            <RefreshCw class="w-3.5 h-3.5" />
            刷新
          </button>
        </div>

        <div v-if="loadError" class="px-4 py-3 text-xs text-rose-600 dark:text-rose-400 bg-rose-50 dark:bg-rose-950/40 border-b border-rose-100 dark:border-rose-900">
          {{ loadError }}
        </div>

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
            <tr v-for="l in items" :key="l.id" class="align-top hover:bg-slate-50 dark:hover:bg-slate-800/40 transition-colors">
              <td class="px-3 py-2 text-slate-500 dark:text-slate-400 whitespace-nowrap font-mono">{{ fmtTime(l.timestamp) }}</td>
              <td class="px-3 py-2 whitespace-nowrap">
                <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border" :class="categoryBadge(l.category)">{{ l.category }}</span>
              </td>
              <td class="px-3 py-2 whitespace-nowrap">
                <span class="inline-block px-1.5 py-0.5 rounded text-[10px] font-bold border" :class="levelBadge(l.level)">{{ l.level }}</span>
              </td>
              <td class="px-3 py-2 whitespace-nowrap text-slate-600 dark:text-slate-300 max-w-[160px] truncate" :title="l.source">{{ l.source }}</td>
              <td class="px-3 py-2 whitespace-nowrap text-slate-500 dark:text-slate-400">{{ l.operation || '-' }}</td>
              <td class="px-3 py-2 whitespace-nowrap text-slate-600 dark:text-slate-300">{{ l.operator || '-' }}</td>
              <td class="px-3 py-2 whitespace-nowrap text-slate-500 dark:text-slate-400 font-mono">{{ l.ipAddress || '-' }}</td>
              <td class="px-3 py-2 text-slate-700 dark:text-slate-200 break-all min-w-[240px]">{{ l.content }}</td>
            </tr>

            <tr v-if="!isLoading && items.length === 0">
              <td colspan="8">
                <div class="flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 py-16 gap-2">
                  <Terminal class="w-8 h-8 text-slate-300 dark:text-slate-600 animate-pulse" />
                  <p class="text-xs font-sans">暂无匹配的日志记录</p>
                  <button
                    v-if="category || selectedLevels.length || keyword || startDate || endDate"
                    @click="resetFilters"
                    class="inline-flex items-center gap-1 text-xs text-indigo-500 hover:text-indigo-600 cursor-pointer"
                  >
                    <X class="w-3.5 h-3.5" />
                    清除筛选条件
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>

        <!-- 分页 -->
        <div class="px-4 py-2.5 border-t border-slate-200 dark:border-slate-800 flex items-center justify-between gap-3 bg-slate-50 dark:bg-slate-950">
          <div class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
            <select
              v-model.number="pageSize"
              @change="applyQuery"
              class="bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-xs px-1.5 py-1 rounded-lg outline-none cursor-pointer"
            >
              <option :value="20">20 / 页</option>
              <option :value="50">50 / 页</option>
              <option :value="100">100 / 页</option>
            </select>
            <span>第 {{ pageIndex }} / {{ totalPages }} 页</span>
          </div>
          <div class="flex items-center gap-1.5">
            <button
              @click="changePage(-1)"
              :disabled="pageIndex <= 1"
              class="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-white dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
            >
              <ChevronLeft class="w-4 h-4" />
            </button>
            <button
              @click="changePage(1)"
              :disabled="pageIndex >= totalPages"
              class="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-white dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
            >
              <ChevronRight class="w-4 h-4" />
            </button>
          </div>
        </div>
      </div>
    </div>

  </div>
</template>
