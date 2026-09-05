/**
 * 前端推送订阅服务（doc/pwa 阶段四 · 步骤 13/14，D3/D11/D12）。
 *
 * - ensurePermission：仅用户手势链内请求 Notification 权限（D11）；
 * - subscribe：拉 VAPID → pushManager.subscribe → POST 订阅 → 续订令牌写入 CacheStorage(push-meta) 供 SW 续订；
 * - unsubscribe：退订 + 清续订令牌 + 删服务器订阅（D3：登出/401 不退订，仅用户主动关闭才退订）；
 * - getSubscriptionState：本地订阅 + 服务器绑定回显；
 * - rebindIfNeeded：登录成功后若本地订阅存在则静默重 POST（换绑，D3 与登录会话解耦）；
 * - 续订令牌存于 CacheStorage（SW 可读，localStorage 对 SW 不可见）。
 */

import {
  getVapidPublicKey,
  postSubscription,
  getMySubscriptions,
  deleteSubscription,
  sendTestPushApi
} from '../api/pushApi';

export interface PushState {
  supported: boolean;
  permission: NotificationPermission | 'unsupported';
  subscribed: boolean;
  serverBound: boolean;
}

function isPushSupported(): boolean {
  return (
    typeof window !== 'undefined' &&
    'serviceWorker' in navigator &&
    'PushManager' in window &&
    'Notification' in window
  );
}

/** VAPID 公钥 base64url → Uint8Array（应用服务器密钥要求） */
export function urlBase64ToUint8Array(base64String: string): Uint8Array {
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
  const rawData = atob(base64);
  const output = new Uint8Array(rawData.length);
  for (let i = 0; i < rawData.length; i++) output[i] = rawData.charCodeAt(i);
  return output;
}

/** 续订令牌写入 CacheStorage push-meta（SW pushsubscriptionchange 时读取，D12） */
async function writeRenewalToken(token: string): Promise<void> {
  try {
    const cache = await caches.open('push-meta');
    await cache.put('renewalToken', new Response(JSON.stringify({ token })));
  } catch {
    // SW/缓存不可用：仅影响自动续订容错，不阻断订阅主流程
  }
}

async function clearRenewalToken(): Promise<void> {
  try {
    await (await caches.open('push-meta')).delete('renewalToken');
  } catch {
    /* ignore */
  }
}

/** 上传订阅并持久化服务器签发的续订令牌（幂等 upsert，既用于首订也用于换绑） */
async function uploadSubscription(raw: any): Promise<string | null> {
  const res = await postSubscription({
    endpoint: raw.endpoint,
    p256dh: raw.keys?.p256dh ?? null,
    auth: raw.keys?.auth ?? null,
    expirationTime: raw.expirationTime ?? null
  });
  const body = res.data;
  const token =
    body?.renewalToken ?? body?.data?.renewalToken ?? (typeof body === 'string' ? body : null);
  if (typeof token === 'string' && token) {
    await writeRenewalToken(token);
    return token;
  }
  return null;
}

/** 仅用户手势链内调用；返回最终授权状态 */
export async function ensurePermission(): Promise<NotificationPermission> {
  if (!isPushSupported()) return 'denied';
  if (Notification.permission === 'granted' || Notification.permission === 'denied') {
    return Notification.permission;
  }
  try {
    return await Notification.requestPermission();
  } catch {
    return 'denied';
  }
}

export async function subscribe(): Promise<boolean> {
  if (!isPushSupported()) throw new Error('当前浏览器不支持 Web Push');
  const permission = await ensurePermission();
  if (permission !== 'granted') {
    throw new Error('浏览器通知权限被拒绝，请在浏览器设置中开启通知权限');
  }
  const reg = await navigator.serviceWorker.ready;
  let sub = await reg.pushManager.getSubscription();
  if (!sub) {
    const res = await getVapidPublicKey();
    const pubKey = res.data?.publicKey ?? res.data?.data ?? res.data;
    if (!pubKey || typeof pubKey !== 'string') {
      throw new Error('未能获取服务器 VAPID 公钥，请确认后端推送服务已启用');
    }
    sub = await reg.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(pubKey)
    });
  }
  await uploadSubscription(sub.toJSON());
  return true;
}

export async function unsubscribe(): Promise<void> {
  const reg = await navigator.serviceWorker.ready;
  const sub = await reg.pushManager.getSubscription();
  if (sub) {
    try {
      await sub.unsubscribe();
    } catch {
      /* 忽略：订阅可能已失效 */
    }
  }
  await clearRenewalToken();
  try {
    await deleteSubscription();
  } catch {
    /* 忽略：服务器订阅可能已不存在 */
  }
}

export async function getSubscriptionState(): Promise<PushState> {
  if (!isPushSupported()) {
    return { supported: false, permission: 'unsupported', subscribed: false, serverBound: false };
  }
  const permission = Notification.permission;
  const reg = await navigator.serviceWorker.ready;
  const sub = await reg.pushManager.getSubscription();
  const subscribed = !!sub;
  let serverBound = false;
  if (subscribed) {
    try {
      const res = await getMySubscriptions();
      const list = res.data?.list ?? res.data ?? [];
      serverBound =
        Array.isArray(list) && list.some((s: any) => s.endpoint === (sub as any).endpoint);
    } catch {
      serverBound = false;
    }
  }
  return { supported: true, permission, subscribed, serverBound };
}

export async function getMyDevices(): Promise<any[]> {
  const res = await getMySubscriptions();
  const list = res.data?.list ?? res.data ?? [];
  return Array.isArray(list) ? list : [];
}

export async function sendTestPush(): Promise<void> {
  await sendTestPushApi();
}

/**
 * 登录成功后静默换绑（D3）：若本浏览器已有订阅，重 POST 一次（后端按 endpoint 比对 UserId，
 * 不同则换绑当前用户并重新签发续订令牌）。订阅与登录会话解耦：登出/401 不退订，仅换绑。
 * 设计为 fire-and-forget，失败仅告警日志。
 */
export async function rebindIfNeeded(): Promise<void> {
  if (!isPushSupported()) return;
  try {
    const reg = await navigator.serviceWorker.ready;
    const sub = await reg.pushManager.getSubscription();
    if (!sub) return;
    await uploadSubscription(sub.toJSON());
  } catch (e) {
    console.warn('[PWA] 推送换绑失败（将下次打开应用时重试）', e);
  }
}
