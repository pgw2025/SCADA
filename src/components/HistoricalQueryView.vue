<script setup lang="ts">
import { ref, computed } from 'vue';
import { historicalRecords } from '../store';
import { 
  Search, 
  Download, 
  Calendar, 
  ListFilter, 
  TrendingUp, 
  ArrowRight, 
  ChevronLeft, 
  ChevronRight, 
  AlertCircle,
  FileSpreadsheet
} from 'lucide-vue-next';
import { HistoricalRecord } from '../types';

// Autocomplete list
const availableVariables = [
  { key: 'tank_level', label: '核心储水罐液位指标 (tank_level)', unit: '%' },
  { key: 'purified_level', label: '二级过滤沉淀池水位 (purified_level)', unit: '%' },
  { key: 'flow_rate', label: '总干线多相流瞬时流阻 (flow_rate)', unit: 'm³/h' },
  { key: 'boiler_temp', label: '热能锅炉受热壁核心温度 (boiler_temp)', unit: '℃' },
  { key: 'boiler_press', label: '汽包炉高压阻抗安全压力 (boiler_press)', unit: 'kPa' },
  { key: 'conveyor_speed', label: '物料流至分拣轮转速设定 (conveyor_speed)', unit: 'rpm' }
];

// Query state parameters
const searchInput = ref('');
const isInputFocused = ref(false);
const selectedTimeframe = ref<'hour' | 'day' | 'three_days' | 'month' | 'all'>('all');
const activeVariableKey = ref('tank_level'); // Defaults to first key
const currentPageNum = ref(1);
const pageSize = 15;

// Match helper for auto-complete dropdown
const filteredDropdownOptions = computed(() => {
  const query = searchInput.value.toLowerCase().trim();
  if (!query) return availableVariables;
  return availableVariables.filter(
    v => v.key.toLowerCase().includes(query) || v.label.toLowerCase().includes(query)
  );
});

const handleSelectVariableFromDropdown = (key: string) => {
  activeVariableKey.value = key;
  const match = availableVariables.find(v => v.key === key);
  searchInput.value = match ? match.label : key;
  isInputFocused.value = false;
  currentPageNum.value = 1;
};

// Filter dataset criteria
const filteredHistoricalRecords = computed(() => {
  let list = historicalRecords.value.filter(
    r => r.variableKey === activeVariableKey.value
  );

  const nowMills = Date.now();
  if (selectedTimeframe.value === 'hour') {
    // 1 hour scale
    const limit = nowMills - 60 * 60 * 1000;
    list = list.filter(r => new Date(r.timestamp).getTime() >= limit);
  } else if (selectedTimeframe.value === 'day') {
    // 24 hours
    const limit = nowMills - 24 * 60 * 60 * 1000;
    list = list.filter(r => new Date(r.timestamp).getTime() >= limit);
  } else if (selectedTimeframe.value === 'three_days') {
    // 72 hours
    const limit = nowMills - 3 * 24 * 60 * 60 * 1000;
    list = list.filter(r => new Date(r.timestamp).getTime() >= limit);
  } else if (selectedTimeframe.value === 'month') {
    // 30 days
    const limit = nowMills - 30 * 24 * 60 * 60 * 1000;
    list = list.filter(r => new Date(r.timestamp).getTime() >= limit);
  }

  // Sort timestamps chronological ascending for graphs, tabular goes descending
  return list;
});

// Chronological sorted for plotting
const chartDataPlotPoints = computed(() => {
  const records = [...filteredHistoricalRecords.value];
  records.sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime());
  // Take last 80 records max for chart performance
  return records.slice(-80);
});

