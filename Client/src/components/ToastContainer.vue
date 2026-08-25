<script setup lang="ts">
import { toasts, dismissToast, ToastItem } from '../services/toastService';
import { CheckCircle2, XCircle, AlertTriangle, Info, X } from 'lucide-vue-next';

const iconByType: Record<ToastItem['type'], any> = {
  success: CheckCircle2,
  error: XCircle,
  warning: AlertTriangle,
  info: Info
};

const styleByType: Record<ToastItem['type'], string> = {
  success: 'bg-emerald-600 text-white',
  error: 'bg-rose-600 text-white',
  warning: 'bg-amber-500 text-white',
  info: 'bg-sky-600 text-white'
};
</script>

<template>
  <div class="fixed top-4 right-4 z-[9999] flex flex-col gap-2 items-end pointer-events-none max-w-[92vw] sm:max-w-md">
    <TransitionGroup
      enter-active-class="transition duration-200 ease-out"
      enter-from-class="opacity-0 translate-x-6"
      enter-to-class="opacity-100 translate-x-0"
      leave-active-class="transition duration-150 ease-in"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0 translate-x-6"
    >
      <div
        v-for="t in toasts"
        :key="t.id"
        class="pointer-events-auto w-full flex items-start gap-2.5 rounded-lg shadow-lg px-3.5 py-3 text-xs font-medium leading-relaxed"
        :class="styleByType[t.type]"
        role="alert"
      >
        <component :is="iconByType[t.type]" class="w-4 h-4 shrink-0 mt-0.5" />
        <span class="flex-1 break-words whitespace-pre-line">{{ t.message }}</span>
        <button
          @click="dismissToast(t.id)"
          class="shrink-0 opacity-70 hover:opacity-100 transition-opacity cursor-pointer"
          title="关闭"
        >
          <X class="w-3.5 h-3.5" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>
