<script setup lang="ts">
import { Device, Area, DataModel } from '../../types';
import {
  Braces,
  Edit3,
  Trash2,
  Eye,
  CheckSquare,
  Square,
  Loader2,
  Cpu
} from 'lucide-vue-next';

const props = defineProps<{
  devices: Device[];
  selectedDeviceIds: Set<number>;
  areas: Area[];
  dataModels: DataModel[];
  togglingId: number | null;
}>();

const emit = defineEmits<{
  (e: 'toggleSelect', id: number): void;
  (e: 'toggleEnabled', device: Device): void;
  (e: 'inspect', device: Device): void;
  (e: 'edit', device: Device): void;
  (e: 'delete', device: Device): void;
  (e: 'variables', device: Device): void;
}>();

const getAreaName = (areaId: number) => props.areas.find(a => a.id === areaId)?.name || '未指定';
const getModelName = (modelId: number | string) => props.dataModels.find(m => String(m.id) === String(modelId))?.name || '未配置';

const formatEndpoint = (d: Device): string => {
  if (d.connection) {
    const parts = [d.connection.protocolName || d.type];
    if (d.connection.host) parts.push(`${d.connection.host}${d.connection.port ? ':' + d.connection.port : ''}`);
    return parts.join(' · ');
  }
  if (d.type === 'OPCUA') return d.endpointUrl || 'opc.tcp://127.0.0.1:4840';
  if (d.type === 'S7') return `${d.ipAddress || '127.0.0.1'}:${d.port || 102} (${d.cpuType || 'S7'})`;
  if (d.type === 'MQTT') return 'MQTT Broker';
  return '宿主机虚拟工业总线';
};
</script>

