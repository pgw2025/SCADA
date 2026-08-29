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
  | 'rounded-btn'  // 工业圆角按钮：支持变量绑定、自定义多状态背景/文字、取反/置位/复位/按1送0
  | 'motor';       // 变频伺服电机 (带旋转定子、风扇叶动效)

export interface HMIComponent {
  id: string;
  /** 后端自增主键（持久化后回填）；未保存的新组件为 undefined */
  serverId?: number;
  type: ComponentType;
  name: string;
  x: number;
  y: number;
  width: number;
  height: number;
  label: string;
  bindField: string; // 遗留：裸变量键（兼容模板与旧组件）
  /** 阶段3 复合绑定：设备ID（对应 Device.id / 后端 HmiComponent.BindDeviceId） */
  bindDeviceId?: number | null;
  /** 阶段3 复合绑定：设备内变量键（对应后端 HmiComponent.BindVariableKey） */
  bindVariableKey?: string | null;
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
    buttonMode?: 'toggle' | 'momentary' | 'set-value' | 'set-bit' | 'reset-bit' | 'navigate'; // 按钮操作模式（toggle=取反, momentary=按1送0/点动, set-bit=置位1, reset-bit=复位0, set-value=设值, navigate=跳转）
    clickValue?: number; // 设值模式下点击写入的具体数值
    buttonText?: string; // 按钮上静态/动态显示的文本
    targetPageId?: string | null; // 导航模式下跳转目标画面 id（仅限同端）

