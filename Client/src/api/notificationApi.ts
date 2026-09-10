import { http } from './http';
import { systemConfig } from '../store/index';

// 类型与后端 NotificationConfigService 保持对应（敏感字段回显为掩码）
export interface DingTalkConfig {
  enabled: boolean;
  webhook: string;
  secret: string; // '******' = 不改
  hasSecret: boolean;
}

export interface EmailConfig {
  enabled: boolean;
  smtpHost: string;
  smtpPort: number;
  useSsl: boolean;
  username: string;
  password: string; // '******' = 不改
  hasPassword: boolean;
  from: string;
  fromName: string;
  to: string[];
}

export interface PushPolicy {
  pushAlarm: boolean;
  pushDeviceOffline: boolean;
  pushDeviceOnline: boolean;
  deviceStatusDebounceMinutes: number;
  pushSystemAlarm: boolean;
  pushSystemError: boolean;
  pushScript: boolean;
  maxPerMinutePerChannel: number;
  maxAttempts: number;
  retryBaseDelayMs: number;
  queueCapacity: number;
}

export interface EventTemplate {
  title: string;
  markdown: string;
  htmlBody: string;
}

export interface NotificationTemplates {
  alarmTriggered: EventTemplate;
  alarmRecovered: EventTemplate;
  deviceStatus: EventTemplate;
  systemAlarm: EventTemplate;
  systemError: EventTemplate;
  scriptExecution: EventTemplate;
}

export interface WeComConfig {
  enabled: boolean;
  webhook: string;
}

export interface NotificationConfig {
  dingTalk: DingTalkConfig;
  weCom: WeComConfig;
  email: EmailConfig;
  push: PushPolicy;
  templates: NotificationTemplates;
}

export interface NotificationTestResult {
  success: boolean;
  message: string;
  latencyMs?: number;
}

export interface NotificationLogItem {
  id: number | string;
  timestamp: string;
  channel: 'dingTalk' | 'weCom' | 'email' | 'webPush';
  eventType: 'alarmTriggered' | 'alarmRecovered' | 'deviceStatus' | 'systemAlarm' | 'systemError' | 'scriptExecution' | 'test';
  title: string;
  recipient: string;
  status: 'Success' | 'Failed' | 'Retrying';
  latencyMs: number;
  error?: string;
  payloadPreview?: string;
}

const base = () => `${systemConfig.value.backendApiUrl}/api/NotificationConfig`;

export const fetchNotificationConfig = async () => (await http.get<NotificationConfig>(`${base()}`)).data;

export const saveNotificationConfig = (dto: NotificationConfig) => http.put(`${base()}`, dto);

export const testDingTalk = async (dto: DingTalkConfig) =>
  (await http.post<NotificationTestResult>(`${base()}/test-dingtalk`, dto)).data;

export const testEmail = async (dto: EmailConfig) =>
  (await http.post<NotificationTestResult>(`${base()}/test-email`, dto)).data;

export const testWeCom = async (dto: WeComConfig) =>
  (await http.post<NotificationTestResult>(`${base()}/test-wecom`, dto)).data;

export const fetchNotificationLogs = async () =>
  (await http.get<NotificationLogItem[]>(`${base()}/logs`)).data;

export const clearNotificationLogs = () =>
  http.post(`${base()}/logs/clear`);

export const retryNotificationLog = (id: number | string) =>
  http.post<NotificationLogItem>(`${base()}/logs/${id}/retry`);
