import { ref, computed } from 'vue';
import axios from 'axios';
import { 
  Area, 
  DataModel, 
  Device, 
  ScadaScreenProject, 
  SystemLog, 
  HMIComponent, 
  DeviceType,
  VariableTrigger,
  ScheduledTask,
  SystemScript,
  ExposedDataInterface,
  HistoricalRecord,
  DatabaseConfig,
  SystemConfig,
  MqttServer,
  DataConversion,
  SystemUser
} from './types';
import { TEMPLATES } from './templates';
import { HubConnectionBuilder, HubConnection, HubConnectionState } from '@microsoft/signalr';

// === BACKEND CONFIGURATION ===
// 使用相对路径，通过 Vite proxy 代理到后端 (开发环境推荐)
// 生产环境可通过环境变量配置或 nginx 反向代理
// const API_BASE_URL = '';

// 可选：如果是独立部署，可以通过 window.location 动态计算
const API_BASE_URL = window.location.origin.replace(':3000', ':5000');

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

// === 2. SIMULATED SYSTEM RESOURCES STATUS ===
export const serverStatus = ref({
  cpuUsage: 14.5,
  memUsage: 48.2,
  diskUsage: 61.4,
  networkIn: 88.4,  // kb/s
  networkOut: 245.1, // kb/s
  uptimeDays: 14,
  uptimeHours: 5,
  uptimeMins: 32,
  pollFreq: 1200,    // ms
  totalPollPackets: 284145,
});

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

// === 3. PRE-SEEDED LOGS STATE ===
export const logs = ref<SystemLog[]>([
  { id: 'log-1', timestamp: '2026-05-31 09:12:00', level: 'info', source: '系统内核', content: 'AntxV6 工业组态核心引擎已启动, PLC驱动自检完毕' },
  { id: 'log-2', timestamp: '2026-05-31 09:12:02', level: 'normal', source: 'OPC驱动', content: '连接到 OPC-UA 污水净化变频站 [opc.tcp://192.168.1.10:4840] 成功, 采样频率已设定为 50ms' },
  { id: 'log-3', timestamp: '2026-05-31 09:12:05', level: 'normal', source: 'S7驱动', content: 'S7-300 通讯协议插槽 [CPU315-2DP] 握手成功, 读出寄存器 DB10' },
  { id: 'log-4', timestamp: '2026-05-31 09:15:30', level: 'info', source: 'MQTT服务', content: '客户端订阅主题 [factory/conveyor/telemetry] 建立成功, 开始捕获遥测负载' },
  { id: 'log-5', timestamp: '2026-05-31 09:30:11', level: 'warning', source: '模拟设备', content: '辅助高备加水泵 [VR-PUMP-404] 轮询超时, 自动转为 离线 状态防损' },
]);

