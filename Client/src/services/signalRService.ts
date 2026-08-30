import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { addLog, systemConfig } from '../store/index';
import { devices } from '../store/deviceStore';
import { syncDevices } from './deviceService';
import { isBackendConnected, signalRConnection } from './socketService';
import { mapRuntimeStatusToStatus } from '../utils/deviceStatus';
import { pushAlarmEvent, refreshActiveAlarms } from '../store/alarmStore';
import { pushScriptExecutionEvent } from '../store/scriptStore';
import { AlarmEventPayload, ScriptExecutionEvent } from '../types';
import { TOKEN_KEY } from '../api/http';

// ===== 设备级订阅管理（引用计数） =====
// 后端变量更新仅推送至订阅该设备的分组（ScadaHub.SubscribeDevice/UnsubscribeDevice），
// 页面挂载/切换时按需订阅，卸载时退订；多页面共用同一设备时由引用计数兜底。
// 连接建立/重连成功后自动对账全部活跃订阅。
const deviceSubscriptions = new Map<number, number>();

const invokeSubscription = async (method: 'SubscribeDevice' | 'UnsubscribeDevice', deviceId: number) => {
    const conn = signalRConnection.value;
    if (!conn || conn.state !== HubConnectionState.Connected) return;
    try {
        await conn.invoke(method, deviceId);
    } catch (err: any) {
        // 订阅/退订失败静默：连接建立与重连时会对账重发订阅
        console.warn(`SignalR ${method}(${deviceId}) 失败:`, err?.message);
    }
};

/** 订阅指定设备的实时变量更新（页面挂载/切换设备时调用） */
export const subscribeDeviceTelemetry = (deviceId: number | string) => {
    const id = Number(deviceId);
    if (!id || Number.isNaN(id)) return;
    const count = (deviceSubscriptions.get(id) ?? 0) + 1;
    deviceSubscriptions.set(id, count);
    if (count === 1) void invokeSubscription('SubscribeDevice', id);
};

/** 取消订阅指定设备的实时变量更新（页面卸载/切走时调用） */
export const unsubscribeDeviceTelemetry = (deviceId: number | string) => {
    const id = Number(deviceId);
    if (!id || Number.isNaN(id)) return;
    const count = deviceSubscriptions.get(id) ?? 0;
    if (count <= 1) deviceSubscriptions.delete(id);
    else deviceSubscriptions.set(id, count - 1);
    if (count === 1) void invokeSubscription('UnsubscribeDevice', id);
};

/** 连接建立/重连成功后对账：重发全部活跃订阅（服务端分组随连接生命周期重置） */
const syncDeviceSubscriptions = () => {
    deviceSubscriptions.forEach((_count, id) => { void invokeSubscription('SubscribeDevice', id); });
};

// 首次 start 失败的重试定时器（自动重连只覆盖连接建立后的掉线，不覆盖首次握手失败）
let startRetryTimer: ReturnType<typeof setTimeout> | null = null;

const clearStartRetryTimer = () => {
    if (startRetryTimer) {
        clearTimeout(startRetryTimer);
        startRetryTimer = null;
    }
};

const scheduleStartRetry = () => {
    if (startRetryTimer) return;
    // 未登录（无 token）时不再定时重试：登录成功后 App.vue 会重新初始化
    if (!localStorage.getItem(TOKEN_KEY)) return;
    startRetryTimer = setTimeout(() => {
        startRetryTimer = null;
        initializeRealtimeSignals();
    }, 5000);
};

/** 后端采集时间（UTC ISO 串）→ 本地 HH:mm:ss；缺失/非法时回退浏览器当前时刻 */
const formatBackendTime = (updateTime?: string | null): string => {
    const d = updateTime ? new Date(updateTime) : new Date();
    const t = isNaN(d.getTime()) ? new Date() : d;
    const pad2 = (n: number) => n.toString().padStart(2, '0');
    return `${pad2(t.getHours())}:${pad2(t.getMinutes())}:${pad2(t.getSeconds())}`;
};

