<script setup lang="ts">
import { computed } from 'vue';
import { Network, Power, MonitorDot } from 'lucide-vue-next';
import { devices } from '../store/deviceStore';
import { Device, protocolKeyToDeviceType } from '../types';

/**
 * 共享「关联设备面板」：展示被某个控制器 / 连接引用的设备列表。
 * 关联判定完全基于 devices store：控制器按 Device.controllerId、连接按 Device.
 * connectionId 过滤（均在后端 DeviceDto 填充、normalizeDevices 透传），无需后端新接口。
 */
const props = defineProps<{
  ownerType: 'controller' | 'connection';
  ownerId: number | null;
}>();

const refereeDevices = computed<Device[]>(() => {
  if (props.ownerId == null) return [];
  return devices.value.filter(d =>
    props.ownerType === 'controller'
      ? Number(d.controllerId) === Number(props.ownerId)
      : Number(d.connectionId) === Number(props.ownerId)
  );
});

const deviceAddress = (d: Device): string => {
  const ip = d.endpointUrl || d.ipAddress || '';
  const port = d.port ? `:${d.port}` : '';
  return ip ? `${ip}${port}` : '—';
};

const typeLabel = (d: Device): string => (d.protocolKey || d.type || protocolKeyToDeviceType(d.protocolKey) || '').toUpperCase();

const statusInfo = (d: Device): { text: string; cls: string; dot: string } => {
  const s = d.status;
  if (s === 1) return { text: '在线', cls: 'text-emerald-600 dark:text-emerald-400', dot: 'bg-emerald-500' };
  if (s === 2) return { text: '故障', cls: 'text-rose-600 dark:text-rose-400', dot: 'bg-rose-500' };
  if (s === 4) return { text: '连接中', cls: 'text-amber-600 dark:text-amber-400', dot: 'bg-amber-500' };
  if (s === 3) return { text: '配置更新', cls: 'text-sky-600 dark:text-sky-400', dot: 'bg-sky-500' };
  return { text: '离线', cls: 'text-slate-400 dark:text-slate-500', dot: 'bg-slate-300 dark:bg-slate-600' };
};

const fmtTime = (ts?: string | null) => {
  if (!ts) return '—';
  const d = new Date(ts);
  if (isNaN(d.getTime())) return ts;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
};
</script>

<template>
  <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm text-left transition-colors">
    <div class="flex items-center justify-between px-4 py-3 border-b border-slate-100 dark:border-slate-800">
      <h3 class="text-xs font-bold tracking-widest uppercase text-slate-500 dark:text-slate-400 inline-flex items-center gap-1.5">
        <Network class="w-3.5 h-3.5" />
        被以下设备关联（{{ refereeDevices.length }}）
      </h3>
      <span class="text-[10px] text-slate-400 dark:text-slate-500">{{ refereeDevices.length > 0 ? '共享连接/控制器可被多台设备引用' : '无设备引用' }}</span>
    </div>

    <table class="w-full text-xs">
      <thead>
        <tr class="bg-slate-50 dark:bg-slate-950/60 ring-1 ring-slate-100 dark:ring-slate-800 uppercase text-[10px] text-slate-400 dark:text-slate-500 font-bold tracking-wider">
          <th class="px-4 py-3 text-left">名称</th>
          <th class="px-4 py-3 text-left">Key</th>
          <th class="px-4 py-3 text-left">区域</th>
          <th class="px-4 py-3 text-left">类型</th>
          <th class="px-4 py-3 text-left">地址</th>
          <th class="px-4 py-3 text-left">启用</th>
          <th class="px-4 py-3 text-left">状态</th>
          <th class="px-4 py-3 text-right">更新时间</th>
        </tr>
      </thead>
      <tbody class="divide-y divide-slate-100 dark:divide-slate-800">
        <tr v-for="d in refereeDevices" :key="d.id" class="hover:bg-slate-50/50 dark:hover:bg-slate-800/40 transition-all">
          <td class="px-4 py-3 font-sans font-bold text-slate-800 dark:text-white inline-flex items-center gap-1.5">
            <MonitorDot class="w-3.5 h-3.5 text-slate-400 shrink-0" />
            {{ d.name }}
          </td>
          <td class="px-4 py-3 font-mono text-slate-400 dark:text-slate-500">{{ d.key }}</td>
          <td class="px-4 py-3 text-slate-500 dark:text-slate-400">{{ d.areaName || '—' }}</td>
          <td class="px-4 py-3">
            <span class="bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 font-bold px-2 py-0.5 rounded-full text-[10px]">
              {{ typeLabel(d) }}
            </span>
          </td>
          <td class="px-4 py-3 font-mono text-slate-500 dark:text-slate-400">{{ deviceAddress(d) }}</td>
          <td class="px-4 py-3">
            <span
              class="inline-flex items-center gap-1 text-[10px] font-bold px-2 py-0.5 rounded-full border"
              :class="d.isEnabled
                ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-800'
                : 'bg-slate-100 dark:bg-slate-800 text-slate-400 border-slate-200 dark:border-slate-700'"
            >
              <Power class="w-3 h-3" />
              {{ d.isEnabled ? '启用' : '停用' }}
            </span>
          </td>
          <td class="px-4 py-3">
            <span class="inline-flex items-center gap-1.5 font-bold">
              <i class="w-2 h-2 rounded-full shrink-0" :class="statusInfo(d).dot" />
              <span :class="statusInfo(d).cls">{{ statusInfo(d).text }}</span>
            </span>
          </td>
          <td class="px-4 py-3 font-mono text-slate-400 dark:text-slate-500 text-right">{{ fmtTime(d.lastUpdated) }}</td>
        </tr>
      </tbody>
    </table>

    <div v-if="refereeDevices.length === 0" class="py-10 text-center text-slate-400 dark:text-slate-500 text-xs">
      <Power class="w-8 h-8 mx-auto mb-2 opacity-20" />
      <span>暂无设备关联</span>
    </div>
  </div>
</template>