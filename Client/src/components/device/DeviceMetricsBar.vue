<script setup lang="ts">
import { computed } from 'vue';
import { Device } from '../../types';
import { Cpu, Wifi, WifiOff, PlayCircle, PauseCircle } from 'lucide-vue-next';

const props = defineProps<{
  devices: Device[];
  activeStatus: 'all' | 'online' | 'offline' | 'enabled' | 'disabled';
}>();

const emit = defineEmits<{
  (e: 'update:activeStatus', val: 'all' | 'online' | 'offline' | 'enabled' | 'disabled'): void;
}>();

const total = computed(() => props.devices.length);
const onlineCount = computed(() => props.devices.filter(d => d.status === 1 || d.status === 'online').length);
const offlineCount = computed(() => props.devices.filter(d => d.status !== 1 && d.status !== 'online').length);
const enabledCount = computed(() => props.devices.filter(d => d.isEnabled).length);
const disabledCount = computed(() => props.devices.filter(d => !d.isEnabled).length);
const onlinePercent = computed(() => (total.value > 0 ? Math.round((onlineCount.value / total.value) * 100) : 0));
</script>

<template>
  <div class="flex items-center gap-2 overflow-x-auto pb-1 sm:pb-0 text-xs no-scrollbar select-none">
    <!-- 全部设备 -->
    <button
      type="button"
      @click="emit('update:activeStatus', 'all')"
      class="flex items-center gap-2 px-3 py-2 rounded-xl border transition-all cursor-pointer shrink-0"
      :class="activeStatus === 'all'
        ? 'bg-slate-900 text-white border-slate-900 dark:bg-sky-600 dark:border-sky-600 shadow-xs'
        : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700'"
    >
      <Cpu class="w-3.5 h-3.5" :class="activeStatus === 'all' ? 'text-white' : 'text-slate-400'" />
      <span class="font-medium">全部设备</span>
      <span class="px-1.5 py-0.5 rounded-full text-[10px] font-bold"
        :class="activeStatus === 'all' ? 'bg-white/20 text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300'">
        {{ total }}
      </span>
    </button>

    <!-- 在线设备 -->
    <button
      type="button"
      @click="emit('update:activeStatus', 'online')"
      class="flex items-center gap-2 px-3 py-2 rounded-xl border transition-all cursor-pointer shrink-0"
      :class="activeStatus === 'online'
        ? 'bg-emerald-600 text-white border-emerald-600 shadow-xs'
        : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-800 hover:border-emerald-300 dark:hover:border-emerald-800'"
    >
      <div class="relative flex items-center justify-center">
        <span class="w-2 h-2 rounded-full bg-emerald-500 animate-ping absolute opacity-75"></span>
        <span class="w-2 h-2 rounded-full bg-emerald-500"></span>
      </div>
      <span class="font-medium">在线</span>
      <span class="px-1.5 py-0.5 rounded-full text-[10px] font-bold"
        :class="activeStatus === 'online' ? 'bg-white/20 text-white' : 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400'">
        {{ onlineCount }} ({{ onlinePercent }}%)
      </span>
    </button>

    <!-- 离线设备 -->
    <button
      type="button"
      @click="emit('update:activeStatus', 'offline')"
      class="flex items-center gap-2 px-3 py-2 rounded-xl border transition-all cursor-pointer shrink-0"
      :class="activeStatus === 'offline'
        ? 'bg-slate-700 text-white border-slate-700 shadow-xs'
        : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-800 hover:border-slate-300'"
    >
      <WifiOff class="w-3.5 h-3.5" :class="activeStatus === 'offline' ? 'text-white' : 'text-slate-400'" />
      <span class="font-medium">离线</span>
      <span class="px-1.5 py-0.5 rounded-full text-[10px] font-bold"
        :class="activeStatus === 'offline' ? 'bg-white/20 text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400'">
        {{ offlineCount }}
      </span>
    </button>

    <!-- 采集中 -->
    <button
      type="button"
      @click="emit('update:activeStatus', 'enabled')"
      class="flex items-center gap-2 px-3 py-2 rounded-xl border transition-all cursor-pointer shrink-0"
      :class="activeStatus === 'enabled'
        ? 'bg-sky-600 text-white border-sky-600 shadow-xs'
        : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-800 hover:border-sky-300 dark:hover:border-sky-800'"
    >
      <PlayCircle class="w-3.5 h-3.5" :class="activeStatus === 'enabled' ? 'text-white' : 'text-sky-500'" />
      <span class="font-medium">采集中</span>
      <span class="px-1.5 py-0.5 rounded-full text-[10px] font-bold"
        :class="activeStatus === 'enabled' ? 'bg-white/20 text-white' : 'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400'">
        {{ enabledCount }}
      </span>
    </button>

    <!-- 已停采 -->
    <button
      type="button"
      @click="emit('update:activeStatus', 'disabled')"
      class="flex items-center gap-2 px-3 py-2 rounded-xl border transition-all cursor-pointer shrink-0"
      :class="activeStatus === 'disabled'
        ? 'bg-amber-600 text-white border-amber-600 shadow-xs'
        : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-800 hover:border-amber-300 dark:hover:border-amber-800'"
    >
      <PauseCircle class="w-3.5 h-3.5" :class="activeStatus === 'disabled' ? 'text-white' : 'text-amber-500'" />
      <span class="font-medium">已停采</span>
      <span class="px-1.5 py-0.5 rounded-full text-[10px] font-bold"
        :class="activeStatus === 'disabled' ? 'bg-white/20 text-white' : 'bg-amber-50 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400'">
        {{ disabledCount }}
      </span>
    </button>
  </div>
</template>
