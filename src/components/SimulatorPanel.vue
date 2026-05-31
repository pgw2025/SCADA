<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted, nextTick } from 'vue';
import { PLCTag } from '../types';
import { Play, Pause, Terminal, Ban, HardDrive, Radio } from 'lucide-vue-next';

const props = defineProps<{
  tags: PLCTag[];
  isOscillating: boolean;
}>();

const emit = defineEmits<{
  (e: 'changeTagValue', key: string, value: number | boolean): void;
  (e: 'toggleOscillation'): void;
}>();

const logs = ref<string[]>([]);
const consoleBottomRef = ref<HTMLDivElement | null>(null);

let logTimer: number | null = null;

const generateLog = () => {
  if (!props.isOscillating) return;
  if (props.tags.length === 0) return;

  const randomTag = props.tags[Math.floor(Math.random() * props.tags.length)];
  const timestamp = new Date().toLocaleTimeString();
  let message = '';

  if (randomTag.type === 'digital') {
    const op = Math.random() > 0.5 ? 'READ_COIL' : 'WRITE_COIL';
    message = `[${timestamp}] OPCUA::${op} [FC1] Addr:${Math.round(Math.random() * 1000)} "${randomTag.key}" -> value: ${randomTag.value ? '1 (HIGH)' : '0 (LOW)'}`;
  } else {
    const op = Math.random() > 0.5 ? 'READ_REG' : 'POLL_REG';
    const rawVal = typeof randomTag.value === 'number' ? randomTag.value.toFixed(1) : '0';
    message = `[${timestamp}] MODBUS::${op} [FC3] Reg:${Math.round(Math.random() * 2000)} "${randomTag.key}" -> val: ${rawVal} ${randomTag.unit}`;
  }

  // Check alerting trigger thresholds
  if (randomTag.key === 'boiler_temp' && (randomTag.value as number) > 95) {
    message += ` ⚠️ WARNING: HIGH PRESSURE THERMAL CRITICAL STATE DETECTED!`;
  }

  logs.value.push(message);
  if (logs.value.length > 30) {
    logs.value.shift();
  }

  // Scroll downwards
  nextTick(() => {
    if (consoleBottomRef.value) {
      consoleBottomRef.value.scrollIntoView({ behavior: 'smooth' });
    }
  });
};

onMounted(() => {
  logTimer = window.setInterval(generateLog, 1200);
});

onUnmounted(() => {
  if (logTimer) {
    window.clearInterval(logTimer);
  }
});

const clearLogs = () => {
  logs.value = [];
};
</script>

