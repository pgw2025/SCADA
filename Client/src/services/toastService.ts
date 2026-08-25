import { ref } from 'vue';

export type ToastType = 'success' | 'error' | 'warning' | 'info';

export interface ToastItem {
  id: number;
  type: ToastType;
  message: string;
}

/** 全局 Toast 队列（ToastContainer.vue 渲染） */
export const toasts = ref<ToastItem[]>([]);

const MAX_TOASTS = 5;
const DEDUP_INTERVAL_MS = 3000;

let nextId = 1;
let lastMessageAt = new Map<string, number>();

/**
 * 弹出全局提示。
 * 内置去重：相同文案 3 秒内只弹一次，避免轮询类请求持续失败时刷屏。
 */
export const showToast = (message: string, type: ToastType = 'info', durationMs = 4500) => {
  const now = Date.now();
  const lastAt = lastMessageAt.get(message) ?? 0;
  if (now - lastAt < DEDUP_INTERVAL_MS) return;
  lastMessageAt.set(message, now);

  const item: ToastItem = { id: nextId++, type, message };
  toasts.value.push(item);
  if (toasts.value.length > MAX_TOASTS) {
    toasts.value.shift();
  }

  setTimeout(() => dismissToast(item.id), durationMs);
};

export const dismissToast = (id: number) => {
  toasts.value = toasts.value.filter(t => t.id !== id);
};
