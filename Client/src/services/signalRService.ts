import { HubConnectionBuilder } from '@microsoft/signalr';
import { addLog, systemConfig } from '../store/index';
import { devices } from '../store/deviceStore';
import { syncDevices } from './deviceService';
import { isBackendConnected, signalRConnection } from './socketService';
import { mapRuntimeStatusToStatus } from '../utils/deviceStatus';
import { pushAlarmEvent, refreshActiveAlarms } from '../store/alarmStore';
import { pushScriptExecutionEvent } from '../store/scriptStore';
import { AlarmEventPayload, ScriptExecutionEvent } from '../types';
import { TOKEN_KEY } from '../api/http';

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
        // 阶段2-1：ScadaHub 已由 [AllowAnonymous] 收紧为 [Authorize]，
        // 客户端经 accessTokenFactory 携带 JWT（WebSocket 走 access_token 查询参数，
        // 后端 JwtBearerEvents 从查询串注入鉴权）；未登录时连接会被 401 拒绝并由自动重连在登录后恢复。
        const connection = new HubConnectionBuilder()
            .withUrl(`${systemConfig.value.backendApiUrl}/hubs/scada`, {
                accessTokenFactory: () => localStorage.getItem(TOKEN_KEY) || ''
            })
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

        // 结构化报警事件实时推送：归一化后进入报警 Store（当前报警 / 未确认角标 / 最近事件）。
        connection.on("ReceiveAlarm", (payload: AlarmEventPayload) => {
            pushAlarmEvent(payload ?? ({} as AlarmEventPayload));
        });

        // 脚本执行事件实时推送（手动 / 周期 / Cron / OnChange / 试运行）：进入脚本事件缓冲供控制台实时刷新。
        connection.on("ReceiveScriptExecution", (payload: ScriptExecutionEvent) => {
            pushScriptExecutionEvent(payload ?? ({} as ScriptExecutionEvent));
        });

        connection.start()
            .then(() => {
                isBackendConnected.value = true;
                addLog('后端对接', `SignalR 通信链路握手建立成功！桥接工业控制链网关。`, 'normal');
                // 显式全量刷新 + realtime 回填（SignalR 握手成功即拿到最新实时值）
                syncDevices({ realtime: true });
                refreshActiveAlarms();
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
            syncDevices({ realtime: true });
            refreshActiveAlarms();
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
