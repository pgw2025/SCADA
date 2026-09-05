<script setup lang="ts">
import { Device, Area, DataModel } from '../../types';
import {
  X,
  Cpu,
  Braces,
  Edit3,
  Link2,
  Server,
  Star,
  Clock,
  MapPin,
  Wifi,
  ExternalLink,
  Copy
} from 'lucide-vue-next';

const props = defineProps<{
  open: boolean;
  device: Device | null;
  areas: Area[];
  dataModels: DataModel[];
}>();

const emit = defineEmits<{
  (e: 'close'): void;
  (e: 'edit', device: Device): void;
  (e: 'variables', device: Device): void;
  (e: 'toggleEnabled', device: Device): void;
}>();

const getAreaName = (areaId?: number) => {
  if (!areaId) return '未指定';
  return props.areas.find(a => a.id === areaId)?.name || '未指定';
};

const getModel = (modelId?: number | string) => {
  if (!modelId) return null;
  return props.dataModels.find(m => String(m.id) === String(modelId)) || null;
};

const copyDeviceInfo = () => {
  if (!props.device) return;
  const d = props.device;
  const text = `【设备信息】
设备名称: ${d.name}
设备标识 (Key): ${d.key}
通讯协议: ${d.type}
所属区域: ${getAreaName(d.areaId)}
通信状态: ${d.status === 1 || d.status === 'online' ? '在线' : '离线'}
采集状态: ${d.isEnabled ? '启用采集' : '停止采集'}
最后更新: ${d.lastUpdated || '无'}`;
  navigator.clipboard?.writeText?.(text);
};
</script>

