/**
 * 本地兜底种子：24 条内置组件（与后端种子一一对应，键名必须一致）。
 * 用途：① 首帧 / 后端不可用时组件库兜底（审查 B8） ② defDefaults 兜底。
 * 内容 = 原 widgetRegistry 全部注册项原样迁移（defaultProps 逻辑不变）。
 * 运行时模板源在 widgetTemplates.ts；组件库列表 = store ∪ 本兜底种子。
 */
import type { WidgetCategory, WidgetDef } from './widgetTemplates';
import { getLucideIcon } from './lucideIcons';
import { BUILTIN_SCHEMAS } from './propSchemas';
import { HmiMenuItem } from './types';
import {
  // nav-menu 内置图标集（Inspector 选择器与 HMIWidget 渲染共用此映射）
  Home, Factory, Bell, LineChart, Settings, Database, Zap, Wrench, ShieldAlert,
  FileText, Users, Camera, Server, Boxes, GaugeCircle, Route, ClipboardList,
  Activity,
} from 'lucide-vue-next';

/**
 * baseProps：从原 widgetRegistry.ts 原样迁入（逻辑不变，含 type 分支）。
 * 单一真相源：InspectorPanel 回显与 HMIWidget 运行时兜底均以此为准。
 * 所有字段显式写全，不做隐式缺省，避免「面板显示值 ≠ 运行生效值」。
 */
const baseProps = (type: string): Record<string, any> => ({
  activeColor: type === 'valve' || type === 'led' ? '#10b981' : '#3b82f6',
  inactiveColor: '#94a3b8',
  maxValue: type === 'gauge-dial' ? 120 : 100,
  minValue: 0, // 量程下限（百分比类/仪表类归一化基准）
  unit: type === 'gauge-dial' ? '℃' : '',
  showValue: false,
  showLabel: false, // 外框浮签标签名称默认隐藏，需显示时在属性面板单独开启
  fontSize: 12,
  bold: false,
  align: 'center',
  thresholdMax: 90, // 与面板回显一致
  thresholdMin: 10, // 与面板回显一致
  // 标题背景默认属性
  ...(type === 'title-header'
    ? {
      headerStyle: 'navy-midnight',
      headerDevice: 'desktop',
      headerTitle: '工业互联网智能监控大屏',
      headerSubtitle: 'INTELLIGENT SCADA MONITORING PLATFORM',
      headerLogoText: 'SCADA 5G',
      headerShowClock: true,
      headerShowStatus: true,
      headerStatusText: '系统运行正常',
      headerGlowColor: '#38bdf8',
      fontSize: 22,
      bold: true,
    }
    : {}),
});

/** 趋势图「从设备导入全部变量」时序列线条颜色轮转调色板 */
export const TREND_SERIES_PALETTE: string[] = [
  '#10b981', // emerald
  '#3b82f6', // blue
  '#f59e0b', // amber
  '#ef4444', // red
  '#a855f7', // purple
  '#14b8a6', // teal
  '#ec4899', // pink
  '#84cc16', // lime
];

