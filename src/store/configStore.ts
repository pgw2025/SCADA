import { ref } from 'vue';
import { 
    MqttServer, 
    DataConversion, 
    VariableTrigger, 
    SystemScript, 
    ScheduledTask, 
    ExposedDataInterface, 
    DatabaseConfig,
    SystemConfig 
} from '../types';

export const mqttServers = ref<MqttServer[]>([]);
export const dataConversions = ref<DataConversion[]>([]);
export const triggers = ref<VariableTrigger[]>([]);
export const systemScripts = ref<SystemScript[]>([]);
export const scheduledTasks = ref<ScheduledTask[]>([]);
export const exposedApis = ref<ExposedDataInterface[]>([]);
export const databaseConfigs = ref<DatabaseConfig[]>([]);

export const systemConfig = ref<SystemConfig>({
  systemTitle: 'IOTA-SCADA 工业物联大脑',
  pollIntervalMs: 1200,
  mqttBrokerHost: '10.120.44.15',
  mqttBrokerPort: 1883,
  opcUaDiscoveryUrl: 'opc.tcp://10.120.44.12:4840',
  alarmEmailNotify: true,
  alarmEmailAddress: 'ops_alerts@iota-factory.com',
  retentionPeriodDays: 90,
  isSimulationActive: false,
  backendApiUrl: window.location.origin.replace(':3000', ':5000')
});
