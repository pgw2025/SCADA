import { ComponentType } from './types';
import {
  Activity,
  Cpu,
  Layers,
  Thermometer,
  ToggleLeft,
  ToggleRight,
  Tv,
  Type,
  BatteryCharging,
  Gauge,
  Workflow,
  SquareTerminal,
  Clock,
  RefreshCw,
  Sparkles,
  Image as ImageIcon,
  Monitor,
  Smartphone,
} from 'lucide-vue-next';

/**
 * 阶段5-5：组件注册机制（消除四处散改）。
 *
 * 单一注册点：type → { 名称 / 默认尺寸 / 图标 / 分类 / 描述 / 默认 props 工厂 }。
 * - `WidgetLibrary` 列表从此处渲染（不再维护独立 WIDGETS 数组）。
 * - `ScadaTopologyView.handleAddWidget` 的默认 props / 尺寸 / 名称从此处取（消除与图库两处散改）。
 * 新增组件类型只需：① 在此注册；② 在 `HMIWidget.vue` 增加渲染分支。
 */

export type WidgetCategory = 'equipment' | 'sensors' | 'structures' | 'headers';

export interface WidgetDef {
  key?: string;
  type: ComponentType;
  name: string;
  defaultWidth: number;
  defaultHeight: number;
  icon: any; // lucide 组件，或 'div-h'/'div-v'/'div-led' 字符串（iconKind='div'）
  iconKind: 'lucide' | 'div';
  iconColor: string;
  description: string;
  category: WidgetCategory;
  defaultProps: () => Record<string, any>;
}

// 单一真相源：InspectorPanel 回显与 HMIWidget 运行时兜底均以此为准。
// 所有字段显式写全，不做隐式缺省，避免「面板显示值 ≠ 运行生效值」。
const baseProps = (type: ComponentType): Record<string, any> => ({
  activeColor: type === 'valve' || type === 'led' ? '#10b981' : '#3b82f6',
  inactiveColor: '#94a3b8',
  maxValue: type === 'gauge-dial' ? 120 : 100,
  minValue: 0, // 量程下限（百分比类/仪表类归一化基准）
  unit: type === 'gauge-dial' ? '℃' : '',
  showValue: false,
  fontSize: 12,
  bold: false,
  align: 'center',
  thresholdMax: 90, // 与面板回显一致
  thresholdMin: 10, // 与面板回显一致
  // 标题背景默认属性
  ...(type === 'title-header'
    ? {
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
      }
    : {}),
});

