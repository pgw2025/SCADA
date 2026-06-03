<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted, watch } from 'vue';
import { 
  devices, 
  areas, 
  dataModels, 
  serverStatus, 
  logs,
  scadaProjects
} from '../store/index';
import { startSystemResourceMonitoring, stopSystemResourceMonitoring } from '../services/systemService';
import { 
  Cpu, 
  Database, 
  Radio, 
  Layers, 
  Activity, 
  Server, 
  Zap, 
  CheckCircle, 
  XCircle, 
  FileCode,
  Network
} from 'lucide-vue-next';

onMounted(() => {
  startSystemResourceMonitoring();
});

onUnmounted(() => {
  stopSystemResourceMonitoring();
});

// Ensure reactivity for devices
watch(
  () => devices.value,
  () => {
    // 强制触发视图更新（如果computed未能自动更新）
  },
  { deep: true }
);

// Stats Computations
const totalDevices = computed(() => devices.value.length);
const onlineDevices = computed(() => devices.value.filter(d => d.status === 1).length);
const offlineDevices = computed(() => devices.value.filter(d => d.status === 0).length);

const totalVars = computed(() => {
  return dataModels.value.reduce((acc, model) => {
    return acc + (model.variables?.length || 0);
  }, 0);
});

const totalAreas = computed(() => areas.value.length);
const totalModels = computed(() => dataModels.value.length);
const totalScreens = computed(() => {
  return scadaProjects.value.reduce((acc, current) => acc + current.pages.length, 0);
});

// Calculate percentages
const onlineRate = computed(() => {
  if (totalDevices.value === 0) return 0;
  return Math.round((onlineDevices.value / totalDevices.value) * 100);
});

// CPU/Memory gauge styles
const cpuColor = computed(() => {
  if (serverStatus.value.cpuUsage > 85) return 'bg-rose-500';
  if (serverStatus.value.cpuUsage > 60) return 'bg-amber-500';
  return 'bg-emerald-500';
});

const memColor = computed(() => {
  if (serverStatus.value.memUsage > 85) return 'bg-rose-500';
  if (serverStatus.value.memUsage > 60) return 'bg-amber-500';
  return 'bg-sky-500';
});

// Last 6 logs for mini ledger
const recentLogs = computed(() => logs.value.slice(0, 5));

// Get variable count for a device by looking up its associated model
const getDeviceVariableCount = (device: typeof devices.value[0]) => {
  const model = dataModels.value.find(m => parseInt(m.id) === device.modelId);
  return model?.variables?.length || 0;
};
</script>

