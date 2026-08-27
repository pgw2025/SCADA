<script setup lang="ts">
import { computed } from 'vue';
import { currentPage } from '../store/scadaStore';
import { devices } from '../store/deviceStore';
import { showToast } from '../services/toastService';

const emit = defineEmits<{ (e: 'locate', id: string): void }>();

// 严格模式：组件必须绑定设备维度。未绑定设备（bindDeviceId 为空）即视为待补绑项。
const unboundComponents = computed(() => {
  const page = currentPage.value;
  if (!page || !page.components) return [];
  return page.components.filter(
    (c) => c.bindDeviceId == null || c.bindDeviceId === '' || c.bindDeviceId === undefined
  );
});

const total = computed(() => currentPage.value?.components.length ?? 0);
const unboundCount = computed(() => unboundComponents.value.length);

function boundDeviceName(bindDeviceId: any): string {
  if (bindDeviceId == null || bindDeviceId === '') return '—';
  const dev = devices.value.find((d) => String(d.id) === String(bindDeviceId));
  return dev ? dev.name : `设备#${bindDeviceId}`;
}

function locate(id: string) {
  emit('locate', id);
  showToast('已定位组件，请在右侧属性面板补选绑定设备', 'info');
}
</script>

<template>
  <div class="binding-check-panel w-72 max-h-[70vh] overflow-y-auto bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg shadow-lg text-slate-800 dark:text-slate-100">
    <div class="sticky top-0 z-10 px-3 py-2 border-b border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900">
      <div class="flex items-center justify-between">
        <span class="font-bold text-sm">绑定检查</span>
        <span
          class="text-[11px] px-1.5 py-0.5 rounded-full"
          :class="unboundCount === 0
            ? 'bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300'
            : 'bg-amber-100 dark:bg-amber-900/40 text-amber-700 dark:text-amber-300'"
        >
          {{ unboundCount === 0 ? '全部已绑定' : `待补绑 ${unboundCount}` }}
        </span>
      </div>
      <p class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">
        当前画面「{{ currentPage?.name }}」共 {{ total }} 个组件
      </p>
    </div>

    <div v-if="unboundCount === 0" class="px-3 py-6 text-center text-emerald-600 dark:text-emerald-400 text-xs">
      ✓ 当前画面所有组件均已绑定设备，无裸 Key 风险
    </div>

    <ul v-else class="divide-y divide-slate-100 dark:divide-slate-800">
      <li v-for="c in unboundComponents" :key="c.id" class="px-3 py-2 flex items-start gap-2 hover:bg-slate-50 dark:hover:bg-slate-800/50">
        <div class="flex-1 min-w-0">
          <div class="flex items-center gap-1.5">
            <span class="text-[11px] font-semibold text-[#1890ff] dark:text-sky-400">{{ c.type }}</span>
            <span class="text-[10px] text-slate-400 dark:text-slate-500 font-mono truncate">{{ c.id }}</span>
          </div>
          <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5 truncate">
            变量：{{ c.bindVariableKey || c.bindField || '（未设置）' }}
          </div>
        </div>
        <button
          class="shrink-0 text-[11px] px-2 py-1 rounded bg-sky-50 dark:bg-sky-950/50 text-[#1890ff] dark:text-sky-400 hover:bg-sky-100 dark:hover:bg-sky-900/60"
          @click="locate(c.id)"
        >
          定位
        </button>
      </li>
    </ul>

    <div v-if="unboundCount > 0" class="px-3 py-2 border-t border-slate-200 dark:border-slate-700 text-[10px] text-slate-400 dark:text-slate-500">
      点击「定位」→ 在属性面板补选绑定设备即可消除裸 Key。
    </div>
  </div>
</template>
