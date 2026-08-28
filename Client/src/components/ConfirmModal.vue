<script setup lang="ts">
import { X } from 'lucide-vue-next';

const props = defineProps<{
  open: boolean;
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  danger?: boolean;
}>();

const emit = defineEmits<{
  (e: 'confirm'): void;
  (e: 'cancel'): void;
}>();
</script>

<template>
  <div
    v-if="open"
    class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-[60] p-4"
    @click.self="emit('cancel')"
  >
    <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
      <div
        class="flex items-center justify-between px-4 py-3 border-b border-slate-100 dark:border-slate-800"
        :class="danger ? 'bg-red-50 dark:bg-red-950/40' : 'bg-slate-50 dark:bg-slate-950'"
      >
        <h3
          class="font-bold text-sm"
          :class="danger ? 'text-red-600 dark:text-red-400' : 'text-slate-800 dark:text-slate-100'"
        >
          {{ title }}
        </h3>
        <button @click="emit('cancel')" class="text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 cursor-pointer">
          <X class="w-4 h-4" />
        </button>
      </div>

      <div class="p-4 text-xs text-slate-600 dark:text-slate-300 leading-relaxed">
        {{ message }}
      </div>

      <div class="p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800">
        <button
          @click="emit('cancel')"
          class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
        >
          {{ cancelText || '取消' }}
        </button>
        <button
          @click="emit('confirm')"
          class="px-4 py-1.5 rounded-lg font-bold text-xs text-white cursor-pointer"
          :class="danger ? 'bg-red-500 hover:bg-red-600' : 'bg-[#1890ff] hover:bg-sky-500'"
        >
          {{ confirmText || '确认' }}
        </button>
      </div>
    </div>
  </div>
</template>
