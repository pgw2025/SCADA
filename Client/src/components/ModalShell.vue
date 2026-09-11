<script setup lang="ts">
// 统一模态壳：标题栏 + 权限锁标 + 关闭按钮（X/ESC/遮罩三路关闭）+ 居中滚动容器。
// 由 PopupHost / PopupPageHost 复用，避免关闭逻辑多份漂移（方案 O1）。
import { onMounted, onUnmounted } from 'vue';
import { X, Lock } from 'lucide-vue-next';

defineProps<{
  /** 弹窗标题 */
  title: string;
  /** 当前角色是否有写权限（显示权限锁标，仅提示用） */
  canControlWrite?: boolean;
}>();

const emit = defineEmits<{ (e: 'close'): void }>();

const onKeydown = (e: KeyboardEvent) => { if (e.key === 'Escape') emit('close'); };
onMounted(() => window.addEventListener('keydown', onKeydown));
onUnmounted(() => window.removeEventListener('keydown', onKeydown));
</script>

<template>
  <!-- 对齐 SetValueDialog 既有约定：fixed inset-0 z-[100]，无 teleport；与画布缩放容器同级，避免随画布缩放失真 -->
  <div class="fixed inset-0 z-[100] flex items-center justify-center bg-black/50 backdrop-blur-sm p-4"
    @click.self="emit('close')">
    <div class="flex max-h-full w-auto max-w-full flex-col overflow-hidden rounded-xl shadow-2xl
                bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700">
      <!-- 标题栏 -->
      <header class="flex items-center justify-between gap-2 px-4 py-2.5 border-b border-slate-200
                     dark:border-slate-700 shrink-0">
        <span class="flex items-center gap-1.5 text-sm font-bold text-slate-800 dark:text-slate-100 truncate">
          <Lock v-if="!canControlWrite" class="w-3.5 h-3.5 text-slate-400 shrink-0" />
          <span class="truncate">{{ title }}</span>
        </span>
        <button @click="emit('close')"
          class="p-1 rounded text-slate-400 hover:text-red-500 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer shrink-0"
          title="关闭 (Esc)">
          <X class="w-4 h-4" />
        </button>
      </header>
      <!-- 内容区：滚动容器 -->
      <div class="min-h-0 flex-1 overflow-auto"><slot /></div>
    </div>
  </div>
</template>