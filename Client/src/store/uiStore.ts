import { ref } from 'vue';

export const activeTab = ref<
    | 'dashboard'
    | 'live-data'
    | 'device-management'
    | 'data-models'
    | 'scada-editor'
    | 'system-logs'
    | 'task-management'
    | 'system-scripts'
    | 'data-interfaces'
    | 'historical-query'
    | 'database-management'
    | 'settings-center'
    | 'mqtt-servers'
    | 'data-conversion'
    | 'user-management'
>('dashboard');