<template>
  <div v-if="open && device" class="fixed inset-0 z-50 overflow-hidden text-left">
    <!-- Backdrop -->
    <div class="absolute inset-0 bg-slate-900/40 backdrop-blur-xs transition-opacity" @click="emit('close')" />

    <!-- Drawer container: bottom-sheet on mobile, right side-over on desktop -->
    <div
      class="fixed inset-x-0 bottom-0 md:inset-y-0 md:right-0 md:left-auto max-h-[88vh] md:max-h-full max-w-full md:max-w-md flex flex-col pl-0 md:pl-10">
      <div
        class="w-full md:w-screen md:max-w-md bg-white dark:bg-slate-900 shadow-2xl rounded-t-2xl md:rounded-none border-t md:border-t-0 md:border-l border-slate-200 dark:border-slate-800 flex flex-col select-none max-h-[88vh] md:max-h-full overflow-hidden">
        <!-- Mobile Drag Handle -->
        <div class="pt-2.5 pb-1 flex justify-center md:hidden shrink-0 bg-slate-900">
          <div class="w-10 h-1.5 bg-slate-600 rounded-full" />
        </div>

        <!-- Header -->
        <div class="px-5 py-4 bg-slate-900 text-white flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-2">
            <div class="w-8 h-8 rounded-lg bg-[#1890ff]/20 text-sky-400 flex items-center justify-center font-bold">
              <Cpu class="w-4 h-4" />
            </div>
            <div>
              <h3 class="font-bold text-sm tracking-tight leading-tight">{{ device.name }}</h3>
              <div class="text-[10px] font-mono text-slate-400">KEY: {{ device.key }}</div>
            </div>
          </div>
          <button type="button" @click="emit('close')"
            class="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 cursor-pointer transition-colors">
            <X class="w-4 h-4" />
          </button>
        </div>

        <!-- Scrollable Content Body -->
        <div class="flex-1 overflow-y-auto p-5 space-y-5 text-xs">
          <!-- Status Banner -->
          <div class="p-3.5 rounded-xl border flex items-center justify-between" :class="device.status === 1 || device.status === 'online'
            ? 'bg-emerald-50/70 dark:bg-emerald-950/30 border-emerald-200 dark:border-emerald-800/60'
            : 'bg-slate-50 dark:bg-slate-800/40 border-slate-200 dark:border-slate-700'">
            <div class="flex items-center gap-2.5">
              <div class="relative flex items-center justify-center">
                <span v-if="device.status === 1 || device.status === 'online'"
                  class="w-3 h-3 rounded-full bg-emerald-500 animate-ping absolute opacity-75"></span>
                <span class="w-3 h-3 rounded-full"
                  :class="device.status === 1 || device.status === 'online' ? 'bg-emerald-500' : 'bg-slate-400'"></span>
              </div>
              <div>
                <div class="font-bold text-sm"
                  :class="device.status === 1 || device.status === 'online' ? 'text-emerald-700 dark:text-emerald-300' : 'text-slate-600 dark:text-slate-300'">
                  {{ device.status === 1 || device.status === 'online' ? '设备通信在线' : '设备通信离线' }}
                </div>
                <div class="text-[10px] text-slate-400 dark:text-slate-400">
                  运行时状态: {{ device.runtimeStatus || (device.status === 1 ? 'Online' : 'Offline') }}
                </div>
              </div>
            </div>

            <button type="button" @click="emit('toggleEnabled', device)"
              class="px-2.5 py-1 rounded-lg font-bold text-xs cursor-pointer transition-colors" :class="device.isEnabled
                ? 'bg-sky-600 text-white hover:bg-sky-700'
                : 'bg-slate-200 dark:bg-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-300'">
              {{ device.isEnabled ? '采集中' : '已停采' }}
            </button>
          </div>

          <!-- Section: Basic Asset Info -->
          <div class="space-y-2">
            <h4 class="text-[11px] font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1.5">
              <MapPin class="w-3.5 h-3.5 text-slate-400" />
              <span>资产归属与编码</span>
            </h4>
            <div
              class="bg-slate-50 dark:bg-slate-950/60 border border-slate-200/80 dark:border-slate-800 rounded-xl p-3 space-y-2 text-[11px]">
              <div class="flex justify-between">
                <span class="text-slate-400">设备ID</span>
                <span class="font-mono font-bold text-slate-800 dark:text-slate-200">#{{ device.id }}</span>
              </div>
              <div class="flex justify-between">
                <span class="text-slate-400">所属区域</span>
                <span class="font-medium text-slate-800 dark:text-slate-200">{{ getAreaName(device.areaId) }}</span>
              </div>
              <div class="flex justify-between">
                <span class="text-slate-400">设备协议类型</span>
                <span class="font-mono font-bold text-sky-600 dark:text-sky-400">{{ device.type }}</span>
              </div>
              <div class="flex justify-between">
                <span class="text-slate-400">最近更新</span>
                <span class="font-mono text-slate-600 dark:text-slate-400">{{ device.lastUpdated || '未知' }}</span>
              </div>
            </div>
          </div>

          <!-- Section: Communication Connection -->
          <div class="space-y-2">
            <h4 class="text-[11px] font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1.5">
              <Server class="w-3.5 h-3.5 text-violet-500" />
              <span>通信连接配置</span>
            </h4>
            <div
              class="bg-slate-50 dark:bg-slate-950/60 border border-slate-200/80 dark:border-slate-800 rounded-xl p-3 space-y-2 text-[11px]">
              <template v-if="device.connection">
                <div class="flex justify-between">
                  <span class="text-slate-400">关联连接</span>
                  <span class="font-medium text-violet-600 dark:text-violet-400">#{{ device.connection.id }} - {{
                    device.connection.controllerName || device.connection.controllerCode }}</span>
                </div>
                <div class="flex justify-between">
                  <span class="text-slate-400">通信协议</span>
                  <span class="font-medium text-slate-800 dark:text-slate-200">{{ device.connection.protocolName ||
                    device.connection.protocolKey }}</span>
                </div>
                <div class="flex justify-between">
                  <span class="text-slate-400">网络地址 (Host:Port)</span>
                  <span class="font-mono font-bold text-slate-800 dark:text-slate-200">
                    {{ device.connection.host || '—' }}{{ device.connection.port ? ':' + device.connection.port : '' }}
                  </span>
                </div>
                <div class="flex justify-between">
                  <span class="text-slate-400">超时 / 重连</span>
                  <span class="font-mono text-slate-600 dark:text-slate-400">{{ device.connection.timeoutMs }}ms / {{
                    device.connection.reconnectIntervalMs }}ms</span>
                </div>
              </template>
              <template v-else>
                <div class="text-slate-400 dark:text-slate-500 py-1">
                  未挂载独立连接（使用直接参数模式）
                </div>
                <div v-if="device.ipAddress" class="flex justify-between">
                  <span class="text-slate-400">IP 地址:端口</span>
                  <span class="font-mono font-bold text-slate-800 dark:text-slate-200">{{ device.ipAddress }}:{{
                    device.port || 102 }}</span>
                </div>
                <div v-if="device.endpointUrl" class="flex flex-col gap-1">
                  <span class="text-slate-400">OPC UA 端点</span>
                  <span class="font-mono text-[10px] break-all text-sky-600">{{ device.endpointUrl }}</span>
                </div>
              </template>
            </div>
          </div>

          <!-- Section: Data Models & Bindings -->
          <div class="space-y-2">
            <h4 class="text-[11px] font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1.5">
              <Link2 class="w-3.5 h-3.5 text-[#1890ff]" />
              <span>数据模型与绑定</span>
            </h4>
            <div
              class="bg-slate-50 dark:bg-slate-950/60 border border-slate-200/80 dark:border-slate-800 rounded-xl p-3 space-y-2.5">
              <!-- Primary Model -->
              <div
                class="flex items-start gap-2 p-2 rounded-lg bg-sky-50/80 dark:bg-sky-950/40 border border-sky-100 dark:border-sky-900">
                <Star class="w-4 h-4 text-[#1890ff] shrink-0 mt-0.5" fill="currentColor" />
                <div class="flex-1 min-w-0">
                  <div class="flex items-center gap-1.5">
                    <span class="font-bold text-slate-800 dark:text-white truncate">
                      {{ getModel(device.modelId)?.name || `模型 #${device.modelId}` }}
                    </span>
                    <span class="text-[9px] font-bold bg-[#1890ff] text-white px-1.5 py-0.2 rounded">主模型</span>
                  </div>
                  <div class="text-[10px] font-mono text-slate-400 mt-0.5">
                    编码: {{ getModel(device.modelId)?.code || '—' }} · v{{ getModel(device.modelId)?.version || '1.0' }}
                  </div>
                </div>
              </div>

              <!-- Additional Models -->
              <div v-if="device.models && device.models.filter(m => !m.isPrimary).length > 0" class="space-y-1.5 pt-1">
                <div class="text-[10px] text-slate-400 font-bold">附加绑定模型:</div>
                <div v-for="b in device.models.filter(m => !m.isPrimary)" :key="b.id"
                  class="flex items-center justify-between p-2 rounded-lg bg-white dark:bg-slate-900 border border-slate-200/80 dark:border-slate-800 text-[11px]">
                  <span class="font-medium text-slate-700 dark:text-slate-300 truncate">{{ b.name || b.code }}</span>
                  <span class="text-[10px] font-mono text-slate-400">{{ b.variableCount }} 点位</span>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Footer Action Buttons -->
        <div
          class="p-4 bg-slate-50 dark:bg-slate-950 border-t border-slate-200 dark:border-slate-800 flex flex-wrap items-center justify-between gap-2.5">
          <button type="button" @click="copyDeviceInfo"
            class="py-2 px-3 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 font-bold text-xs inline-flex items-center justify-center gap-1.5 cursor-pointer shadow-2xs">
            <Copy class="w-3.5 h-3.5 text-slate-400" />
            <span>复制信息</span>
          </button>
          <div class="flex-1 flex items-center gap-2">
            <button type="button" @click="emit('variables', device)"
              class="flex-1 py-2 px-3 rounded-lg bg-emerald-600 hover:bg-emerald-700 text-white font-bold text-xs inline-flex items-center justify-center gap-1.5 cursor-pointer transition-colors shadow-xs">
              <Braces class="w-4 h-4" />
              <span>点位映射</span>
            </button>
            <button type="button" @click="emit('edit', device)"
              class="flex-1 py-2 px-3 rounded-lg bg-[#1890ff] hover:bg-sky-600 text-white font-bold text-xs inline-flex items-center justify-center gap-1.5 cursor-pointer transition-colors shadow-xs">
              <Edit3 class="w-4 h-4" />
              <span>编辑配置</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
