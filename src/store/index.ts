import { computed, ref } from 'vue';
import { DataConversion, HistoricalRecord, HMIComponent, ScadaScreenProject } from '../types';
import { TEMPLATES } from '../templates';
import { addLog, serverStatus, systemConfig } from './system';
import { devices, fetchDevicesFromBackend, historicalRecords, isBackendConnected, signalRConnection } from './devices';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';

export * from './areas';
export * from './devices';
export * from './models';
export * from './system';


// === 7. MULTI-PROJECT & MULTI-PAGE TOPOLOGY SCADA SCREEN STATE ===
// We take our templates as standard preloaded multi-screen projects
export const scadaProjects = ref<ScadaScreenProject[]>([
    {
        id: 'project-purify',
        name: '循环污水高倍净化系统工程',
        description: '工业曝气池双水箱重力落差级联调节、离心排量流量管线监控',
        pages: [
            {
                id: 'page-ww-primary',
                name: '曝气净化段主画面 (Primary Monitor)',
                components: JSON.parse(JSON.stringify(TEMPLATES[0].components)) // Wastewater
            },
            {
                id: 'page-ww-sub',
                name: '气动闸阀调试辅助图 (Valve Tuning Mimic)',
                components: [
                    // Subpage preloaded layout elements
                    {
                        id: 'intro-valve-sub',
                        type: 'text',
                        name: '子页面说明',
                        x: 100,
                        y: 40,
                        width: 500,
                        height: 40,
                        label: '区域B电磁排量闸阀点对点操作面板',
                        bindField: '',
                        zIndex: 1,
                        props: { fontSize: 16, bold: true, align: 'left' }
                    },
                    {
                        id: 'sub-valve-1',
                        type: 'valve',
                        name: '1号子阀 KV101',
                        x: 150,
                        y: 120,
                        width: 100,
                        height: 100,
                        label: '1号初滤进水电动阀 4001',
                        bindField: 'valve_state',
                        zIndex: 2,
                        props: { activeColor: '#10b981', inactiveColor: '#ef4444' }
                    },
                    {
                        id: 'sub-val-led1',
                        type: 'led',
                        name: '阀合闸状态',
                        x: 350,
                        y: 155,
                        width: 32,
                        height: 32,
                        label: '阀门双位行程常开指示',
                        bindField: 'valve_state',
                        zIndex: 3,
                        props: { activeColor: '#10b981', inactiveColor: '#ef4444' }
                    },
                    {
                        id: 'sub-valve-btn-ctrl',
                        type: 'button',
                        name: '按钮',
                        x: 150,
                        y: 260,
                        width: 140,
                        height: 60,
                        label: '手动阀门紧急切断',
                        bindField: 'valve_state',
                        zIndex: 3,
                        props: { buttonMode: 'toggle', buttonText: '阀门合闸/开路切换' }
                    }
                ]
            }
        ]
    },
    {
        id: 'project-boiler',
        name: '热力站2号超真空高压反应大底盘',
        description: '核心锅炉受阻高温熔池蒸汽缓冲压力、排风冷却机风扇联动监控系统',
        pages: [
            {
                id: 'page-blr-main',
                name: '过热熔融反应主视图 (Boiler Hearth)',
                components: JSON.parse(JSON.stringify(TEMPLATES[1].components)) // Thermal boiler
            }
        ]
    },
    {
        id: 'project-sorting',
        name: '3号变频传动轮物料流水分拣线',
        description: '变频电动机转速反馈与重力吨位落料池动态曲线仓储',
        pages: [
            {
                id: 'page-sort-main',
                name: '配给打包输送传送带主视图 (Packaging line)',
                components: JSON.parse(JSON.stringify(TEMPLATES[2].components)) // Conveyor
            }
        ]
    }
]);

// Track active selection
export const selectedProjectId = ref<string>('project-purify');
export const selectedPageId = ref<string>('page-ww-primary');

// Help computeds
export const currentProject = computed(() => {
    return scadaProjects.value.find(p => p.id === selectedProjectId.value) || scadaProjects.value[0];
});

export const currentPage = computed(() => {
    const proj = currentProject.value;
    return proj.pages.find(pg => pg.id === selectedPageId.value) || proj.pages[0];
});

