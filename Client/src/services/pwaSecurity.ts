/**
 * PWA 跨账号安全（doc/pwa 阶段三 · 步骤 10，D5 防线二/三）。
 *
 * clearUserData() 在两处执行：
 *  1. SW 侧：postMessage CLEAR_USER_DATA → 删 api-runtime / html-runtime 运行时缓存
 *     （不删 precache，壳不破；precache 无账号数据，天然安全）；
 *  2. IndexedDB 侧：清空快照库（防线三命名空间由 snapshotDB 内部按 userId 隔离，
 *     此处整体 clearAll 仅在登录态已切换/登出后调用，移除的是当前浏览器全部快照）。
 *
 * 调用点（authApi 登出/换号 + http.ts 401）见 authApi.ts / http.ts。
 */

export const LAST_USER_ID_KEY = 'scada_last_user_id';

export const getLastUserId = (): string | null => {
  try {
    return localStorage.getItem(LAST_USER_ID_KEY);
  } catch {
    return null;
  }
};

export const setLastUserId = (id: string): void => {
  try {
    localStorage.setItem(LAST_USER_ID_KEY, id);
  } catch {
    /* 隐私模式等不可写场景忽略 */
  }
};

export const clearLastUserId = (): void => {
  try {
    localStorage.removeItem(LAST_USER_ID_KEY);
  } catch {
    /* ignore */
  }
};

/**
 * 清空当前浏览器的用户态缓存（SW 运行时缓存 + IndexedDB 快照）。
 * 设计为可安全多次调用；任何单步失败都不抛出（不阻断登出/换号主流程）。
 */
export async function clearUserData(): Promise<void> {
  // 先停快照写入器（阶段六：防止清理过程中写入器再落盘新数据）
  try {
    const writer = await import('./snapshotWriter');
    writer.stop();
  } catch {
    /* ignore */
  }

  // 防线二：通知 SW 清空运行时缓存（navigateFallback / 白名单 API 响应）
  try {
    const controller = navigator.serviceWorker?.controller;
    if (controller) {
      await new Promise<void>((resolve) => {
        const ch = new MessageChannel();
        const timer = setTimeout(resolve, 1500);
        ch.port1.onmessage = () => {
          clearTimeout(timer);
          resolve();
        };
        controller.postMessage({ type: 'CLEAR_USER_DATA' }, [ch.port2]);
      });
    }
  } catch (e) {
    console.warn('[PWA] 清空 SW 运行时缓存失败', e);
  }

  // 防线三：清空 IndexedDB 快照（阶段六落地，此处容错——未初始化时静默跳过）
  try {
    const mod = await import('./snapshotDB');
    await mod.clearAll();
  } catch (e) {
    // snapshotDB 尚未初始化/不可用：忽略（防御性，不阻断主流程）
  }
}