<template>
  <div class="bg-white border-t border-[#d9d9d9] text-[#262626] flex flex-col md:flex-row h-72 select-none">
    <!-- Simulation variables column -->
    <div class="w-full md:w-3/5 border-r border-[#f0f0f0] p-4 overflow-y-auto flex flex-col bg-[#fafafa]">
      <!-- Header banner -->
      <div class="flex items-center justify-between mb-3 shrink-0 flex-wrap gap-2">
        <div class="flex items-center gap-2">
          <Radio class="w-4 h-4 text-[#1890ff] animate-pulse" />
          <h4 class="text-xs font-bold uppercase tracking-wider text-[#141414]">
            PLC 寄存器仿真模拟器 (SCADA Driver Hub)
          </h4>
        </div>

        <button
          @click="emit('toggleOscillation')"
          :class="[
            'flex items-center gap-1.5 px-3 py-1.5 text-[10.5px] font-bold rounded transition-all cursor-pointer',
            isOscillating
              ? 'bg-[#1890ff] text-white shadow-sm'
              : 'bg-white border border-[#d9d9d9] text-gray-500 hover:text-gray-700 hover:bg-gray-50'
          ]"
        >
          <template v-if="isOscillating">
            <Pause class="w-3 h-3 animate-spin-slow" />
            自动物理模拟: ON
          </template>
          <template v-else>
            <Play class="w-3 h-3" />
            开启自动数据突变
          </template>
        </button>
      </div>

      <!-- Dynamic PLC tags grid layout -->
      <div class="flex-1 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
        <div
          v-for="tag in tags"
          :key="tag.key"
          :class="[
            'p-2.5 rounded border flex flex-col justify-between transition-all',
            (tag.key === 'boiler_temp' && (tag.value as number) > 95) || (tag.key === 'boiler_press' && (tag.value as number) > 85)
              ? 'bg-red-50 border-red-300 shadow-sm'
              : 'bg-white border-[#f0f0f0] hover:border-[#1890ff]'
          ]"
        >
          <div class="flex justify-between items-start">
            <div>
              <span class="text-[9px] text-gray-400 font-mono font-medium block">
                {{ tag.key.toUpperCase() }}
              </span>
              <span class="text-[11px] font-bold text-gray-800 truncate max-w-[120px]" :title="tag.name">
                {{ tag.name }}
              </span>
            </div>
            <span
              :class="[
                'text-[11px] font-mono font-bold border-b',
                (tag.key === 'boiler_temp' && (tag.value as number) > 95) || (tag.key === 'boiler_press' && (tag.value as number) > 85)
                  ? 'text-red-500 border-red-400'
                  : 'text-[#1890ff] border-[#1890ff]'
              ]"
            >
              {{ typeof tag.value === 'boolean' ? (tag.value ? 'ON' : 'OFF') : `${(tag.value as number).toFixed(1)}${tag.unit}` }}
            </span>
          </div>

          <div class="mt-2 text-xs">
            <div v-if="tag.type === 'digital'" class="flex items-center gap-1.5 mt-1">
              <button
                @click="emit('changeTagValue', tag.key, !tag.value)"
                :class="[
                  'w-full text-[9.5px] py-1 rounded font-bold uppercase transition-all cursor-pointer',
                  tag.value
                    ? 'bg-[#1890ff] text-white shadow-lg'
                    : 'bg-gray-100 text-gray-500 hover:bg-gray-200'
                ]"
              >
                {{ tag.value ? '强制高电位 (1)' : '强制低电位 (0)' }}
              </button>
            </div>
            <div v-else class="flex items-center gap-2 mt-1">
              <span class="text-[9px] font-mono text-gray-400">{{ tag.min }}</span>
              <input
                type="range"
                :min="tag.min"
                :max="tag.max"
                step="0.5"
                :value="tag.value"
                @input="emit('changeTagValue', tag.key, parseFloat(($event.target as HTMLInputElement).value))"
                class="flex-1 accent-[#1890ff] h-1 bg-gray-200 rounded-lg cursor-pointer"
              />
              <span class="text-[9px] font-mono text-gray-400">{{ tag.max }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Terminal log console -->
    <div class="w-full md:w-2/5 p-4 flex flex-col h-full bg-[#001529] text-gray-300">
      <div class="flex items-center justify-between border-b border-white/10 pb-2 mb-2 shrink-0">
        <div class="flex items-center gap-1.5">
          <Terminal class="w-3.5 h-3.5 text-orange-400" />
          <span class="text-[10px] font-bold uppercase tracking-wider text-gray-200 font-mono">
            寄存器报文监视器 (Modbus-RTU / OPC-UA)
          </span>
        </div>

        <button
          @click="clearLogs"
          class="text-[9px] hover:text-white text-gray-400 flex items-center gap-1 transition-all cursor-pointer"
          title="清空日志"
        >
          <Ban class="w-3 h-3" />
          清空
        </button>
      </div>

      <!-- Logs view list container -->
      <div class="flex-1 overflow-y-auto font-mono text-[9px] leading-relaxed text-gray-300 space-y-1 pr-1 custom-scrollbar">
        <div v-if="logs.length === 0" class="h-full flex flex-col items-center justify-center text-gray-500 italic text-center">
          <HardDrive class="w-5 h-5 mb-1 text-gray-500 animate-pulse" />
          正在监听 PLC 仿真寄存器数据包...
          <br />
          (开启“自动物理模拟”以观察动态报文)
        </div>
        <div
          v-else
          v-for="(log, i) in logs"
          :key="i"
          :class="[
            'py-0.5 border-b border-white/5 select-text',
            log.includes('WARNING') ? 'text-red-300 bg-red-950/20 font-semibold animate-pulse' : ''
          ]"
        >
          {{ log }}
        </div>
        <div ref="consoleBottomRef" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.animate-spin-slow {
  animation: spin 8s linear infinite;
}
@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
</style>
