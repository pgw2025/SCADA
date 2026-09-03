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
  | 'var-display'  // 数据变量显示：大字号数值显示（可配小数位），可选「可设定」点击弹窗写值
  | 'rounded-btn'  // 工业圆角按钮：支持变量绑定、自定义多状态背景/文字、取反/置位/复位/按1送0
  | 'motor'        // 变频伺服电机 (带重载散热肋片、金属主轴与极速冷却风叶)
  | 'title-header' // 工业大屏与移动端高精度矢量标题背景栏 (3套风格 x 桌面/手机)
  | 'nav-menu'     // 组态导航菜单：桌面顶部横向导航条 / 移动底部 Tab 栏（图标+文字+页面跳转）
  | 'multi-var-dashboard' // 实时多变量看板：支持多变量绑定、列数调节、边框与主题样式、阈值预警与卡片/表格/紧凑模式
  | 'image';       // 自定义图片图元：上传/图库选择，URL 存 props.imageUrl

/** 实时多变量看板子监控项配置（存于 HMIComponent.props.dashboardItems） */
export interface HmiDashboardItem {
  id: string;
  deviceId?: number | null; // 绑定的所属设备ID（若未选则继承或使用全局变量池）
  variableKey: string;     // 变量键名 (如 "boiler_temp", "pump_state")
  label?: string;          // 自定义显示名称/点位标签（留空则默认读取变量模板名称或键名）
  unit?: string;           // 自定义单位（留空则继承变量自带单位）
  precision?: number | null; // 小数位数 (0~4，留空为默认自动)
  showStatusDot?: boolean; // 是否显示状态指示圆点 / 报警呼吸灯
  thresholdMin?: number | null; // 低限预警阈值 (数值低于此值标黄/报警)
  thresholdMax?: number | null; // 高限预警阈值 (数值高于此值标红/报警)
}

/** 实时波段趋势图（trend-chart）单条曲线序列：支持多变量绑定与逐线颜色/粗细自定义 */
export interface HmiTrendSeries {
  id: string;                 // 稳定主键（缓冲 key，增删/重排不变）
  deviceId?: number | null;   // 绑定设备；空则继承组件 bindDeviceId / 全局首设备
  variableKey: string;       // 变量键名
  label?: string;            // 图例名称（空则取变量模板名/键名）
  unit?: string;             // 单位（空则继承）
  color: string;             // 线条颜色（必填，默认调色板轮转）
  lineWidth: number;         // 线条粗细（px，默认 2）
  minValue?: number | null;  // 该序列量程下限（空→参与全局自适应）
  maxValue?: number | null;  // 该序列量程上限
  precision?: number | null; // 小数位数 (0~4)
  thresholdMin?: number | null; // 低限预警阈值（超限线条标黄）
  thresholdMax?: number | null; // 高限预警阈值（超限线条标红）
}

/** 导航菜单项（存于 HMIComponent.props.menuItems，随 PropsJson 落库） */
export interface HmiMenuItem {
  /** lucide 图标名（MENU_ICON_OPTIONS 内置集合中的 name） */
  icon: string;
  /** 显示文字 */
  text: string;
  /** 跳转目标页面 id（同端；null=未配置） */
  targetPageId: string | null;
}

export interface HMILayer {
  id: string;
  name: string;
  visible: boolean;
  locked: boolean;
  opacity?: number;
  colorBadge?: string;
}

// ===== 组件事件系统（事件属性面板）=====
// 事件配置整体存于 HMIComponent.props.events（PropsJson 透传落库，后端无需改动）。

/** 事件触发源类型 */
export type HmiEventType =
  | 'click'        // 单击（所有组件可用）
  | 'press'        // 按下（点动写1场景）
  | 'release'      // 松开（点动写0场景）
  | 'valueChange'  // 绑定变量值变化（需已绑定设备+变量，可配条件）
  | 'alarm';       // 绑定变量进入报警状态（需已绑定设备+变量）

/** 写变量动作的写入模式（与既有 buttonMode 写入指令链对齐） */
export type HmiEventWriteMode = 'toggle' | 'setBit' | 'resetBit' | 'setValue' | 'momentary';

/** 值变化条件运算符 */
export type HmiEventConditionOp = '>' | '<' | '=' | '>=' | '<=' | '!=';

export interface HmiEventCondition {
  op: HmiEventConditionOp;
  operand: number;
}

/** 动作类型 */
export type HmiEventActionKind =
  | 'writeVar'   // 写变量
  | 'navigate'   // 页面跳转
  | 'runScript'  // 执行系统脚本
  | 'setProp';   // 修改组件属性（运行态生效，不落库）

export interface HmiEventAction {
  id: string;
  kind: HmiEventActionKind;
  enabled: boolean;
  params: {
    /** writeVar：写入目标设备（null=沿用组件主绑定 bindDeviceId） */
    deviceId?: number | null;
    /** writeVar：写入变量键（空=沿用组件主绑定 bindVariableKey） */
    variableKey?: string;
    writeMode?: HmiEventWriteMode;
    /** setValue 模式写入值 */
    value?: number;
    /** navigate：目标画面 id（仅限同端） */
    targetPageId?: string;
    /** runScript：系统脚本 id */
    scriptId?: number;
    /** setProp：目标组件（空=自身） */
    targetComponentId?: string;
    /** setProp：运行态补丁（不落库） */
    patch?: {
      visible?: boolean;
      label?: string;
      props?: Record<string, any>;
    };
  };
}

