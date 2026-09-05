/**
 * 推送相关 API 封装（doc/pwa 阶段四 · 步骤 13，§5.6）。
 * 五个 UI 面端点（renew 由 SW 直接 fetch，不在此处）：
 *  - GET  /api/push/vapid-public-key
 *  - POST /api/push/subscriptions        （JWT，upsert 换绑 + 续订令牌签发）
 *  - GET  /api/push/subscriptions/me     （JWT，本人订阅列表）
 *  - DELETE /api/push/subscriptions      （JWT，归属校验）
 *  - POST /api/push/test                 （JWT，向本人订阅发测试推送）
 */

import { http } from './http';
import { systemConfig } from '../store/configStore';

const base = () => systemConfig.value.backendApiUrl;

export interface PushSubscriptionPayload {
  endpoint: string;
  p256dh?: string | null;
  auth?: string | null;
  expirationTime?: number | null;
}

export const getVapidPublicKey = () =>
  http.get(`${base()}/api/push/vapid-public-key`);

export const postSubscription = (payload: PushSubscriptionPayload) =>
  http.post(`${base()}/api/push/subscriptions`, payload);

export const getMySubscriptions = () =>
  http.get(`${base()}/api/push/subscriptions/me`);

export const deleteSubscription = () =>
  http.delete(`${base()}/api/push/subscriptions`);

export const sendTestPushApi = () =>
  http.post(`${base()}/api/push/test`);
