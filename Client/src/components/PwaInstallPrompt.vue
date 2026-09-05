<script setup lang="ts">
/**
 * 可安装入口与 iOS 引导（doc/pwa 阶段二 · 步骤 7，目标 1）。
 * - 桌面/Android：捕获 beforeinstallprompt 后展示「安装到桌面」按钮；
 * - iOS Safari：无 beforeinstallprompt，展示一次性「分享 → 添加到主屏幕」图文引导；
 * - 用户拒绝/收起后 7 天内不再主动弹（localStorage 记时间戳）。
 */
import { computed, onMounted, ref } from 'vue';
import { canInstall, isIOS, isStandalone, promptInstall } from '../services/pwaRegister';

const DISMISS_KEY = 'scada_pwa_install_dismissed_ts';
const SUPPRESS_DAYS = 7;

const dismissed = ref(false);
const installing = ref(false);

const isStandaloneMode = ref(false);

onMounted(() => {
  isStandaloneMode.value = isStandalone();
  const ts = Number(localStorage.getItem(DISMISS_KEY) || 0);
  if (ts && Date.now() - ts < SUPPRESS_DAYS * 24 * 3600 * 1000) {
    dismissed.value = true;
  }
});

// 已安装态不再展示任何引导
const hidden = computed(
  () => isStandaloneMode.value || dismissed.value || !canInstall.value
);

const visible = computed(() => !hidden.value && (canInstall.value || isIOS()));

const markDismissed = () => {
  dismissed.value = true;
  localStorage.setItem(DISMISS_KEY, String(Date.now()));
};

const doInstall = async () => {
  installing.value = true;
  const ok = await promptInstall();
  installing.value = false;
  if (!ok) markDismissed();
};
</script>

<template>
  <transition name="pwa-fade">
    <!-- 桌面/Android：安装按钮 -->
    <div v-if="visible && canInstall && !isStandaloneMode"
      class="fixed bottom-4 left-4 z-50 w-[300px] rounded-xl border shadow-lg backdrop-blur-xl px-4 py-3 flex items-center gap-3 text-xs transition-colors bg-white/95 dark:bg-slate-900/95 border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200">
      <div
        class="w-8 h-8 rounded-lg bg-sky-100 dark:bg-slate-800 flex items-center justify-center shrink-0">
        <svg class="w-4 h-4 text-sky-600 dark:text-sky-400" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M12 3v12" />
          <path d="m8 11 4 4 4-4" />
          <path d="M4 21h16" />
        </svg>
      </div>
      <div class="flex-1 min-w-0">
        <p class="font-bold leading-snug">安装到桌面</p>
        <p class="text-[11px] opacity-70 mt-0.5 leading-snug">
          添加到桌面后可作为独立应用启动，支持离线访问与消息通知。
        </p>
      </div>
      <button @click="doInstall" :disabled="installing"
        class="px-3 py-1.5 rounded-lg bg-gradient-to-r from-sky-600 to-indigo-600 hover:from-sky-500 hover:to-indigo-500 text-white font-bold text-[11px] shadow-sm active:scale-95 disabled:opacity-60 cursor-pointer transition-all shrink-0">
        {{ installing ? '安装中…' : '安装' }}
      </button>
      <button @click="markDismissed"
        class="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer shrink-0">
        <svg class="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
          stroke-linecap="round" stroke-linejoin="round">
          <path d="M18 6 6 18M6 6l12 12" />
        </svg>
      </button>
    </div>

    <!-- iOS Safari：添加到主屏幕引导（一次性） -->
    <div v-else-if="visible && isIOS() && !canInstall"
      class="fixed bottom-4 left-4 z-50 w-[300px] rounded-xl border shadow-lg backdrop-blur-xl px-4 py-3 text-xs transition-colors bg-white/95 dark:bg-slate-900/95 border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200">
      <div class="flex items-start gap-3">
        <div
          class="w-8 h-8 rounded-lg bg-sky-100 dark:bg-slate-800 flex items-center justify-center shrink-0">
          <svg class="w-4 h-4 text-sky-600 dark:text-sky-400" viewBox="0 0 24 24" fill="none"
            stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 3v12" />
            <path d="m8 11 4 4 4-4" />
            <path d="M4 21h16" />
          </svg>
        </div>
        <div class="flex-1 min-w-0">
          <p class="font-bold leading-snug">添加到主屏幕</p>
          <p class="text-[11px] opacity-70 mt-1 leading-snug">
            点击底部「分享」按钮
            <span class="inline-flex mx-1 align-middle">
              <svg class="w-3.5 h-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8" />
                <path d="m16 6-4-4-4 4" />
                <path d="M12 2v14" />
              </svg>
            </span>
            ，选择「添加到主屏幕」即可安装为 App，并支持推送通知。
          </p>
        </div>
        <button @click="markDismissed"
          class="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer shrink-0">
          <svg class="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
            stroke-linecap="round" stroke-linejoin="round">
            <path d="M18 6 6 18M6 6l12 12" />
          </svg>
        </button>
      </div>
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
