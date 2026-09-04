/**
 * SVG 模板工具（P4 全量启用；P2 起供组件库 svg 图标清洗与 SVG 轨占位渲染使用）：
 * - sanitizeSvg：前端二次清洗（纵深防御，规则与后端 SvgSanitizer 对齐，审查 A12）
 * - bindSvgTemplate：{key} 占位符替换（未知占位符原样保留，便于模板作者排查拼写）
 */

/** 运行态绑定上下文：与后端种子 defaultProps 的键对齐（量程/阈值/颜色/文案） */
export interface SvgBindingContext {
  value: number | boolean;
  numValue: number;
  boolValue: boolean;
  normalizedPercent: number;   // 量程归一化 0~100（SVG 高度/宽度类绑定友好）
  state: string;               // on/off 文案（onText/offText）
  unit: string;
  label: string;               // component.label
  activeColor: string;
  inactiveColor: string;
  alertColor: string;          // 阈值告警色
  thresholdMin: number | null;
  thresholdMax: number | null;
  fontSize: number;
  quality: string;
}

/** {key} 占位符替换；未知占位符原样保留（便于模板作者排查拼写） */
export const bindSvgTemplate = (svg: string, ctx: SvgBindingContext): string =>
  svg.replace(/\{([a-zA-Z0-9_]+)\}/g, (m, k: string) => {
    const v = (ctx as Record<string, unknown>)[k];
    return v === undefined || v === null ? m : String(v);
  });

/** 前端二次清洗（纵深防御；规则与后端 SvgSanitizer 对齐，审查 A12） */
export const sanitizeSvg = (svg: string): string => {
  let out = svg
    .replace(/<script[\s\S]*?<\/script>/gi, '')
    .replace(/<script[^>]*\/>/gi, '')
    .replace(/<foreignObject[\s\S]*?<\/foreignObject>/gi, '')
    .replace(/\son\w+\s*=\s*("[^"]*"|'[^']*'|[^\s>]+)/gi, '');
  out = out.replace(/(href|xlink:href|src)\s*=\s*(["'])([\s\S]*?)\2/gi, (m, attr, q, v) =>
    /^(#|data:image\/|\/)/i.test(v.trim()) ? m : `${attr}=${q}${q}`);
  out = out.replace(/url\(\s*(["']?)([\s\S]*?)\1\s*\)/gi, (m, _q, v) =>
    /^(#|data:image\/|\/)/i.test(v.trim()) ? m : 'url()');
  return out.length > 256 * 1024 ? out.slice(0, 256 * 1024) : out;
};