<template>
  <div class="h-full overflow-y-auto space-y-6 text-[#1e293b] select-none p-4 sm:p-6 bg-slate-50/50">
    <!-- Screen Header -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-slate-200 pb-5">
      <div>
        <h1 class="text-xl font-bold font-sans tracking-tight text-[#0f172a]">系统概览</h1>
        <p class="text-xs text-slate-500 mt-1">
          实时监控系统运行状态，包括设备、变量、服务器资源等关键指标。
        </p>
      </div>
      <div class="flex items-center gap-2 text-xs font-mono bg-slate-900 text-slate-300 px-3 py-1.5 rounded-lg border border-slate-800 shadow-sm">
        <span class="w-2 h-2 rounded-full bg-emerald-500 animate-ping"></span>
        <span class="text-emerald-400">系统运行正常</span>
      </div>
    </div>

    <!-- Quick stats grid -->
    <div class="grid grid-cols-2 md:grid-cols-4 gap-4">
      <!-- Stat 1 -->
      <div class="bg-white border border-slate-200/80 rounded-xl p-4 shadow-sm relative overflow-hidden flex flex-col justify-between min-h-[96px]">
        <div class="flex items-center justify-between">
          <span class="text-[11px] text-slate-400 font-bold uppercase tracking-wider">设备总数</span>
          <div class="p-1 px-1.5 rounded bg-emerald-50 text-emerald-600 text-[10px] font-bold flex items-center gap-1">
            <CheckCircle class="w-3 h-3" />
            {{ onlineRate }}% 在线
          </div>
        </div>
        <div class="mt-2 flex items-baseline gap-1.5">
          <span class="text-3xl font-bold font-sans text-slate-900 tracking-tight">{{ totalDevices }}</span>
          <span class="text-xs text-slate-500">台</span>
        </div>
        <div class="text-[10px] text-slate-500 flex items-center gap-1 mt-1 font-mono">
          <span class="text-emerald-600">{{ onlineDevices }} 台在线</span>
          <span>/</span>
          <span class="text-rose-600">{{ offlineDevices }} 台离线</span>
        </div>
      </div>

      <!-- Stat 2 -->
      <div class="bg-white border border-slate-200/80 rounded-xl p-4 shadow-sm flex flex-col justify-between min-h-[96px]">
        <div class="flex items-center justify-between">
          <span class="text-[11px] text-slate-400 font-bold uppercase tracking-wider">变量总数</span>
          <Database class="w-4 h-4 text-sky-500" />
        </div>
        <div class="mt-2 flex items-baseline gap-1.5">
          <span class="text-3xl font-bold font-sans text-slate-900 tracking-tight">{{ totalVars }}</span>
          <span class="text-xs text-slate-500">个</span>
        </div>
        <p class="text-[10px] text-slate-500 mt-1 leading-none font-mono">
          轮询周期 200ms
        </p>
      </div>

      <!-- Stat 3 -->
      <div class="bg-white border border-slate-200/80 rounded-xl p-4 shadow-sm flex flex-col justify-between min-h-[96px]">
        <div class="flex items-center justify-between">
          <span class="text-[11px] text-slate-400 font-bold uppercase tracking-wider">区域与模型</span>
          <Layers class="w-4 h-4 text-violet-500" />
        </div>
        <div class="mt-2 flex lg:items-baseline flex-wrap gap-x-2">
          <span class="text-2xl font-bold font-sans text-slate-900 tracking-tight">{{ totalAreas }}</span>
          <span class="text-xs text-slate-500">个区域</span>
          <span class="text-xs text-slate-300">/</span>
          <span class="text-2xl font-bold font-sans text-slate-900 tracking-tight">{{ totalModels }}</span>
          <span class="text-xs text-slate-500">个模型</span>
        </div>
        <p class="text-[10px] text-slate-500 mt-1 leading-none font-mono">
          支持 S7/OPC-UA/MQTT 协议
        </p>
      </div>

      <!-- Stat 4 -->
      <div class="bg-white border border-slate-200/80 rounded-xl p-4 shadow-sm flex flex-col justify-between min-h-[96px]">
        <div class="flex items-center justify-between">
          <span class="text-[11px] text-slate-400 font-bold uppercase tracking-wider">组态画面</span>
          <FileCode class="w-4 h-4 text-amber-500" />
        </div>
        <div class="mt-2 flex items-baseline gap-1.5">
          <span class="text-3xl font-bold font-sans text-slate-900 tracking-tight">{{ totalScreens }}</span>
          <span class="text-xs text-slate-500">个</span>
        </div>
        <p class="text-[10px] text-slate-500 mt-1 leading-none font-mono">
          支持多工艺场景
        </p>
      </div>
    </div>

    <!-- Middle: System Server Resources CPU/Mem/Disk Visualizers -->
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
      
      <!-- Server hardware stats (CPU/Memory gauges) -->
      <div class="bg-white border border-slate-200/80 rounded-xl p-5 shadow-sm space-y-4 lg:col-span-2">
      <div class="flex items-center justify-between border-b border-slate-100 pb-3">
        <div class="flex items-center gap-2">
          <Server class="w-4 h-4 text-[#1890ff]" />
          <span class="font-bold text-sm tracking-tight">服务器资源监控</span>
        </div>
        <span class="text-xs font-mono text-slate-400">运行时间: {{ serverStatus.uptimeDays }}天 {{ serverStatus.uptimeHours }}时 {{ serverStatus.uptimeMins }}分</span>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <!-- CPU Gauge element -->
        <div class="bg-slate-50 p-4 rounded-xl border border-slate-100 space-y-2">
          <div class="flex items-center justify-between text-xs">
            <div class="flex items-center gap-1 font-bold text-slate-700">
              <Cpu class="w-3.5 h-3.5 text-chrome" />
              CPU 使用率
            </div>
            <span class="font-mono text-slate-900 font-bold">{{ serverStatus.cpuUsage }}%</span>
          </div>

          <div class="w-full bg-slate-200 rounded-full h-2 overflow-hidden shadow-inner">
            <div class="h-full rounded-full transition-all duration-500" :class="cpuColor" :style="{ width: `${serverStatus.cpuUsage}%` }" />
          </div>
        </div>

        <!-- Memory Gauge element -->
        <div class="bg-slate-50 p-4 rounded-xl border border-slate-100 space-y-2">
          <div class="flex items-center justify-between text-xs">
            <div class="flex items-center gap-1 font-bold text-slate-700">
              <Zap class="w-3.5 h-3.5 text-amber-500" />
              内存使用率
            </div>
            <span class="font-mono text-slate-900 font-bold">{{ serverStatus.memUsage }}%</span>
          </div>

          <div class="w-full bg-slate-200 rounded-full h-2 overflow-hidden shadow-inner">
            <div class="h-full rounded-full transition-all duration-500" :class="memColor" :style="{ width: `${serverStatus.memUsage}%` }" />
          </div>
        </div>
      </div>

      <!-- Disks Section -->
      <div class="grid grid-cols-1 md:grid-cols-3 gap-4 mt-2">
        <div v-for="disk in serverStatus.disks" :key="disk.name" class="bg-slate-50 p-3 rounded-xl border border-slate-100">
          <div class="flex justify-between text-[10px] mb-2 font-bold">
            <span class="text-slate-700">{{ disk.label }} ({{ disk.name }})</span>
            <span :class="disk.usagePercentage > 85 ? 'text-rose-500' : 'text-slate-500'">
              {{ disk.usagePercentage }}%
            </span>
          </div>
          <div class="h-1.5 w-full bg-slate-200 rounded-full overflow-hidden">
            <div 
              class="h-full transition-all duration-500" 
              :class="disk.usagePercentage > 85 ? 'bg-rose-500' : 'bg-sky-500'"
              :style="{ width: `${disk.usagePercentage}%` }"
            />
          </div>
          <div class="text-[9px] text-slate-400 mt-2 font-mono">
            {{ disk.usedSizeGb }} GB / {{ disk.totalSizeGb }} GB
          </div>
        </div>
      </div>

      <!-- Network Rates & IO Statistics -->
      <div class="bg-slate-950 p-4 rounded-xl border border-slate-900 text-slate-300 font-mono text-[11px] leading-relaxed relative overflow-hidden">
        <div class="absolute right-3 top-3 opacity-10">
          <Network class="w-16 h-16 text-emerald-400" />
        </div>
        <div class="flex items-center gap-1.5 text-emerald-400 border-b border-slate-800 pb-2 mb-2 font-bold uppercase tracking-wider">
          <Network class="w-3.5 h-3.5" />
          网络流量监控
        </div>
        <div class="grid grid-cols-2 md:grid-cols-4 gap-2">
          <div>
            <span class="text-slate-500">接收:</span>
            <span class="text-white ml-1 font-bold">{{ serverStatus.networkIn }} kbps</span>
          </div>
          <div>
            <span class="text-slate-500">发送:</span>
            <span class="text-white ml-1 font-bold">{{ serverStatus.networkOut }} kbps</span>
          </div>
          <div>
            <span class="text-slate-500">轮询周期:</span>
            <span class="text-sky-400 ml-1 font-bold">{{ serverStatus.pollFreq }} ms</span>
          </div>
          <div>
            <span class="text-slate-500">总包数:</span>
            <span class="text-amber-400 ml-1 font-bold">{{ serverStatus.totalPollPackets }}</span>
          </div>
        </div>
      </div>
      </div>
      <!-- Mini log preview / Ledger -->
      <div class="bg-white border border-slate-200/80 rounded-xl p-5 shadow-sm flex flex-col justify-between min-h-[250px]">
        <div class="border-b border-slate-100 pb-3 mb-2">
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-1.5 font-bold text-sm text-slate-900">
              <Activity class="w-4 h-4 text-emerald-500" />
              <span>系统日志</span>
            </div>
            <span class="text-[9px] font-mono bg-slate-100 text-slate-500 px-1.5 py-0.5 rounded leading-none">实时</span>
          </div>
        </div>

        <!-- Scroll logs -->
        <div class="flex-1 space-y-2 pointer-events-none max-h-[160px] overflow-hidden text-[10px]">
          <div 
            v-for="log in recentLogs" 
            :key="log.id" 
            class="flex items-start gap-1 p-1 bg-slate-50 rounded border-l-2 border-slate-300 font-mono"
            :class="{
              'border-l-rose-500 bg-rose-50/40 text-rose-800': log.level === 'warning',
              'border-l-sky-500 bg-sky-50/40 text-sky-800': log.level === 'info',
              'border-l-slate-400': log.level === 'normal'
            }"
          >
            <div class="shrink-0 font-bold opacity-60 text-[8px] mt-0.5">{{ log.timestamp.split(' ').pop() }}</div>
            <div class="shrink-0 bg-slate-200 px-1 rounded text-[8px] font-bold">{{ log.source }}</div>
            <div class="truncate flex-1 font-sans">{{ log.content }}</div>
          </div>
        </div>

        <div class="pt-3 border-t border-slate-50 flex items-center justify-end">
          <button 
            @click="activeTab = 'system-logs'"
            class="text-[10px] text-[#1890ff] hover:underline font-bold"
          >
            查看全部日志 &rarr;
          </button>
        </div>
      </div>
    </div>

    <!-- Active devices summary cards -->
    <div class="space-y-3">
      <div class="flex items-center justify-between">
        <h3 class="text-xs font-bold text-slate-500 uppercase tracking-widest flex items-center gap-2">
          <Radio class="w-3.5 h-3.5 text-sky-500 animate-pulse" />
          设备连接状态 ({{ onlineDevices }}/{{ totalDevices }} 在线)
        </h3>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        <div 
          v-for="dev in devices" 
          :key="dev.id" 
          class="bg-white border border-slate-200/80 rounded-xl p-4 shadow-sm relative hover:border-slate-300 transition-all flex flex-col justify-between"
        >
          <!-- Absolute status point -->
          <span 
            class="absolute top-4 right-4 w-2 h-2 rounded-full" 
            :class="dev.status === 1 ? 'bg-emerald-500 shadow-[0_0_8px_#10b981]' : 'bg-slate-300'"
          />
          
          <div class="space-y-1 pr-6">
            <span class="text-[8px] text-slate-400 font-mono bg-slate-100 px-1 rounded leading-none uppercase">
              {{ dev.type }}
            </span>
            <h4 class="font-bold text-xs font-sans text-slate-800 line-clamp-1 mt-1 leading-snug">
              {{ dev.name }}
            </h4>
            <p class="text-[9px] font-mono text-slate-500">
              设备编码: {{ dev.key }}
            </p>
          </div>

          <div class="border-t border-slate-100 mt-3 pt-2 flex items-center justify-between text-[10px]">
            <span class="text-slate-400">更新时间: <b class="font-mono text-slate-600">{{ dev.lastUpdated }}</b></span>
            <!-- Variables counts -->
            <span class="text-[#1890ff] bg-sky-50 font-mono px-1 rounded font-bold leading-none py-0.5">
              {{ getDeviceVariableCount(dev) }} 个变量 
            </span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
