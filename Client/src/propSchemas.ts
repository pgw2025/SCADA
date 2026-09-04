/**
 * 属性面板 Schema（P5）：组件属性面板由 schema 驱动渲染（PropSchemaForm.vue）。
 * - PropSchemaItem：通用条目结构（key/label/type/min/max/options）
 * - BUILTIN_SCHEMAS：24 条内置种子 + legacy button 的 schema（键 = templateKey）
 *   内容 = 原 InspectorPanel 硬编码各 type 分支的字段等价迁移：
 *   showLabel / 颜色 / 量程 / 阈值 / 状态文案 / 外观显隐 / 字体 / 时间格式。
 * 复杂组件（trend-chart / multi-var-dashboard / rounded-btn / nav-menu / var-display）
 * 的专属面板保留，schema 仅覆盖通用外观项；专属配置仍在 InspectorPanel 内按 type 渲染。
 */

export interface PropSchemaOption {
  value: string | number | boolean;
  label: string;
}

export interface PropSchemaItem {
  key: string;                 // props 键名（与 defaultProps 键对齐）
  label: string;               // 面板标签
  type: 'text' | 'number' | 'color' | 'select' | 'switch';
  default?: any;               // 展示兜底（props 与 defDefaults 均缺省时）
  min?: number;
  max?: number;
  step?: number;
  nullable?: boolean;          // number：可清空为 null（阈值类）
  options?: PropSchemaOption[]; // select 候选项
  placeholder?: string;
  help?: string;               // 帮助说明
}

/** 构建器（避免 24 条种子重复样板） */
const text = (key: string, label: string, def = '', placeholder?: string): PropSchemaItem =>
  ({ key, label, type: 'text', default: def, placeholder });
const num = (key: string, label: string, def: number, extra: Partial<PropSchemaItem> = {}): PropSchemaItem =>
  ({ key, label, type: 'number', default: def, ...extra });
const color = (key: string, label: string, def = '#3b82f6'): PropSchemaItem =>
  ({ key, label, type: 'color', default: def });
const sw = (key: string, label: string, def = false, help?: string): PropSchemaItem =>
  ({ key, label, type: 'switch', default: def, help });
const sel = (key: string, label: string, options: PropSchemaOption[], def: any): PropSchemaItem =>
  ({ key, label, type: 'select', options, default: def });

const ALIGN_OPTIONS: PropSchemaOption[] = [
  { value: 'left', label: '靠左对齐' },
  { value: 'center', label: '居中对齐' },
  { value: 'right', label: '靠右对齐' },
];
const BORDER_WIDTH_OPTIONS: PropSchemaOption[] = [
  { value: 1, label: '1 px (细)' },
  { value: 1.5, label: '1.5 px (标准)' },
  { value: 2, label: '2 px (中等)' },
  { value: 3, label: '3 px (粗)' },
  { value: 4, label: '4 px (加粗)' },
];
const BORDER_STYLE_OPTIONS: PropSchemaOption[] = [
  { value: 'solid', label: '实线 (Solid)' },
  { value: 'dashed', label: '虚线 (Dashed)' },
  { value: 'dotted', label: '点线 (Dotted)' },
];
const TIME_FORMAT_OPTIONS: PropSchemaOption[] = [
  { value: 'HH:mm:ss', label: '时分秒 (HH:mm:ss)' },
  { value: 'YYYY-MM-DD HH:mm:ss', label: '年月日 时分秒' },
  { value: 'YYYY-MM-DD', label: '仅显示日期 (YYYY-MM-DD)' },
];

/**
 * 通用基底：运行激活光效 / 空闲正常底色 + 外框标签（排除本无浮签的内部标签型组件）。
 * 与原 InspectorPanel「showLabel 排除列表 + 颜色块」语义一致。
 */
const base = (type: string): PropSchemaItem[] => {
  const items: PropSchemaItem[] = [
    color('activeColor', '运行激活光效', type === 'valve' || type === 'led' ? '#10b981' : '#3b82f6'),
    color('inactiveColor', '空闲正常底色', '#94a3b8'),
  ];
  if (!['text', 'led', 'gauge-level', 'gauge-dial', 'digital-val'].includes(type)) {
    items.push(sw('showLabel', '显示外框标签名称'));
  }
  return items;
};