// Update components on the selected project's page
export const updateCurrentPageComponents = (newComponents: HMIComponent[]) => {
    const projIdx = scadaProjects.value.findIndex(p => p.id === selectedProjectId.value);
    if (projIdx === -1) return;
    const pageIdx = scadaProjects.value[projIdx].pages.findIndex(pg => pg.id === selectedPageId.value);
    if (pageIdx === -1) return;

    scadaProjects.value[projIdx].pages[pageIdx].components = [...newComponents];
};

export const fetchHistoryFromBackend = async (variableKey: string, limit: number = 80) => {
    if (systemConfig.value.isSimulationActive) return;

    try {
        addLog('历史查询', `正在向后端调取时间曲线. 变量: ${variableKey}, 长度: ${limit}...`, 'info');
        const res = await fetch(`${systemConfig.value.backendApiUrl}/api/scada/history?variableKey=${variableKey}&limit=${limit}`);
        if (!res.ok) {
            throw new Error(`HTTP status code ${res.status}`);
        }
        const data = await res.json();
        if (Array.isArray(data)) {
            const otherRecords = historicalRecords.value.filter(r => r.variableKey !== variableKey);

            const converted: HistoricalRecord[] = data.map((item: any) => ({
                id: item.id || `hist-net-${Date.now()}-${Math.random().toString().slice(-4)}`,
                variableKey: item.variableKey || variableKey,
                variableName: item.variableName || variableKey,
                value: Number(item.value),
                timestamp: item.timestamp
            }));

            historicalRecords.value = [...converted, ...otherRecords];
            addLog('历史查询', `同步后端时序库记录成功！拉取 ${converted.length} 条数据点`, 'normal');
        }
    } catch (err: any) {
        addLog('历史查询', `调取时序时钟出线硬阻塞: ${err.message}`, 'warning');
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

        connection.on("ReceiveVariableUpdate", (variableKey: string, newValue: any) => {
            let updated = false;
            devices.value.forEach(dev => {
                if (dev.variables[variableKey] !== undefined) {
                    dev.variables[variableKey] = newValue;
                    if (!dev.variableTimestamps) dev.variableTimestamps = {};
                    const pad2 = (n: number) => n.toString().padStart(2, '0');
                    const d = new Date();
                    dev.variableTimestamps[variableKey] = `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;
                    updated = true;
                }
            });
            if (updated) {
                addLog('SignalR 接收', `网络遥测更新: [${variableKey}] -> ${newValue}`, 'info');
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


// === CYCLE DETECTION ALGORITHM FOR DATA CONVERSIONS ===
export const checkCycleInConversions = (tempConversions: DataConversion[]): boolean => {
    const adj = new Map<string, string[]>();

    for (const conv of tempConversions) {
        if (!conv.active) continue;
        const src = `${conv.sourceDeviceId}:${conv.sourceVariableKey}`;
        const dst = `${conv.targetDeviceId}:${conv.targetVariableKey}`;
        if (!adj.has(src)) {
            adj.set(src, []);
        }
        adj.get(src)!.push(dst);
    }

    const visited = new Set<string>();
    const recStack = new Set<string>();

    const dfs = (node: string): boolean => {
        visited.add(node);
        recStack.add(node);

        const neighbors = adj.get(node) || [];
        for (const neighbor of neighbors) {
            if (!visited.has(neighbor)) {
                if (dfs(neighbor)) return true;
            } else if (recStack.has(neighbor)) {
                return true; // Cycle detected
            }
        }

        recStack.delete(node);
        return false;
    };

    const allNodes = new Set<string>();
    for (const [src, dsts] of adj.entries()) {
        allNodes.add(src);
        for (const dst of dsts) {
            allNodes.add(dst);
        }
    }

    for (const node of allNodes) {
        if (!visited.has(node)) {
            if (dfs(node)) return true;
        }
    }

    return false;
};



// === 1. NAVIGATION TAB STATE ===
export const activeTab = ref<
    | 'dashboard'
    | 'live-data'
    | 'device-management'
    | 'data-models'
    | 'scada-editor'
    | 'system-logs'
    | 'trigger-management'
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


// Random resource fluctuation interval
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