<script setup lang="ts">
import { computed } from 'vue';
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
  (e: 'toggleSelectAll'): void;
  (e: 'toggleEnabled', device: Device): void;
  (e: 'inspect', device: Device): void;
  (e: 'edit', device: Device): void;
  (e: 'delete', device: Device): void;
  (e: 'variables', device: Device): void;
}>();

const allSelected = computed(() => {
  return props.devices.length > 0 && props.devices.every(d => props.selectedDeviceIds.has(d.id));
});

const someSelected = computed(() => {
  return props.devices.some(d => props.selectedDeviceIds.has(d.id)) && !allSelected.value;
});

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
  <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-xs text-left">
    <div class="overflow-x-auto">
      <table class="w-full text-xs text-left">
        <thead>
          <tr class="bg-slate-50 dark:bg-slate-950/60 border-b border-slate-200 dark:border-slate-800 text-[11px] font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider select-none">
            <th class="py-3 px-3 w-10 text-center">
              <button
                type="button"
                @click="emit('toggleSelectAll')"
                class="flex items-center justify-center cursor-pointer text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
              >
                <CheckSquare v-if="allSelected" class="w-4 h-4 text-[#1890ff]" />
                <Square v-else class="w-4 h-4" />
              </button>
            </th>
            <th class="py-3 px-3 w-20">状态</th>
            <th class="py-3 px-3 min-w-[180px]">设备名称 / 编号</th>
            <th class="py-3 px-3 min-w-[110px]">所属工艺区域</th>
            <th class="py-3 px-3 min-w-[140px]">主数据模型</th>
            <th class="py-3 px-3 min-w-[200px]">通信连接与端点</th>
            <th class="py-3 px-3 w-28 text-center">采集开关</th>
            <th class="py-3 px-4 w-36 text-right">操作</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100 dark:divide-slate-800/80">
          <tr
            v-for="d in devices"
            :key="d.id"
            class="hover:bg-slate-50/70 dark:hover:bg-slate-800/40 transition-colors group"
            :class="selectedDeviceIds.has(d.id) ? 'bg-[#1890ff]/5 dark:bg-sky-950/20' : ''"
          >
            <!-- Checkbox -->
            <td class="py-3 px-3 text-center">
              <button
                type="button"
                @click="emit('toggleSelect', d.id)"
                class="flex items-center justify-center cursor-pointer text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
              >
                <CheckSquare v-if="selectedDeviceIds.has(d.id)" class="w-4 h-4 text-[#1890ff]" />
                <Square v-else class="w-4 h-4" />
              </button>
            </td>

            <!-- Status Indicator -->
            <td class="py-3 px-3">
              <span
                class="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[10px] font-bold border"
                :class="d.status === 1 || d.status === 'online'
                  ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400 border-emerald-200/60 dark:border-emerald-800'
                  : 'bg-slate-50 dark:bg-slate-800/60 text-slate-400 border-slate-200 dark:border-slate-700'"
              >
                <span
                  class="w-1.5 h-1.5 rounded-full"
                  :class="d.status === 1 || d.status === 'online' ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'"
                />
                {{ d.status === 1 || d.status === 'online' ? '在线' : '离线' }}
              </span>
            </td>

            <!-- Device Name & Key -->
            <td class="py-3 px-3">
              <div class="flex items-center gap-1.5">
                <span class="font-bold text-slate-900 dark:text-white hover:text-[#1890ff] cursor-pointer" @click="emit('inspect', d)">
                  {{ d.name }}
                </span>
                <span class="text-[9px] font-mono px-1 py-0.2 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 font-medium">
                  {{ d.type }}
                </span>
              </div>
              <div class="text-[10px] font-mono text-slate-400 dark:text-slate-500 truncate mt-0.5">
                {{ d.key }}
              </div>
            </td>

            <!-- Area -->
            <td class="py-3 px-3 text-slate-700 dark:text-slate-300">
              <span class="px-2 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-[11px] font-medium">
                {{ getAreaName(d.areaId) }}
              </span>
            </td>

            <!-- Data Model -->
            <td class="py-3 px-3">
              <div class="flex items-center gap-1">
                <span class="font-medium text-[#1890ff] dark:text-sky-400 truncate max-w-[140px]" :title="getModelName(d.modelId)">
                  {{ getModelName(d.modelId) }}
                </span>
                <span
                  v-if="d.models && d.models.length > 1"
                  class="text-[9px] px-1 py-0.2 rounded bg-sky-50 dark:bg-sky-950 text-sky-600 dark:text-sky-300 font-mono font-bold"
                  :title="`附加 ${d.models.length - 1} 个模型`"
                >
                  +{{ d.models.length - 1 }}
                </span>
              </div>
            </td>

            <!-- Protocol & Endpoint -->
            <td class="py-3 px-3 font-mono text-[11px] text-slate-600 dark:text-slate-300 truncate max-w-[220px]" :title="formatEndpoint(d)">
              <div class="truncate">
                {{ formatEndpoint(d) }}
              </div>
              <div v-if="d.connection" class="text-[9px] text-violet-500 dark:text-violet-400 truncate font-sans">
                连接 #{{ d.connection.id }}: {{ d.connection.controllerName || d.connection.controllerCode }}
              </div>
            </td>

            <!-- Collection Switch Toggle -->
            <td class="py-3 px-3 text-center">
              <label
                class="relative inline-flex items-center cursor-pointer select-none"
                :class="togglingId === d.id ? 'opacity-50 pointer-events-none' : ''"
                :title="d.isEnabled ? '点击停用数据采集' : '点击启用数据采集'"
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
                  class="ml-1.5 text-[10px] font-bold"
                  :class="d.isEnabled ? 'text-[#1890ff] dark:text-sky-400' : 'text-slate-400'"
                >
                  {{ d.isEnabled ? '采集' : '停采' }}
                </span>
                <Loader2 v-if="togglingId === d.id" class="w-3 h-3 ml-1 animate-spin text-slate-400" />
              </label>
            </td>

            <!-- Actions -->
            <td class="py-3 px-4 text-right whitespace-nowrap">
              <div class="inline-flex items-center gap-2">
                <button
                  type="button"
                  @click="emit('inspect', d)"
                  class="text-slate-500 hover:text-[#1890ff] dark:hover:text-sky-400 cursor-pointer transition-colors"
                  title="查看详情"
                >
                  <Eye class="w-3.5 h-3.5" />
                </button>
                <button
                  type="button"
                  @click="emit('variables', d)"
                  class="text-emerald-600 hover:text-emerald-700 dark:text-emerald-400 cursor-pointer transition-colors"
                  title="变量实例映射"
                >
                  <Braces class="w-3.5 h-3.5" />
                </button>
                <button
                  type="button"
                  @click="emit('edit', d)"
                  class="text-[#1890ff] hover:text-sky-600 dark:text-sky-400 cursor-pointer transition-colors"
                  title="编辑配置"
                >
                  <Edit3 class="w-3.5 h-3.5" />
                </button>
                <button
                  type="button"
                  @click="emit('delete', d)"
                  class="text-rose-500 hover:text-rose-700 cursor-pointer transition-colors"
                  title="删除设备"
                >
                  <Trash2 class="w-3.5 h-3.5" />
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Empty State -->
    <div v-if="devices.length === 0" class="py-16 text-center text-slate-400 dark:text-slate-500 text-xs">
      <Cpu class="w-8 h-8 mx-auto mb-2 opacity-30" />
      <span>未匹配到符合条件的工业设备</span>
    </div>
  </div>
</template>
