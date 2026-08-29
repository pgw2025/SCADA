import { ref } from 'vue';
import {
  DataConversion,
  VariableTrigger,
  SystemScript,
  ExposedDataInterface,
  DatabaseConfig,
  SystemConfig
} from '../types';

export const dataConversions = ref<DataConversion[]>([]);
export const triggers = ref<VariableTrigger[]>([]);
export const systemScripts = ref<SystemScript[]>([]);
export const exposedApis = ref<ExposedDataInterface[]>([]);
export const databaseConfigs = ref<DatabaseConfig[]>([]);

export const systemConfig = ref<SystemConfig>({
  systemTitle: '晋鑫设备管理系统',
  pollIntervalMs: 1200,
  mqttBrokerHost: '10.120.44.15',
  mqttBrokerPort: 1883,
  opcUaDiscoveryUrl: 'opc.tcp://10.120.44.12:4840',
  alarmEmailNotify: true,
  alarmEmailAddress: 'ops_alerts@iota-factory.com',
  retentionPeriodDays: 90,
  isSimulationActive: false,
  // 空字符串=相对路径，由 Vite dev proxy 转发到 :5555（开发）或反向代理（生产）
  // 如需指向独立后端，可在「设置中心」手动填写完整 URL 覆盖
  backendApiUrl: ''
});