export const addLog = (source: string, content: string, level: 'info' | 'warning' | 'normal' = 'info') => {
  const pad = (n: number) => n.toString().padStart(2, '0');
  const d = new Date();
  const timeStr = `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
  
  logs.value.unshift({
    id: `log-${Date.now()}`,
    timestamp: timeStr,
    level,
    source,
    content
  });

  // Keep logs list trimmed to prevent performance degradation
  if (logs.value.length > 200) {
    logs.value.pop();
  }
};

// === 4. AREAS STATE ===
export const areas = ref<Area[]>([
  { id: 'area-1', name: '1号低位蓄水罐区', description: '负责厂区入水口污水预存及杂质高倍度沉降排沙流程' },
  { id: 'area-2', name: '2号高压除氧汽机房', description: '负责蒸汽包增压、余热冷凝循环及高参数水冷壁管防冻调节' },
  { id: 'area-3', name: '3号变频配投包装线', description: '包含1-4号配给气缸、驱动电机变频调速及智能打包传动支路' },
  { id: 'area-4', name: '4号中空余热交换区', description: '烟气阻隔闸门驱动及冷却塔逆流喷溅换热变压子站' },
]);

// === MQTT SERVERS STATE ===
export const mqttServers = ref<MqttServer[]>([
  {
    id: 'mqtt-srv-1',
    name: '阿里云物联大脑 IoT 转发网关',
    brokerUrl: 'mqtt://iot-0604.mqtt.iothub.aliyuncs.com',
    port: 1883,
    clientId: 'scada_edge_transmitter_01',
    username: 'scada_node_admin',
    password: 'secure_token_abc123',
    status: 'connected',
    associatedVariables: [
      { deviceId: 'dev-1', variableKey: 'tank_level' },
      { deviceId: 'dev-1', variableKey: 'flow_rate' },
      { deviceId: 'dev-2', variableKey: 'boiler_temp' }
    ]
  },
  {
    id: 'mqtt-srv-2',
    name: 'EMQX 本地工业局域物网中台',
    brokerUrl: 'mqtt://192.168.10.250',
    port: 1883,
    clientId: 'scada_local_bridge_02',
    username: 'industrial_edge',
    password: 'local_pass_edge_789',
    status: 'disconnected',
    associatedVariables: [
      { deviceId: 'dev-3', variableKey: 'conveyor_speed' }
    ]
  }
]);

// === DATA CONVERSION LINKAGES STATE ===
export const dataConversions = ref<DataConversion[]>([
  {
    id: 'conv-1',
    name: '1号净化池瞬时液位关联虚拟补偿泵液位',
    sourceDeviceId: 'dev-1',
    sourceVariableKey: 'tank_level',
    targetDeviceId: 'dev-4', // Notice dev-4 is the Virtual pump
    targetVariableKey: 'tank_level',
    active: true
  }
]);

// === SYSTEM USERS STATE ===
export const systemUsers = ref<SystemUser[]>([
  { id: 'user-1', username: 'admin', role: '超级管理员', createdAt: '2026-05-01 10:00:00', status: 'active' },
  { id: 'user-2', username: 'operator_li', role: '操作员', createdAt: '2026-05-15 14:30:00', status: 'active' },
  { id: 'user-3', username: 'viewer_wang', role: '观察员', createdAt: '2026-05-20 09:15:00', status: 'active' }
]);

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

// === PROPAGATION LOGIC FOR DATA CONVERSIONS ===
export const propagateDataLinkages = (startDeviceId: string, startVariableKey: string, newValue: number | boolean) => {
  const queue: { deviceId: string; variableKey: string; value: number | boolean }[] = [];
  queue.push({ deviceId: startDeviceId, variableKey: startVariableKey, value: newValue });
  
  const visited = new Set<string>();
  visited.add(`${startDeviceId}:${startVariableKey}`);
  
  while (queue.length > 0) {
    const current = queue.shift()!;
    
    // Find active conversions that take current node as source
    const matched = dataConversions.value.filter(
      c => c.active && c.sourceDeviceId === current.deviceId && c.sourceVariableKey === current.variableKey
    );
    
    for (const conv of matched) {
      const dstKey = `${conv.targetDeviceId}:${conv.targetVariableKey}`;
      if (!visited.has(dstKey)) {
        visited.add(dstKey);
        
        const targetDev = devices.value.find(d => d.id === conv.targetDeviceId);
        if (targetDev) {
          targetDev.variables[conv.targetVariableKey] = current.value;
          
          if (!targetDev.variableTimestamps) {
            targetDev.variableTimestamps = {};
          }
          const pad2 = (n: number) => n.toString().padStart(2, '0');
          const d = new Date();
          targetDev.variableTimestamps[conv.targetVariableKey] = `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;
          
          queue.push({
            deviceId: conv.targetDeviceId,
            variableKey: conv.targetVariableKey,
            value: current.value
          });
        }
      }
    }
  }
};

// === 5. MODULE DATA MODELS ===
export const dataModels = ref<DataModel[]>([
  {
    id: 'model-wastewater',
    name: 'OPC污水高倍沉降净化模型',
    description: '适用于工业污水预处理、离心流速平衡与联动气动阀控制场景',
    type: 'OPCUA',
    variables: [
      { key: 'tank_level', name: '污水储缸瞬时液位', type: 'analog', dataType: 'Float', unit: '%', min: 0, max: 100, address: 'ns=2;s=LiquidLevelRaw', description: '原水罐传感器电容探头绝对比率值' },
      { key: 'purified_level', name: '净化水池实时水位', type: 'analog', dataType: 'Float', unit: '%', min: 0, max: 100, address: 'ns=2;s=PurifiedLevelFloat', description: '2号净化循环缓冲池水位传感器高度' },
      { key: 'flow_rate', name: '干线超声波流量计值', type: 'analog', dataType: 'Float', unit: 'L/s', min: 0, max: 40, address: 'ns=2;s=MainPipeRateUltrasonic', description: '泵后高敏排量测算模块' },
      { key: 'pump_state', name: '主水流泵开关反馈', type: 'digital', dataType: 'Boolean', unit: '', min: 0, max: 1, address: 'ns=2;s=CentrifugalPumpCoil', description: '配电柜1号继电器吸合反馈标志值' },
      { key: 'valve_state', name: '管道切断电磁阀反馈', type: 'digital', dataType: 'Boolean', unit: '', min: 0, max: 1, address: 'ns=2;s=CutOffSolenoidValve', description: '管道电磁阀双位行程开关反馈物' },
      { key: 'alarm_status', name: '全线高水位警报灯', type: 'digital', dataType: 'Boolean', unit: '', min: 0, max: 1, address: 'ns=2;s=WaterLevelCriticalLED', description: '系统超过95%阈值时强制硬锁红灯标志' }
    ]
  },
  {
    id: 'model-thermal',
    name: 'Siemens S7锅炉热参数模型',
    description: '西门子 S7 通讯格式，采集高炉炉温、容器负压以及上升管道干度',
    type: 'S7',
    variables: [
      { key: 'boiler_temp', name: '反应器炉膛绝对温度', type: 'analog', dataType: 'REAL', unit: '℃', min: 20, max: 150, address: 'DB10.DBD12', description: '高精度K型热电偶采样温度' },
      { key: 'boiler_press', name: '气包上升容腔工作压力', type: 'analog', dataType: 'REAL', unit: 'kPa', min: 0, max: 120, address: 'DB10.DBD16', description: '过热器联箱出口微变发送指示计' },
      { key: 'pump_state', name: '引风机调速接触继电器', type: 'digital', dataType: 'BOOL', unit: '', min: 0, max: 1, address: 'DB10.DBX24.0', description: '用于排烟及进风强制空气回路' },
      { key: 'alarm_status', name: '超温连锁报警触发线', type: 'digital', dataType: 'BOOL', unit: '', min: 0, max: 1, address: 'DB10.DBX24.1', description: '高于98度极限逻辑连锁触发切断燃料阀' }
    ]
  },
  {
    id: 'model-conveyor',
    name: 'MQTT传动轮变频监控模型',
    description: '采用轻量级数据总线通信模式，监控多速变频电机的RPM速度以及集料池瞬时重力',
    type: 'MQTT',
    variables: [
      { key: 'conveyor_speed', name: '变频传动轮目标速度', type: 'analog', dataType: 'Float', unit: 'rpm', min: 0, max: 150, address: 'factory/conveyor/speed', description: '配电侧1号变频反馈变频转速' },
      { key: 'tank_level', name: '集料罐落料总重监控', type: 'analog', dataType: 'Float', unit: 'kg', min: 0, max: 100, address: 'factory/conveyor/weight', description: '集料仓引引力传感器吨重换算值' }
    ]
  }
]);

