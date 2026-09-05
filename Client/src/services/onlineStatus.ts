/**
 * 网络状态响应式封装（doc/pwa 阶段三 · 步骤 9 / 阶段六 · 步骤 29）。
 *
 * - isOnline：online/offline 事件驱动的响应式状态；
 * - onRecover：网络从离线恢复时的一次性回调（供阶段六触发全量刷新）；
 * - 恢复信号做 3s 去抖，避免抖动网络（频繁断连）触发刷新风暴。
 */

import { ref } from 'vue';

export const isOnline = ref<boolean>(
  typeof navigator !== 'undefined' ? navigator.onLine : true
);

type RecoverListener = () => void;
const recoverListeners: RecoverListener[] = [];

let wasOffline = !isOnline.value;
let recoverTimer: ReturnType<typeof setTimeout> | null = null;

/** 注册「网络恢复」回调（去抖后仅触发一次） */
export function onRecover(cb: RecoverListener): void {
  recoverListeners.push(cb);
}

function emitRecover(): void {
  if (recoverTimer) clearTimeout(recoverTimer);
  recoverTimer = setTimeout(() => {
    recoverListeners.forEach((cb) => {
      try {
        cb();
      } catch (e) {
        console.warn('[PWA] onRecover 回调异常', e);
      }
    });
  }, 3000);
}

if (typeof window !== 'undefined') {
  window.addEventListener('online', () => {
    isOnline.value = true;
    if (wasOffline) {
      wasOffline = false;
      emitRecover();
    }
  });
  window.addEventListener('offline', () => {
    isOnline.value = false;
    wasOffline = true;
  });
}
