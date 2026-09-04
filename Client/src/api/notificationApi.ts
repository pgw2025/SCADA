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

export interface NotificationConfig {
  dingTalk: DingTalkConfig;
  email: EmailConfig;
  push: PushPolicy;
  templates: NotificationTemplates;
}

export interface NotificationTestResult {
  success: boolean;
  message: string;
  latencyMs?: number;
}

const base = () => `${systemConfig.value.backendApiUrl}/api/NotificationConfig`;

export const fetchNotificationConfig = () => http.get<NotificationConfig>(`${base()}`);

export const saveNotificationConfig = (dto: NotificationConfig) => http.put(`${base()}`, dto);

export const testDingTalk = (dto: DingTalkConfig) =>
  http.post<NotificationTestResult>(`${base()}/test-dingtalk`, dto);

export const testEmail = (dto: EmailConfig) =>
  http.post<NotificationTestResult>(`${base()}/test-email`, dto);