// === 6. CONNECTED DEVICES REALTIME STATE ===
export const devices = ref<Device[]>([
  {
    id: 'dev-1',
    name: '1号污水净化备用循环变频站',
    code: 'OPC-WWT-101',
    areaId: 'area-1',
    modelId: 'model-wastewater',
    type: 'OPCUA',
    ipAddress: '192.168.1.10',
    port: '4840',
    status: 'online',
    variables: {
      tank_level: 68.0,
      purified_level: 32.0,
      flow_rate: 18.5,
      pump_state: true,
      valve_state: true,
      alarm_status: false,
    },
    lastUpdated: '刚刚'
  },
  {
    id: 'dev-2',
    name: '中温中压过热蒸汽汽水反应锅炉',
    code: 'S7-BLR-202',
    areaId: 'area-2',
    modelId: 'model-thermal',
    type: 'S7',
    ipAddress: '192.168.2.14',
    port: '102',
    status: 'online',
    variables: {
      boiler_temp: 72.5,
      boiler_press: 55.2,
      pump_state: true,
      alarm_status: false,
    },
    lastUpdated: '刚刚'
  },
  {
    id: 'dev-3',
    name: '2速变频食品级物流分拣传送线',
    code: 'MQT-CY-303',
    areaId: 'area-3',
    modelId: 'model-conveyor',
    type: 'MQTT',
    topic: 'factory/conveyor/telemetry',
    status: 'online',
    variables: {
      conveyor_speed: 120.0,
      tank_level: 42.5,
    },
    lastUpdated: '刚刚'
  },
  {
    id: 'dev-4',
    name: '虚拟辅助应急注水补偿电磁泵组',
    code: 'VR-PUMP-404',
    areaId: 'area-4',
    modelId: 'model-wastewater',
    type: 'Virtual',
    status: 'offline',
    variables: {
      tank_level: 0.0,
      purified_level: 0.0,
      flow_rate: 0.0,
      pump_state: false,
      valve_state: false,
      alarm_status: false,
    },
    lastUpdated: '无'
  }
]);