/** 本地兜底种子：24 条，内容 = 原 widgetRegistry 全部注册项原样迁移（审查 A5 key 唯一）。 */
export const BUILTIN_SEEDS: WidgetDef[] = [
  {
    key: 'boiler', type: 'boiler', name: '加热锅炉反应釜',
    defaultWidth: 140, defaultHeight: 180,
    icon: getLucideIcon('battery-charging'), iconKind: 'lucide', iconColor: 'text-amber-500',
    description: '工业超温蒸汽燃煤锅炉，带火焰动态变频效果。',
    category: 'equipment', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 10,
    defaultProps: () => baseProps('boiler'),
  },
  {
    key: 'pump', type: 'pump', name: '离心输送水泵',
    defaultWidth: 70, defaultHeight: 70,
    icon: getLucideIcon('cpu'), iconKind: 'lucide', iconColor: 'text-emerald-500',
    description: '液体或气体加压叶轮主输水泵，运行自带叶片旋转效果。',
    category: 'equipment', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 20,
    defaultProps: () => baseProps('pump'),
  },
  {
    key: 'valve', type: 'valve', name: '智能两位电磁阀',
    defaultWidth: 60, defaultHeight: 60,
    icon: getLucideIcon('toggle-left'), iconKind: 'lucide', iconColor: 'text-indigo-500',
    description: '蝶阀/电磁球阀，状态切换时蝶阀手轮旋转90°。',
    category: 'equipment', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 30,
    defaultProps: () => baseProps('valve'),
  },
  {
    key: 'tank', type: 'tank', name: '圆角储液容器罐',
    defaultWidth: 120, defaultHeight: 160,
    icon: getLucideIcon('layers'), iconKind: 'lucide', iconColor: 'text-sky-500',
    description: '带刻度及气泡波纹的液体深度容器。',
    category: 'equipment', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 40,
    defaultProps: () => baseProps('tank'),
  },
  {
    key: 'conveyor', type: 'conveyor', name: '变频滚轮传送带',
    defaultWidth: 260, defaultHeight: 40,
    icon: getLucideIcon('workflow'), iconKind: 'lucide', iconColor: 'text-orange-500',
    description: '物料或箱体传动物件传送带，速度非零时展现位移动画。',
    category: 'equipment', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 50,
    defaultProps: () => baseProps('conveyor'),
  },
  {
    key: 'motor', type: 'motor', name: '变频伺服AC电机',
    defaultWidth: 120, defaultHeight: 90,
    icon: getLucideIcon('refresh-cw'), iconKind: 'lucide', iconColor: 'text-sky-500',
    description: '变频配给驱动电机，工作时伴随冷却风扇叶极速旋转效果。',
    category: 'equipment', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 60,
    defaultProps: () => baseProps('motor'),
  },
  {
    key: 'gauge-dial', type: 'gauge-dial', name: '高精度机械表盘',
    defaultWidth: 120, defaultHeight: 120,
    icon: getLucideIcon('gauge'), iconKind: 'lucide', iconColor: 'text-purple-500',
    description: '圆形度盘表，支持设置极限阈值并同步变红警告。',
    category: 'sensors', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 70,
    defaultProps: () => baseProps('gauge-dial'),
  },
  {
    key: 'gauge-level', type: 'gauge-level', name: '液位刻度警告柱',
    defaultWidth: 50, defaultHeight: 140,
    icon: getLucideIcon('thermometer'), iconKind: 'lucide', iconColor: 'text-rose-500',
    description: '带有高、中、低限阈值的段式刻度检测条。',
    category: 'sensors', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 80,
    defaultProps: () => baseProps('gauge-level'),
  },
  {
    key: 'digital-val', type: 'digital-val', name: '多功能数显仪表',
    defaultWidth: 130, defaultHeight: 60,
    icon: getLucideIcon('tv'), iconKind: 'lucide', iconColor: 'text-cyan-500',
    description: '工业LED高亮七段数值显示面板，可绑定任意PLC点。',
    category: 'sensors', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 90,
    defaultProps: () => baseProps('digital-val'),
  },
  {
    key: 'var-display', type: 'var-display', name: '数据变量显示框',
    defaultWidth: 150, defaultHeight: 70,
    icon: getLucideIcon('hash'), iconKind: 'lucide', iconColor: 'text-lime-500',
    description: '大字号数值显示，可设小数位与阈值变色；开启「可设定」后点击弹出数字键盘写入变量。',
    category: 'sensors', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 100,
    defaultProps: () => ({
      ...baseProps('var-display'),
      decimals: 2,
      settable: false,
      writeMin: null,
      writeMax: null,
      confirmRequired: false,
      // 外观显隐与边框样式：默认边框关闭，支持自由自定义
      showBorder: false,
      borderColor: '#cbd5e1',
      borderWidth: 1.5,
      borderStyle: 'solid',
      borderRadius: 8,
      showBackground: false,
      bgColor: '#ffffff',
      showInnerLabel: false,
      enableAlarmBorder: true,
      thresholdMin: null,
      thresholdMax: null,
    }),
  },
  {
    key: 'multi-var-dashboard', type: 'multi-var-dashboard', name: '实时多变量看板',
    defaultWidth: 360, defaultHeight: 240,
    icon: getLucideIcon('layout-dashboard'), iconKind: 'lucide', iconColor: 'text-sky-500',
    description: '多变量实时聚合看板：支持多变量绑定、列数调节(1~6列/自适应)、边框与底色、阈值报警指示及卡片/表格/紧凑三种排版。',
    category: 'sensors', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 110,
    defaultProps: () => ({
      ...baseProps('multi-var-dashboard'),
      dashboardTitle: '实时参数监控看板',
      showDashboardTitle: true,
      dashboardTitleBgColor: '',
      dashboardTitleColor: '',
      dashboardLayout: 'grid', // 'grid' | 'table' | 'compact'
      dashboardColumns: 2,    // 1, 2, 3, 4, 6, 0 (0 为自适应)
      dashboardGap: 8,
      showBorder: true,
      borderColor: '#cbd5e1',
      borderWidth: 1.5,
      borderStyle: 'solid',
      borderRadius: 8,
      showBackground: true,
      bgColor: '#ffffff',
      dashboardShowItemBorder: true,
      dashboardItemBorderColor: '#e2e8f0',
      dashboardItemBgColor: '#f8fafc',
      dashboardValueFontSize: 16,
      dashboardLabelFontSize: 11,
      dashboardZebra: false,
      dashboardTheme: 'pure-white',
      dashboardItems: [
        { id: 'item-1', variableKey: 'boiler_temp', label: '锅炉温度', unit: '℃', precision: 1, showStatusDot: true, thresholdMin: 20, thresholdMax: 90 },
        { id: 'item-2', variableKey: 'boiler_press', label: '主管道压力', unit: 'MPa', precision: 2, showStatusDot: true, thresholdMin: null, thresholdMax: 8.5 },
        { id: 'item-3', variableKey: 'tank_level', label: '储罐液位', unit: '%', precision: 1, showStatusDot: true, thresholdMin: 15, thresholdMax: 95 },
        { id: 'item-4', variableKey: 'pump_state', label: '主循环泵', unit: '', precision: null, showStatusDot: true, thresholdMin: null, thresholdMax: null },
      ],
    }),
  },
  {
    key: 'trend-chart', type: 'trend-chart', name: '实时波段趋势图',
    defaultWidth: 280, defaultHeight: 160,
    icon: getLucideIcon('activity'), iconKind: 'lucide', iconColor: 'text-red-500',
    description: '动态微积分平滑滤波趋势图，记录历史PLC模拟参数，支持多变量序列与逐线颜色/粗细自定义。',
    category: 'sensors', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 120,
    defaultProps: () => ({ ...baseProps('trend-chart'), trendSeries: [], trendShowLegend: true, trendLegendFontSize: 9, trendUseGlobalRange: true, trendAxisMode: 'absolute', trendAxisMin: null, trendAxisMax: null, trendShowGrid: true, trendShowAxisLabels: true, trendAxisLabelFontSize: 8, trendShowPointValues: false, trendPointValueFontSize: 8, trendPointValueColor: 'auto', trendPointValueEveryN: null }),
  },
  {
    key: 'led', type: 'led', name: '高发光LED指示灯',
    defaultWidth: 40, defaultHeight: 50,
    icon: 'div-led', iconKind: 'div', iconColor: '',
    description: '红绿双色状态警告信源灯，支持光晕频闪效果。',
    category: 'sensors', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 130,
    defaultProps: () => baseProps('led'),
  },
  {
    key: 'sys-time', type: 'sys-time', name: '实时系统时钟',
    defaultWidth: 160, defaultHeight: 50,
    icon: getLucideIcon('clock'), iconKind: 'lucide', iconColor: 'text-emerald-500',
    description: '数字式数码时钟控件，秒级刷新显示当前时间。',
    category: 'sensors', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 140,
    defaultProps: () => baseProps('sys-time'),
  },
  {
    key: 'pipe-h', type: 'pipe-h', name: '水平输水管路',
    defaultWidth: 160, defaultHeight: 16,
    icon: 'div-h', iconKind: 'div', iconColor: '',
    description: '支持流向光带闪烁动效的水平流动金属管。',
    category: 'structures', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 150,
    defaultProps: () => baseProps('pipe-h'),
  },
  {
    key: 'pipe-v', type: 'pipe-v', name: '垂直高压管道',
    defaultWidth: 16, defaultHeight: 160,
    icon: 'div-v', iconKind: 'div', iconColor: '',
    description: '支持流速频闪的垂直重力回流水管。',
    category: 'structures', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 160,
    defaultProps: () => baseProps('pipe-v'),
  },
  {
    key: 'text', type: 'text', name: '自定义文本组态',
    defaultWidth: 120, defaultHeight: 35,
    icon: getLucideIcon('type'), iconKind: 'lucide', iconColor: 'text-slate-300',
    description: '静态或者动态映射文字说明，可调节字号和对齐方式。',
    category: 'structures', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 170,
    defaultProps: () => baseProps('text'),
  },
  {
    key: 'rounded-btn', type: 'rounded-btn', name: '圆角按钮',
    defaultWidth: 110, defaultHeight: 46,
    icon: getLucideIcon('sparkles'), iconKind: 'lucide', iconColor: 'text-emerald-500',
    description: '可绑定变量的高级圆角控制按钮：背景/操作变量可分离绑定，支持取反/置位/复位/按1送0/设值/画面跳转/执行脚本，内置启动/停止/复位/点动/急停 5 种预设风格。',
    category: 'structures', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 180,
    defaultProps: () => ({
      ...baseProps('rounded-btn'),
      buttonMode: 'toggle',
      borderRadius: 10,
      borderWidth: 1,
      strokeColor: '#38bdf8',
      buttonText: '圆角按钮',
      state0Text: 'OFF 停止',
      state0BgColor: '#1e293b',
      state0TextColor: '#94a3b8',
      state1Text: 'ON 运行',
      state1BgColor: '#0284c7',
      state1TextColor: '#ffffff',
      customStates: '0:停止:#334155:#94a3b8;1:运行:#0284c7:#ffffff;2:报警:#dc2626:#ffffff',
      targetPageId: null,
      targetScriptId: null,
      showModeBadge: true,
      opDeviceId: null,
      opVariableKey: null,
      presetStyle: '',
    }),
  },
  {
    key: 'switch', type: 'switch', name: '两位旋动选择按钮',
    defaultWidth: 70, defaultHeight: 90,
    icon: getLucideIcon('toggle-right'), iconKind: 'lucide', iconColor: 'text-[#1890ff]',
    description: '自复位旋钮式状态控制开关，触手可及。',
    category: 'structures', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 190,
    defaultProps: () => baseProps('switch'),
  },
  {
    key: 'image', type: 'image', name: '自定义图片',
    defaultWidth: 200, defaultHeight: 150,
    icon: getLucideIcon('image'), iconKind: 'lucide', iconColor: 'text-sky-400',
    description: '上传或从图库选择图片作为图元，可缩放、跨页面复用。',
    category: 'structures', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 200,
    // 图片路径延迟到选图后写入（handleImageSelected），默认空=占位提示
    defaultProps: () => ({ imageUrl: '', imageFit: 'fill' }),
  },
  {
    key: 'title-header-tech-desktop', type: 'title-header', name: '科技蓝·大屏标题栏',
    defaultWidth: 960, defaultHeight: 72,
    icon: getLucideIcon('monitor'), iconKind: 'lucide', iconColor: 'text-sky-400',
    description: '经典未来科技蓝宽屏大屏标题栏，带晶蓝发光切角翼展、数字时钟与在线状态。',
    category: 'headers', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 210,
    defaultProps: () => ({
      ...baseProps('title-header'),
      headerStyle: 'tech-blue',
      headerDevice: 'desktop',
      headerTitle: '工业互联网智能监控大屏',
      headerSubtitle: 'INTELLIGENT SCADA MONITORING PLATFORM',
      headerLogoText: 'SCADA 5G',
      headerShowClock: true,
      headerShowStatus: true,
      headerStatusText: '系统运行正常',
      headerGlowColor: '#38bdf8',
      fontSize: 22,
      bold: true,
    }),
  },
  {
    key: 'title-header-tech-mobile', type: 'title-header', name: '科技蓝·移动标题栏',
    defaultWidth: 375, defaultHeight: 56,
    icon: getLucideIcon('smartphone'), iconKind: 'lucide', iconColor: 'text-sky-400',
    description: '科技蓝移动竖屏标题栏，紧凑机能流光切角与紧凑状态指示点。',
    category: 'headers', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 220,
    defaultProps: () => ({
      ...baseProps('title-header'),
      headerStyle: 'tech-blue',
      headerDevice: 'mobile',
      headerTitle: '车间移动监控中心',
      headerSubtitle: 'MOBILE SCADA TERMINAL',
      headerLogoText: '5G',
      headerShowClock: false,
      headerShowStatus: true,
      headerStatusText: '在线',
      headerGlowColor: '#38bdf8',
      fontSize: 16,
      bold: true,
    }),
  },
  {
    key: 'nav-menu-desktop', type: 'nav-menu', name: '桌面端·顶部导航条',
    defaultWidth: 960, defaultHeight: 56,
    icon: getLucideIcon('panel-top'), iconKind: 'lucide', iconColor: 'text-sky-400',
    description: '桌面端横向导航菜单条：图标+文字+同端画面跳转，3~5 项，运行态自动高亮当前画面。',
    category: 'headers', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 230,
    defaultProps: () => ({
      menuStyle: 'navy-midnight',
      menuDevice: 'desktop' as const,
      menuItems: [
        { icon: 'home', text: '总览', targetPageId: null },
        { icon: 'factory', text: '工艺监控', targetPageId: null },
        { icon: 'bell', text: '报警中心', targetPageId: null },
      ] as HmiMenuItem[],
      menuAccentColor: '#38bdf8',
      menuFontSize: 14,
    }),
  },
  {
    key: 'nav-menu-mobile', type: 'nav-menu', name: '移动端·底部标签栏',
    defaultWidth: 375, defaultHeight: 64,
    icon: getLucideIcon('smartphone'), iconKind: 'lucide', iconColor: 'text-emerald-400',
    description: '移动端底部 Tab 导航栏：图标+文字+同端画面跳转，3~5 项，运行态自动高亮当前画面。',
    category: 'headers', renderKind: 'builtin', propSchema: [], isSystem: true, sortOrder: 240,
    defaultProps: () => ({
      menuStyle: 'navy-midnight',
      menuDevice: 'mobile' as const,
      menuItems: [
        { icon: 'home', text: '首页', targetPageId: null },
        { icon: 'line-chart', text: '趋势', targetPageId: null },
        { icon: 'bell', text: '报警', targetPageId: null },
      ] as HmiMenuItem[],
      menuAccentColor: '#38bdf8',
      menuFontSize: 12,
    }),
  },
];