export const initializeRealtimeSignals = () => {
    // 首次失败重试对账：进入初始化时取消待执行的定时重试
    clearStartRetryTimer();
    if (systemConfig.value.isSimulationActive) {
        if (signalRConnection.value) {
            signalRConnection.value.stop().catch(() => { });
            signalRConnection.value = null;
        }
        isBackendConnected.value = false;
        return;
    }

    // 已存在连接：仍处于已连接/重连中则跳过，避免重复初始化；
    // 若因登录前无 token 首次 start 401 而停在 Disconnected，则销毁重建，
    // 让登录后能带着最新 JWT 重新握手（自动重连不覆盖首次 start 失败）。
    if (signalRConnection.value) {
        if (signalRConnection.value.state === 'Disconnected') {
            signalRConnection.value.stop().catch(() => { });
            signalRConnection.value = null;
        } else {
            return;
        }
    }

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

        // 结构化变量更新载荷：{ deviceId, variableKey, value, quality, updateTime(UTC) }。
        // 携带质量位（读取失败时值为最近一次有效"僵尸值"）与后端采集时刻，
        // 前端据此展示失效标记，并以采集时刻（而非浏览器接收时刻）作为更新时间。
        connection.on("ReceiveVariableUpdate", (payload: any) => {
            const deviceId = payload?.deviceId ?? payload?.DeviceId;
            const variableKey = payload?.variableKey ?? payload?.VariableKey;
            const newValue = payload?.value !== undefined ? payload.value : payload?.Value;
            const quality = payload?.quality ?? payload?.Quality;
            const updateTime = payload?.updateTime ?? payload?.UpdateTime;
            if (deviceId == null || variableKey == null) return;

            const dev = devices.value.find(d => d.id === deviceId);
            if (!dev) return;

            // 实时值推送直接写入变量表；normalizeDevices 已为后端变量预置 null 占位，
            // 此处不再用 !== undefined 拦截（避免运行时新增变量被丢弃）。
            if (dev.variables) {
                dev.variables[variableKey] = newValue;
                if (!dev.variableTimestamps) dev.variableTimestamps = {};
                dev.variableTimestamps[variableKey] = formatBackendTime(updateTime);
                addLog('SignalR 接收', `网络遥测更新: 设备#${deviceId} [${variableKey}] -> ${newValue}`, 'info');
            }

            // 质量位同步写入 variableMeta（与 REST realtime 回填同构），供监控页标记僵尸值
            if (quality != null) {
                if (!dev.variableMeta) dev.variableMeta = {};
                const meta = dev.variableMeta[variableKey];
                if (meta) {
                    meta.quality = quality;
                } else {
                    dev.variableMeta[variableKey] = { key: variableKey, quality } as any;
                }
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
                clearStartRetryTimer();
                addLog('后端对接', `SignalR 通信链路握手建立成功！桥接工业控制链网关。`, 'normal');
                // 重发设备级分组订阅（服务端分组随新连接重置）
                syncDeviceSubscriptions();
                // 显式全量刷新 + realtime 回填（SignalR 握手成功即拿到最新实时值）
                syncDevices({ realtime: true });
                refreshActiveAlarms();
            })
            .catch((err) => {
                isBackendConnected.value = false;
                addLog('后端对接', `SignalR 连接失败: ${err.message}. 系统自适配并启用 HTTP 降级轮询机制...`, 'warning');
                // 自动重连不覆盖首次 start 失败：定时重建连接，避免停留在高频 HTTP 降级轮询
                scheduleStartRetry();
            });

        connection.onreconnecting((error) => {
            isBackendConnected.value = false;
            addLog('后端对接', `SignalR 桥接网络瞬断重连中: ${error?.message || '未知异常'}`, 'warning');
        });

        connection.onreconnected((connectionId) => {
            isBackendConnected.value = true;
            addLog('后端对接', `SignalR 物理转发信道自动重连成功！ID: ${connectionId}`, 'normal');
            // 重连后服务端分组已随旧连接失效，对账重发全部活跃订阅
            syncDeviceSubscriptions();
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
