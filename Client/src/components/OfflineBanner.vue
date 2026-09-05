<script setup lang="ts">
/**
 * 离线横幅（doc/pwa 阶段三 · 步骤 9，D10）。
 * 仅离线时显示顶部细横幅「离线模式 · 数据截至 HH:mm:ss」；
 * 时间戳读取快照 meta.savedAtUtc（阶段六前显示「—」占位）。
 * 高度 ≤32px，本地化时间格式。
 */
import { computed, ref, watch } from 'vue';
import { isOnline } from '../services/onlineStatus';
import { currentTheme } from '../store';

const savedAt = ref<string>('—');

const fmt = (iso: string | null): string => {
  if (!iso) return '—';
  try {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '—';
    const p = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
  } catch {
    return '—';
  }
};

const refreshTs = async (): Promise<void> => {
  try {
    const mod = await import('../services/snapshotDB');
    savedAt.value = fmt(await mod.getSnapshotTimestamp());
  } catch {
    savedAt.value = '—';
  }
};

watch(
  isOnline,
  async (off) => {
    if (!off) await refreshTs();
  },
  { immediate: true }
);

const visible = computed(() => !isOnline.value);
</script>

<template>
  <transition name="offline-slide">
    <div v-if="visible"
      class="fixed top-0 inset-x-0 z-40 h-7 flex items-center justify-center gap-2 text-[11px] font-semibold tracking-wide"
      :class="currentTheme === 'dark'
        ? 'bg-amber-500/90 text-slate-950'
        : 'bg-amber-400 text-slate-900'">
      <svg class="w-3.5 h-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
        stroke-linecap="round" stroke-linejoin="round">
        <path d="M1 1l22 22" />
        <path d="M16.72 11.06A10.94 10.94 0 0 1 19 12.55" />
        <path d="M5 12.55a10.94 10.94 0 0 1 5.17-2.39" />
        <path d="M10.71 5.05A16 16 0 0 1 22.58 9" />
        <path d="M1.42 9a15.91 15.91 0 0 1 4.7-2.88" />
        <path d="M8.53 16.11a6 6 0 0 1 6.95 0" />
        <path d="M12 20h.01" />
      </svg>
      <span>离线模式 · 数据截至 {{ savedAt }}</span>
    </div>
  </transition>
</template>

<style scoped>
.offline-slide-enter-active,
.offline-slide-leave-active {
  transition: transform 0.2s ease, opacity 0.2s ease;
}

.offline-slide-enter-from,
.offline-slide-leave-to {
  transform: translateY(-100%);
  opacity: 0;
}
</style>
