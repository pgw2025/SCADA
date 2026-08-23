import { HubConnectionBuilder } from '@microsoft/signalr';
import { addLog, systemConfig } from '../store/index';
import { devices } from '../store/deviceStore';
import { fetchDevicesFromBackend } from '../api/deviceApi';
import { isBackendConnected, signalRConnection } from './socketService';

export const initializeRealtimeSignals = () => {
    if (systemConfig.value.isSimulationActive) {
        if (signalRConnection.value) {
            signalRConnection.value.stop().catch(() => { });
            signalRConnection.value = null;
        }
        isBackendConnected.value = false;
        return;
    }

    if (signalRConnection.value) return; // Avoid double initialization

    addLog('后端对接', `正在构建 ASP.NET Core SignalR 信道 (网关: ${systemConfig.value.backendApiUrl})...`, 'info');

    try {
        const connection = new HubConnectionBuilder()
            .withUrl(`${systemConfig.value.backendApiUrl}/hubs/scada`)
            .withAutomaticReconnect()
            .build();

        connection.on("ReceiveVariableUpdate", (deviceId: number, variableKey: string, newValue: any) => {
            const dev = devices.value.find(d => d.id === deviceId);
            if (!dev) return;

            if (dev.variables && dev.variables[variableKey] !== undefined) {
                dev.variables[variableKey] = newValue;
                if (!dev.variableTimestamps) dev.variableTimestamps = {};
                const pad2 = (n: number) => n.toString().padStart(2, '0');
                const d = new Date();
                dev.variableTimestamps[variableKey] = `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;
                addLog('SignalR 接收', `网络遥测更新: 设备#${deviceId} [${variableKey}] -> ${newValue}`, 'info');
            }
        });

        connection.on("ReceiveSystemAlarm", (message: string) => {
            addLog('后端发布警报', message, 'warning');
        });

        connection.start()
            .then(() => {
                isBackendConnected.value = true;
                addLog('后端对接', `SignalR 通信链路握手建立成功！桥接工业控制链网关。`, 'normal');
                fetchDevicesFromBackend();
            })
            .catch((err) => {
                isBackendConnected.value = false;
                addLog('后端对接', `SignalR 连接失败: ${err.message}. 系统自适配并启用 HTTP 降级轮询机制...`, 'warning');
            });

        connection.onreconnecting((error) => {
            isBackendConnected.value = false;
            addLog('后端对接', `SignalR 桥接网络瞬断重连中: ${error?.message || '未知异常'}`, 'warning');
        });

        connection.onreconnected((connectionId) => {
            isBackendConnected.value = true;
            addLog('后端对接', `SignalR 物理转发信道自动重连成功！ID: ${connectionId}`, 'normal');
            fetchDevicesFromBackend();
        });

        connection.onclose((error) => {
            isBackendConnected.value = false;
            addLog('后端对接', `SignalR 信道已关闭断开: ${error?.message || '正常退出'}`, 'warning');
        });

        signalRConnection.value = connection;
    } catch (error: any) {
        addLog('后端对接', `SignalR 信道初始化失败: ${error.message}`, 'warning');
    }
};