// Continuous simulation update background driver
let simulationInterval: any = null;
export const startDeviceSimulation = () => {
  if (simulationInterval) return;
  
  let tick = 0;
  simulationInterval = setInterval(() => {
    if (!systemConfig.value.isSimulationActive) return; // Skip if local simulation is disabled
    tick++;
    const time = Date.now() * 0.001;
    const pad2 = (n: number) => n.toString().padStart(2, '0');
    const now = new Date();
    const curTimeStr = `${pad2(now.getHours())}:${pad2(now.getMinutes())}:${pad2(now.getSeconds())}`;

    devices.value.forEach((dev) => {
      if (dev.status === 'offline') return;

      const prevFields = JSON.parse(JSON.stringify(dev.variables));

      dev.lastUpdated = curTimeStr;

      // 1. Sewage system physics
      if (dev.id === 'dev-1') {
        const pumpActive = dev.variables.pump_state as boolean;
        const valveActive = dev.variables.valve_state as boolean;

        let levelA = dev.variables.tank_level as number;
        let levelB = dev.variables.purified_level as number;
        let flowVolt = 0;

        if (pumpActive && valveActive) {
          flowVolt = 15.0 + Math.sin(time * 1.5) * 2.5;
          levelA = Math.max(5, levelA - 0.08);
          levelB = Math.min(99.5, levelB + 0.08);
        } else if (pumpActive && !valveActive) {
          flowVolt = 0;
          levelA = Math.max(0, levelA);
        } else if (!pumpActive && valveActive) {
          flowVolt = 4.0;
          levelA = Math.min(95, levelA + 0.05);
          levelB = Math.max(10, levelB - 0.05);
        } else {
          flowVolt = 0;
        }

        // Loop water boundaries
        if (levelA <= 6.0) levelA = 95.0;
        if (levelB >= 98.5) levelB = 18.0;

        const isCritical = levelA > 94 || levelB > 96;
        if (isCritical && !dev.variables.alarm_status) {
          addLog('OPC驱动', `警报: [${dev.name}] 液位超出临界安全阈值! 红灯报警点亮`, 'warning');
        }

        dev.variables = {
          ...dev.variables,
          tank_level: +levelA.toFixed(2),
          purified_level: +levelB.toFixed(2),
          flow_rate: +flowVolt.toFixed(2),
          alarm_status: isCritical,
        };
      }

      // 2. Boiler thermal physics
      if (dev.id === 'dev-2') {
        const pumpActive = dev.variables.pump_state as boolean;
        const tempNoise = Math.sin(time * 0.4) * 0.3 + Math.cos(time * 0.8) * 0.08;
        const pressNoise = Math.cos(time * 0.5) * 0.15 + Math.sin(time * 0.7) * 0.05;

        let currentTemp = dev.variables.boiler_temp as number;
        if (pumpActive) {
          currentTemp = Math.min(130, currentTemp + 0.12 + tempNoise);
        } else {
          currentTemp = Math.max(30, currentTemp - 0.22 + tempNoise);
        }

        let currentPress = dev.variables.boiler_press as number;
        if (currentTemp > 80) {
          currentPress = Math.min(115, currentPress + (currentTemp - 80) * 0.04 + pressNoise);
        } else {
          currentPress = Math.max(10, currentPress - 0.18 + pressNoise);
        }

        const isOverHeat = currentTemp > 96.0 || currentPress > 85.0;
        if (isOverHeat && !dev.variables.alarm_status) {
          addLog('S7驱动', `告警: [${dev.name}] 检测炉膛最高内部实测参数处于连锁危险区间(${currentTemp.toFixed(1)}℃/ ${currentPress.toFixed(1)}kPa)! 触发硬切逻辑`, 'warning');
        }

        dev.variables = {
          ...dev.variables,
          boiler_temp: +currentTemp.toFixed(2),
          boiler_press: +currentPress.toFixed(2),
          alarm_status: isOverHeat,
        };
      }

      // 3. Conveyor machine physics
      if (dev.id === 'dev-3') {
        const speed = dev.variables.conveyor_speed as number;
        let weight = dev.variables.tank_level as number;

        if (speed > 0) {
          weight = Math.min(99.5, weight + 0.06 + Math.sin(time * 2.2) * 0.01);
        } else {
          weight = Math.max(5, weight - 0.03);
        }

        if (weight >= 99.0) {
          weight = 10.0; // Reset
          addLog('MQTT服务', `提示: [${dev.name}] 集料仓自动倾倒翻砂作业完毕, 空箱重力归零`, 'normal');
        }

        dev.variables = {
          ...dev.variables,
          tank_level: +weight.toFixed(2),
        };
      }

      // Detect value changes, register timestamps, and propagate live conversions
      Object.keys(dev.variables).forEach((vKey) => {
        const valNow = dev.variables[vKey];
        if (prevFields === undefined || valNow !== prevFields[vKey]) {
          if (!dev.variableTimestamps) {
            dev.variableTimestamps = {};
          }
          dev.variableTimestamps[vKey] = curTimeStr;
          
          // Propagate any matched conversions
          propagateDataLinkages(dev.id, vKey, valNow);
        }
      });

      // Record dynamic variables into history regularly (every 10 ticks = 2s)
      if (tick % 10 === 0) {
        Object.keys(dev.variables).forEach((vKey) => {
          const val = dev.variables[vKey];
          if (typeof val === 'number') {
            const hNames: Record<string, string> = {
              tank_level: '储水罐瞬时液位 (tank_level)',
              purified_level: '净化池实时水位 (purified_level)',
              flow_rate: '管路瞬时排量波动 (flow_rate)',
              boiler_temp: '反应炉膛核心温度 (boiler_temp)',
              boiler_press: '反应容膛瞬时压力 (boiler_press)',
              conveyor_speed: '传送轮变频设定转速 (conveyor_speed)'
            };
            historicalRecords.value.unshift({
              id: `hist-${Date.now()}-${Math.random().toString().slice(-4)}`,
              variableKey: vKey,
              variableName: hNames[vKey] || `变设备通道 [${vKey}]`,
              value: val,
              timestamp: `${now.getFullYear()}-${pad2(now.getMonth()+1)}-${pad2(now.getDate())} ${pad2(now.getHours())}:${pad2(now.getMinutes())}:${pad2(now.getSeconds())}`
            });
            if (historicalRecords.value.length > 800) {
              historicalRecords.value.pop();
            }
          }
        });
      }
    });

    // Evaluate active triggers
    evaluateTriggers();

    // Check automated scripts execution
    if (tick % 25 === 0) {
      systemScripts.value.forEach((script) => {
        if (script.triggerType === 'auto') {
          runScriptEngine(script);
        }
      });
    }
  }, 200); // 5 records per second for dynamic live feeling!
};

// Synchronize all dev/custom simulator variables back to the active HMI components values!
export const getDeviceVariableValue = (variableKey: string): number | boolean => {
  // Seek the first online device that hosts this variable key
  for (const dev of devices.value) {
    if (dev.status === 'online' && dev.variables[variableKey] !== undefined) {
      return dev.variables[variableKey];
    }
  }
  return 0;
};

