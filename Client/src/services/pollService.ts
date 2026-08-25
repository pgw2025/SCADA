import { HubConnectionState } from '@microsoft/signalr';
import { systemConfig } from '../store/index';
import { syncDevices } from './deviceService';
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
            // 复用 syncDevices：内部拉取 + normalize + setDevices，确保轮询结果真正写回全局 store。
            // 修复旧实现直接 fetchDevicesFromBackend() 丢弃返回值、设备列表不刷新的问题。
            syncDevices();
        }
    }, 100);
};

export const stopBackendPolling = () => {
    if (backendPollInterval) {
        clearInterval(backendPollInterval);
        backendPollInterval = null;
    }
};
