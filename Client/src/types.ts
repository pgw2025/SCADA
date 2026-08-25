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
  id?: number;
  name: string;
  description?: string;
}

// 变量类型枚举
export type VariableType = 'analog' | 'digital';

// 数据类型枚举（与后端 ScadaServer.Domain.Enums.DataTypeEnum 对齐）
export type DataTypeEnum = 
  | 'INT' | 'REAL' | 'BOOL' | 'DINT' | 'BYTE' | 'BIT'
  | 'FLOAT' | 'DOUBLE' | 'STRING'
  | 'UINT16' | 'UINT32' | 'INT64' | 'UINT64'
  | 'WORD' | 'CHAR';

// 更新模式枚举
export type UpdateMode = 'polling' | 'subscription';

export interface ModelVariable {
  id: number;
  modelId: number;
  key: string;
  name: string;
  type?: VariableType;
  dataType: DataTypeEnum;
  unit?: string;
  min?: number;
  max?: number;
  description?: string;
  isStored: boolean;
  storeMode: 'None' | 'Change' | 'Cycle' | 'Compressed' | 'Aggregated';
  updateMode: UpdateMode;
  
  // 工业级增强字段
  scaleSlope: number;
  scaleOffset: number;
  deadBand?: number;
  isReadOnly: boolean;
  extensionData?: Record<string, string>;
}

/**
 * 设备变量实例：变量模板（ModelVariable）在某台具体设备上的落地配置。
 * 对应后端 DeviceVariableDto（ScadaServer.Application.DTOs.DeviceVariableDto）。
 * "是什么"（Key/Name/DataType/Unit）来自模板；"怎么采集"（Address/BitOffset/PollingInterval/覆盖值）为实例级。
 * 创建（POST）仅需 deviceId + modelVariableId + isEnabled，地址等需后续 PUT 补全。
 */
export interface DeviceVariable {
  id: number;
  deviceId: number;
  modelVariableId: number;
  key: string;               // 来自模板 ModelVariable.Key
  name: string;              // 来自模板 ModelVariable.Name
  dataType: DataTypeEnum;    // 来自模板 ModelVariable.DataType
  unit?: string;             // 来自模板 ModelVariable.Unit
  address?: string | null;           // 实例级：实际寄存器地址；空字符串采集会失败
  bitOffset?: number | null;         // 实例级：位偏移（BOOL/BIT 用）
  pollingIntervalMs?: number | null; // 实例级：轮询间隔，空=运行时默认 1000ms
  isEnabled: boolean;                // 实例级：是否启用采集
  scaleSlopeOverride?: number | null;   // 实例级覆盖：缩放斜率，空=用模板值
  scaleOffsetOverride?: number | null;  // 实例级覆盖：缩放偏移，空=用模板值
  deadBandOverride?: number | null;     // 实例级覆盖：死区，空=用模板值
  isReadOnlyOverride?: boolean | null;  // 实例级覆盖：读写权限，空=继承模板
  templateIsReadOnly?: boolean;         // 回显：模板定义的只读权限
  effectiveIsReadOnly?: boolean;        // 回显：有效权限（Override ?? 模板）
}

export type DeviceType = 'OPCUA' | 'S7' | 'MQTT' | 'Virtual' | 'ModbusTcp' | 'BACnet' | 'DNP3';

/**
 * 通信协议（协议/驱动解耦真相源）。
 * 对应后端 ProtocolDto（Id / Key / Name / DriverKey / Description / IsEnabled）。
 * 创建数据模型时必须选择协议（ProtocolId 必填），运行期由 Protocol.DriverKey 派发驱动。
 */
export interface Protocol {
  id: number;
  key: string;         // 如 "S7" / "OPCUA" / "VIRTUAL" / "MQTT" / "MODBUSTCP"
  name: string;
  driverKey: string;
  description?: string;
  isEnabled: boolean;
}

/**
 * 将后端 Protocol.Key 映射为前端 DeviceType(枚举)。
 * 协议真相源在 Protocol 实体后，protocolKey 取代 DataModel.Type 派生协议类型。
 */
export const protocolKeyToDeviceType = (key?: string): DeviceType => {
  switch ((key || '').trim().toUpperCase()) {
    case 'S7': return 'S7';
    case 'OPCUA': return 'OPCUA';
    case 'MQTT': return 'MQTT';
    case 'MODBUSTCP': return 'ModbusTcp';
    case 'BACNET': return 'BACnet';
    case 'DNP3': return 'DNP3';
    case 'VIRTUAL': return 'Virtual';
    default: return 'Virtual';
  }
};

