<script setup lang="ts">
import { ref, computed } from 'vue';
import { logs, addLog } from '../store/index';
import { 
  Terminal, 
  Search, 
  Trash2, 
  Download, 
  CheckCircle, 
  AlertTriangle, 
  Info,
  Calendar
} from 'lucide-vue-next';
import { SystemLog } from '../types';

// Query filters
const searchQuery = ref<string>('');
const levelFilter = ref<'ALL' | 'info' | 'normal' | 'warning'>('ALL');

// Computed list of logs filtered
const filteredLogs = computed(() => {
  return logs.value.filter((log) => {
    const matchesSearch = log.content.toLowerCase().includes(searchQuery.value.toLowerCase()) || 
                          log.source.toLowerCase().includes(searchQuery.value.toLowerCase());
    const matchesLevel = levelFilter.value === 'ALL' || log.level === levelFilter.value;
    return matchesSearch && matchesLevel;
  });
});

// Clear everything
const handleClearAllLogs = () => {
  if (confirm('确定要清空所有运行审计日志吗？清空后将重新从系统初始化开始录入。')) {
    logs.value = [];
    addLog('系统内核', '操作审计日志数据库已被管理员强制初始化清空', 'warning');
  }
};

// Export to text file
const handleExportLogs = () => {
  const content = logs.value.map(log => `[${log.timestamp}] [${log.level.toUpperCase()}] [${log.source}] ${log.content}`).join('\n');
  const blob = new Blob([content], { type: 'text/plain;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.setAttribute('href', url);
  link.setAttribute('download', `scada_system_audit_${Date.now()}.log`);
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
};

// Simulated mock warning triggers (for demo value)
const handleMockWarning = () => {
  const warnings = [
    { src: 'Modbus驱动', txt: '轮询设备 [OPC-WWT-101] 时产生连续丢包(3次), 强制刷新握手包', lvl: 'warning' as const },
    { src: 'S7驱动', txt: '高炉气包压力(DB10.DBD16)高于临界 85 kPa (当前 88.4 kPa)! 触发超阈报警灯闪烁', lvl: 'warning' as const },
    { src: '系统安全', txt: '后台用户 [Admin] 下发寄存器写指令: 强制改写 Valve_State 为 ON', lvl: 'normal' as const },
    { src: '以太网通讯', txt: '以太网交换环路 A2-D1 段发生毫秒级拥塞, 自动重试中', lvl: 'info' as const }
  ];
  
  const chosen = warnings[Math.floor(Math.random() * warnings.length)];
  addLog(chosen.src, chosen.txt, chosen.lvl);
};
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] select-none bg-slate-50">
    
    <!-- Header panel with query filters & action outputs -->
    <div class="bg-white p-5 border-b border-slate-200 shadow-sm shrink-0 text-left flex flex-col md:flex-row md:items-center justify-between gap-4">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 tracking-tight flex items-center gap-2">
          <Terminal class="w-4 h-4 text-slate-600" />
          系统日志
        </h2>
        <p class="text-xs text-slate-500 font-sans">
          记录系统运行状态、操作事件和诊断信息。
        </p>
      </div>

      <div class="flex items-center gap-2 self-end md:self-center">
        <button 
          @click="handleMockWarning"
          class="font-bold text-xs text-slate-600 border border-slate-200 hover:bg-slate-50 bg-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer"
        >
          生成测试事件
        </button>
        <button 
          @click="handleExportLogs"
          class="font-bold text-xs text-indigo-600 border border-indigo-100 hover:bg-indigo-50 bg-indigo-50/50 px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer"
        >
          <Download class="w-4 h-4" />
          导出日志
        </button>
        <button 
          @click="handleClearAllLogs"
          class="font-bold text-xs text-rose-600 border border-rose-100 hover:bg-rose-50 bg-rose-50/50 px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer"
        >
          <Trash2 class="w-4 h-4" />
          清空日志
        </button>
      </div>
    </div>

    <!-- Filter selectors -->
    <div class="bg-white px-5 py-3 border-b border-slate-100 shadow-xs shrink-0 flex flex-col sm:flex-row gap-3">
      <!-- Fast text input search -->
      <div class="relative flex-1">
        <Search class="absolute left-2.5 top-2 ml-0.5 w-4 h-4 text-slate-400" />
        <input 
          v-model="searchQuery"
          type="text"
          placeholder="搜索日志内容或来源..."
          class="w-full bg-slate-50 border border-slate-200 focus:bg-white text-xs pl-9 pr-3 py-1.5 rounded-lg outline-none text-slate-800 focus:border-[#1890ff]"
        />
      </div>

      <!-- Level tabs selection -->
      <div class="flex items-center gap-1.5 shrink-0">
        <span class="text-xs text-slate-400 font-medium">日志等级:</span>
        <div class="flex bg-slate-100 p-0.5 rounded-lg gap-0.5 text-[11px] font-bold">
          <button 
            v-for="lvl in ['ALL', 'info', 'normal', 'warning']" 
            :key="lvl"
            @click="levelFilter = lvl as any"
            class="px-2.5 py-1 rounded-md transition-all cursor-pointer font-sans"
            :class="levelFilter === lvl ? 'bg-white shadow-xs text-slate-800' : 'text-slate-400 hover:text-slate-600'"
          >
            {{ lvl === 'ALL' ? '全部' : lvl === 'info' ? '信息' : lvl === 'normal' ? '常规' : '告警' }}
          </button>
        </div>
      </div>
    </div>

    <!-- Active logs scrolling wrapper -->
    <div class="flex-1 p-5 overflow-y-auto">
      <div class="bg-slate-950 rounded-xl border border-slate-900 overflow-hidden shadow-md flex flex-col h-full min-h-[300px]">
        
        <!-- Terminal Header -->
        <div class="bg-slate-900 px-4 py-2 flex items-center justify-between border-b border-slate-950 font-mono text-[10px] text-slate-400">
          <div class="flex items-center gap-1.5">
            <span class="w-2.5 h-2.5 rounded-full bg-rose-500" />
            <span class="w-2.5 h-2.5 rounded-full bg-amber-500" />
            <span class="w-2.5 h-2.5 rounded-full bg-emerald-500" />
            <span class="ml-1 text-slate-500 font-bold">audit_trail_agent.log</span>
          </div>
          <span>记录数: {{ logs.length }}</span>
        </div>

        <!-- Scroll logs ledger box -->
        <div class="flex-1 overflow-y-auto p-4 space-y-2 text-left font-mono text-[11px] leading-relaxed select-text select-all">
          <div 
            v-for="log in filteredLogs" 
            :key="log.id"
            class="flex items-start gap-3 p-2 rounded hover:bg-slate-900 border-b border-slate-900/60"
            :class="{
              'text-rose-400 bg-rose-950/20': log.level === 'warning',
              'text-sky-300 bg-sky-950/10': log.level === 'info',
              'text-slate-300': log.level === 'normal'
            }"
          >
            <!-- Badge indicators left -->
            <div class="shrink-0 font-bold opacity-30 select-none flex items-center gap-1">
              <Calendar class="w-3 h-3" />
              {{ log.timestamp }}
            </div>

            <!-- Source badge -->
            <span 
              class="shrink-0 px-2 py-0.5 rounded text-[9px] font-bold uppercase font-sans tracking-wide"
              :class="{
                'bg-rose-900/30 text-rose-300 border border-rose-800/20': log.level === 'warning',
                'bg-sky-900/30 text-sky-200 border border-sky-800/20': log.level === 'info',
                'bg-slate-800 text-slate-300 border border-slate-700/50': log.level === 'normal'
              }"
            >
              {{ log.source }}
            </span>

            <!-- Content -->
            <p class="flex-1 break-all font-sans">
              {{ log.content }}
            </p>
          </div>

          <!-- Empty filter logs placeholder -->
          <div v-if="filteredLogs.length === 0" class="h-full flex flex-col items-center justify-center text-slate-500 py-16 gap-2">
            <Terminal class="w-8 h-8 text-slate-600 animate-pulse" />
            <p class="text-xs font-sans">暂无匹配的日志记录</p>
          </div>
        </div>

      </div>
    </div>

  </div>
</template>