    // 圆角按钮/自定义状态专属属性
    borderRadius?: number; // 圆角弧度（px，如 4/8/12/20/999）
    borderWidth?: number; // 边框粗细（px）
    customStates?: string; // 自定义状态配置字典: "0:停止:#64748b:#ffffff;1:运行:#10b981:#ffffff;2:报警:#ef4444:#ffffff" 或 JSON 字符串
    state0Text?: string; // 状态0默认文本
    state0BgColor?: string; // 状态0默认背景色
    state0TextColor?: string; // 状态0默认文字颜色
    state1Text?: string; // 状态1默认文本
    state1BgColor?: string; // 状态1默认背景色
    state1TextColor?: string; // 状态1默认文字颜色

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
  /** 历史存储周期（毫秒）。Change 作为超时兜底周期，Cycle 作为定时采样周期。 */
  storeIntervalMs: number;
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
  quality?: string;                     // 运行期实时值质量（Good/Bad/Uncertain/CommunicationError/…），组态运行端分级显示
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
  S7: { addressLabel: '寄存器地址', addressPlaceholder: '如 DB1.DBD4 / DB1.DBX0.0', addressRequired: true, needsBitOffset: true },
  OPCUA: { addressLabel: '节点ID', addressPlaceholder: '如 ns=2;i=5', addressRequired: true, needsBitOffset: false },
  ModbusTcp: { addressLabel: '寄存器地址', addressPlaceholder: '如 40001', addressRequired: true, needsBitOffset: true },
  MQTT: { addressLabel: 'Topic/路径', addressPlaceholder: '如 plant1/pump/level', addressRequired: true, needsBitOffset: false },
  BACnet: { addressLabel: '对象地址', addressPlaceholder: '如 AV:1', addressRequired: true, needsBitOffset: false },
  DNP3: { addressLabel: '点表索引', addressPlaceholder: '如 2-3', addressRequired: true, needsBitOffset: false },
  Virtual: {}
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
  // 设备实例级变量元数据（按 key 索引，来源后端 DeviceDto.Variables 数组）。
  // normalizeDevices 把后端数组压扁成 variables 键值表时，同步保留这里以便消费方
  // 取 effectiveIsReadOnly / isReadOnlyOverride / templateIsReadOnly 等实例级权限，
  // 实时监控页据此判断写入按钮显隐（设备级覆盖优先于模板 isReadOnly）。
  variableMeta?: Record<string, DeviceVariable>;

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

// 后端 MqttServerDto
export interface MqttServer {
  id: number;
  name: string;
  brokerUrl: string;
  port: number;
  clientId: string;
  username?: string;
  // 密码仅写入用；列表/详情接口不回传明文（空串）。编辑时留空=保持原密码。
  password?: string;
  topicPrefix?: string;
  isEnabled: boolean;
  // 该服务器下已关联的变量数量（后端填充）
  variableCount: number;
}

// 后端 MqttServerStatusDto（卡片状态展示，由 MqttManager 维护动态状态）
export interface MqttServerStatus {
  id: number;
  name: string;
  status: 'Connected' | 'Connecting' | 'Disconnected' | 'Error' | 'Disabled';
  lastError: string;
  lastConnectedUtc: string | null;
  reconnectAttempts: number;
  variableCount: number;
}

// 后端 MqttVariableConfigDto（服务器关联变量）
export interface MqttVariableConfig {
  id: number;
  mqttServerId: number;
  deviceId: number;
  deviceName: string;
  variableKey: string;
  variableName: string;
  alias: string;
  customTopic?: string | null;
  isEnabled: boolean;
  topicPreview: string;
  realtimeValue?: unknown;
}

// 新增映射请求体（MqttVariableConfigCreateDto）
export interface MqttVariableConfigCreate {
  deviceId: number;
  variableKey: string;
  alias: string;
  customTopic?: string;
}

// 更新映射请求体（MqttVariableConfigUpdateDto）
export interface MqttVariableConfigUpdate {
  alias: string;
  customTopic?: string;
  isEnabled: boolean;
}

export interface DataConversion {
  id: number;
  name: string;
  sourceDeviceId: number;
  sourceVariableKey: string;
  targetDeviceId: number;
  targetVariableKey: string;
  active: boolean;
}

export interface SystemUser {
  id: number;
  username: string;
  role: string;
  status: string;
  createdAt?: string;
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

export interface ResetPasswordDto {
  newPassword: string;
}

export interface LoginDto {
  username: string;
  password: string;
}

export interface ScadaPage {
  id: string;
  /** 后端自增主键（持久化后回填）；未保存的新页面为 undefined */
  serverId?: number;
  name: string;
  /** 画面归属端：Desktop（桌面端）/ Mobile（移动端）。缺省回退 Desktop。 */
  platform?: 'Desktop' | 'Mobile';
  /** 是否为所在端（桌面端/移动端）的首页。同一 (工程, 端) 至多一个首页。 */
  isHome?: boolean;
  /** 画布尺寸（后端持久化；缺省回退默认 1100×700） */
  width?: number;
  height?: number;
  /** 画布背景配置（后端 BackgroundJson 反序列化；null/undefined=未配置回退白底） */
  background?: PageBackground | null;
  /** 运行端自适应屏幕模式（后端持久化；null/undefined=未配置回退兼容行为：等比缩小不放大） */
  adaptMode?: PageAdaptMode | null;
  components: HMIComponent[];
}

// ===== 组态页面背景与自适应配置 =====

/** 背景类型：纯色 / 渐变 / 图片（URL） */
export type PageBackgroundType = 'color' | 'gradient' | 'image';

/** 页面背景配置（序列化为 JSON 存后端 ScadaPage.BackgroundJson） */
export interface PageBackground {
  type: PageBackgroundType;
  /** 纯色：CSS 颜色值 */
  color?: string;
  /** 渐变：起始色 / 终止色 / 角度（deg，0-360） */
  gradientStart?: string;
  gradientEnd?: string;
  gradientAngle?: number;
  /** 图片：URL 及填充方式 */
  imageUrl?: string;
  /** fill=拉伸铺满（非等比）、contain=等比完整显示、cover=等比铺满裁切、tile=平铺 */
  imageFit?: 'fill' | 'contain' | 'cover' | 'tile';
}

/** 运行端自适应屏幕模式：FitScaleUp=等比缩放（允许放大）；Stretch=拉伸填满（非等比） */
export type PageAdaptMode = 'FitScaleUp' | 'Stretch';

export interface ScadaScreenProject {
  id: string;
  /** 后端自增主键（持久化后回填）；未保存的新工程为 undefined */
  serverId?: number;
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

// ===== 后端统一系统日志（运行/操作/安全），对应 GET /api/SystemLog =====
export interface SystemLogRecord {
  id: number;
  timestamp: string;
  /** 日志分类：Runtime（运行）/ Operation（操作审计）/ Security（安全审计） */
  category: 'Runtime' | 'Operation' | 'Security';
  /** 日志级别：Trace/Debug/Information/Warning/Error/Critical */
  level: string;
  /** 日志来源 */
  source: string;
  /** 动作类型（仅操作/安全日志） */
  operation?: string | null;
  /** 操作人（仅操作/安全日志） */
  operator?: string | null;
  /** 客户端 IP（仅操作/安全日志） */
  ipAddress?: string | null;
  /** 关联对象标识 */
  relatedId?: string | null;
  content: string;
}

/** 系统日志分页查询条件（GET /api/SystemLog 查询参数） */
export interface SystemLogQuery {
  category?: string;
  levels?: string[];
  keyword?: string;
  source?: string;
  startTime?: string | null;
  endTime?: string | null;
  pageIndex?: number;
  pageSize?: number;
}

/** 系统日志分页查询结果 */
export interface SystemLogPagedResult {
  total: number;
  items: SystemLogRecord[];
}

// ===== 报警管理（AlarmRule 规则 + AlarmRecord 记录），对应后端 AlarmRule/ AlarmRecord=====

/** 报警级别枚举（与后端 ScadaServer.Domain.Enums.AlarmLevelEnum 对齐） */
export type AlarmLevel = 'Low' | 'Medium' | 'High' | 'Critical';

/** 报警条件枚举（与后端 TriggerConditionEnum 对齐） */
export type TriggerCondition =
  | 'GreaterThan' | 'GreaterOrEqual'
  | 'LessThan' | 'LessOrEqual'
  | 'EqualTo' | 'NotEqualTo';

/** 报警来源（与后端 AlarmSourceEnum 对齐） */
export type AlarmSource = 'Rule' | 'MinMaxLimit' | 'System';

/** 报警规则（GET /api/AlarmRule） */
export interface AlarmRule {
  id: number;
  name: string;
  deviceId: number;
  variableKey: string;
  condition: TriggerCondition;
  threshold: number;
  level: AlarmLevel;
  active: boolean;
  message?: string | null;
  debounceSeconds: number;
}

/** 报警记录（GET /api/AlarmRecord） */
export interface AlarmRecord {
  id: number;
  deviceId: number;
  deviceKey: string;
  variableKey: string;
  variableName: string;
  ruleId?: number | null;
  ruleName?: string | null;
  level: AlarmLevel;
  condition?: TriggerCondition | null;
  threshold?: number | null;
  actualValue?: string | null;
  message: string;
  source: AlarmSource;
  triggeredAt: string;
  recoveredAt?: string | null;
  recoveryValue?: string | null;
  acked: boolean;
  ackedAt?: string | null;
  ackedBy?: string | null;
}

/** 报警记录查询条件（GET /api/AlarmRecord） */
export interface AlarmRecordQuery {
  deviceId?: number | null;
  level?: AlarmLevel | null;
  unacked?: boolean | null;
  unrecovered?: boolean | null;
  startTime?: string | null;
  endTime?: string | null;
  pageIndex?: number;
  pageSize?: number;
}

/** 报警记录分页查询结果 */
export interface AlarmRecordPagedResult {
  total: number;
  items: AlarmRecord[];
}

/**
 * 实时报警事件（SignalR "ReceiveAlarm" 载荷，对应后端 AlarmEvent）。
 * SignalR 默认 JSON 协议把枚举序列化为数字（与 REST 的字符串枚举不同），
 * 故 Level / Condition / Source 使用 string | number，由 alarmStore 归一化处理。
 */
export interface AlarmEventPayload {
  eventType?: string | number;
  deviceId: number;
  deviceKey?: string | null;
  variableKey: string;
  variableName?: string | null;
  ruleId?: number | null;
  ruleName?: string | null;
  level?: string | number | null;
  condition?: string | number | null;
  threshold?: number | null;
  actualValue?: string | null;
  message?: string | null;
  source?: string | number | null;
  triggeredAt?: string | null;
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
    /** 严格模式：set_value 任务的目标设备（禁止裸 key）。 */
    deviceId?: number | null;
    scriptId?: string;
    retentionDays?: number;
  };
  lastRun?: string;
  status: 'idle' | 'running' | 'success' | 'failed';
  active: boolean;
}

export interface SystemScript {
  id: number;
  /** 脚本名称 */
  name: string;
  /** 脚本代码（仅含逻辑函数声明 run/onChange，在服务端沙箱执行） */
  code: string;
  /** 触发类型：Manual / Periodic / Schedule / OnChange */
  triggerType: 'Manual' | 'Periodic' | 'Schedule' | 'OnChange';
  /** 执行间隔（秒），Periodic 触发时使用 */
  intervalSeconds?: number | null;
  /** Cron 表达式，Schedule 触发时使用 */
  cronExpression?: string | null;
  /** 监听设备键，OnChange 触发时使用 */
  watchDeviceKey?: string | null;
  /** 监听变量键，OnChange 触发时使用 */
  watchVariableKey?: string | null;
  /** OnChange 死区阈值：|new-old| > DeadBand 才触发 */
  deadBand?: number | null;
  /** OnChange 触发冷却时间（毫秒） */
  cooldownMs: number;
  /** 单次执行超时（毫秒） */
  timeoutMs: number;
  /** 读授权：分号分隔的设备键列表（设备级） */
  scopeRead?: string | null;
  /** 写授权：分号分隔的 "设备键.变量键" 列表（变量级） */
  scopeWrite?: string | null;
  /** 是否启用（调度器仅执行已启用脚本） */
  active: boolean;
  /** 脚本版本，保存时自动 +1 */
  version: number;
  /** 连续失败计数（成功清零），达到阈值触发熔断 */
  failureCount: number;
  /** 熔断标记：连续失败达阈值时为 true，人工复位后恢复 */
  tripped: boolean;
  /** 最近一次执行错误信息 */
  lastError?: string | null;
  /** 最近一次执行开始时间（ISO 字符串，服务端 UTC） */
  lastExecutedAt?: string | null;
  /** 最近一次执行耗时（毫秒） */
  lastDurationMs?: number | null;
}

/** 脚本单次执行记录（控制台追溯）。与后端 ScriptExecutionRecord 对齐。 */
export interface ScriptExecutionRecord {
  id: number;
  scriptId: number;
  scriptVersion: number;
  triggerSource: string;
  result: string;
  startedAt: string;
  durationMs?: number | null;
  error?: string | null;
  output?: string | null;
  executedBy?: string | null;
}

/** 脚本执行事件（SignalR ReceiveScriptExecution 载荷）。与后端 ScriptExecutionEvent 对齐。 */
export interface ScriptExecutionEvent {
  scriptId: number;
  scriptVersion: number;
  triggerSource: string;
  result: string;
  startedAt: string;
  durationMs?: number | null;
  error?: string | null;
  output?: string | null;
  executedBy?: string | null;
}

/** 脚本校验问题条目。 */
export interface ScriptValidationIssue {
  level: 'Error' | 'Warning';
  message: string;
}

/** 脚本校验结果。 */
export interface ScriptValidationResult {
  valid: boolean;
  issues: ScriptValidationIssue[];
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
  /** 所属设备标识（区分不同设备的同名变量，可为空=无设备上下文） */
  deviceKey?: string;
  variableKey: string;
  variableName: string;
  value: number;
  timestamp: string;
}

export type DatabaseBackendType = 'MySQL' | 'PostgreSQL' | 'SQLite' | 'InfluxDB' | 'TimescaleDB';

// 与后端 DatabaseConfigDto 对齐
export interface DatabaseConfig {
  id: number;
  name: string;
  type: 'Realtime' | 'Historical';
  backendType: DatabaseBackendType;
  host: string;
  port: number;
  username: string;
  /** 回显为掩码；保存时掩码/空 = 不改密 */
  password?: string | null;
  hasPassword?: boolean;
  databaseName: string;
  /** 回显为掩码（InfluxDB） */
  token?: string | null;
  hasToken?: boolean;
  org?: string | null;
  bucket?: string | null;
  isActive: boolean;
  /** 最近连接测试结果（Ok/Failed/出错信息） */
  status?: string | null;
  lastStatus?: string | null;
  lastCheckedAt?: string | null;
}

// 主库（MySQL，自举依赖）配置 DTO
export interface MainDatabaseConfig {
  host: string;
  port: number;
  databaseName: string;
  username: string;
  password?: string | null;
  hasPassword?: boolean;
}

// 数据库连接测试请求/结果
export interface TestConnectionRequest {
  backendType: string;
  host: string;
  port: number;
  username: string;
  password: string;
  databaseName: string;
  token?: string | null;
  org?: string | null;
  bucket?: string | null;
}

export interface TestConnectionResult {
  success: boolean;
  latencyMs: number;
  message: string;
}

// 历史数据迁移结果
export interface HistoryMigrationResult {
  isRunning: boolean;
  total: number;
  migrated: number;
  message: string;
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