// CSV Raw Exporter logic
const handleExportCSV = () => {
  const records = filteredHistoricalRecords.value;
  if (records.length === 0) {
    alert('当前无可供导出的历史序列记录！');
    return;
  }

  let csvContent = "\ufeff"; // BOM wrapper to support Excel utf-8 encoding natively on Windows
  csvContent += "唯一记录标识 ID,设备观测指标键 (Key),可读测位名称,物理测量值,捕获采集时间戳\n";

  records.forEach((r) => {
    csvContent += `"${r.id}","${r.variableKey}","${r.variableName}",${r.value},"${r.timestamp}"\n`;
  });

  const blobBytes = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
  const downloadLink = document.createElement("a");
  const url = URL.createObjectURL(blobBytes);
  downloadLink.setAttribute("href", url);
  downloadLink.setAttribute("download", `iota_scada_history_${activeVariableKey.value}_${selectedTimeframe.value}.csv`);
  document.body.appendChild(downloadLink);
  downloadLink.click();
  document.body.removeChild(downloadLink);
};

// Layout parameters for the adaptive line chart SVG
const svgWidth = 800;
const svgHeight = 240;
const paddingX = 50;
const paddingY = 30;

// Draw path commands
const svgChartPathsAndLabels = computed(() => {
  const pts = chartDataPlotPoints.value;
  if (pts.length < 2) return { linePath: '', areaPath: '', circles: [], yTicks: [], xTicks: [] };

  const values = pts.map(p => p.value);
  const minVal = Math.max(0, Math.min(...values) * 0.9);
  const maxVal = Math.max(...values) * 1.1;
  const valueDelta = (maxVal - minVal) || 1;

  // Generate mapping grid
  const getX = (idx: number) => paddingX + (idx / (pts.length - 1)) * (svgWidth - 2 * paddingX);
  const getY = (val: number) => svgHeight - paddingY - ((val - minVal) / valueDelta) * (svgHeight - 2 * paddingY);

  // Cubic bezier segment drawing
  let linePath = `M ${getX(0)} ${getY(pts[0].value)}`;
  for (let i = 0; i < pts.length - 1; i++) {
    const x1 = getX(i);
    const y1 = getY(pts[i].value);
    const x2 = getX(i + 1);
    const y2 = getY(pts[i + 1].value);
    // Smooth control points
    const cpX1 = x1 + (x2 - x1) / 3;
    const cpX2 = x2 - (x2 - x1) / 3;
    linePath += ` C ${cpX1} ${y1}, ${cpX2} ${y2}, ${x2} ${y2}`;
  }

  // Close area
  const areaPath = `${linePath} L ${getX(pts.length - 1)} ${svgHeight - paddingY} L ${getX(0)} ${svgHeight - paddingY} Z`;

  // Plot circular highlighting points (samples a fraction if list is dense to avoid visual noise)
  const circles: Array<{ x: number; y: number; val: number; label: string }> = [];
  const sampleInterval = Math.max(1, Math.floor(pts.length / 15));
  pts.forEach((p, idx) => {
    if (idx % sampleInterval === 0 || idx === pts.length - 1) {
      circles.push({
        x: getX(idx),
        y: getY(p.value),
        val: p.value,
        label: p.timestamp.slice(11, 16)
      });
    }
  });

  // Calculate ticks
  const yTicks: number[] = [];
  for (let i = 0; i <= 4; i++) {
    yTicks.push(minVal + (valueDelta * i) / 4);
  }

  return { linePath, areaPath, circles, yTicks, getX, getY };
});

// Paginated table lists
const totalRecordsCount = computed(() => filteredHistoricalRecords.value.length);
const totalPagesCount = computed(() => Math.ceil(totalRecordsCount.value / pageSize) || 1);

const paginatedTableRecords = computed(() => {
  const startIdx = (currentPageNum.value - 1) * pageSize;
  return filteredHistoricalRecords.value.slice(startIdx, startIdx + pageSize);
});

const variableUnit = computed(() => {
  const match = availableVariables.find(v => v.key === activeVariableKey.value);
  return match ? match.unit : '';
});