/** 量程：下限 / 上限 / 单位（gauge-dial 上限默认 120） */
const range = (maxDefault = 100): PropSchemaItem[] => [
  num('minValue', '量程下限 (Min)', 0),
  num('maxValue', '量程上限 (Max)', maxDefault),
  text('unit', '单位 (Unit)', '', 'e.g. L/s, MPa, ℃'),
];

/** 高/低限报警阈值：可清空（留空不设） */
const threshold = (): PropSchemaItem[] => [
  num('thresholdMax', '红色高限报警值', 90, { nullable: true, placeholder: '默认不设', help: '留空则不设高限' }),
  num('thresholdMin', '黄色低限预警值', 10, { nullable: true, placeholder: '默认不设', help: '留空则不设低限' }),
];

/** 状态文案（阀/数显/开关/灯等有状态控件） */
const stateText = (): PropSchemaItem[] => [
  text('onText', '开启状态文本', '开启'),
  text('offText', '关闭状态文本', '关闭'),
];

/** 字体：对齐 / 字号 / 加粗（可选组件内显示变量值） */
const font = (withShowValue = false): PropSchemaItem[] => [
  sel('align', '对齐方式', ALIGN_OPTIONS, 'center'),
  num('fontSize', '字体大小 (px)', 12),
  sw('bold', '加粗字体'),
  ...(withShowValue ? [sw('showValue', '组件内显示变量值（隐藏顶部浮签）')] : []),
];

/** var-display 外观显隐：边框 / 背景 / 内部标签 / 报警变色（原「外观与边框设置」区） */
const varDisplayAppearance = (): PropSchemaItem[] => [
  sw('showBorder', '显示边框 (Show Border)'),
  color('borderColor', '边框颜色', '#cbd5e1'),
  sel('borderWidth', '边框粗细', BORDER_WIDTH_OPTIONS, 1.5),
  sel('borderStyle', '边框线条', BORDER_STYLE_OPTIONS, 'solid'),
  num('borderRadius', '圆角弧度', 8, { min: 0, max: 24, step: 2 }),
  sw('showBackground', '显示背景底色'),
  color('bgColor', '底色', '#ffffff'),
  sw('showInnerLabel', '显示内部变量标签'),
  sw('enableAlarmBorder', '超限时报警变色 (红/黄报警边框)', true, '仅在配置了有效报警阈值且变量超限时生效'),
];

/** 24 条内置种子 + legacy button（键 = templateKey / type） */
export const BUILTIN_SCHEMAS: Record<string, PropSchemaItem[]> = {
  boiler: [...base('boiler'), color('fillColor', '填充介质颜色', '#1890ff'), ...range(100), ...threshold()],
  pump: [...base('pump'), ...range(100), ...threshold()],
  valve: [...base('valve'), ...stateText()],
  tank: [...base('tank'), color('fillColor', '填充介质颜色', '#1890ff'), ...range(100)],
  conveyor: [...base('conveyor'), color('fillColor', '填充介质颜色', '#1890ff')],
  motor: [...base('motor'), ...range(100), ...threshold()],
  'gauge-dial': [...base('gauge-dial'), ...range(120), ...threshold()],
  'gauge-level': [...base('gauge-level'), ...range(100), ...threshold()],
  'digital-val': [...base('digital-val'), ...range(100), ...threshold(), ...stateText()],
  'var-display': [...base('var-display'), ...range(100), ...threshold(), ...stateText(), ...varDisplayAppearance()],
  'multi-var-dashboard': base('multi-var-dashboard'),
  'trend-chart': [...base('trend-chart'), ...range(100), ...threshold()],
  led: [...base('led'), ...threshold(), ...stateText()],
  'sys-time': [...base('sys-time'), sel('timeFormat', '排版格式 (DateTime Format)', TIME_FORMAT_OPTIONS, 'HH:mm:ss')],
  'pipe-h': base('pipe-h'),
  'pipe-v': base('pipe-v'),
  text: [...base('text'), ...font(false)],
  'rounded-btn': [...base('rounded-btn'), ...font(true)],
  switch: [...base('switch'), ...stateText()],
  image: base('image'),
  'title-header-tech-desktop': base('title-header-tech-desktop'),
  'title-header-tech-mobile': base('title-header-tech-mobile'),
  'nav-menu-desktop': base('nav-menu-desktop'),
  'nav-menu-mobile': base('nav-menu-mobile'),
  // legacy button：无 DB 种子，存量页面在用，schema 兜底保持通用属性可编辑
  button: [...base('button'), ...font(true)],
};