<template>
  <div>
    <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 gap-3 text-left">
      <div
        v-for="d in devices"
        :key="d.id"
        class="bg-white dark:bg-slate-900 border rounded-xl p-3.5 flex flex-col justify-between hover:shadow-md transition-all relative overflow-hidden group"
        :class="[
          selectedDeviceIds.has(d.id)
            ? 'border-[#1890ff] ring-1 ring-[#1890ff]/30 shadow-xs'
            : 'border-slate-200 dark:border-slate-800'
        ]"
      >
        <!-- Top Status Bar Indicator -->
        <div
          class="absolute top-0 left-0 right-0 h-1 transition-colors"
          :class="d.status === 1 || d.status === 'online' ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-700'"
        />

        <div>
          <!-- Header: Status + Selection + Collection Toggle -->
          <div class="flex items-center justify-between gap-2 mt-0.5 mb-2">
            <div class="flex items-center gap-1.5 min-w-0">
              <button
                type="button"
                @click="emit('toggleSelect', d.id)"
                class="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer shrink-0"
              >
                <CheckSquare v-if="selectedDeviceIds.has(d.id)" class="w-4 h-4 text-[#1890ff]" />
                <Square v-else class="w-4 h-4" />
              </button>

              <!-- Online/offline badge -->
              <span
                class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold border"
                :class="d.status === 1 || d.status === 'online'
                  ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400 border-emerald-200/60 dark:border-emerald-800'
                  : 'bg-slate-50 dark:bg-slate-800 text-slate-400 border-slate-200 dark:border-slate-700'"
              >
                <span
                  class="w-1.5 h-1.5 rounded-full"
                  :class="d.status === 1 || d.status === 'online' ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'"
                />
                {{ d.status === 1 || d.status === 'online' ? '在线' : '离线' }}
              </span>

              <span class="text-[9px] font-mono px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 uppercase font-bold shrink-0">
                {{ d.type }}
              </span>
            </div>

            <!-- Collection Switch Toggle -->
            <label
              class="relative inline-flex items-center cursor-pointer select-none shrink-0"
              :class="togglingId === d.id ? 'opacity-50 pointer-events-none' : ''"
              :title="d.isEnabled ? '点击停用采集' : '点击启用采集'"
            >
              <input
                type="checkbox"
                class="sr-only peer"
                :checked="d.isEnabled"
                :disabled="togglingId === d.id"
                @click.prevent="emit('toggleEnabled', d)"
              />
              <div
                class="w-8 h-4.5 rounded-full transition-colors peer-checked:bg-[#1890ff] bg-slate-300 dark:bg-slate-700 relative after:content-[''] after:absolute after:top-0.5 after:left-0.5 after:bg-white after:rounded-full after:h-3.5 after:w-3.5 after:transition-all peer-checked:after:translate-x-3.5"
              />
              <span
                class="ml-1 text-[10px] font-bold"
                :class="d.isEnabled ? 'text-[#1890ff] dark:text-sky-400' : 'text-slate-400'"
              >
                {{ d.isEnabled ? '采集' : '停采' }}
              </span>
              <Loader2 v-if="togglingId === d.id" class="w-3 h-3 ml-0.5 animate-spin text-slate-400" />
            </label>
          </div>

          <!-- Device Name & Key -->
          <div class="mb-2">
            <h4
              class="font-bold text-xs sm:text-sm text-slate-900 dark:text-white leading-snug line-clamp-1 hover:text-[#1890ff] cursor-pointer"
              @click="emit('inspect', d)"
            >
              {{ d.name }}
            </h4>
            <div class="text-[10px] font-mono text-slate-400 dark:text-slate-500 truncate mt-0.5">
              KEY: {{ d.key }}
            </div>
          </div>

          <!-- Metadata Tags -->
          <div class="space-y-1.5 py-2 border-t border-b border-slate-100 dark:border-slate-800 text-[11px]">
            <div class="flex items-center justify-between text-slate-500 dark:text-slate-400">
              <span>工艺区域</span>
              <span class="font-medium text-slate-800 dark:text-slate-200 truncate max-w-[150px]">
                {{ getAreaName(d.areaId) }}
              </span>
            </div>
            <div class="flex items-center justify-between text-slate-500 dark:text-slate-400">
              <span>数据模型</span>
              <div class="flex items-center gap-1 min-w-0">
                <span class="font-medium text-[#1890ff] dark:text-sky-400 truncate max-w-[120px]">
                  {{ getModelName(d.modelId) }}
                </span>
                <span v-if="d.models && d.models.length > 1" class="text-[9px] px-1 py-0.2 rounded bg-sky-50 dark:bg-sky-950 text-sky-600 font-mono font-bold">
                  +{{ d.models.length - 1 }}
                </span>
              </div>
            </div>
            <div class="flex items-start justify-between text-slate-500 dark:text-slate-400 gap-2">
              <span class="shrink-0">通信端点</span>
              <span class="font-mono text-[10px] text-slate-700 dark:text-slate-300 truncate max-w-[180px]" :title="formatEndpoint(d)">
                {{ formatEndpoint(d) }}
              </span>
            </div>
          </div>
        </div>

        <!-- Footer: update time & action buttons -->
        <div class="flex items-center justify-between mt-3 pt-1 text-[11px]">
          <span class="text-[10px] text-slate-400 truncate max-w-[100px]">
            {{ d.lastUpdated ? new Date(d.lastUpdated).toLocaleDateString('zh-CN') : '刚刚' }}
          </span>

          <div class="flex items-center gap-2">
            <button
              type="button"
              @click="emit('inspect', d)"
              class="text-slate-500 hover:text-[#1890ff] dark:hover:text-sky-400 cursor-pointer p-1"
              title="查看详情"
            >
              <Eye class="w-3.5 h-3.5" />
            </button>
            <button
              type="button"
              @click="emit('variables', d)"
              class="text-emerald-600 hover:text-emerald-700 dark:text-emerald-400 cursor-pointer p-1"
              title="变量实例"
            >
              <Braces class="w-3.5 h-3.5" />
            </button>
            <button
              type="button"
              @click="emit('edit', d)"
              class="text-[#1890ff] hover:text-sky-600 dark:text-sky-400 cursor-pointer p-1"
              title="编辑配置"
            >
              <Edit3 class="w-3.5 h-3.5" />
            </button>
            <button
              type="button"
              @click="emit('delete', d)"
              class="text-rose-500 hover:text-rose-700 cursor-pointer p-1"
              title="删除设备"
            >
              <Trash2 class="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Empty State -->
    <div v-if="devices.length === 0" class="py-16 text-center text-slate-400 dark:text-slate-500 text-xs">
      <Cpu class="w-8 h-8 mx-auto mb-2 opacity-30" />
      <span>未匹配到符合条件的工业设备</span>
    </div>
  </div>
</template>