/** 设备类型下拉/筛选选项（label 与后端 DeviceTypeJsonConverter.SerializeMap 对齐） */
export const DEVICE_TYPES: { value: DeviceType; label: string; implemented: boolean }[] = [
  { value: 'OPCUA', label: 'OPC UA', implemented: true },
  { value: 'S7', label: '西门子 S7', implemented: true },
  { value: 'MQTT', label: 'MQTT', implemented: false },
  { value: 'Virtual', label: '虚拟设备', implemented: true },
  { value: 'ModbusTcp', label: 'Modbus TCP', implemented: false },
  { value: 'BACnet', label: 'BACnet', implemented: false },
  { value: 'DNP3', label: 'DNP3', implemented: false }
];

/**
 * 协议 → 设备变量实例字段需求配置（单一真相源）。
 * 不同协议设备的"采集属性"不同，前端据此条件渲染字段：
 * - addressLabel 缺省 = 该协议不需要地址（如虚拟设备），不渲染地址列/输入框
 * - addressRequired 用于编辑弹窗的"必填"提示
 * - needsBitOffset 控制是否渲染位偏移字段
 */
export interface ProtocolFieldConfig {
  addressLabel?: string;
  addressPlaceholder?: string;
  addressRequired?: boolean;
  needsBitOffset?: boolean;
}

/** 协议字段配置表：新增协议只需在此补一行，页面自动适配 */
export const PROTOCOL_FIELD_CONFIG: Record<DeviceType, ProtocolFieldConfig> = {
  S7:        { addressLabel: '寄存器地址', addressPlaceholder: '如 DB1.DBD4 / DB1.DBX0.0', addressRequired: true, needsBitOffset: true },
  OPCUA:     { addressLabel: '节点ID',    addressPlaceholder: '如 ns=2;i=5',             addressRequired: true, needsBitOffset: false },
  ModbusTcp: { addressLabel: '寄存器地址', addressPlaceholder: '如 40001',                addressRequired: true, needsBitOffset: true },
  MQTT:      { addressLabel: 'Topic/路径', addressPlaceholder: '如 plant1/pump/level',    addressRequired: true, needsBitOffset: false },
  BACnet:    { addressLabel: '对象地址',   addressPlaceholder: '如 AV:1',                 addressRequired: true, needsBitOffset: false },
  DNP3:      { addressLabel: '点表索引',   addressPlaceholder: '如 2-3',                  addressRequired: true, needsBitOffset: false },
  Virtual:   { }
};

export interface DataModel {
  id: string;
  name: string;
  description: string;
  // 协议绑定（协议真相源）：对应后端 DataModelDto.ProtocolId / ProtocolKey / ProtocolName。
  // 协议真相源在独立的 Protocol 实体，不再有过渡字段 Type；
  // 协议类型由 protocolKey 经 protocolKeyToDeviceType() 派生。
  // ProtocolId 必填：创建模型时必须选择协议；更新时必须原样回传，避免后端 PUT 全量替换语义解绑协议。
  protocolId: number;
  protocolKey?: string;
  protocolName?: string;
  variables: ModelVariable[];
}

export interface Device {
  id: number;
  name: string;
  key: string;        // e.g. SCADA-PUMP-01
  code?: string;
  areaId: number;
  areaName?: string;
  modelId: number;
  modelName?: string;
  type: DeviceType;   // 派生只读：由 modelId 反查 dataModels → protocolKey 得到，设备本身不再存储协议
  ipAddress?: string; // S7/OPCUA specific
  port?: number | string;      // Port, e.g. 502, 4840
  status: number | string;     // 0: offline, 1: online or 'online' | 'offline'
  runtimeStatus?: string;       // 后端运行时状态枚举名: Online | Offline | Fault | Connecting
  lastUpdated: string; // ISO 8601 datetime string
  variables?: Record<string, any>;
  variableTimestamps?: Record<string, string>;

  // Advanced connection parameters
  cpuType?: string;         // S7 CPU类型 (e.g. S7-1200, S7-1500, S7-300, S7-400)
  rack?: number;            // S7 机架号
  slot?: number;            // S7 插槽号
  topic?: string;
  mqttServer?: string;
  publishTopic?: string;
  subscribeTopic?: string;
  payloadTemplate?: string;

  // 后端协议配置 JSON(对应 CreateDeviceDto.ConfigJson)
  configJson?: string;
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
  id: number;         // 必传：后端 PUT /api/SystemUser/{id} 从路由取 id，实体不存在会报错
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