export const widgetRegistry: Record<string, WidgetDef> = {
  boiler: {
    type: 'boiler',
    name: '加热锅炉反应釜',
    defaultWidth: 140,
    defaultHeight: 180,
    icon: BatteryCharging,
    iconKind: 'lucide',
    iconColor: 'text-amber-500',
    description: '工业超温蒸汽燃煤锅炉，带火焰动态变频效果。',
    category: 'equipment',
    defaultProps: () => baseProps('boiler'),
  },
  pump: {
    type: 'pump',
    name: '离心输送水泵',
    defaultWidth: 70,
    defaultHeight: 70,
    icon: Cpu,
    iconKind: 'lucide',
    iconColor: 'text-emerald-500',
    description: '液体或气体加压叶轮主输水泵，运行自带叶片旋转效果。',
    category: 'equipment',
    defaultProps: () => baseProps('pump'),
  },
  valve: {
    type: 'valve',
    name: '智能两位电磁阀',
    defaultWidth: 60,
    defaultHeight: 60,
    icon: ToggleLeft,
    iconKind: 'lucide',
    iconColor: 'text-indigo-500',
    description: '蝶阀/电磁球阀，状态切换时蝶阀手轮旋转90°。',
    category: 'equipment',
    defaultProps: () => baseProps('valve'),
  },
  tank: {
    type: 'tank',
    name: '圆角储液容器罐',
    defaultWidth: 120,
    defaultHeight: 160,
    icon: Layers,
    iconKind: 'lucide',
    iconColor: 'text-sky-500',
    description: '带刻度及气泡波纹的液体深度容器。',
    category: 'equipment',
    defaultProps: () => baseProps('tank'),
  },
  conveyor: {
    type: 'conveyor',
    name: '变频滚轮传送带',
    defaultWidth: 260,
    defaultHeight: 40,
    icon: Workflow,
    iconKind: 'lucide',
    iconColor: 'text-orange-500',
    description: '物料或箱体传动物件传送带，速度非零时展现位移动画。',
    category: 'equipment',
    defaultProps: () => baseProps('conveyor'),
  },
  motor: {
    type: 'motor',
    name: '变频伺服AC电机',
    defaultWidth: 120,
    defaultHeight: 90,
    icon: RefreshCw,
    iconKind: 'lucide',
    iconColor: 'text-sky-500',
    description: '变频配给驱动电机，工作时伴随冷却风扇叶极速旋转效果。',
    category: 'equipment',
    defaultProps: () => baseProps('motor'),
  },
  'gauge-dial': {
    type: 'gauge-dial',
    name: '高精度机械表盘',
    defaultWidth: 120,
    defaultHeight: 120,
    icon: Gauge,
    iconKind: 'lucide',
    iconColor: 'text-purple-500',
    description: '圆形度盘表，支持设置极限阈值并同步变红警告。',
    category: 'sensors',
    defaultProps: () => baseProps('gauge-dial'),
  },
  'gauge-level': {
    type: 'gauge-level',
    name: '液位刻度警告柱',
    defaultWidth: 50,
    defaultHeight: 140,
    icon: Thermometer,
    iconKind: 'lucide',
    iconColor: 'text-rose-500',
    description: '带有高、中、低限阈值的段式刻度检测条。',
    category: 'sensors',
    defaultProps: () => baseProps('gauge-level'),
  },
  'digital-val': {
    type: 'digital-val',
    name: '多功能数显仪表',
    defaultWidth: 130,
    defaultHeight: 60,
    icon: Tv,
    iconKind: 'lucide',
    iconColor: 'text-cyan-500',
    description: '工业LED高亮七段数值显示面板，可绑定任意PLC点。',
    category: 'sensors',
    defaultProps: () => baseProps('digital-val'),
  },
  'trend-chart': {
    type: 'trend-chart',
    name: '实时波段趋势图',
    defaultWidth: 280,
    defaultHeight: 160,
    icon: Activity,
    iconKind: 'lucide',
    iconColor: 'text-red-500',
    description: '动态微积分平滑滤波趋势图，记录历史PLC模拟参数。',
    category: 'sensors',
    defaultProps: () => baseProps('trend-chart'),
  },
  led: {
    type: 'led',
    name: '高发光LED指示灯',
    defaultWidth: 40,
    defaultHeight: 50,
    icon: 'div-led',
    iconKind: 'div',
    iconColor: '',
    description: '红绿双色状态警告信源灯，支持光晕频闪效果。',
    category: 'sensors',
    defaultProps: () => baseProps('led'),
  },
  'sys-time': {
    type: 'sys-time',
    name: '实时系统时钟',
    defaultWidth: 160,
    defaultHeight: 50,
    icon: Clock,
    iconKind: 'lucide',
    iconColor: 'text-emerald-500',
    description: '数字式数码时钟控件，秒级刷新显示当前时间。',
    category: 'sensors',
    defaultProps: () => baseProps('sys-time'),
  },
  'pipe-h': {
    type: 'pipe-h',
    name: '水平输水管路',
    defaultWidth: 160,
    defaultHeight: 16,
    icon: 'div-h',
    iconKind: 'div',
    iconColor: '',
    description: '支持流向光带闪烁动效的水平流动金属管。',
    category: 'structures',
    defaultProps: () => baseProps('pipe-h'),
  },
  'pipe-v': {
    type: 'pipe-v',
    name: '垂直高压管道',
    defaultWidth: 16,
    defaultHeight: 160,
    icon: 'div-v',
    iconKind: 'div',
    iconColor: '',
    description: '支持流速频闪的垂直重力回流水管。',
    category: 'structures',
    defaultProps: () => baseProps('pipe-v'),
  },
  text: {
    type: 'text',
    name: '自定义文本组态',
    defaultWidth: 120,
    defaultHeight: 35,
    icon: Type,
    iconKind: 'lucide',
    iconColor: 'text-slate-300',
    description: '静态或者动态映射文字说明，可调节字号和对齐方式。',
    category: 'structures',
    defaultProps: () => baseProps('text'),
  },
  button: {
    type: 'button',
    name: '3D重载控制按钮',
    defaultWidth: 100,
    defaultHeight: 50,
    icon: SquareTerminal,
    iconKind: 'lucide',
    iconColor: 'text-amber-500',
    description: '工业现场操作主令按钮，支持自锁(Toggle)、点动(Momentary)、设值(SetValue)、跳转(Navigate)四种执行逻辑。',
    category: 'structures',
    defaultProps: () => ({ ...baseProps('button'), targetPageId: null }),
  },
  'rounded-btn': {
    type: 'rounded-btn',
    name: '工业圆角多态按钮',
    defaultWidth: 110,
    defaultHeight: 46,
    icon: Sparkles,
    iconKind: 'lucide',
    iconColor: 'text-emerald-500',
    description: '可绑定变量的高级圆角控制按钮：背景/操作变量可分离绑定，支持取反/置位/复位/按1送0/设值/画面跳转/执行脚本，内置启动/停止/复位/点动/急停 5 种预设风格。',
    category: 'structures',
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
  switch: {
    type: 'switch',
    name: '两位旋动选择按钮',
    defaultWidth: 70,
    defaultHeight: 90,
    icon: ToggleRight, // 与 valve(ToggleLeft) 区分，图库图标可辨识
    iconKind: 'lucide',
    iconColor: 'text-[#1890ff]',
    description: '自复位旋钮式状态控制开关，触手可及。',
    category: 'structures',
    defaultProps: () => baseProps('switch'),
  },
  image: {
    type: 'image',
    name: '自定义图片',
    defaultWidth: 200,
    defaultHeight: 150,
    icon: ImageIcon,
    iconKind: 'lucide',
    iconColor: 'text-sky-400',
    description: '上传或从图库选择图片作为图元，可缩放、跨页面复用。',
    category: 'structures',
    // 图片路径延迟到选图后写入（handleImageSelected），默认空=占位提示
    defaultProps: () => ({ imageUrl: '', imageFit: 'fill' }),
  },
  'title-header-tech-desktop': {
    type: 'title-header',
    name: '科技蓝·大屏标题栏',
    defaultWidth: 960,
    defaultHeight: 72,
    icon: Monitor,
    iconKind: 'lucide',
    iconColor: 'text-sky-400',
    description: '经典未来科技蓝宽屏大屏标题栏，带晶蓝发光切角翼展、数字时钟与在线状态。',
    category: 'headers',
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
  'title-header-tech-mobile': {
    type: 'title-header',
    name: '科技蓝·移动标题栏',
    defaultWidth: 375,
    defaultHeight: 56,
    icon: Smartphone,
    iconKind: 'lucide',
    iconColor: 'text-sky-400',
    description: '科技蓝移动竖屏标题栏，紧凑机能流光切角与紧凑状态指示点。',
    category: 'headers',
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
};

// 保持注册表声明顺序的列表（图库渲染用）
export const widgetList: WidgetDef[] = Object.values(widgetRegistry);

// 取某类型注册项：优先按注册键精确命中；未命中再按 type 字段泛化匹配第一个，
// 兼容复合注册键（如标题栏 title-header-xxx 的 type 为 title-header），避免新增类型查不到。
export const getWidgetDef = (type: string): WidgetDef | undefined =>
  widgetRegistry[type] ?? widgetList.find((d) => d.type === type);

/** 可下发写指令的控制类器件（运行态可点击写值）；其余器件为纯展示 */
export const CONTROL_WIDGET_TYPES: ReadonlySet<string> = new Set([
  'button',
  'rounded-btn',
  'switch',
  'valve',
]);
export const isControlWidget = (type: string): boolean =>
  CONTROL_WIDGET_TYPES.has(type);
