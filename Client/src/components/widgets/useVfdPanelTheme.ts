import { computed, type Ref } from 'vue';

// ===== 风格主题体系（与导航菜单 nav-menu 同套 8 种预设，支持自定义强调色）=====
// 供变频电机控制面板、实时多变量看板等工业控件复用同一套外观主题令牌。

// hex → rgba（用于强调色透明变体；非 6 位 hex 原样返回）
export const hexToRgba = (hex: string, alpha: number): string => {
  const m = /^#?([0-9a-f]{6})$/i.exec((hex || '').trim());
  if (!m) return hex;
  const n = parseInt(m[1], 16);
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`;
};

export interface VfdPanelTheme {
  isLight: boolean;
  accent: string;
  blur: string;
  page: string;
  border: string;
  header: string;
  section: string;
  footer: string;
  well: string;
  wellSoft: string;
  wellBorder: string;
  divider: string;
  title: string;
  body: string;
  muted: string;
  faint: string;
  sp: string;
  btnBg: string;
  btnBgHover: string;
  btnBorder: string;
  btnText: string;
  btnDisabledBg: string;
  hover: string;
  ok: string;
  okSoft: string;
  err: string;
  errSoft: string;
  warn: string;
  warnSoft: string;
  tagBg: string;
  tagText: string;
  tagBorder: string;
  overlay: string;
  dialog: string;
  dialogBorder: string;
  stator: [string, string, string];
  shaft: [string, string, string];
  svgStroke: string;
  svgBase: string;
  svgDeep: string;
}

export interface UseVfdPanelThemeOptions {
  /** 主题预设 key，如 'slate-dark' | 'pure-white' | ... */
  panelStyle: Ref<string> | string;
  /** 自定义强调色（'#38bdf8' 视为未自定义，回退各主题默认强调色） */
  panelAccentColor: Ref<string> | string;
}

const resolve = <T,>(v: Ref<T> | T): T => (typeof v === 'object' && v !== null && 'value' in v ? (v as Ref<T>).value : (v as T));

export function useVfdPanelTheme(options: UseVfdPanelThemeOptions) {
  const panelTheme = computed<VfdPanelTheme>(() => {
    const style = resolve(options.panelStyle);
    const custom = resolve(options.panelAccentColor);
    const pick = (presetAccent: string) => (custom && custom !== '#38bdf8' ? custom : presetAccent);

    // 语义色（运行绿/故障红/警示橙）随明暗主题切换对比度变体
    const darkSem = { ok: '#34d399', okSoft: 'rgba(16,185,129,0.2)', err: '#f87171', errSoft: 'rgba(244,63,94,0.2)', warn: '#fbbf24', warnSoft: 'rgba(245,158,11,0.2)' };
    const lightSem = { ok: '#059669', okSoft: '#d1fae5', err: '#dc2626', errSoft: '#fee2e2', warn: '#d97706', warnSoft: '#fef3c7' };
    const darkTag = { tagBg: 'rgba(245,158,11,0.2)', tagText: '#fbbf24', tagBorder: 'rgba(245,158,11,0.3)' };
    const lightTag = { tagBg: '#fef3c7', tagText: '#b45309', tagBorder: '#fcd34d' };

    if (style === 'pure-white') {
      return {
        isLight: true, accent: pick('#2563eb'), blur: 'none',
        page: '#ffffff', border: '#e2e8f0',
        header: '#f8fafc', section: '#ffffff', footer: '#f1f5f9',
        well: '#f1f5f9', wellSoft: 'rgba(241,245,249,0.7)', wellBorder: '#e2e8f0', divider: '#e2e8f0',
        title: '#0f172a', body: '#334155', muted: '#64748b', faint: '#94a3b8', sp: '#d97706',
        btnBg: '#e2e8f0', btnBgHover: '#cbd5e1', btnBorder: '#cbd5e1', btnText: '#475569', btnDisabledBg: 'rgba(226,232,240,0.6)',
        hover: '#e2e8f0', ...lightSem, ...lightTag,
        overlay: 'rgba(15,23,42,0.35)', dialog: '#ffffff', dialogBorder: '#cbd5e1',
        stator: ['#e2e8f0', '#cbd5e1', '#94a3b8'], shaft: ['#cbd5e1', '#f8fafc', '#64748b'],
        svgStroke: '#94a3b8', svgBase: '#cbd5e1', svgDeep: '#e2e8f0',
      };
    }

    if (style === 'titanium-light') {
      return {
        isLight: true, accent: pick('#0284c7'), blur: 'none',
        page: 'linear-gradient(180deg, #f8fafc 0%, #eef2f7 100%)', border: '#cbd5e1',
        header: '#e8edf3', section: '#f4f7fa', footer: '#e8edf3',
        well: '#e2e8f0', wellSoft: 'rgba(226,232,240,0.75)', wellBorder: '#c4cedb', divider: '#d5dce5',
        title: '#1e293b', body: '#3f4b5f', muted: '#64748b', faint: '#8a97aa', sp: '#d97706',
        btnBg: '#dfe6ee', btnBgHover: '#cfd9e4', btnBorder: '#c4cedb', btnText: '#3f4b5f', btnDisabledBg: 'rgba(223,230,238,0.6)',
        hover: '#dfe6ee', ...lightSem, ...lightTag,
        overlay: 'rgba(30,41,59,0.35)', dialog: '#f4f7fa', dialogBorder: '#b7c3d2',
        stator: ['#d7dee8', '#b7c3d2', '#8e9cb0'], shaft: ['#b7c3d2', '#e8edf3', '#64748b'],
        svgStroke: '#8e9cb0', svgBase: '#b7c3d2', svgDeep: '#d7dee8',
      };
    }

    if (style === 'navy-midnight') {
      return {
        isLight: false, accent: pick('#38bdf8'), blur: 'none',
        page: 'linear-gradient(180deg, #0b172a 0%, #081a36 60%, #061426 100%)', border: '#1e293b',
        header: 'rgba(4,12,28,0.85)', section: 'rgba(8,26,54,0.55)', footer: 'rgba(4,12,28,0.92)',
        well: '#061426', wellSoft: 'rgba(6,20,38,0.65)', wellBorder: '#16305a', divider: '#142b4d',
        title: '#f0f6ff', body: '#dbe7f5', muted: '#9fb6cc', faint: '#6b87a6', sp: '#fbbf24',
        btnBg: '#12233f', btnBgHover: '#1a3054', btnBorder: '#1e3a5f', btnText: '#c9d8ec', btnDisabledBg: 'rgba(18,35,63,0.6)',
        hover: '#122a4a', ...darkSem, ...darkTag,
        overlay: 'rgba(4,12,28,0.9)', dialog: '#0b1c36', dialogBorder: '#25466e',
        stator: ['#2a4165', '#1b3153', '#0e1e38'], shaft: ['#cbd5e1', '#f8fafc', '#64748b'],
        svgStroke: '#3d5a86', svgBase: '#1b3153', svgDeep: '#0e1e38',
      };
    }

    if (style === 'tech-blue') {
      return {
        isLight: false, accent: pick('#60a5fa'), blur: 'none',
        page: 'linear-gradient(180deg, #0b1e3a 0%, #0a2145 55%, #071630 100%)', border: 'rgba(59,130,246,0.5)',
        header: 'rgba(7,18,40,0.88)', section: 'rgba(10,31,63,0.6)', footer: 'rgba(7,18,40,0.94)',
        well: '#061634', wellSoft: 'rgba(6,22,52,0.65)', wellBorder: '#1e3a8a', divider: '#16327a',
        title: '#eaf4ff', body: '#cfe3ff', muted: '#8fb6e8', faint: '#5f83b5', sp: '#fbbf24',
        btnBg: '#12295a', btnBgHover: '#1a3a75', btnBorder: '#1e40af', btnText: '#cfe0ff', btnDisabledBg: 'rgba(18,41,90,0.55)',
        hover: '#12295a', ...darkSem, ...darkTag,
        overlay: 'rgba(5,14,32,0.92)', dialog: '#0a1f3f', dialogBorder: '#2563eb',
        stator: ['#2d4a8a', '#1c3266', '#0e1f45'], shaft: ['#cbd5e1', '#f8fafc', '#64748b'],
        svgStroke: '#3b5a9e', svgBase: '#1c3266', svgDeep: '#0e1f45',
      };
    }

    if (style === 'translucent-frost') {
      return {
        isLight: false, accent: pick('#38bdf8'), blur: 'blur(8px)',
        page: 'rgba(15, 23, 42, 0.82)', border: 'rgba(255,255,255,0.15)',
        header: 'rgba(2,6,23,0.5)', section: 'rgba(15,23,42,0.45)', footer: 'rgba(2,6,23,0.55)',
        well: 'rgba(2,6,23,0.55)', wellSoft: 'rgba(2,6,23,0.45)', wellBorder: 'rgba(148,163,184,0.25)', divider: 'rgba(148,163,184,0.2)',
        title: '#f1f5f9', body: '#e2e8f0', muted: '#a8b6c8', faint: '#7889a0', sp: '#fbbf24',
        btnBg: 'rgba(148,163,184,0.12)', btnBgHover: 'rgba(148,163,184,0.22)', btnBorder: 'rgba(148,163,184,0.28)', btnText: '#cbd5e1', btnDisabledBg: 'rgba(148,163,184,0.08)',
        hover: 'rgba(148,163,184,0.15)', ...darkSem, ...darkTag,
        overlay: 'rgba(2,6,23,0.75)', dialog: 'rgba(15,23,42,0.95)', dialogBorder: 'rgba(255,255,255,0.2)',
        stator: ['#3a4a63', '#26354c', '#141f33'], shaft: ['#cbd5e1', '#f8fafc', '#64748b'],
        svgStroke: '#4c5e7a', svgBase: '#26354c', svgDeep: '#141f33',
      };
    }

    if (style === 'eco-green') {
      return {
        isLight: false, accent: pick('#34d399'), blur: 'none',
        page: 'linear-gradient(180deg, #073a26 0%, #052c1c 55%, #032015 100%)', border: '#064e3b',
        header: 'rgba(2,20,12,0.85)', section: 'rgba(5,48,35,0.6)', footer: 'rgba(2,20,12,0.92)',
        well: '#02170f', wellSoft: 'rgba(2,23,15,0.65)', wellBorder: '#0a4a33', divider: '#0a4430',
        title: '#ecfdf5', body: '#d1fae5', muted: '#7fd9b8', faint: '#4f9c7d', sp: '#fbbf24',
        btnBg: '#0b4030', btnBgHover: '#0f5138', btnBorder: '#0a5c3e', btnText: '#b7e8d2', btnDisabledBg: 'rgba(11,64,48,0.6)',
        hover: '#0b4030', ...darkSem, ...darkTag,
        overlay: 'rgba(2,20,12,0.9)', dialog: '#053023', dialogBorder: '#0a4a33',
        stator: ['#1d5c43', '#0f4531', '#06301f'], shaft: ['#a7d8c2', '#ecfdf5', '#4f9c7d'],
        svgStroke: '#2d7a5a', svgBase: '#0f4531', svgDeep: '#06301f',
      };
    }

    if (style === 'carbon-orange') {
      return {
        isLight: false, accent: pick('#f59e0b'), blur: 'none',
        page: 'linear-gradient(180deg, #2a1b0c 0%, #201407 50%, #170d04 100%)', border: '#78350f',
        header: 'rgba(20,10,3,0.85)', section: 'rgba(36,21,5,0.55)', footer: 'rgba(20,10,3,0.92)',
        well: '#140a02', wellSoft: 'rgba(20,10,2,0.65)', wellBorder: '#5c3410', divider: '#4d2d0e',
        title: '#fff7ed', body: '#f3e2cd', muted: '#cfaa85', faint: '#9c7a58', sp: '#fbbf24',
        btnBg: '#3d2409', btnBgHover: '#4d2f0c', btnBorder: '#5c3410', btnText: '#f3e2cd', btnDisabledBg: 'rgba(61,36,9,0.6)',
        hover: '#3d2409', ...darkSem, ...darkTag,
        overlay: 'rgba(20,10,3,0.9)', dialog: '#241505', dialogBorder: '#5c3410',
        stator: ['#5c3a1a', '#3f2810', '#291a08'], shaft: ['#e2c9a8', '#fff7ed', '#9c7a58'],
        svgStroke: '#7a5223', svgBase: '#3f2810', svgDeep: '#291a08',
      };
    }

    // 默认：经典石板深灰 (Classic Slate Dark)，与组件原始工业风格一致
    return {
      isLight: false, accent: pick('#38bdf8'), blur: 'none',
      page: '#0f172a', border: 'rgba(51,65,85,0.8)',
      header: 'rgba(2,6,23,0.8)', section: '#0f172a', footer: 'rgba(2,6,23,0.9)',
      well: '#020617', wellSoft: 'rgba(2,6,23,0.6)', wellBorder: '#1e293b', divider: '#1e293b',
      title: '#ffffff', body: '#f1f5f9', muted: '#94a3b8', faint: '#64748b', sp: '#fbbf24',
      btnBg: '#1e293b', btnBgHover: '#334155', btnBorder: '#334155', btnText: '#cbd5e1', btnDisabledBg: 'rgba(30,41,59,0.6)',
      hover: '#1e293b', ...darkSem, ...darkTag,
      overlay: 'rgba(2,6,23,0.9)', dialog: '#0f172a', dialogBorder: '#334155',
      stator: ['#334155', '#1e293b', '#0f172a'], shaft: ['#cbd5e1', '#f8fafc', '#64748b'],
      svgStroke: '#475569', svgBase: '#1e293b', svgDeep: '#0f172a',
    };
  });

  // 主题令牌 → CSS 变量（模板与 SVG 通过 var(--vfd-*) 消费，含 hover 等伪类样式）
  const themeVars = computed<Record<string, string>>(() => {
    const t = panelTheme.value;
    return {
      '--vfd-page': t.page,
      '--vfd-blur': t.blur,
      '--vfd-border': t.border,
      '--vfd-header': t.header,
      '--vfd-section': t.section,
      '--vfd-footer': t.footer,
      '--vfd-well': t.well,
      '--vfd-well-soft': t.wellSoft,
      '--vfd-well-border': t.wellBorder,
      '--vfd-divider': t.divider,
      '--vfd-title': t.title,
      '--vfd-body': t.body,
      '--vfd-muted': t.muted,
      '--vfd-faint': t.faint,
      '--vfd-sp': t.sp,
      '--vfd-accent': t.accent,
      '--vfd-accent-glow': hexToRgba(t.accent, 0.25),
      '--vfd-btn-bg': t.btnBg,
      '--vfd-btn-bg-hover': t.btnBgHover,
      '--vfd-btn-border': t.btnBorder,
      '--vfd-btn-text': t.btnText,
      '--vfd-btn-disabled-bg': t.btnDisabledBg,
      '--vfd-hover': t.hover,
      '--vfd-ok': t.ok,
      '--vfd-ok-soft': t.okSoft,
      '--vfd-run': t.ok,
      '--vfd-err': t.err,
      '--vfd-err-soft': t.errSoft,
      '--vfd-warn': t.warn,
      '--vfd-warn-soft': t.warnSoft,
      '--vfd-tag-bg': t.tagBg,
      '--vfd-tag-text': t.tagText,
      '--vfd-tag-border': t.tagBorder,
      '--vfd-overlay': t.overlay,
      '--vfd-dialog': t.dialog,
      '--vfd-dialog-border': t.dialogBorder,
      '--vfd-stator1': t.stator[0],
      '--vfd-stator2': t.stator[1],
      '--vfd-stator3': t.stator[2],
      '--vfd-shaft1': t.shaft[0],
      '--vfd-shaft2': t.shaft[1],
      '--vfd-shaft3': t.shaft[2],
      '--vfd-svg-stroke': t.svgStroke,
      '--vfd-svg-base': t.svgBase,
      '--vfd-svg-deep': t.svgDeep,
      '--vfd-pv-grad': `linear-gradient(90deg, ${hexToRgba(t.accent, 0.55)}, ${t.accent})`,
    };
  });

  return { panelTheme, themeVars };
}