// Sync search bar label to preselected indicator on startup
const syncInputLabel = () => {
  const match = availableVariables.find(v => v.key === activeVariableKey.value);
  if (match) searchInput.value = match.label;
};
syncInputLabel();
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] select-none bg-slate-50 overflow-y-auto">
    
    <!-- Top banner -->
    <div class="bg-white p-5 border-b border-slate-200 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 tracking-tight flex items-center gap-2">
          <Calendar class="w-5 h-5 text-[#1890ff]" />
          时序大数据库遥测指标历史记录多向追踪中心
        </h2>
        <p class="text-xs text-slate-500 font-sans">
          支持实时、历史分列聚合。通过自定义时间筛选和模糊感知自动匹配输入框，检索时序参数，渲染指标拟合曲线图，并导出标准的时序分卷表格。
        </p>
      </div>

      <!-- Export CSV button -->
      <button 
        @click="handleExportCSV"
        class="font-bold text-xs bg-emerald-600 hover:bg-emerald-700 text-white px-4 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-xs active:translate-y-0.5"
      >
        <FileSpreadsheet class="w-4 h-4" />
        导出 CSV 结果集 (.csv)
      </button>
    </div>

    <!-- Query toolbar and autocomplete center -->
    <div class="p-6 bg-slate-50 border-b border-slate-200/60 flex flex-col xl:flex-row items-stretch xl:items-center gap-4 text-left select-none relative z-30">
      
      <!-- Fuzzy autocomplete variable -->
      <div class="w-full xl:w-[420px] relative">
        <label class="block text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-1.5 font-sans">
          输入物理通道标识 (自动匹配/完成)
        </label>
        
        <div class="relative">
          <input 
            v-model="searchInput"
            @focus="isInputFocused = true"
            @blur="setTimeout(() => isInputFocused = false, 250)"
            type="text"
            placeholder="输入如: tank_level 或 储水罐"
            class="w-full bg-white border border-slate-200 rounded-lg p-2.5 pl-9 text-xs text-slate-800 font-bold focus:outline-none focus:border-[#1890ff] shadow-xs"
          />
          <Search class="absolute left-3 top-3 w-4 h-4 text-slate-400" />
        </div>

        <!-- Popover drop list -->
        <div 
          v-if="isInputFocused" 
          class="absolute left-0 right-0 top-[58px] bg-white border border-slate-200 rounded-lg shadow-xl max-h-56 overflow-y-auto z-40 p-1 divide-y divide-slate-50"
        >
          <div 
            v-for="item in filteredDropdownOptions" 
            :key="item.key"
            @mousedown="handleSelectVariableFromDropdown(item.key)"
            class="p-2.5 hover:bg-sky-50 text-[11px] text-slate-700 font-bold cursor-pointer flex justify-between items-center transition-colors rounded-md"
          >
            <span>{{ item.label }}</span>
            <span class="text-[9px] bg-indigo-50 text-indigo-600 px-1.5 py-0.5 rounded-md font-mono">{{ item.key }}</span>
          </div>

          <div v-if="filteredDropdownOptions.length === 0" class="p-4 text-center text-slate-400 text-xs">
            暂无匹配寄存器项，您可以继续输入
          </div>
        </div>
      </div>

      <!-- Time range filter -->
      <div class="flex-1 flex flex-col md:flex-row gap-4 items-stretch md:items-end">
        <div class="flex-1">
          <label class="block text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-1.5 font-sans">
            快捷检索时间域
          </label>
          <div class="grid grid-cols-5 bg-white border border-slate-200 rounded-lg p-0.5 font-bold text-[11px] shadow-xs text-center">
            <button 
              @click="selectedTimeframe = 'hour'; currentPageNum = 1"
              class="py-2 rounded-md transition-all cursor-pointer"
              :class="selectedTimeframe === 'hour' ? 'bg-slate-900 text-white font-bold' : 'text-slate-500 hover:text-slate-800'"
            >
              最近1小时
            </button>
            <button 
              @click="selectedTimeframe = 'day'; currentPageNum = 1"
              class="py-2 rounded-md transition-all cursor-pointer"
              :class="selectedTimeframe === 'day' ? 'bg-slate-900 text-white font-bold' : 'text-slate-500 hover:text-slate-800'"
            >
              最近1天
            </button>
            <button 
              @click="selectedTimeframe = 'three_days'; currentPageNum = 1"
              class="py-2 rounded-md transition-all cursor-pointer"
              :class="selectedTimeframe === 'three_days' ? 'bg-slate-900 text-white font-bold' : 'text-slate-500 hover:text-slate-800'"
            >
              最近3天
            </button>
            <button 
              @click="selectedTimeframe = 'month'; currentPageNum = 1"
              class="py-2 rounded-md transition-all cursor-pointer"
              :class="selectedTimeframe === 'month' ? 'bg-slate-900 text-white font-bold' : 'text-slate-500 hover:text-slate-800'"
            >
              最近1月
            </button>
            <button 
              @click="selectedTimeframe = 'all'; currentPageNum = 1"
              class="py-2 rounded-md transition-all cursor-pointer"
              :class="selectedTimeframe === 'all' ? 'bg-slate-900 text-white font-bold' : 'text-slate-500 hover:text-slate-800'"
            >
              所有数据
            </button>
          </div>
        </div>
      </div>

    </div>

    <!-- Live Graphic Chart plotting variables -->
    <div class="px-6 pb-6 shrink-0 text-left">
      <div class="bg-white border border-slate-200 rounded-xl p-5 shadow-xs overflow-hidden">
        
        <div class="flex items-center justify-between mb-4 border-b border-slate-100 pb-3">
          <div class="flex items-center gap-2">
            <TrendingUp class="w-4 h-4 text-emerald-500 animate-pulse" />
            <span class="text-xs font-bold text-slate-800 uppercase tracking-tight">
              观测参数波动趋势图：<span class="text-indigo-600 font-mono text-[11px] font-bold">{{ activeVariableKey }} ({{ variableUnit }})</span>
            </span>
          </div>
          <span class="text-[10px] text-slate-400 font-mono font-medium">绘制时序样点：{{ chartDataPlotPoints.length }} 个</span>
        </div>

        <!-- Custom SVG line/area smooth path plotter -->
        <div class="w-full relative bg-slate-50/50 rounded-xl border border-slate-100 p-2 overflow-x-auto">
          <svg 
            v-if="chartDataPlotPoints.length >= 2"
            :viewBox="`0 0 ${svgWidth} ${svgHeight}`" 
            class="w-full h-auto min-w-[640px]"
          >
            <!-- Draw Grids -->
            <line 
              v-for="(tick, idx) in svgChartPathsAndLabels.yTicks" 
              :key="idx"
              :x1="paddingX" 
              :y1="svgChartPathsAndLabels.getY(tick)" 
              :x2="svgWidth - paddingX" 
              :y2="svgChartPathsAndLabels.getY(tick)" 
              stroke="#e2e8f0" 
              stroke-width="1"
              stroke-dasharray="3,3"
            />

            <!-- Y Axis indicators -->
            <text 
              v-for="(tick, idx) in svgChartPathsAndLabels.yTicks" 
              :key="'text-' + idx"
              :x="paddingX - 10" 
              :y="svgChartPathsAndLabels.getY(tick) + 4" 
              fill="#94a3b8" 
              font-family="monospace"
              font-size="9"
              font-weight="bold"
              text-anchor="end"
            >
              {{ tick.toFixed(1) }}
            </text>

            <!-- Gradient shaders -->
            <defs>
              <linearGradient id="areaGrad" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stop-color="#3b82f6" stop-opacity="0.25" />
                <stop offset="100%" stop-color="#3b82f6" stop-opacity="0.0" />
              </linearGradient>
            </defs>

            <!-- Draw Area -->
            <path :d="svgChartPathsAndLabels.areaPath" fill="url(#areaGrad)" />

            <!-- Draw Lines -->
            <path 
              :d="svgChartPathsAndLabels.linePath" 
              fill="none" 
              stroke="#2563eb" 
              stroke-width="2.2" 
              stroke-linecap="round"
            />

            <!-- Circles highlights with labels -->
            <g v-for="(pt, idx) in svgChartPathsAndLabels.circles" :key="idx">
              <circle 
                :cx="pt.x" 
                :cy="pt.y" 
                r="3.5" 
                fill="#ffffff" 
                stroke="#2563eb" 
                stroke-width="2" 
              />
              <text 
                :x="pt.x" 
                :y="pt.y - 8" 
                fill="#1e293b" 
                font-size="8.5" 
                font-family="monospace"
                font-weight="bold"
                text-anchor="middle"
              >
                {{ pt.val.toFixed(1) }}
              </text>
              <text 
                :x="pt.x" 
                :y="svgHeight - paddingY + 12" 
                fill="#94a3b8" 
                font-size="8" 
                font-family="monospace"
                text-anchor="middle"
              >
                {{ pt.label }}
              </text>
            </g>
          </svg>

          <!-- Standard empty state -->
          <div v-else class="py-16 text-center text-slate-400 flex flex-col items-center justify-center gap-2">
            <AlertCircle class="w-8 h-8 text-slate-300 animate-bounce" />
            <span class="text-xs">
              在选定的时间框架下，未抓取到物理变量 <code class="bg-slate-100 p-0.5 rounded font-mono">{{ activeVariableKey }}</code> 的任何时序。
            </span>
          </div>
        </div>

      </div>
    </div>

    <!-- Tabular detailed log lists with paginations -->
    <div class="px-6 pb-6 select-none text-left flex-1 min-h-[300px] flex">
      <div class="w-full bg-white border border-slate-200 rounded-xl overflow-hidden flex flex-col justify-between">
        <div class="overflow-x-auto flex-1">
          <table class="w-full text-left text-xs font-sans">
            <thead class="bg-slate-50 border-b border-slate-100 text-slate-400 font-bold uppercase tracking-wider text-[10px]">
              <tr>
                <th class="p-3.5 pl-5">采集项 ID</th>
                <th class="p-3.5">变量键名 (Variable Key)</th>
                <th class="p-3.5">物标测位中文注释</th>
                <th class="p-3.5 font-mono">核算实测值</th>
                <th class="p-3.5 pr-5 text-right">时间戳 (采样时域)</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100 text-slate-700">
              <tr v-for="rec in paginatedTableRecords" :key="rec.id" class="hover:bg-slate-50/40 transition-colors">
                <td class="p-3.5 pl-5 font-mono font-medium text-slate-400">{{ rec.id }}</td>
                <td class="p-3.5 font-bold font-mono text-slate-800">{{ rec.variableKey }}</td>
                <td class="p-3.5 font-medium text-slate-500">{{ rec.variableName }}</td>
                <td class="p-3.5 font-bold font-mono text-indigo-650">
                  {{ rec.value }} <span class="text-[9px] font-sans text-slate-400">{{ variableUnit }}</span>
                </td>
                <td class="p-3.5 pr-5 text-right font-mono text-slate-400">{{ rec.timestamp }}</td>
              </tr>

              <tr v-if="filteredHistoricalRecords.length === 0">
                <td colspan="5" class="text-center py-16 text-slate-400">
                  没有符合检索过滤条件的物标时序块。
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Autopager controls -->
        <div v-if="totalPagesCount > 1" class="p-4 border-t border-slate-100 bg-slate-50 flex items-center justify-between text-xs font-medium shrink-0">
          <span class="text-slate-400">
            共计 <b class="text-slate-700 font-bold">{{ totalRecordsCount }}</b> 采样槽 · 分页 {{ currentPageNum }} / {{ totalPagesCount }}
          </span>

          <div class="flex items-center gap-1">
            <button 
              @click="currentPageNum = Math.max(1, currentPageNum - 1)"
              :disabled="currentPageNum === 1"
              class="p-1 px-2.5 rounded-lg border border-slate-200 bg-white hover:bg-slate-50 text-slate-600 font-bold inline-flex items-center gap-1 cursor-pointer disabled:opacity-45 disabled:cursor-not-allowed select-none active:scale-95 transition-all text-[11px]"
            >
              <ChevronLeft class="w-3.5 h-3.5" /> 上一页
            </button>
            
            <button 
              @click="currentPageNum = Math.min(totalPagesCount, currentPageNum + 1)"
              :disabled="currentPageNum === totalPagesCount"
              class="p-1 px-2.5 rounded-lg border border-slate-200 bg-white hover:bg-slate-50 text-slate-600 font-bold inline-flex items-center gap-1 cursor-pointer disabled:opacity-45 disabled:cursor-not-allowed select-none active:scale-95 transition-all text-[11px]"
            >
              下一页 <ChevronRight class="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </div>
    </div>

  </div>
</template>
