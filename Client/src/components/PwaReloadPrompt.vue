<script setup lang="ts">
/**
 * 新版本就绪提示（doc/pwa 阶段二 · 步骤 6，D2 prompt 更新）。
 * 绝不自动 reload：用户点「立即更新」才触发 SW 跳过等待 + 刷新；
 * 点「稍后」仅本地收起，保留 waiting SW，下次刷新横幅再现。
 */
import { computed, ref } from 'vue';
import { needRefresh, updateServiceWorker } from '../services/pwaRegister';
import { currentTheme } from '../store';

const dismissed = ref(false);
const applying = ref(false);

const visible = computed(() => needRefresh.value && !dismissed.value);

const dismiss = () => {
  dismissed.value = true;
};

const reload = () => {
  applying.value = true;
  updateServiceWorker();
};
</script>

<template>
  <transition name="pwa-fade">
    <div v-if="visible"
      class="fixed bottom-4 right-4 z-50 max-w-xs w-[320px] rounded-xl border shadow-lg backdrop-blur-xl px-4 py-3 flex items-start gap-3 text-xs transition-colors"
      :class="currentTheme === 'dark'
        ? 'bg-slate-900/95 border-slate-700 text-slate-200'
        : 'bg-white/95 border-slate-200 text-slate-700'">
      <div
        class="w-8 h-8 rounded-lg bg-sky-100 dark:bg-slate-800 flex items-center justify-center shrink-0">
        <svg class="w-4 h-4 text-sky-600 dark:text-sky-400" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M21 12a9 9 0 1 1-3-6.7L21 8" />
          <path d="M21 3v5h-5" />
        </svg>
      </div>
      <div class="flex-1 min-w-0">
        <p class="font-bold leading-snug">新版本已就绪</p>
        <p class="text-[11px] opacity-70 mt-0.5 leading-snug">
          已下载到本地，点击更新即可切换到最新版本（不会自动刷新）。
        </p>
        <div class="flex items-center gap-2 mt-2">
          <button @click="reload" :disabled="applying"
            class="px-3 py-1 rounded-lg bg-gradient-to-r from-sky-600 to-indigo-600 hover:from-sky-500 hover:to-indigo-500 text-white font-bold text-[11px] shadow-sm active:scale-95 disabled:opacity-60 cursor-pointer transition-all">
            {{ applying ? '更新中…' : '立即更新' }}
          </button>
          <button @click="dismiss"
            class="px-3 py-1 rounded-lg border border-slate-200 dark:border-slate-700 text-[11px] font-bold hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer transition-all">
            稍后
          </button>
        </div>
      </div>
      <button @click="dismiss"
        class="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer shrink-0">
        <svg class="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
          stroke-linecap="round" stroke-linejoin="round">
          <path d="M18 6 6 18M6 6l12 12" />
        </svg>
      </button>
    </div>
  </transition>
</template>

<style scoped>
.pwa-fade-enter-active,
.pwa-fade-leave-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}

.pwa-fade-enter-from,
.pwa-fade-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
