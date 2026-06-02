import { HubConnectionState } from '@microsoft/signalr';
import { systemConfig } from '../store/system';
import { fetchDevicesFromBackend } from '../api/deviceApi';
import { signalRConnection } from './socketService';

let backendPollInterval: any = null;

export const startBackendPolling = () => {
    if (backendPollInterval) return;

    let lastRun = 0;
    backendPollInterval = setInterval(() => {
        if (systemConfig.value.isSimulationActive) return;

        const now = Date.now();
        const isSigsConnected = signalRConnection.value && signalRConnection.value.state === HubConnectionState.Connected;
        const interval = isSigsConnected ? 5000 : systemConfig.value.pollIntervalMs;

        if (now - lastRun >= interval) {
            lastRun = now;
            fetchDevicesFromBackend();
        }
    }, 100);
};
