/// <reference types="vite-plugin-pwa/client" />
/// <reference lib="webworker" />

/**
 * PWA Service Worker（injectManifest 模式，由 vite-plugin-pwa 注入预缓存清单）。
 *
 * 职责（doc/pwa 02/03）：
 * - 预缓存 App Shell（precacheAndRoute 注入 self.__WB_MANIFEST）；
 * - 运行时缓存：导航 NetworkFirst(html-runtime) + 白名单 API NetworkFirst(api-runtime)（阶段三 D4）；
 * - 消息通道：SKIP_WAITING / CLEAR_USER_DATA / GET_VERSION（页面 ↔ SW）；
 * - 推送：push → showNotification（tag 折叠 + 角色落地 URL）；
 * - 点击：notificationclick → 聚焦已有窗口并 postMessage(NAVIGATE) 或新开 landingUrl；
 * - 自动续订：pushsubscriptionchange → 重新订阅并凭续订令牌 renew（D12）。
 */

declare const self: ServiceWorkerGlobalScope & typeof globalThis;

import { precacheAndRoute, cleanupOutdatedCaches } from 'workbox-precaching';
import { registerRoute } from 'workbox-routing';
import { NetworkFirst } from 'workbox-strategies';
import { ExpirationPlugin } from 'workbox-expiration';
import { CACHE, isCacheableApi, isNavigationRequest } from './constants/pwaCache';

/** 静态应用版本（仅用于 GET_VERSION 调试/验收；升级靠 precache 哈希驱动 SW 更新流程） */
const APP_VERSION = '1.0.0-pwa';

// ---- 预缓存（App Shell） ----
precacheAndRoute(self.__WB_MANIFEST || []);
cleanupOutdatedCaches();

// ---- 运行时路由 ----

// 导航请求（SPA 路由刷新/深链）：NetworkFirst，弱网/断网回退缓存，再回退预缓存壳
registerRoute(
  ({ request }) => isNavigationRequest(request),
  new NetworkFirst({
    cacheName: CACHE.htmlRuntime,
    networkTimeoutSeconds: 3,
    plugins: [
      {
        handlerDidError: async () =>
          (await caches.match('index.html')) ?? Response.error()
      }
    ]
  }),
  'GET'
);

// 白名单 API GET：NetworkFirst + 过期清理（D4 跨账号安全防线一）
registerRoute(
  ({ url, request }) => isCacheableApi(url.pathname, request.method),
  new NetworkFirst({
    cacheName: CACHE.apiRuntime,
    networkTimeoutSeconds: 3,
    plugins: [
      new ExpirationPlugin({ maxEntries: 200, maxAgeSeconds: 86400 })
    ]
  }),
  'GET'
);

// ---- 生命周期纪律（§3.6） ----
self.addEventListener('install', () => {
  // 不自动 skipWaiting：新 SW 进入 waiting，由 prompt 更新流程（SKIP_WAITING 消息）接管
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      // 首代 SW 立即接管，避免首次访问要二次刷新才受控
      await self.clients.claim();
      // 清理超过两代的过期 runtime 缓存（precache 由 cleanupOutdatedCaches 管理，此处不动）
      const keys = await caches.keys();
      await Promise.all(
        keys
          .filter((k) => k !== CACHE.apiRuntime && k !== CACHE.htmlRuntime && k !== CACHE.precache && k !== 'push-meta')
          .map((k) => caches.delete(k))
      );
    })()
  );
});

// ---- 消息通道（页面 ↔ SW，§3.4） ----
self.addEventListener('message', (event) => {
  const data = event.data || {};
  if (data.type === 'SKIP_WAITING') {
    void self.skipWaiting();
  } else if (data.type === 'CLEAR_USER_DATA') {
    // 换号/登出/401：删运行时缓存（precache 不删，壳不破）—— 跨账号安全防线二
    event.waitUntil(
      (async () => {
        await caches.delete(CACHE.apiRuntime);
        await caches.delete(CACHE.htmlRuntime);
        const port = event.ports?.[0];
        port?.postMessage({ type: 'CLEAR_USER_DATA_DONE' });
      })()
    );
  } else if (data.type === 'GET_VERSION') {
    const port = event.ports?.[0];
    port?.postMessage({ type: 'VERSION', version: APP_VERSION });
  }
});

