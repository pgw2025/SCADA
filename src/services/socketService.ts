import { ref } from 'vue';
import { HubConnection } from '@microsoft/signalr';

export const signalRConnection = ref<HubConnection | null>(null);
export const isBackendConnected = ref<boolean>(false);