/** 单个事件的完整配置（条件 + 有序动作链） */
export interface HmiEventConfig {
  type: HmiEventType;
  enabled: boolean;
  /** 仅 valueChange：值需满足条件才触发（null=任何变化都触发） */
  condition?: HmiEventCondition | null;
  /** 动作链：按顺序执行 */
  actions: HmiEventAction[];
}

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
  layerId?: string; // 归属的图层 ID（例如 'layer-default'）
  visible?: boolean;
  locked?: boolean;
  props: {
    activeColor?: string;
    inactiveColor?: string;
    showValue?: boolean;
    maxValue?: number;
    minValue?: number;   // 量程下限（百分比类/仪表类归一化基准）
    unit?: string;
    fillColor?: string;
    strokeColor?: string;
    thresholdMin?: number;
    thresholdMax?: number;
    fontSize?: number;
    align?: 'left' | 'center' | 'right';
    bold?: boolean;
    showLabel?: boolean;
    showBorder?: boolean;
    borderColor?: string;
    borderWidth?: number;
    borderStyle?: 'solid' | 'dashed' | 'dotted';
    borderRadius?: number; // 圆角弧度（px，如 4/8/12/20/999）
    showBackground?: boolean;
    bgColor?: string;
    showInnerLabel?: boolean;
    enableAlarmBorder?: boolean;

    // 按钮/开关专属属性
    buttonMode?: 'toggle' | 'momentary' | 'set-value' | 'set-bit' | 'reset-bit' | 'navigate' | 'run-script'; // 按钮操作模式（toggle=取反, momentary=按1送0/点动, set-bit=置位1, reset-bit=复位0, set-value=设值, navigate=跳转, run-script=执行系统脚本）
    clickValue?: number; // 设值模式下点击写入的具体数值
    buttonText?: string; // 按钮上静态/动态显示的文本
    targetPageId?: string | null; // 导航模式下跳转目标画面 id（仅限同端）
    targetScriptId?: number | null; // run-script 模式下点击触发执行的系统脚本 id
    showModeBadge?: boolean; // 是否显示模式角标文字（[取反]/[置位1]/[复位0]/[按1送0]/[设值:x]/[跳转]/[脚本]），默认 true
    opDeviceId?: number | null; // 操作变量绑定设备（写入目标；null=沿用主绑定 bindDeviceId）
    opVariableKey?: string | null; // 操作变量键（写入目标；空=沿用主绑定 bindVariableKey）
    presetStyle?: string; // 圆角按钮预设风格标记（start/stop/reset/jog/estop，仅用于回显当前预设）

    // 圆角按钮/自定义状态专属属性
    customStates?: string; // 自定义状态配置字典: "0:停止:#64748b:#ffffff;1:运行:#10b981:#ffffff;2:报警:#ef4444:#ffffff" 或 JSON 字符串
    state0Text?: string; // 状态0默认文本
    state0BgColor?: string; // 状态0默认背景色
    state0TextColor?: string; // 状态0默认文字颜色
    state1Text?: string; // 状态1默认文本
    state1BgColor?: string; // 状态1默认背景色
    state1TextColor?: string; // 状态1默认文字颜色

    // 有状态文本控件（开关/阀等）的状态文案
    onText?: string; // 开启状态文案
    offText?: string; // 关闭状态文案

    // 系统时间控件格式
    timeFormat?: 'HH:mm:ss' | 'YYYY-MM-DD HH:mm:ss' | 'YYYY-MM-DD';

    // 数据变量显示（var-display）专属属性
    decimals?: number;        // 显示/写入小数位数（0~4），写入前按位数四舍五入
    settable?: boolean;       // 是否可设定：运行态点击弹出数字键盘写入（需绑定变量且有写权限）
    writeMin?: number | null; // 写入范围下限（null/undefined=不限制）
    writeMax?: number | null; // 写入范围上限（null/undefined=不限制）
    confirmRequired?: boolean; // 确认写入前是否二次确认（高危变量防误写）

    // 大屏标题背景图元专属属性（type === 'title-header'）
    headerStyle?: 'pure-white' | 'titanium-light' | 'slate-dark' | 'navy-midnight' | 'translucent-frost' | 'tech-blue' | 'eco-green' | 'carbon-orange'; // 风格主题
    headerDevice?: 'desktop' | 'mobile'; // 适配设备：桌面大屏 / 手机移动端
    headerTitle?: string; // 主标题文本
    headerSubtitle?: string; // 英文副标题
    headerLogoText?: string; // 品牌/Logo 标识文字
    headerShowClock?: boolean; // 是否展示动态时钟
    headerShowStatus?: boolean; // 是否展示在线状态
    headerStatusText?: string; // 运行状态文案
    headerGlowColor?: string; // 自定义发光/主辅高亮色

    // 图片图元专属属性（type === 'image'）
    imageUrl?: string;   // 图片访问 URL（/api/HmiImage/file/... 相对路径，经代理转发）
    imageFit?: 'fill' | 'contain' | 'cover' | 'tile'; // 填充方式（默认 fill 拉伸）

    // 组件事件配置（事件属性面板；运行态优先于 buttonMode 旧逻辑）
    events?: HmiEventConfig[];
    // ===== nav-menu 导航菜单专属 props =====
    menuStyle?: 'pure-white' | 'titanium-light' | 'slate-dark' | 'navy-midnight' | 'translucent-frost' | 'tech-blue' | 'eco-green' | 'carbon-orange'; // 风格主题
    menuDevice?: 'desktop' | 'mobile';
    menuItems?: HmiMenuItem[];
    menuAccentColor?: string;
    menuFontSize?: number;

    // ===== multi-var-dashboard 实时多变量看板专属 props =====
    dashboardTitle?: string;            // 看板标题，如 "空压站监测总览"
    showDashboardTitle?: boolean;       // 是否显示标题栏
    dashboardTitleBgColor?: string;     // 标题栏背景色（留空则跟随主题）
    dashboardTitleColor?: string;       // 标题栏文字颜色
    dashboardLayout?: 'grid' | 'table' | 'compact'; // 布局模式：卡片网格 / 列表表格 / 紧凑标签
    dashboardColumns?: number;          // 列数：1, 2, 3, 4, 6, 或 0 (自适应 Auto-fit)
    dashboardGap?: number;              // 间距 (px，如 4, 8, 12, 16)
    dashboardItems?: HmiDashboardItem[];// 绑定的多变量列表
    dashboardShowItemBorder?: boolean;  // 子卡片/单元格是否显示边框
    dashboardItemBorderColor?: string;  // 子卡片边框颜色
    dashboardItemBgColor?: string;      // 子卡片背景底色
    dashboardValueFontSize?: number;    // 数值文字字号大小 (px)
    dashboardLabelFontSize?: number;    // 变量标签字号大小 (px)
    dashboardZebra?: boolean;           // 表格模式隔行交替底色 (斑马纹)
    dashboardTheme?: 'pure-white' | 'titanium-light' | 'slate-dark' | 'navy-midnight' | 'translucent-frost'; // 看板内置快速主题

    // ===== trend-chart 实时波段趋势图专属 props（多变量序列）=====
    trendSeries?: HmiTrendSeries[];     // 绑定的多变量序列（每条含 deviceId/variableKey/color/lineWidth 等）
    trendShowLegend?: boolean;         // 是否显示图例（色块+名称+当前值）
    trendLegendFontSize?: number;      // 图例字号 (px，默认 9)
    trendUseGlobalRange?: boolean;     // 多序列是否共用同一 Y 轴量程（默认 true，便于对比）

    // ===== trend-chart 坐标轴 / 刻度 / 显示增强 props =====
    trendAxisMode?: 'absolute' | 'relative'; // Y 轴坐标模式：绝对工程量值 / 相对量程百分比 0-100%
    trendAxisMin?: number | null;            // 手动 Y 轴下限（与 trendAxisMax 同时有效时固定范围，优先于自适应）
    trendAxisMax?: number | null;            // 手动 Y 轴上限
    trendShowGrid?: boolean;                 // 是否显示网格线（默认 true）
    trendShowAxisLabels?: boolean;           // 是否显示坐标轴刻度数值（默认 true）
    trendAxisLabelFontSize?: number;         // 刻度数值字号 (px，默认 8)
    trendShowPointValues?: boolean;          // 是否在图形上显示每个点位数值（默认 false）
    trendPointValueFontSize?: number;        // 点位数值字号 (px，默认 8)
    trendPointValueColor?: string;           // 点位数值颜色（'auto'=取序列色，否则颜色字符串，默认 'auto'）
    trendPointValueEveryN?: number | null;   // 仅每 N 个点显示（null=自动抽稀）
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
  parentId?: number | null;
  name: string;
  code?: string | null;
  /** 区域类型（AreaTypeEnum：Factory=1/Workshop=2/ProductionLine=3/Area=4/Warehouse=5） */
  areaType?: number;
  description?: string;
  sort?: number;
  isEnabled?: boolean;
  createdAt?: string;
  updatedAt?: string;
}

