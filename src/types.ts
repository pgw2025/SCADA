export type ComponentType =
  | 'pump'
  | 'valve'
  | 'tank'
  | 'boiler'
  | 'pipe-v'
  | 'pipe-h'
  | 'gauge-dial'
  | 'gauge-level'
  | 'digital-val'
  | 'trend-chart'
  | 'conveyor'
  | 'text'
  | 'led'
  | 'button'       // 按钮控制：支持自锁(Toggle)或点动(Momentary)或设值(SetValue)
  | 'switch'       // 开关拨码
  | 'sys-time'     // 工业实时系统时间时钟
  | 'state-text'   // PLC变量对应的多状态文本状态翻译 (e.g. 0=故障, 1=运行)
  | 'motor';       // 变频伺服电机 (带旋转定子、风扇叶动效)

export interface HMIComponent {
  id: string;
  type: ComponentType;
  name: string;
  x: number;
  y: number;
  width: number;
  height: number;
  label: string;
  bindField: string; // The simulation variable key
  zIndex: number;
  props: {
    activeColor?: string;
    inactiveColor?: string;
    showValue?: boolean;
    maxValue?: number;
    unit?: string;
    fillColor?: string;
    strokeColor?: string;
    thresholdMin?: number;
    thresholdMax?: number;
    fontSize?: number;
    align?: 'left' | 'center' | 'right';
    bold?: boolean;
    
    // 按钮/开关专属属性
    buttonMode?: 'toggle' | 'momentary' | 'set-value'; // 按钮操作模式
    clickValue?: number; // 设值模式下点击写入的具体数值
    buttonText?: string; // 按钮上静态/动态显示的文本
    
    // 多状态文本映射属性
    stateMappings?: string; // 用户可自定义配置: "0:停止;1:预热;2:满载运行" 或 "false:关闭;true:激活"
    
    // 系统时间控件格式
    timeFormat?: 'HH:mm:ss' | 'YYYY-MM-DD HH:mm:ss' | 'YYYY-MM-DD';
  };
}

export interface PLCTag {
  key: string;
  name: string;
  value: number | boolean;
  unit: string;
  type: 'analog' | 'digital';
  min: number;
  max: number;
  description: string;
}

export interface SimulationConfig {
  boiler_press: number;
  boiler_temp: number;
  tank_level: number;
  pump_state: boolean;
  valve_state: boolean;
  conveyor_speed: number;
  flow_rate: number;
  gas_flow: number;
}

// === NEW BACKEND MANAGEMENT SYSTEM TYPES ===

export interface Area {
  id: string;
  name: string;
  description: string;
}

export interface ModelVariable {
  key: string;
  name: string;
  type: 'analog' | 'digital';
  dataType?: string; // S7: INT, REAL etc. OPCUA: Int32, Float etc.
  unit: string;
  min: number;
  max: number;
  address: string; // SLC/Modbus/DB Offset, e.g. "DB1.DBD4" or "40001" or "telemetry/temp"
  description: string;
  
  // Advanced parameters based on device type
  dataArea?: string;            // S7 数据区域 (DB, I, Q, M)
  accessLevel?: 'RO' | 'RW';    // 访问级别 (RO: 只读, RW: 读写)
  scaleExpr?: string;           // 换算放缩表达式 (e.g. "x * 10" or "x / 1.5")
  isStored?: boolean;           // 是否存储时序数据库
  storeMode?: 'change' | 'interval'; // 存储方式 (change: 变化存储, interval: 定时存储)
  nodeId?: string;              // OPCUA 节点 ID
  updateMode?: 'subscription' | 'polling'; // OPCUA 更新方式 (订阅更新 / 轮询更新)
  pollIntervalSecs?: number;     // 变量专用轮询周期 (秒)
}

export type DeviceType = 'OPCUA' | 'S7' | 'MQTT' | 'Virtual';

export interface DataModel {
  id: string;
  name: string;
  description: string;
  type: DeviceType;
  variables: ModelVariable[];
}

export interface Device {
  id: string;
  name: string;
  code: string;       // e.g. SCADA-PUMP-01
  areaId: string;
  modelId: string;
  type: DeviceType;
  ipAddress?: string; // S7/OPCUA specific
  port?: string;      // Port, e.g. 502, 4840
  topic?: string;     // MQTT specific
  status: 'online' | 'offline';
  variables: Record<string, number | boolean>;
  lastUpdated: string;
  
