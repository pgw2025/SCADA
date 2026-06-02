import { ref } from 'vue';
import { 
    MqttServer, 
    DataConversion, 
    VariableTrigger, 
    SystemScript, 
    ScheduledTask, 
    ExposedDataInterface, 
    DatabaseConfig 
} from '../types';

export const mqttServers = ref<MqttServer[]>([]);
export const dataConversions = ref<DataConversion[]>([]);
export const triggers = ref<VariableTrigger[]>([]);
export const systemScripts = ref<SystemScript[]>([]);
export const scheduledTasks = ref<ScheduledTask[]>([]);
export const exposedApis = ref<ExposedDataInterface[]>([]);
export const databaseConfigs = ref<DatabaseConfig[]>([]);