export const setDeviceVariableValue = (variableKey: string, newValue: number | boolean) => {
  // Seek and update across all online devices that have this key
  devices.value.forEach((dev) => {
    if (dev.status === 'online' && dev.variables[variableKey] !== undefined) {
      dev.variables[variableKey] = newValue;
      
      if (!dev.variableTimestamps) {
        dev.variableTimestamps = {};
      }
      const pad2 = (n: number) => n.toString().padStart(2, '0');
      const d = new Date();
      dev.variableTimestamps[variableKey] = `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;
      
      // Propagate linkages
      propagateDataLinkages(dev.id, variableKey, newValue);
      
      // Post log
      addLog('核心控制器', `写变量 [${variableKey}] -> ${newValue} (${typeof newValue === 'boolean' ? (newValue ? 'ON/合闸' : 'OFF/开路') : newValue})`, 'info');
    }
  });

  // Call backend API if simulation data is deactivated
  if (!systemConfig.value.isSimulationActive) {
    writeVariableToBackend(variableKey, newValue);
  }
};

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

// === 8. RESPONSIVE SECURITY LOGIN SYSTEM ===
export const isAuthenticated = ref<boolean>(false);
export const loginUser = ref<{ username: string; role: string } | null>(null);

// JWT Token 常量
const TOKEN_KEY = 'scada_access_token';

// 从 localStorage 读取 Token 并尝试自动登录
export const initializeAuth = () => {
  const token = localStorage.getItem(TOKEN_KEY);
  if (token) {
    // 检查 Token 是否过期（简单实现）
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      if (payload.exp * 1000 > Date.now()) {
        // Token 有效，设置登录状态
        isAuthenticated.value = true;
        loginUser.value = {
          username: payload.username || 'admin',
          role: payload.role || '系统管理员'
        };
        addLog('安全认证', 'Token 自动登录成功', 'normal');
      } else {
        localStorage.removeItem(TOKEN_KEY);
      }
    } catch {
      localStorage.removeItem(TOKEN_KEY);
    }
  }
};

// 登录接口对接：POST /api/Auth/login
export const performLogin = async (username: string, passwordString: string): Promise<{ success: boolean; errorMessage?: string }> => {
  try {
    const response = await axios.post(`${API_BASE_URL}/api/Auth/login`, {
      username: username,
      password: passwordString
    });

    if (response.data && response.data.success) {
      const token = response.data.token;
      localStorage.setItem(TOKEN_KEY, token);

      isAuthenticated.value = true;
      loginUser.value = {
        username: response.data.user?.username || username,
        role: response.data.user?.role || '系统管理员'
      };

      addLog('安全认证', `用户 [${username}] 通过API登录系统成功`, 'normal');
      return { success: true };
    } else {
      const errorMsg = response.data?.message || '用户名或密码错误';
      addLog('安全认证', `用户 [${username}] 登录失败: ${errorMsg}`, 'warning');
      return { success: false, errorMessage: errorMsg };
    }
  } catch (error: any) {
    const errorMessage = error.response?.data?.message || error.message || '服务器连接失败，请检查网络或后端服务';
    addLog('安全认证', `登录失败: ${errorMessage}`, 'warning');
    return { success: false, errorMessage: errorMessage };
  }
};

export const performLogout = () => {
  addLog('安全认证', `用户 [${loginUser.value?.username || 'admin'}] 注销系统登录`, 'normal');
  isAuthenticated.value = false;
  loginUser.value = null;
  localStorage.removeItem(TOKEN_KEY);
};

// === 9. VARIABLE TRIGGERS STATE & EVAL ENGINE ===
export const triggers = ref<VariableTrigger[]>([
  {
    id: 'trig-1',
    name: '储水罐极限高位警报',
    deviceId: 'dev-1',
    variableKey: 'tank_level',
    condition: 'greater',
    threshold: 90,
    actionType: 'alarm',
    alarmLevel: 'warning',
    active: true
  },
  {
    id: 'trig-2',
    name: '净化水池超低液位提示',
    deviceId: 'dev-1',
    variableKey: 'purified_level',
    condition: 'less',
    threshold: 15,
    actionType: 'alarm',
    alarmLevel: 'info',
    active: true
  },
  {
    id: 'trig-3',
    name: '热力锅炉超温联动安全联锁',
    deviceId: 'dev-2',
    variableKey: 'boiler_temp',
    condition: 'greater',
    threshold: 95,
    actionType: 'linkage',
    alarmLevel: 'warning',
    linkageVariableKey: 'pump_state',
    linkageValue: false, // Turn off heater cooling fan induction (pump_state)
    active: true
  }
]);

export const evaluateTriggers = () => {
  triggers.value.forEach((trigger) => {
    if (!trigger.active) return;
    const dev = devices.value.find((d) => d.id === trigger.deviceId);
    if (!dev || dev.status === 'offline') return;

    const currentVal = dev.variables[trigger.variableKey];
    if (currentVal === undefined) return;

    const numVal = Number(currentVal);
    let isFired = false;

    if (trigger.condition === 'greater') {
      isFired = numVal > trigger.threshold;
    } else if (trigger.condition === 'less') {
      isFired = numVal < trigger.threshold;
    } else if (trigger.condition === 'equal') {
      isFired = numVal === trigger.threshold;
    }

    if (isFired) {
      if (trigger.actionType === 'alarm') {
        // Flood control: Log only occasionally in simulation tick to save performance
        if (Math.random() > 0.96) {
          addLog(
            '联锁报警触发',
            `【触发器报警】${trigger.name}：当前物标 [${trigger.variableKey}] = ${currentVal}，已超过限定阈值 ${trigger.threshold}！`,
            trigger.alarmLevel
          );
        }
      } else if (trigger.actionType === 'linkage') {
        // Data linkage write set value
        const targetField = trigger.linkageVariableKey;
        if (targetField) {
          const currentTargetVal = getDeviceVariableValue(targetField);
          const linkVal = trigger.linkageValue;

          // Use linkage value directly
          const targetValueRaw = linkVal;

          if (targetValueRaw !== undefined && currentTargetVal !== targetValueRaw) {
            setDeviceVariableValue(targetField, targetValueRaw);
            addLog(
              '数据联动触发',
              `【连锁控制反馈】[${trigger.name}] 已触发：写入 [${targetField}] -> ${targetValueRaw}`,
              'warning'
            );
          }
        }
      }
    }
  });
};

// === 10. SYSTEM SCRIPTS CONFIG & LIVE SANDBOX RUNNER ===
export const systemScripts = ref<SystemScript[]>([
  {
    id: 'script-1',
    name: '温度越阈保护调速辅助机',
    code: `// 智能温控及风机保护联动脚本
let temp = getVal('boiler_temp');
if (temp > 85) {
  setVal('pump_state', true);
  log('【逻辑执行】炉膛核心温度达到=' + temp + '℃, 自动调高强制进风冷却叶。');
} else {
  log('【安全指标】温度=' + temp + '℃，在逻辑平稳带。');
}`,
    triggerType: 'auto',
    intervalSeconds: 5,
    executionStatus: 'idle',
    logOutput: '等待首次自动轮询触发...'
  },
  {
    id: 'script-2',
    name: '污水罐溢流自动断闸泄流阀',
    code: `// 一号溢流水位行程保护联动脚本
let levelRaw = getVal('tank_level');
if (levelRaw > 92) {
  setVal('valve_state', false);
  log('【水位高阈警报】储罐水位=' + levelRaw + '%, 强制切断一级供水闸阀防止漏溢！');
} else if (levelRaw < 15) {
  setVal('valve_state', true);
  log('【二次补水作业】储罐液位降至水箱底部水位=' + levelRaw + '%, 已重新合闸补水阀。');
} else {
  log('【液位平缓】液位=' + levelRaw + '%, 流程保持常开运转。');
}`,
    triggerType: 'manual',
    executionStatus: 'idle',
    logOutput: '等待计划任务调度或手动执行...'
  }
]);

export const runScriptEngine = (script: SystemScript) => {
  script.executionStatus = 'running' as any;
  const executionLogs: string[] = [];
  const logFormatter = (msg: string) => {
    executionLogs.push(`[${new Date().toLocaleTimeString()}] ${msg}`);
  };

  // Safe client-side sandbox container
  const sandbox = {
    getVal: (key: string) => {
      const val = getDeviceVariableValue(key);
      logFormatter(`读取绑定键 [${key}] = ${val}`);
      return val;
    },
    setVal: (key: string, val: any) => {
      setDeviceVariableValue(key, val);
      logFormatter(`命令写入 [${key}] = ${val}`);
    },
    log: (msg: string) => {
      logFormatter(`[用户输出] ${msg}`);
    }
  };

  try {
    // Compile and execute code in safety with context mapping
    const executor = new Function('sandbox', `
      with (sandbox) {
        ${script.code}
      }
    `);
    executor(sandbox);
    script.executionStatus = 'success';
    script.logOutput = executionLogs.join('\n');
    const pad = (n: number) => n.toString().padStart(2, '0');
    const d = new Date();
    script.lastExecuted = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
  } catch (e: any) {
    script.executionStatus = 'error';
    executionLogs.push(`[编译执行故障] -> ${e.message}`);
    script.logOutput = executionLogs.join('\n');
  }
};

// === 11. TASK MANAGEMENT (CRON & SCHEDULERS) ===
export const scheduledTasks = ref<ScheduledTask[]>([
  {
    id: 'task-1',
    name: '每天凌晨定时备份工业SCADA控制全库',
    type: 'backup',
    cronExpression: '每个工作日凌晨 02:00',
    params: {},
    status: 'idle',
    active: true
  },
  {
    id: 'task-2',
    name: '每周末定时清理30天外历史时序数据',
    type: 'clear_history',
    cronExpression: '每周日 00:00:00',
    params: { retentionDays: 30 },
    status: 'idle',
    active: true
  },
  {
    id: 'task-3',
    name: '计划驱动冷凝引风风机开启合闸',
    type: 'set_value',
    cronExpression: '每隔 15 分钟',
    params: { variableKey: 'pump_state', newValue: 1 },
    status: 'idle',
    active: false
  },
  {
    id: 'task-4',
    name: '按计划间隔触发高温排气阀自检脚本',
    type: 'execute_script',
    cronExpression: '每 5 分钟间隔轮询',
    params: { scriptId: 'script-1' },
    status: 'idle',
    active: true
  }
]);

export const executeTask = (taskId: string) => {
  const task = scheduledTasks.value.find((t) => t.id === taskId);
  if (!task) return;

  task.status = 'running';
  setTimeout(() => {
    try {
      const pad = (n: number) => n.toString().padStart(2, '0');
      const d = new Date();
      task.lastRun = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;

      if (task.type === 'set_value') {
        if (task.params.variableKey && task.params.newValue !== undefined) {
          const valToWrite = task.params.variableKey === 'pump_state' || task.params.variableKey === 'valve_state' 
            ? !!task.params.newValue 
            : task.params.newValue;
          setDeviceVariableValue(task.params.variableKey, valToWrite);
          addLog('调度执行', `计划任务 [${task.name}] 写入 [${task.params.variableKey}] = ${task.params.newValue} 成功`, 'normal');
        }
      } else if (task.type === 'backup') {
        addLog('系统内核', `计划任务 [${task.name}]：已成功压缩主数据库并导出 iota_scada_v6_dump_${Date.now()}.sql.gz`, 'normal');
      } else if (task.type === 'execute_script') {
        if (task.params.scriptId) {
          const targetScr = systemScripts.value.find(s => s.id === task.params.scriptId);
          if (targetScr) {
            runScriptEngine(targetScr);
            addLog('调度执行', `计划任务 [${task.name}] 执行脚本 [${targetScr.name}] 成功`, 'info');
          }
        }
      } else if (task.type === 'clear_history') {
        const keepDays = task.params.retentionDays || 30;
        historicalRecords.value = historicalRecords.value.slice(0, 120); // Retain live records
        addLog('系统内核', `计划任务 [${task.name}] 执行完毕：清空 ${keepDays} 天之前的时序块`, 'warning');
      }

      task.status = 'success';
    } catch (err: any) {
      task.status = 'failed';
      addLog('调度控制器', `调度日常执行遇到常规阻断: [${task.name}] Error: ${err.message}`, 'warning');
    }
  }, 1200);
};

// === 12. EXPOSED API DATA INTERFACES ===
export const exposedApis = ref<ExposedDataInterface[]>([
  {
    id: 'api-1',
    name: '一号污水净化池瞬时遥测液位数据集',
    exposedKey: 'tank_level',
    deviceId: 'dev-1',
    active: true,
    routeUrl: '/api/v1/telemetry/tank_level',
    requestMethod: 'GET'
  },
  {
    id: 'api-2',
    name: '高真空冷热机反应熔炉热电偶最高实测温度',
    exposedKey: 'boiler_temp',
    deviceId: 'dev-2',
    active: true,
    routeUrl: '/api/v1/telemetry/boiler_temp',
    requestMethod: 'GET'
  },
  {
    id: 'api-3',
    name: '主供水管干线多相超声流量瞬测计反馈值',
    exposedKey: 'flow_rate',
    deviceId: 'dev-1',
    active: true,
    routeUrl: '/api/v1/telemetry/flow_rate',
    requestMethod: 'GET'
  }
]);

// === 13. REALTIME & HISTORICAL DATABASES CONFIG ===
export const databaseConfigs = ref<DatabaseConfig[]>([
  {
    id: 'db-1',
    name: '主厂区核心设备寄存器实时写缓存 (Relational DB)',
    type: 'realtime',
    backendType: 'MySQL',
    host: '10.150.2.140',
    port: 3306,
    username: 'scada_core_writer',
    databaseName: 'iota_m2m_live',
    status: 'connected'
  },
  {
    id: 'db-2',
    name: '时序流式遥测指标超千兆存储舱 (Time-Series DB)',
    type: 'historical',
    backendType: 'TimescaleDB',
    host: '10.150.2.141',
    port: 5432,
    username: 'scada_analyzer_reader',
    databaseName: 'iota_ts_telemetry',
    status: 'connected'
  }
]);

// === 14. GLOBAL SYSTEM SETTINGS CENTER ===
export const systemConfig = ref<SystemConfig>({
  systemTitle: 'IOTA-SCADA 工业物联大脑',
  pollIntervalMs: 1200,
  mqttBrokerHost: '10.120.44.15',
  mqttBrokerPort: 1883,
  opcUaDiscoveryUrl: 'opc.tcp://10.120.44.12:4840',
  alarmEmailNotify: true,
  alarmEmailAddress: 'ops_alerts@iota-factory.com',
  retentionPeriodDays: 90,
  isSimulationActive: false, // Default: False (Deactivated), so we fetch from backend instead
  backendApiUrl: 'http://localhost:5000'
});

// === 15. SEEDING ENGINE FOR REALTIME / HISTORICAL COMPARISON ===
const generateSeededHistoricalRecords = (): HistoricalRecord[] => {
  const result: HistoricalRecord[] = [];
  const keys = ['tank_level', 'purified_level', 'flow_rate', 'boiler_temp', 'boiler_press', 'conveyor_speed'];
  const names: Record<string, string> = {
    tank_level: '储水罐瞬时液位 (tank_level)',
    purified_level: '净化池实时水位 (purified_level)',
    flow_rate: '管路瞬时排量波动 (flow_rate)',
    boiler_temp: '反应炉膛核心温度 (boiler_temp)',
    boiler_press: '反应容膛瞬时压力 (boiler_press)',
    conveyor_speed: '传送轮变频设定转速 (conveyor_speed)'
  };
  const pad = (n: number) => n.toString().padStart(2, '0');

  // Seed 180 records over the past hours to populate charts
  for (let i = 120; i >= 1; i--) {
    const date = new Date(Date.now() - i * 60000 * 4); // 4-minute steps
    const timeStr = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;

    keys.forEach((key) => {
      let baseVal = 50;
      if (key === 'tank_level') baseVal = 65 + Math.sin(i * 0.12) * 12 + (Math.random() - 0.5) * 2;
      else if (key === 'purified_level') baseVal = 32 + Math.cos(i * 0.15) * 6 + (Math.random() - 0.5) * 1.5;
      else if (key === 'flow_rate') baseVal = 18.5 + Math.sin(i * 0.22) * 4 + (Math.random() - 0.5) * 0.8;
      else if (key === 'boiler_temp') baseVal = 72 + Math.sin(i * 0.08) * 16 + (Math.random() - 0.5) * 3;
      else if (key === 'boiler_press') baseVal = 55 + Math.sin(i * 0.08) * 9 + (Math.random() - 0.5) * 1.2;
      else if (key === 'conveyor_speed') baseVal = 120 + (i % 3 === 0 ? 10 : i % 3 === 1 ? -12 : 5);

      result.push({
        id: `hist-seed-${i}-${key}`,
        variableKey: key,
        variableName: names[key] || key,
        value: +baseVal.toFixed(2),
        timestamp: timeStr
      });
    });
  }
  return result;
};

export const historicalRecords = ref<HistoricalRecord[]>(generateSeededHistoricalRecords());

// === 16. ASP.NET CORE BACKEND BRIDGE INTEGRATION ===

export const isBackendConnected = ref<boolean>(false);
export const signalRConnection = ref<HubConnection | null>(null);

export const fetchDevicesFromBackend = async () => {
  if (systemConfig.value.isSimulationActive) return;

  try {
    const res = await fetch(`${systemConfig.value.backendApiUrl}/api/scada/devices`);
    if (!res.ok) {
      throw new Error(`HTTP Status ${res.status}`);
    }
    const data = await res.json();
    if (Array.isArray(data)) {
      data.forEach((backendDev: any) => {
        const localDev = devices.value.find(d => d.id === backendDev.id || d.code === backendDev.code);
        if (localDev) {
          localDev.status = backendDev.status || localDev.status;
          if (backendDev.variables) {
            Object.keys(backendDev.variables).forEach(k => {
              localDev.variables[k] = backendDev.variables[k];
            });
          }
          if (backendDev.lastUpdated) {
            localDev.lastUpdated = backendDev.lastUpdated;
          }
        } else {
          // If a new device is created on the backend, add it locally!
          devices.value.push({
            id: backendDev.id || `dev-${Date.now()}`,
            name: backendDev.name,
            code: backendDev.code || 'UNKNOWN',
            areaId: backendDev.areaId || 'area-1',
            modelId: backendDev.modelId || 'model-wastewater',
            type: backendDev.type || 'OPCUA',
            status: backendDev.status || 'online',
            variables: backendDev.variables || {},
            lastUpdated: backendDev.lastUpdated || '刚刚'
          });
        }
      });
    }
  } catch (err: any) {
    if (Math.random() > 0.95) {
      addLog('REST 轮询', `无法同步设备变量: ${err.message}`, 'warning');
    }
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

export const initializeRealtimeSignals = () => {
  if (systemConfig.value.isSimulationActive) {
    if (signalRConnection.value) {
      signalRConnection.value.stop().catch(() => {});
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

export const writeVariableToBackend = async (variableKey: string, value: any) => {
  if (systemConfig.value.isSimulationActive) return;

  // SignalR socket write first
  if (signalRConnection.value && signalRConnection.value.state === HubConnectionState.Connected) {
    try {
      await signalRConnection.value.invoke("WritePlcVariable", variableKey, value);
      addLog('SignalR 写入', `下行写指令成功 (WebSocket): [${variableKey}] = ${value}`, 'info');
      return;
    } catch (err: any) {
      addLog('SignalR 写入', `Websocket 下发失败: ${err.message}，正在尝试使用 REST API 写入...`, 'warning');
    }
  }

  // REST API write fallback
  try {
    const res = await fetch(`${systemConfig.value.backendApiUrl}/api/scada/variables/write`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ variableKey, value })
    });
    if (!res.ok) {
      throw new Error(`HTTP status code ${res.status}`);
    }
    const data = await res.json();
    if (data.success) {
      addLog('REST 写入', `下行写指令成功 (REST): [${variableKey}] = ${value}`, 'normal');
    } else {
      addLog('REST 写入', `接口下行拦截报错: ${data.message || '未知原因'}`, 'warning');
    }
  } catch (err: any) {
    addLog('REST 写入', `下行链路硬阻断: ${err.message}`, 'warning');
  }
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

