import { systemConfig, serverStatus } from '../store/system';

let resourceInterval: any = null;

export const startSystemResourceMonitoring = () => {
    if (resourceInterval) return;
    resourceInterval = setInterval(() => {
        if (!systemConfig.value.isSimulationActive) return; // Skip if local simulation is disabled
        // Generate organic industrial system telemetry drift
        serverStatus.value.cpuUsage = Math.min(99, Math.max(1, +(serverStatus.value.cpuUsage + (Math.random() - 0.5) * 4).toFixed(1)));
        serverStatus.value.memUsage = Math.min(95, Math.max(20, +(serverStatus.value.memUsage + (Math.random() - 0.5) * 0.4).toFixed(1)));
        serverStatus.value.diskUsage = Math.min(100, Math.max(10, +(serverStatus.value.diskUsage + (Math.random() > 0.9 ? 0.1 : 0)).toFixed(1)));
        serverStatus.value.networkIn = Math.max(5, Math.floor(serverStatus.value.networkIn + (Math.random() - 0.5) * 20));
        serverStatus.value.networkOut = Math.max(10, Math.floor(serverStatus.value.networkOut + (Math.random() - 0.5) * 50));
        serverStatus.value.totalPollPackets += Math.floor(Math.random() * 8) + 2;
    }, 2000);
};