/** 区域树节点（GET /api/Area/tree 返回）：含直接挂载设备数与子节点。 */
export interface AreaTreeNode extends Area {
  id: number;
  deviceCount: number;
  children: AreaTreeNode[];
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

// 读写访问模式（阶段 4 权威；与旧 bool IsReadOnly 并存，一个版本周期后移除后者）
export type AccessMode = 'Read' | 'Write' | 'ReadWrite';

export interface DataPoint {
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
  /** 工程换算表达式（raw→eng），以 x 代表原始值；空/undefined = 恒等变换。例：'x*0.1'、'(x-4000)/160' */
  scaleExpression?: string | null;
  deadBand?: number;

  // ===== 阶段 4 数据定义层增强（AccessMode 权威，IsReadOnly 保留兼容）=====
  /** 读写访问模式（阶段 4 权威，与后端 DataPointDto.AccessMode 对齐）。 */
  accessMode?: AccessMode;
  /** 是否必采/必填（模板级定义），默认 false */
  isRequired?: boolean;
  /** 排序权重（值越小越靠前），默认 0；存量回填 = 行号 */
  sort?: number;
  /** 模板级是否启用，默认 true */
  isEnabled?: boolean;
  /**
   * @deprecated 阶段 4 起由 accessMode 取代（语义：只读 == accessMode==='Read'）。
   * 保留以兼容旧后端/旧 store 缓存；读写权限的展示与编辑请使用 accessMode。
   */
  isReadOnly: boolean;
  extensionData?: Record<string, string>;
}

/**
 * 设备变量实例：变量模板（DataPoint）在某台具体设备上的落地配置。
 * 对应后端 DataPointMappingDto（ScadaServer.Application.DTOs.DataPointMappingDto）。
 * "是什么"（Key/Name/DataType/Unit）来自模板；"怎么采集"（Address/BitOffset/PollingInterval/覆盖值）为实例级。
 * 创建（POST）仅需 deviceId + dataPointId + isEnabled，地址等需后续 PUT 补全。
 */
export interface DataPointMapping {
  id: number;
  deviceId: number;
  dataPointId: number;
  key: string;               // 来自模板 DataPoint.Key
  name: string;              // 来自模板 DataPoint.Name
  dataType: DataTypeEnum;    // 来自模板 DataPoint.DataType
  unit?: string;             // 来自模板 DataPoint.Unit
  address?: string | null;           // 实例级：实际寄存器地址（展示串，由后端从 addressConfigJson 自动生成，只读）
  addressConfigJson?: string | null; // 实例级：结构化地址 JSON（权威机读形态），前端仅编辑本字段
  bitOffset?: number | null;         // 实例级：位偏移（BOOL/BIT 用）
  pollingIntervalMs?: number | null; // 实例级：轮询间隔，空=运行时默认 1000ms
  isEnabled: boolean;                // 实例级：是否启用采集
  scaleExpressionOverride?: string | null; // 实例级覆盖：换算表达式，空=用模板值
  deadBandOverride?: number | null;     // 实例级覆盖：死区，空=用模板值
  isReadOnlyOverride?: boolean | null;  // 实例级覆盖：读写权限，空=继承模板（阶段 4 语义保持：Override ?? 模板）
  // ===== 阶段 4 数据定义层增强（ConnectionId/RawDataType 本阶段仅透传，运行时阶段 6 启用）=====
  /** 实例级连接覆盖（FK→DeviceConnections.Id），空=使用设备默认连接 */
  connectionId?: number | null;
  /** 记录性字段：创建实例时快照的模板 DataType 字符串（如 "REAL"），本阶段不启用校验 */
  rawDataType?: string | null;
  /**
   * @deprecated 阶段 4 由 templateAccessMode 取代（== templateAccessMode === 'Read'）。
   */
  templateIsReadOnly?: boolean;         // 回显：模板定义的只读权限
  /**
   * @deprecated 阶段 4 由 effectiveAccessMode 取代（== effectiveAccessMode === 'Read'，运行时实际生效值）。
   */
  effectiveIsReadOnly?: boolean;        // 回显：有效权限（Override ?? 模板）
  /** 回显：模板定义的访问模式（只出不进） */
  templateAccessMode?: AccessMode;
  /** 回显：有效访问模式 = 实例覆盖 ?? 模板（只出不进） */
  effectiveAccessMode?: AccessMode;
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
 * 控制器（Controller/PLC 资产台账，阶段 2）。
 * 承接目标设计中"物理控制硬件资产"（S7-1500 PLC、Kepware 服务器等）；
 * 当前阶段仅资产登记，不产生任何采集行为，运行连接配置在后续阶段接入。
 */
export interface Controller {
  id: number;
  code: string;              // 控制器编码（全局唯一）
  name: string;
  protocolId: number;        // 控制器类型/所用协议（S7、OPCUA...）
  protocolName: string;      // 派生展示字段
  manufacturer?: string;
  model?: string;
  description?: string;
  isEnabled: boolean;
  connectionCount?: number;  // 阶段 3：该控制器下的连接数（后端填充）
  createdAt: string;
  updatedAt: string;
}

/** 控制器创建/更新请求体（与后端 CreateControllerDto 对齐，PascalCase）。 */
export interface ControllerRequest {
  Code: string;
  Name: string;
  ProtocolId: number;
  Manufacturer?: string;
  Model?: string;
  Description?: string;
  IsEnabled?: boolean;
}

/** 控制器分页查询参数。 */
export interface ControllerQueryParams {
  protocolId?: number;
  keyword?: string;
  pageIndex?: number;
  pageSize?: number;
}

/** 控制器分页结果（后端 camelCase 序列化：total/items）。 */
export interface ControllerPagedResult {
  total: number;
  items: Controller[];
}

/** 控制器下拉选项（GET /api/controllers/options）。 */
export interface ControllerOption {
  id: number;
  code: string;
  name: string;
  protocolId: number;
  protocolName: string;
}

/**
 * 设备连接（阶段 3：连接参数抽取实体，DeviceConnectionDto）。
 * ConfigJson 为驱动完整配置原文（P3-B 真相源），Host/Port 为服务端按协议提取的冗余展示列。
 */
export interface DeviceConnection {
  id: number;
  controllerId: number;
  controllerCode?: string;     // 派生展示字段
  controllerName?: string;     // 派生展示字段
  name: string;
  protocolId: number;
  protocolName?: string;       // 派生展示字段
  configJson?: string | null;  // 驱动完整配置原文
  host?: string | null;        // 冗余展示列
  port?: number | null;        // 冗余展示列
  timeoutMs: number;
  reconnectIntervalMs: number;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
}

/** 设备连接创建/更新请求体（与后端 CreateDeviceConnectionDto 对齐，PascalCase）。 */
export interface DeviceConnectionRequest {
  ControllerId: number;
  Name: string;
  ProtocolId: number;
  ConfigJson?: string | null;
  Host?: string | null;
  Port?: number | null;
  TimeoutMs?: number;
  ReconnectIntervalMs?: number;
  IsEnabled?: boolean;
}

/** 设备详情中的连接摘要（DeviceDto.Connection，阶段 3，只读展示）。 */
export interface DeviceConnectionSummary {
  id: number;
  controllerId: number;
  controllerCode?: string | null;
  controllerName?: string | null;
  protocolId: number;
  protocolKey?: string | null;
  protocolName?: string | null;
  host?: string | null;
  port?: number | null;
  timeoutMs: number;
  reconnectIntervalMs: number;
  isEnabled: boolean;
  updatedAt: string;
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
 * - addressFields = 结构化地址表单字段（S7/OPC UA/Modbus 各异）
 */
export interface ProtocolFieldConfig {
  addressLabel?: string;
  addressPlaceholder?: string;
  addressRequired?: boolean;
  needsBitOffset?: boolean;
  addressFields: StructuredAddressField[];
}

/** 结构化地址表单字段描述：前端据此按协议渲染输入控件，后端据此生成展示串。 */
export interface StructuredAddressField {
  key: keyof AddressConfig;
  label: string;
  type: 'text' | 'number' | 'select';
  placeholder?: string;
  options?: { value: string | number; label: string }[];
  min?: number;
  max?: number;
  required?: boolean;
  /** 值合法判定（如 S7 位偏移需 0~7）；返回错误文案或空串表示合法。 */
  validate?: (cfg: AddressConfig) => string;
}

/** 结构化地址配置（映射后端 AddressConfig，camelCase 键）。JSON 为地址唯一权威身份。 */
export interface AddressConfig {
  protocol: string;      // S7 / OPCUA / Modbus / Virtual
  area?: string;         // S7: DB / I / Q / M
  dbNumber?: number;     // S7: DB 号（DB 区域）
  byteOffset?: number;   // S7: 字节偏移
  bitOffset?: number;    // S7: 位偏移（-1 = 非位地址）
  width?: string;        // S7: BIT / BYTE / WORD / DWORD
  nodeId?: string;       // OPC UA: 节点标识
  function?: number;     // Modbus: 功能码
  startAddress?: number; // Modbus: 起始地址
  registerCount?: number;// Modbus: 寄存器数量
  bitIndex?: number;     // Modbus: 位索引（-1 = 非位）
}

/** 按协议返回一个空的地址配置骨架（填充协议判别与默认值）。 */
export const newAddressConfig = (protocol: string): AddressConfig => {
  const proto = (protocol || '').toUpperCase();
  switch (proto) {
    case 'S7':
      return { protocol: 'S7', area: 'DB', dbNumber: 1, byteOffset: 0, bitOffset: -1, width: 'WORD' };
    case 'OPCUA':
      return { protocol: 'OPCUA', nodeId: '' };
    case 'MODBUSTCP':
      return { protocol: 'Modbus', function: 3, startAddress: 0, registerCount: 1, bitIndex: -1 };
    default:
      return { protocol: proto || 'Virtual' };
  }
};

/** 将后端返回的 JSON 串解析为地址配置对象；空/非法返回 null。 */
export const parseAddressConfig = (json?: string | null): AddressConfig | null => {
  if (!json) return null;
  try {
    const obj = JSON.parse(json);
    if (!obj || typeof obj !== 'object' || !obj.protocol) return null;
    return obj as AddressConfig;
  } catch {
    return null;
  }
};

/** 将地址配置对象序列化为 JSON 串（提交后端用）。 */
export const stringifyAddressConfig = (cfg?: AddressConfig | null): string | null => {
  if (!cfg) return null;
  try {
    return JSON.stringify(cfg);
  } catch {
    return null;
  }
};

/**
 * 本地生成地址展示串（仅作编辑弹窗预览；最终展示串由后端权威生成）。
 * 与后端 AddressConfigSerializer.ToDisplay 对齐。
 */
export const buildAddressDisplay = (cfg?: AddressConfig | null): string => {
  if (!cfg) return '';
  const proto = (cfg.protocol || '').toUpperCase();
  if (proto === 'S7') return buildS7Display(cfg);
  if (proto === 'OPCUA') return cfg.nodeId?.trim() || '';
  if (proto === 'MODBUS') return cfg.startAddress != null ? String(cfg.startAddress) : '';
  return '';
};

const buildS7Display = (c: AddressConfig): string => {
  const area = (c.area || '').toUpperCase();
  const width = (c.width || '').toUpperCase();
  if (c.byteOffset == null || c.byteOffset < 0) return '';
  const isBit = c.bitOffset != null && c.bitOffset >= 0 && c.bitOffset <= 7 && width === 'BIT';
  if (isBit) {
    if (area === 'DB') return `DB${c.dbNumber}.DBX${c.byteOffset}.${c.bitOffset}`;
    if (area === 'I' || area === 'Q' || area === 'M') return `${area}${c.byteOffset}.${c.bitOffset}`;
    return '';
  }
  if (area === 'DB') return `DB${c.dbNumber}.DB${s7Suffix(width)}${c.byteOffset}`;
  if (area === 'I' || area === 'Q' || area === 'M') return `${area}${s7Prefix(area, width)}${c.byteOffset}`;
  return '';
};
const s7Suffix = (w: string) => (w === 'BYTE' ? 'B' : w === 'WORD' ? 'W' : w === 'DWORD' ? 'D' : 'B');
const s7Prefix = (area: string, w: string) =>
  area === 'I' ? (w === 'WORD' ? 'W' : w === 'DWORD' ? 'D' : 'B')
  : area === 'Q' ? (w === 'WORD' ? 'W' : w === 'DWORD' ? 'D' : 'B')
  : (w === 'WORD' ? 'W' : w === 'DWORD' ? 'D' : 'B');

/** 协议字段配置表：新增协议只需在此补一行，页面自动适配 */
export const PROTOCOL_FIELD_CONFIG: Record<DeviceType, ProtocolFieldConfig> = {
  S7: {
    addressLabel: '寄存器地址', addressPlaceholder: '如 DB1.DBD4 / DB1.DBX0.0', addressRequired: true, needsBitOffset: true,
    addressFields: [
      { key: 'area', label: '区域', type: 'select', options: [{ value: 'DB', label: 'DB 数据块' }, { value: 'I', label: 'I 输入' }, { value: 'Q', label: 'Q 输出' }, { value: 'M', label: 'M 存储' }], required: true },
      { key: 'dbNumber', label: 'DB 号', type: 'number', min: 1, placeholder: '区域为 DB 时必填' },
      { key: 'byteOffset', label: '字节偏移', type: 'number', min: 0, required: true },
      { key: 'width', label: '访问宽度', type: 'select', options: [{ value: 'BIT', label: 'BIT 位' }, { value: 'BYTE', label: 'BYTE 字节' }, { value: 'WORD', label: 'WORD 字' }, { value: 'DWORD', label: 'DWORD 双字' }], required: true },
      { key: 'bitOffset', label: '位偏移', type: 'number', min: -1, max: 7, placeholder: '宽度 BIT 时填 0~7，否则填 -1', validate: (cfg) => (cfg.width === 'BIT' && (cfg.bitOffset == null || cfg.bitOffset < 0 || cfg.bitOffset > 7)) ? '位地址需在 0~7 之间' : '' }
    ]
  },
  OPCUA: {
    addressLabel: '节点ID', addressPlaceholder: '如 ns=2;i=5', addressRequired: true, needsBitOffset: false,
    addressFields: [
      { key: 'nodeId', label: '节点ID', type: 'text', placeholder: '如 ns=2;i=5', required: true }
    ]
  },
  ModbusTcp: {
    addressLabel: '寄存器地址', addressPlaceholder: '如 40001', addressRequired: true, needsBitOffset: true,
    addressFields: [
      { key: 'function', label: '功能码', type: 'select', options: [{ value: 3, label: '03 读保持寄存器' }, { value: 4, label: '04 读输入寄存器' }, { value: 6, label: '06 写单个寄存器' }, { value: 16, label: '16 写多个寄存器' }], required: true },
      { key: 'startAddress', label: '起始地址', type: 'number', min: 0, required: true },
      { key: 'registerCount', label: '寄存器数量', type: 'number', min: 1 },
      { key: 'bitIndex', label: '位索引', type: 'number', min: -1, placeholder: '位访问时填 0~15，否则 -1' }
    ]
  },
  MQTT: {
    addressLabel: 'Topic/路径', addressPlaceholder: '如 plant1/pump/level', addressRequired: true, needsBitOffset: false,
    addressFields: []
  },
  BACnet: {
    addressLabel: '对象地址', addressPlaceholder: '如 AV:1', addressRequired: true, needsBitOffset: false,
    addressFields: []
  },
  DNP3: {
    addressLabel: '点表索引', addressPlaceholder: '如 2-3', addressRequired: true, needsBitOffset: false,
    addressFields: []
  },
  Virtual: { addressFields: [] }
};

export interface DataModel {
  id: string;
  name: string;
  // ===== 阶段 4 数据定义层增强 =====
  /** 模型编码（业务唯一键，后端 DataModel.Code 全局唯一；存量回填自 Name 重名加 -2/-3 后缀） */
  code?: string;
  /** 模型版本号，默认 "1.0" */
  version?: string;
  /** 是否已发布（标识模型是否可被新建设备引用），默认 true */
  isPublished?: boolean;
  description: string;
  // 协议绑定（协议真相源）：对应后端 DataModelDto.ProtocolId / ProtocolKey / ProtocolName。
  // 协议真相源在独立的 Protocol 实体，不再有过渡字段 Type；
  // 协议类型由 protocolKey 经 protocolKeyToDeviceType() 派生。
  // ProtocolId 必填：创建模型时必须选择协议；更新时必须原样回传，避免后端 PUT 全量替换语义解绑协议。
  protocolId: number;
  protocolKey?: string;
  protocolName?: string;
  variables: DataPoint[];
}

/**
 * 设备-数据模型绑定摘要（阶段 5，对应后端 DeviceModelBindingDto）。
 * 描述设备与数据模型的多对多绑定关系：一台设备至多一条 IsPrimary=true（主模型，
 * 与后端 Device.ModelId 严格一致），其余为附加模型（仅供管理，运行时暂不参与采集）。
 */
export interface DeviceModelBinding {
  /** 绑定行 ID（主键，管理用）。 */
  id: number;
  /** 绑定行的设备 ID。 */
  deviceId: number;
  /** 所绑定数据模型 ID。 */
  dataModelId: number;
  /** 模型编码（只读，来自 DataModel.Code）。 */
  code?: string;
  /** 模型名称（只读，来自 DataModel.Name）。 */
  name?: string;
  /** 绑定版本快照（绑定时刻的模型版本）。 */
  version: string;
  /** 是否主模型（主模型行与 Device.ModelId 严格一致）。 */
  isPrimary: boolean;
  /** 绑定是否启用（MVP 预留）。 */
  isEnabled: boolean;
  /** 该模型的模型变量数（绑定列表接口填充；设备详情列表为 0）。 */
  variableCount: number;
  /** 创建时间。 */
  createdAt: string;
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
  // 阶段 5：该设备全部数据模型绑定（后端 DeviceDto.Models；含主模型行，与 device.modelId 一致）。
  // normalizeDevices 保留该数组，供设备变量视图顶栏展示主模型 Code/Version 等只读信息。
  models?: DeviceModelBinding[];
  // 阶段 3：连接/控制器关联（只读，后端 DeviceDto 填充；快速模式由后端自动维护，高级模式显式附加）。
  controllerId?: number | null;
  connectionId?: number | null;
  // 连接摘要（DeviceDto.Connection）：host/port/协议/控制器/启用等；null = 设备尚未关联连接。
  connection?: DeviceConnectionSummary | null;
  protocolKey?: string; // 后端 DeviceDto 直接携带的协议标识（S7/OPCUA/ModbusTcp...），归一化据此推导 type
  type: DeviceType;   // 派生只读：由 modelId 反查 dataModels → protocolKey 得到，设备本身不再存储协议
  /**
   * 以下连接参数均为后端派生只读字段：由 DeviceDto 从唯一真相源 `Connection.ConfigJson`
   * 按协议投影而来（阶段 6 起后端 Device 详情不再输出 configJson 原文；原文经连接 API 读取）。
   * 提交创建/更新时不要发送这些字段，后端会忽略它们——写入一律走 configJson（快速模式）。
   * 配置缺失或 JSON 非法时后端返回 null，前端应显示「未配置」而不是编造默认值。
   */
  ipAddress?: string | null; // S7/ModbusTcp 主机；OPCUA 由 endpointUrl 解析出的 host
  port?: number | string | null;      // Port, e.g. 502, 102, 1883
  isEnabled: boolean;          // 后端 DeviceDto.IsEnabled：是否启用采集（用于卡片启停开关状态）
  status: number | string;     // 0: offline, 1: online or 'online' | 'offline'
  runtimeStatus?: string;       // 后端运行时状态枚举名: Online | Offline | Fault | Connecting
  lastUpdated: string; // ISO 8601 datetime string
  variables?: Record<string, any>;
  variableTimestamps?: Record<string, string>;
  // 设备实例级变量元数据（按 key 索引，来源后端 DeviceDto.Variables 数组）。
  // normalizeDevices 把后端数组压扁成 variables 键值表时，同步保留这里以便消费方
  // 取 effectiveIsReadOnly / isReadOnlyOverride / templateIsReadOnly 等实例级权限，
  // 实时监控页据此判断写入按钮显隐（设备级覆盖优先于模板 isReadOnly）。
  variableMeta?: Record<string, DataPointMapping>;