  // Advanced connection parameters
  cpuType?: string;         // S7 CPU类型 (e.g. S7-1200, S7-1500, S7-300, S7-400)
  rack?: number;            // S7 机架号
  slot?: number;            // S7 插槽号
  mqttServer?: string;      // MQTT服务器地址
  publishTopic?: string;    // MQTT发布主题
  subscribeTopic?: string;  // MQTT订阅主题 (可以与 topic 映射)
  payloadTemplate?: string;  // MQTT发布内容模板
  variableTimestamps?: Record<string, string>; // 每个具体变量的更新时间 (键为变量Key, 值为时间)
}

export interface MqttServer {
  id: string;
  name: string;
  brokerUrl: string;
  port: number;
  clientId: string;
  username?: string;
  password?: string;
  topicPrefix?: string;
  status: 'connected' | 'disconnected';
  associatedVariables: { deviceId: string; variableKey: string }[];
}

export interface DataConversion {
  id: string;
  name: string;
  sourceDeviceId: string;
  sourceVariableKey: string;
  targetDeviceId: string;
  targetVariableKey: string;
  active: boolean;
}

export interface SystemUser {
  id: number;
  username: string;
  passwordHash?: string;
  role: string;
  status: string;
}

export interface CreateUserDto {
  username: string;
  password: string;
  role: string;
  status?: string;
}

export interface UpdateUserDto {
  username?: string;
  role?: string;
  status?: string;
}

export interface LoginDto {
  username: string;
  password: string;
}

export interface ScadaPage {
  id: string;
  name: string;
  components: HMIComponent[];
}

export interface ScadaScreenProject {
  id: string;
  name: string;
  description: string;
  pages: ScadaPage[];
}

export interface SystemLog {
  id: string;
  timestamp: string;
  level: 'info' | 'warning' | 'normal';
  source: string; // e.g. "设备管理", "MQTT服务", "S7驱动", "用户操作", "系统参数"
  content: string;
}

export interface ServerStatus {
  cpuUsage: number;
  memUsage: number;
  diskLoadPercentage: number;
  networkIn: number;
  networkOut: number;
  uptimeDays: number;
  uptimeHours: number;
  uptimeMins: number;
  pollFreq: number;
  totalPollPackets: number;
  disks: {
    name: string;
    label: string;
    totalSizeGb: number;
    usedSizeGb: number;
    usagePercentage: number;
  }[];
}

// === SCADA INDUSTRIAL PLUGINS & EXTENSIONS ===

export interface VariableTrigger {
  id: string;
  name: string;
  deviceId: string;
  variableKey: string;
  condition: 'less' | 'greater' | 'equal';
  threshold: number;
  actionType: 'alarm' | 'linkage';
  alarmLevel: 'info' | 'normal' | 'warning';
  linkageVariableKey?: string;
  linkageValue?: number | boolean;
  active: boolean;
}

export interface ScheduledTask {
  id: string;
  name: string;
  type: 'set_value' | 'backup' | 'execute_script' | 'clear_history';
  cronExpression: string; // e.g. "每5秒", "每天凌晨2:00", "每分钟"
  params: {
    variableKey?: string;
    newValue?: number;
    scriptId?: string;
    retentionDays?: number;
  };
  lastRun?: string;
  status: 'idle' | 'running' | 'success' | 'failed';
  active: boolean;
}

export interface SystemScript {
  id: string;
  name: string;
  code: string;
  triggerType: 'auto' | 'manual';
  intervalSeconds?: number;
  lastExecuted?: string;
  executionStatus?: 'idle' | 'success' | 'error';
  logOutput?: string;
}

export interface ExposedDataInterface {
  id: string;
  name: string;
  exposedKey: string;
  deviceId: string;
  active: boolean;
  routeUrl: string;
  requestMethod: 'GET' | 'POST';
}

export interface HistoricalRecord {
  id: string;
  variableKey: string;
  variableName: string;
  value: number;
  timestamp: string;
}

export interface DatabaseConfig {
  id: string;
  name: string;
  type: 'realtime' | 'historical';
  backendType: 'MySQL' | 'PostgreSQL' | 'SQLite' | 'InfluxDB' | 'ClickHouse' | 'TimescaleDB';
  host: string;
  port: number;
  username: string;
  databaseName: string;
  status: 'connected' | 'disconnected' | 'testing';
}

export interface SystemConfig {
  systemTitle: string;
  pollIntervalMs: number;
  mqttBrokerHost: string;
  mqttBrokerPort: number;
  opcUaDiscoveryUrl: string;
  alarmEmailNotify: boolean;
  alarmEmailAddress: string;
  retentionPeriodDays: number;
  isSimulationActive?: boolean;
  backendApiUrl?: string;
}