// P5：为本地兜底种子挂接属性 Schema（键 = templateKey；与 BUILTIN_SCHEMAS 一一对应）
BUILTIN_SEEDS.forEach((d) => {
  d.propSchema = BUILTIN_SCHEMAS[d.key] ?? [];
});

/**
 * nav-menu 内置图标集：Inspector 图标选择网格与 HMIWidget 渲染共用的单一映射。
 * name 即存库值（props.menuItems[].icon），新增图标只需在此追加。
 */
export const MENU_ICON_OPTIONS: { name: string; label: string; icon: any }[] = [
  { name: 'home', label: '首页', icon: Home },
  { name: 'factory', label: '工厂', icon: Factory },
  { name: 'bell', label: '报警', icon: Bell },
  { name: 'line-chart', label: '趋势', icon: LineChart },
  { name: 'settings', label: '设置', icon: Settings },
  { name: 'database', label: '数据', icon: Database },
  { name: 'zap', label: '电力', icon: Zap },
  { name: 'wrench', label: '运维', icon: Wrench },
  { name: 'shield-alert', label: '安全', icon: ShieldAlert },
  { name: 'file-text', label: '报表', icon: FileText },
  { name: 'users', label: '用户', icon: Users },
  { name: 'camera', label: '摄像', icon: Camera },
  { name: 'server', label: '服务器', icon: Server },
  { name: 'boxes', label: '库存', icon: Boxes },
  { name: 'gauge-circle', label: '仪表', icon: GaugeCircle },
  { name: 'route', label: '工艺路线', icon: Route },
  { name: 'clipboard-list', label: '工单', icon: ClipboardList },
  { name: 'activity', label: '实时曲线', icon: Activity },
];

/** 按存库名取图标组件；未命中回退 Home（渲染兜底） */
export const getMenuIcon = (name: string): any =>
  MENU_ICON_OPTIONS.find((o) => o.name === name)?.icon ?? Home;

/** 可下发写指令的控制类器件（运行态可点击写值）；其余器件为纯展示 */
export const CONTROL_WIDGET_TYPES: ReadonlySet<string> = new Set([
  'button',
  'rounded-btn',
  'switch',
  'valve',
]);
export const isControlWidget = (type: string): boolean =>
  CONTROL_WIDGET_TYPES.has(type);