  // Advanced connection parameters（同为后端派生只读，见上方说明）
  cpuType?: string | null;  // S7 CPU类型 (e.g. S7-1200, S7-1500, S7-300, S7-400)
  rack?: number | null;     // S7 机架号
  slot?: number | null;     // S7 插槽号
  endpointUrl?: string | null; // OPCUA 端点完整 URL（可能带路径），编辑必须原样回填，勿用 ip+port 拼接
  unitId?: number | null;   // ModbusTcp 从站单元地址（预留）
  broker?: string | null;   // MQTT Broker 地址（预留）
  intervalMs?: number | null;   // Virtual 值更新间隔 ms（预留）
  randomValues?: boolean | null;// Virtual 是否随机产生数值（预留）
  topic?: string;
  mqttServer?: string;
  publishTopic?: string;
  subscribeTopic?: string;
  payloadTemplate?: string;
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
  /** 页面图层管理列表（PS 式多图层系统，支持图层显隐、锁定、透明度与拖拽排序） */
  layers?: HMILayer[];
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

/** 定时任务（后端 ScheduledTaskDto 镜像）。 */
export interface ScheduledTask {
  id: number;
  name: string;
  type: 'set_value' | 'backup' | 'execute_script' | 'clear_history';
  /** Cron 表达式：5 段分钟级或 6 段秒级（如 "0 2 * * *"，秒级首段支持步进如每 5 秒） */
  cronExpression: string;
  /** 任务参数 JSON 字符串：{ deviceId?, variableKey?, newValue?, scriptId?, retentionDays? } */
  paramsJson: string;
  active: boolean;
  /** 最近一次执行开始时间（UTC ISO，前端本地化展示） */
  lastRunAt?: string | null;
  /** 最近一次执行状态：Idle / Running / Success / Failed / Skipped */
  lastStatus?: string | null;
  /** 最近一次执行错误信息 */
  lastError?: string | null;
  /** 最近一次执行耗时（毫秒） */
  lastDurationMs?: number | null;
  /** 下次计划触发时间（UTC ISO，前端本地化展示） */
  nextRunAt?: string | null;
}

/** 定时任务手动执行结果（POST /api/ScheduledTask/{id}/execute 返回）。 */
export interface ScheduledTaskRunResult {
  taskId: number;
  status: 'Success' | 'Failed' | 'Skipped';
  output?: string | null;
  error?: string | null;
  durationMs: number;
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
  id: number;
  name: string;
  exposedKey: string;
  deviceId: number;
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
  /** 采样质量位（Good/Bad/Uncertain/CommunicationError/…），历史查询图表按质量分级展示 */
  quality?: string;
}

/** 历史查询页面可选变量项（设备→变量 级联动态数据源） */
export interface HistoryVariableOption {
  deviceId: number;
  deviceKey: string;
  deviceName: string;
  variableKey: string;
  variableName: string;
  unit?: string;
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

// ===== 模型变量导入/导出（对应后端 VariableTransferDto）=====

/** 冲突处理策略：Skip(跳过)/ Overwrite(覆盖更新)/ Abort(存在冲突即失败) */
export type ConflictStrategy = 'Skip' | 'Overwrite' | 'Abort';

/** 单行导入解析结果（预览展示用），与后端 VariableImportRow 对齐 */
export interface VariableImportRow {
  rowNumber: number;
  key: string;
  name: string;
  dataTypeRaw?: string | null;
  dataType: DataTypeEnum;
  isApproxType?: boolean;
  address?: string | null;
  description?: string | null;
  path?: string | null;
  hasError: boolean;
  errorReason?: string | null;
  isConflict: boolean;
  // CSV 增强字段（可选）
  unit?: string | null;
  min?: number | null;
  max?: number | null;
  storeMode?: 'None' | 'Change' | 'Cycle' | 'Compressed' | 'Aggregated' | null;
  storeIntervalMs?: number | null;
  updateMode?: UpdateMode | null;
  scaleExpression?: string | null;
  deadBand?: number | null;
  /** @deprecated 阶段 4 由 accessMode 取代（== accessMode === 'Read'）。 */
  isReadOnly?: boolean | null;
  /** 读写访问模式（Read/Write/ReadWrite）；非法/缺省时后端忽略（旧客户端仅传 IsReadOnly 亦可） */
  accessMode?: AccessMode | null;
  /** 是否必采 */
  isRequired?: boolean | null;
  /** 排序权重 */
  sort?: number | null;
  /** 是否启用 */
  isEnabled?: boolean | null;
}

/** 导入预览结果（POST /api/DataPoint/import/preview 返回） */
export interface VariableImportPreview {
  modelId: number;
  totalRows: number;
  validRows: number;
  errorRows: number;
  conflictRows: number;
  rows: VariableImportRow[];
}

/** 导入结果（POST /api/DataPoint/import 返回） */
export interface VariableImportResult {
  inserted: number;
  updated: number;
  skipped: number;
  failed: number;
  failedRows: VariableImportRow[];
}

