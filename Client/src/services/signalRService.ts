import { HubConnectionBuilder } from '@microsoft/signalr';
import { addLog, systemConfig } from '../store/index';
import { devices } from '../store/deviceStore';
import { fetchDevicesFromBackend } from '../api/deviceApi';
import { setDevices } from '../store/deviceStore';
import { normalizeDevices } from '../utils/deviceStatus';
import { isBackendConnected, signalRConnection } from './socketService';
import { mapRuntimeStatusToStatus } from '../utils/deviceStatus';

// 拉取设备列表并写回全局 store。包装 fetchDevicesFromBackend + normalizeDevices，
// 修复 SignalR 握手成功/重连后设备拉取结果被丢弃、设备列表从未进入 store 的问题。
const refreshDevices = async () => {
    try {
        const { data } = await fetchDevicesFromBackend();
        setDevices(normalizeDevices(data));
    } catch (err: any) {
        addLog('后端对接', `同步设备列表失败: ${err.message}`, 'warning');
    }
};

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

            // 实时值推送直接写入变量表；normalizeDevices 已为后端变量预置 null 占位，
            // 此处不再用 !== undefined 拦截（避免运行时新增变量被丢弃）。
            if (dev.variables) {
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

        // 设备运行时状态实时推送：按设备 ID 定位写入 status，实时覆盖轮询值。
        connection.on("ReceiveDeviceStatus", (deviceId: number, status: string) => {
            const dev = devices.value.find(d => d.id === deviceId);
            if (!dev) return;

            const next = mapRuntimeStatusToStatus(status);
            if (dev.status !== next) {
                dev.status = next;
                addLog('SignalR 接收', `设备#${deviceId} 状态变更: ${status}`, 'info');
            }
        });

        connection.start()
            .then(() => {
                isBackendConnected.value = true;
                addLog('后端对接', `SignalR 通信链路握手建立成功！桥接工业控制链网关。`, 'normal');
                refreshDevices();
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
            refreshDevices();
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
