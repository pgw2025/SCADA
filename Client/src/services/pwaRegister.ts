/**
 * PWA 注册与生命周期接线（doc/pwa 阶段二 · 步骤 5/7）。
 *
 * - 通过 virtual:pwa-register 的 registerSW 注册 SW（immediate:false，D2 prompt 更新）；
 * - 导出响应式 needRefresh / offlineReady / updateServiceWorker()；
 * - 监听 SW 的 NAVIGATE 消息（点击推送通知后跳转，D6）；
 * - 捕获 beforeinstallprompt，导出 canInstall / promptInstall()（目标 1 可安装）；
 * - 提供 isStandalone() / isIOS() 工具。
 *
 * 命名约定：本模块函数均以浏览器全局对象（navigator / window / matchMedia）为前置，
 * 仅在已挂载的客户端环境调用；首屏注册入口为 registerPwa()，在 main.ts 的 app.mount 之后调用。
 */

import { registerSW } from 'virtual:pwa-register';
import { ref, type Ref } from 'vue';
import router from '../router';

/** beforeinstallprompt 事件的最小类型（浏览器未内置） */
interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

/** 新版本已就绪（prompt 模式：绝不自动 reload，等用户点「立即更新」） */
export const needRefresh: Ref<boolean> = ref(false);
/** 首次预缓存完成（离线可用） */
export const offlineReady: Ref<boolean> = ref(false);
/** 可安装态（捕获到 beforeinstallprompt 且尚未安装） */
export const canInstall: Ref<boolean> = ref(false);

let updateSW: (reloadPage?: boolean) => Promise<void> = async () => {};
const deferredPrompt: Ref<BeforeInstallPromptEvent | null> = ref(null);
let started = false;

/** 触发 SW 跳过等待并刷新（用户手势「立即更新」调用，D2） */
export function updateServiceWorker(): void {
  void updateSW(true);
}

/** 用户手势触发安装（返回是否接受）。拒绝后由组件级 localStorage 抑制重复弹窗 */
export async function promptInstall(): Promise<boolean> {
  const evt = deferredPrompt.value;
  if (!evt) return false;
  try {
    await evt.prompt();
    const { outcome } = await evt.userChoice;
    return outcome === 'accepted';
  } catch {
    return false;
  } finally {
    deferredPrompt.value = null;
    canInstall.value = false;
  }
}

/** 是否以 PWA 独立模式运行（已安装到桌面/主屏） */
export function isStandalone(): boolean {
  return (
    typeof window !== 'undefined' &&
    (window.matchMedia('(display-mode: standalone)').matches ||
      (navigator as unknown as { standalone?: boolean }).standalone === true)
  );
}

/** 是否 iOS Safari（无 beforeinstallprompt，需引导「添加到主屏幕」） */
export function isIOS(): boolean {
  if (typeof navigator === 'undefined') return false;
  const ua = navigator.userAgent;
  const iOS = /iPad|iPhone|iPod/.test(ua);
  const webkit = /WebKit/.test(ua);
  const notChrome = !/CriOS|FxiOS/.test(ua);
  return iOS && webkit && notChrome;
}

function init(): void {
  updateSW = registerSW({
    immediate: false,
    onNeedRefresh() {
      needRefresh.value = true;
    },
    onOfflineReady() {
      offlineReady.value = true;
    },
    onRegisterError(error) {
      console.error('[PWA] Service Worker 注册失败', error);
    }
  });

  // 捕获可安装事件（仅在桌面 Chrome/Edge/Android 触发；iOS 不触发）
  window.addEventListener('beforeinstallprompt', (e: Event) => {
    e.preventDefault();
    deferredPrompt.value = e as BeforeInstallPromptEvent;
    canInstall.value = true;
  });
  window.addEventListener('appinstalled', () => {
    deferredPrompt.value = null;
    canInstall.value = false;
    console.info('[PWA] 应用已安装到本设备');
  });

  // SW → 页面：点击推送通知后的路由跳转（D6，角色兜底由路由守卫负责）
  if (navigator.serviceWorker) {
    navigator.serviceWorker.addEventListener('message', (event: MessageEvent) => {
      const data = event.data || {};
      if (data.type === 'NAVIGATE' && typeof data.url === 'string') {
        router.push(data.url);
      }
    });
  }
}

/** 首屏注册入口（main.ts 在 app.mount 之后调用，避免与 initializeAuth/路由守卫竞态） */
export function registerPwa(): void {
  if (started) return;
  started = true;
  init();
}