// ---- Web Push：展示通知（tag 折叠，§5.4） ----
self.addEventListener('push', (event) => {
  let payload: any = {};
  try {
    payload = event.data ? event.data.json() : {};
  } catch {
    payload = {};
  }

  const title = payload.title || '收到一条新通知';
  // renotify 需与 tag 同用（同 tag 折叠后重提醒）；部分 TS lib 尚未收录该字段，此处显式放宽
  const options: NotificationOptions & { renotify?: boolean } = {
    body: payload.body || '',
    icon: '/pwa/icon-192.png',
    badge: '/pwa/icon-192.png',
    tag: payload.tag || 'scada-push',
    renotify: true,
    data: {
      landingUrl: payload.landingUrl || '/scada-view',
      timestampUtc: payload.timestampUtc || null
    },
    // Critical 级要求用户交互（实施时评估为 true；其余不强制）
    requireInteraction: payload.severity === 'Critical'
  };

  // userVisibleOnly：每条推送必须至少展示一条通知，否则浏览器会抛错
  event.waitUntil(
    self.registration
      .showNotification(title, options)
      .catch(() =>
        self.registration.showNotification('收到一条新通知', {
          icon: '/pwa/icon-192.png'
        })
      )
  );
});

// ---- 通知点击：聚焦已有窗口并路由，或新开落地页（§5.6 / D6） ----
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const landingUrl: string = event.notification.data?.landingUrl || '/scada-view';

  event.waitUntil(
    (async () => {
      const allClients = await self.clients.matchAll({
        type: 'window',
        includeUncontrolled: true
      });
      // 只聚焦一个同源窗口（避免通知点击弹出多窗口）
      const target = allClients.find(
        (c) => new URL(c.url).origin === self.location.origin
      );
      if (target) {
        (target as WindowClient).focus();
        target.postMessage({ type: 'NAVIGATE', url: landingUrl });
      } else {
        await self.clients.openWindow(landingUrl);
      }
    })()
  );
});

// ---- 自动续订（D12）：订阅静默过期时重新订阅并凭续订令牌换绑 ----
// pushsubscriptionchange 未进 TS 标准 lib：显式声明事件形状（ExtendableEvent + 新旧订阅）
self.addEventListener('pushsubscriptionchange', (event) => {
  const evt = event as ExtendableEvent & {
    oldSubscription?: PushSubscription;
    newSubscription?: PushSubscription;
  };
  const oldSub = evt.oldSubscription;
  const newSub = evt.newSubscription;

  evt.waitUntil(
    (async () => {
      try {
        if (oldSub) {
          try {
            await oldSub.unsubscribe();
          } catch {
            /* 旧订阅已失效，忽略 */
          }
        }
        const reg = await self.registration;
        let sub = newSub;
        if (!sub) {
          const vapid = await fetchVapidPublicKey();
          sub = await reg.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: vapid
          });
        }
        const raw = (sub as PushSubscription).toJSON();
        const renewal = await readRenewalToken();
        await fetch('/api/push/subscriptions/renew', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            ...(renewal ? { 'X-Renewal-Token': renewal } : {})
          },
          body: JSON.stringify({
            endpoint: raw.endpoint,
            p256dh: raw.keys?.p256dh,
            auth: raw.keys?.auth,
            expirationTime: raw.expirationTime ?? null
          })
        }).catch(() => {
          /* 续订失败静默：等待用户下次打开应用走正常订阅流程 */
        });
      } catch {
        /* 续订整体失败静默放弃 */
      }
    })()
  );
});

// ---- SW 内部辅助 ----

async function fetchVapidPublicKey(): Promise<string> {
  const res = await fetch('/api/push/vapid-public-key');
  const json = await res.json();
  return json?.publicKey ?? json?.data ?? '';
}

/** 续订令牌存于 CacheStorage（页面与 SW 共享，localStorage 对 SW 不可见） */
async function readRenewalToken(): Promise<string | null> {
  try {
    const cache = await caches.open('push-meta');
    const res = await cache.match('renewalToken');
    if (!res) return null;
    const obj = (await res.json()) as { token?: string } | null;
    return obj?.token ?? null;
  } catch {
    return null;
  }
}
