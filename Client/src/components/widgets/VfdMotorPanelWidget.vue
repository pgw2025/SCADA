<script setup lang="ts">
import { computed, ref, watch, onMounted, onUnmounted } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';
import { devices } from '../../store/deviceStore';
import { setDataPointMappingValue } from '../../services/dataOrchestration';
import { loginUser } from '../../store/userStore';
import { ROLE_ADMIN, ROLE_OPERATOR } from '../../constants/roles';
import { showToast } from '../../services/toastService';
import {
  Activity,
  AlertTriangle,
  CheckCircle2,
  Minus,
  Plus,
  RotateCcw,
  Power,
  Square,
  Radio,
  Sliders,
  Zap
} from 'lucide-vue-next';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, propOr, width, height, ticks } = base;

// 检查操作员写权限
const hasWritePermission = computed(() => {
  const role = loginUser.value?.role;
  return role === ROLE_ADMIN || role === ROLE_OPERATOR;
});

// 配置属性读取
const motorName = computed(() => propOr('motorName', '1# 变频主循环泵'));
const motorTag = computed(() => propOr('motorTag', 'M101'));
const ratedCurrent = computed(() => Number(propOr('ratedCurrent', 25.0)));
const minFreq = computed(() => Number(propOr('minFreq', 0.0)));
const maxFreq = computed(() => Number(propOr('maxFreq', 50.0)));
const freqStep = computed(() => Number(propOr('freqStep', 1.0)));

// ===== 风格主题体系（与导航菜单 nav-menu 同套 8 种预设，支持自定义强调色）=====
const panelStyle = computed(() => propOr('panelStyle', 'slate-dark'));
const panelAccentColor = computed(() => propOr('panelAccentColor', '#38bdf8'));

// hex → rgba（用于强调色透明变体；非 6 位 hex 原样返回）
const hexToRgba = (hex: string, alpha: number): string => {
  const m = /^#?([0-9a-f]{6})$/i.exec((hex || '').trim());
  if (!m) return hex;
  const n = parseInt(m[1], 16);
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`;
};

interface VfdPanelTheme {
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

const vfdPanelTheme = computed<VfdPanelTheme>(() => {
  const style = panelStyle.value;
  // '#38bdf8' 为调色器默认值，视为未自定义，回退各主题默认强调色
  const custom = panelAccentColor.value;
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
const vfdThemeVars = computed<Record<string, string>>(() => {
  const t = vfdPanelTheme.value;
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

// 点位变量名读取
const startVar = computed(() => propOr('startVar', 'cmd_start'));
const stopVar = computed(() => propOr('stopVar', 'cmd_stop'));
const resetVar = computed(() => propOr('resetVar', 'cmd_reset'));
const readyVar = computed(() => propOr('readyVar', 'st_ready'));
const runningVar = computed(() => propOr('runningVar', 'st_running'));
const faultVar = computed(() => propOr('faultVar', 'st_fault'));
const signalVar = computed(() => propOr('signalVar', 'comm_ok'));
const spFreqVar = computed(() => propOr('spFreqVar', 'sp_freq'));
const pvFreqVar = computed(() => propOr('pvFreqVar', 'pv_freq'));
const currentVar = computed(() => propOr('currentVar', 'pv_current'));

// 所属设备解析
const effectiveDeviceId = computed<number | null>(() => {
  const opId = props.component.props?.opDeviceId;
  if (opId != null && opId !== '') return Number(opId);
  if (props.component.bindDeviceId != null) return Number(props.component.bindDeviceId);
  return devices.value[0]?.id ?? null;
});

const targetDevice = computed(() => {
  if (effectiveDeviceId.value == null) return null;
  return devices.value.find(d => String(d.id) === String(effectiveDeviceId.value)) ?? null;
});

// 本地内部状态（供未绑定设备或演示模式下交互与平滑仿真）
const localRunning = ref(false);
const localFault = ref(false);
const localReady = ref(true);
const localSpFreq = ref(30.0);
const localPvFreq = ref(0.0);
const localCurrent = ref(0.0);

// 从设备读取变量的辅助函数
const resolveDeviceId = (overrideDevId?: any): number | null => {
  if (overrideDevId !== undefined && overrideDevId !== null && overrideDevId !== '') {
    return Number(overrideDevId);
  }
  return effectiveDeviceId.value;
};

const readVar = (key: string, fallback: any, overrideDevId?: any) => {
  const devId = resolveDeviceId(overrideDevId);
  if (devId == null) return fallback;
  const dev = devices.value.find(d => Number(d.id) === Number(devId));
  if (dev && dev.variables && dev.variables[key] !== undefined) {
    return dev.variables[key];
  }
  return fallback;
};

// 实际状态解析
const isReady = computed<boolean>(() => {
  const val = readVar(readyVar.value, null, propOr('readyDeviceId', null));
  if (val !== null) return Boolean(val);
  return localReady.value;
});

const isRunning = computed<boolean>(() => {
  const val = readVar(runningVar.value, null, propOr('runningDeviceId', null));
  if (val !== null) return Boolean(val);
  return localRunning.value;
});

const isFault = computed<boolean>(() => {
  const val = readVar(faultVar.value, null, propOr('faultDeviceId', null));
  if (val !== null) return Boolean(val);
  return localFault.value;
});

const isSignalOk = computed<boolean>(() => {
  const devId = resolveDeviceId(propOr('signalDeviceId', null));
  const dev = devId != null ? devices.value.find(d => Number(d.id) === Number(devId)) : targetDevice.value;
  if (dev) {
    if (dev.runtimeStatus === 'Offline') return false;
    const val = readVar(signalVar.value, null, propOr('signalDeviceId', null));
    if (val !== null) return Boolean(val);
  }
  return true;
});

// 设定频率 (SP)
const spFreq = computed<number>(() => {
  const val = readVar(spFreqVar.value, null, propOr('spFreqDeviceId', null));
  if (val !== null && typeof val === 'number') return val;
  return localSpFreq.value;
});

// 反馈频率 (PV)
const pvFreq = computed<number>(() => {
  const val = readVar(pvFreqVar.value, null, propOr('pvFreqDeviceId', null));
  if (val !== null && typeof val === 'number') return val;
  return localPvFreq.value;
});

// 运行电流 (Current)
const motorCurrent = computed<number>(() => {
  const val = readVar(currentVar.value, null, propOr('currentDeviceId', null));
  if (val !== null && typeof val === 'number') return val;
  return localCurrent.value;
});

// 负荷百分比与告警等级（徽标配色随明暗主题调整对比度）
const loadPercent = computed<number>(() => {
  if (ratedCurrent.value <= 0) return 0;
  return Math.max(0, (motorCurrent.value / ratedCurrent.value) * 100);
});

const loadStatus = computed(() => {
  const t = vfdPanelTheme.value;
  const pct = loadPercent.value;
  if (pct > 100) return {
    text: '过载报警', color: '#ef4444', pulse: true,
    badge: { backgroundColor: t.isLight ? '#fee2e2' : 'rgba(239,68,68,0.25)', color: t.isLight ? '#dc2626' : '#fca5a5' },
  };
  if (pct >= 85) return {
    text: '接近额定', color: '#f59e0b',
    badge: { backgroundColor: t.warnSoft, color: t.warn },
  };
  return {
    text: '负荷正常', color: '#10b981',
    badge: { backgroundColor: t.okSoft, color: t.ok },
  };
});

// 电机旋转角度计算（根据反馈频率调节转速）
const motorAngle = computed(() => {
  if (!isRunning.value) return 0;
  const speedFactor = Math.max(5, (pvFreq.value / (maxFreq.value || 50)) * 40);
  return (ticks.value * speedFactor) % 360;
});

// 频率与电流的动态平滑仿真定时器（运行模式下自适应跟踪）
let simTimer: any = null;
onMounted(() => {
  simTimer = setInterval(() => {
    if (!props.isActiveMode) return;

    // 如果没有绑定真实设备变量，提供平滑加减速物理仿真反馈
    if (targetDevice.value?.variables?.[pvFreqVar.value] === undefined) {
      if (isRunning.value && !isFault.value) {
        // 加速爬升至设定频率
        const target = spFreq.value;
        const diff = target - localPvFreq.value;
        if (Math.abs(diff) > 0.1) {
          localPvFreq.value += Math.sign(diff) * Math.min(1.5, Math.abs(diff));
        } else {
          localPvFreq.value = target;
        }
        // 电流随频率线性上升并叠加轻微工业扰动
        const baseCur = ratedCurrent.value * (localPvFreq.value / (maxFreq.value || 50)) * 0.75;
        const jitter = (Math.sin(Date.now() / 1000) * 0.4);
        localCurrent.value = Math.max(0, Number((baseCur + jitter).toFixed(1)));
      } else {
        // 自由滑行减速到 0
        if (localPvFreq.value > 0.2) {
          localPvFreq.value -= 1.8;
        } else {
          localPvFreq.value = 0;
        }
        localCurrent.value = 0;
      }
    }
  }, 100);
});

onUnmounted(() => {
  if (simTimer) clearInterval(simTimer);
});

// 下发指令的通用方法
const writeToDevice = (key: string, value: number | boolean, overrideDevId?: any) => {
  const devId = resolveDeviceId(overrideDevId);
  if (devId != null) {
    const dev = devices.value.find(d => Number(d.id) === Number(devId));
    if (dev?.variables?.[key] !== undefined) {
      setDataPointMappingValue(devId, key, value);
    }
  }
};

// 设定频率弹窗/快速调整
const showFreqDialog = ref(false);
const tempFreqInput = ref(30.0);

const openFreqModal = (e: MouseEvent | TouchEvent) => {
  e.stopPropagation();
  if (!props.isActiveMode) return;
  if (!hasWritePermission.value) {
    showToast('当前用户权限不足，无法修改电机设定频率', 'warning');
    return;
  }
  tempFreqInput.value = spFreq.value;
  showFreqDialog.value = true;
};

const confirmFreqModal = () => {
  let val = Number(tempFreqInput.value);
  if (isNaN(val)) val = minFreq.value;
  val = Math.max(minFreq.value, Math.min(maxFreq.value, Number(val.toFixed(1))));
  localSpFreq.value = val;
  writeToDevice(spFreqVar.value, val, propOr('spFreqDeviceId', null));
  showFreqDialog.value = false;
  showToast(`电机频率设定已更新: ${val} Hz`, 'info');
};

const stepFrequency = (delta: number, e: MouseEvent | TouchEvent) => {
  e.stopPropagation();
  if (!props.isActiveMode) return;
  if (!hasWritePermission.value) {
    showToast('当前用户权限不足，无法修改参数', 'warning');
    return;
  }
  let next = Number((spFreq.value + delta).toFixed(1));
  next = Math.max(minFreq.value, Math.min(maxFreq.value, next));
  localSpFreq.value = next;
  writeToDevice(spFreqVar.value, next, propOr('spFreqDeviceId', null));
};

// 启动操作
const handleStart = (e: MouseEvent | TouchEvent) => {
  e.stopPropagation();
  if (!props.isActiveMode) return;
  if (!hasWritePermission.value) {
    showToast('当前用户角色无电机写控权限', 'warning');
    return;
  }
  if (isFault.value) {
    showToast('电机存在故障，必须先排查并复位后方可启动！', 'error');
    return;
  }
  if (!isReady.value) {
    showToast('电机未就绪（联锁保护条件未满足）', 'warning');
    return;
  }
  if (isRunning.value) {
    return;
  }

  // 写入启动指令
  localRunning.value = true;
  writeToDevice(startVar.value, true, propOr('startDeviceId', null));
  writeToDevice(runningVar.value, true, propOr('runningDeviceId', null));
  showToast(`[${motorTag.value}] 启动指令已下发`, 'success');

  // 若为点动脉冲，0.5s 后复位 startVar 脉冲
  setTimeout(() => {
    writeToDevice(startVar.value, false, propOr('startDeviceId', null));
  }, 500);
};

// 停止操作
const handleStop = (e: MouseEvent | TouchEvent) => {
  e.stopPropagation();
  if (!props.isActiveMode) return;
  if (!hasWritePermission.value) {
    showToast('当前用户角色无操作权限', 'warning');
    return;
  }
  if (!isRunning.value) return;

  localRunning.value = false;
  writeToDevice(stopVar.value, true, propOr('stopDeviceId', null));
  writeToDevice(runningVar.value, false, propOr('runningDeviceId', null));
  showToast(`[${motorTag.value}] 电机停机指令已下发`, 'info');

  setTimeout(() => {
    writeToDevice(stopVar.value, false, propOr('stopDeviceId', null));
  }, 500);
};

// 故障复位操作
const handleReset = (e: MouseEvent | TouchEvent) => {
  e.stopPropagation();
  if (!props.isActiveMode) return;
  if (!hasWritePermission.value) {
    showToast('当前用户角色无复位权限', 'warning');
    return;
  }

  localFault.value = false;
  localReady.value = true;
  writeToDevice(resetVar.value, true, propOr('resetDeviceId', null));
  writeToDevice(faultVar.value, false, propOr('faultDeviceId', null));
  showToast(`[${motorTag.value}] 故障告警已清除复位`, 'success');

  setTimeout(() => {
    writeToDevice(resetVar.value, false, propOr('resetDeviceId', null));
  }, 500);
};
</script>

<template>
  <div class="vfd-root w-full h-full rounded-xl shadow-xl flex flex-col justify-between select-none overflow-hidden font-sans relative"
    :class="[isFault ? 'vfd--fault' : '', isRunning ? 'vfd--running' : '']"
    :style="vfdThemeVars">

    <!-- 1. Header: 设备标签与通讯状态 -->
    <div class="vfd-header px-3 py-2 flex items-center justify-between shrink-0">
      <div class="flex items-center gap-1.5 min-w-0">
        <span class="vfd-tag px-1.5 py-0.5 rounded text-[10px] font-mono font-bold shrink-0">
          {{ motorTag }}
        </span>
        <span class="vfd-title-text font-bold text-xs truncate max-w-[130px]" :title="motorName">
          {{ motorName }}
        </span>
      </div>

      <div class="flex items-center gap-1.5 shrink-0">
        <span class="text-[9px] px-1.5 py-0.5 rounded font-mono"
          :class="isSignalOk ? 'vfd-pill--ok' : 'vfd-pill--err'">
          <span class="inline-block w-1.5 h-1.5 rounded-full mr-0.5"
            :class="isSignalOk ? 'vfd-pill-dot--ok' : 'vfd-pill-dot--err'"></span>
          {{ isSignalOk ? '通信正常' : '链路离线' }}
        </span>
      </div>
    </div>

    <!-- 2. Status & Animation Section: 电机动效与 4 状态信号灯 -->
    <div class="vfd-section p-2.5 flex items-center gap-3">
      <!-- 仿真电机视窗 -->
      <div class="vfd-well w-24 h-20 relative shrink-0 flex items-center justify-center rounded-lg p-1">
        <svg viewBox="0 0 120 90" class="w-full h-full">
          <defs>
            <linearGradient :id="'vfd-stator-' + component.id" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" style="stop-color: var(--vfd-stator1)" />
              <stop offset="50%" style="stop-color: var(--vfd-stator2)" />
              <stop offset="100%" style="stop-color: var(--vfd-stator3)" />
            </linearGradient>
            <linearGradient :id="'vfd-shaft-' + component.id" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" style="stop-color: var(--vfd-shaft1)" />
              <stop offset="50%" style="stop-color: var(--vfd-shaft2)" />
              <stop offset="100%" style="stop-color: var(--vfd-shaft3)" />
            </linearGradient>
          </defs>

          <!-- 电机底座 -->
          <rect x="25" y="68" width="70" height="6" rx="1.5" style="fill: var(--vfd-svg-base); stroke: var(--vfd-svg-stroke)" stroke-width="1" />
          <rect x="30" y="74" width="16" height="5" rx="1" style="fill: var(--vfd-svg-deep)" />
          <rect x="74" y="74" width="16" height="5" rx="1" style="fill: var(--vfd-svg-deep)" />

          <!-- 主轴 -->
          <rect x="6" y="42" width="22" height="14" rx="1.5" :fill="`url(#vfd-shaft-${component.id})`" style="stroke: var(--vfd-svg-stroke)" stroke-width="0.75" />
          <!-- 旋转指示器 -->
          <g :transform="`translate(12, 49) rotate(${motorAngle})`">
            <line x1="-4" y1="0" x2="4" y2="0" :style="{ stroke: isRunning ? 'var(--vfd-accent)' : 'var(--vfd-svg-stroke)' }" stroke-width="2" stroke-linecap="round" />
          </g>

          <!-- 定子外壳与散热肋片 -->
          <rect x="26" y="24" width="68" height="46" rx="4" :fill="`url(#vfd-stator-${component.id})`" style="stroke: var(--vfd-svg-stroke)" stroke-width="1" />
          <line x1="38" y1="24" x2="38" y2="70" style="stroke: var(--vfd-svg-stroke)" stroke-width="1.5" />
          <line x1="48" y1="24" x2="48" y2="70" style="stroke: var(--vfd-svg-stroke)" stroke-width="1.5" />
          <line x1="58" y1="24" x2="58" y2="70" style="stroke: var(--vfd-svg-stroke)" stroke-width="1.5" />
          <line x1="68" y1="24" x2="68" y2="70" style="stroke: var(--vfd-svg-stroke)" stroke-width="1.5" />
          <line x1="78" y1="24" x2="78" y2="70" style="stroke: var(--vfd-svg-stroke)" stroke-width="1.5" />

          <!-- 接线盒 -->
          <rect x="44" y="14" width="32" height="12" rx="2" style="fill: var(--vfd-svg-base); stroke: var(--vfd-svg-stroke)" stroke-width="1" />

          <!-- 后风扇罩 & 旋转动效扇叶 -->
          <path d="M 94 28 C 104 28 106 40 106 47 C 106 54 104 66 94 66 Z" style="fill: var(--vfd-svg-base); stroke: var(--vfd-svg-stroke)" stroke-width="1" />
          <g :transform="`translate(98, 47) rotate(${motorAngle})`">
            <line x1="-5" y1="0" x2="5" y2="0" :style="{ stroke: isRunning ? '#22c55e' : 'var(--vfd-svg-stroke)' }" stroke-width="2" />
            <line x1="0" y1="-5" x2="0" y2="5" :style="{ stroke: isRunning ? '#22c55e' : 'var(--vfd-svg-stroke)' }" stroke-width="2" />
          </g>
        </svg>

        <!-- 运行中心频率浮标 -->
        <span class="vfd-pv-badge absolute bottom-1 right-1 text-[9px] font-mono font-bold px-1 rounded">
          {{ pvFreq.toFixed(1) }}Hz
        </span>
      </div>

      <!-- 4 状态信号列表 -->
      <div class="flex-1 grid grid-cols-2 gap-1.5 text-[11px]">
        <!-- 准备 -->
        <div class="vfd-cell flex items-center gap-1.5 p-1 rounded">
          <span class="w-2.5 h-2.5 rounded-full shrink-0 transition-all"
            :style="isReady
              ? { backgroundColor: 'var(--vfd-accent)', boxShadow: '0 0 6px var(--vfd-accent)' }
              : { backgroundColor: 'var(--vfd-well-border)' }"></span>
          <span class="font-bold" :style="{ color: isReady ? 'var(--vfd-title)' : 'var(--vfd-faint)' }">就绪</span>
        </div>

        <!-- 运行 -->
        <div class="vfd-cell flex items-center gap-1.5 p-1 rounded">
          <span class="w-2.5 h-2.5 rounded-full shrink-0 transition-all"
            :class="isRunning ? 'animate-pulse' : ''"
            :style="isRunning
              ? { backgroundColor: 'var(--vfd-ok)', boxShadow: '0 0 6px var(--vfd-ok)' }
              : { backgroundColor: 'var(--vfd-well-border)' }"></span>
          <span class="font-bold" :style="{ color: isRunning ? 'var(--vfd-ok)' : 'var(--vfd-faint)' }">运行</span>
        </div>

        <!-- 故障 -->
        <div class="vfd-cell flex items-center gap-1.5 p-1 rounded">
          <span class="w-2.5 h-2.5 rounded-full shrink-0 transition-all"
            :class="isFault ? 'animate-ping' : ''"
            :style="isFault
              ? { backgroundColor: 'var(--vfd-err)', boxShadow: '0 0 8px var(--vfd-err)' }
              : { backgroundColor: 'var(--vfd-well-border)' }"></span>
          <span class="font-bold" :style="{ color: isFault ? 'var(--vfd-err)' : 'var(--vfd-faint)' }">故障</span>
        </div>

        <!-- 信号 -->
        <div class="vfd-cell flex items-center gap-1.5 p-1 rounded">
          <span class="w-2.5 h-2.5 rounded-full shrink-0 transition-all"
            :style="isSignalOk
              ? { backgroundColor: 'var(--vfd-accent)', boxShadow: '0 0 6px var(--vfd-accent)' }
              : { backgroundColor: 'var(--vfd-well-border)' }"></span>
          <span :style="{ color: isSignalOk ? 'var(--vfd-title)' : 'var(--vfd-faint)' }">信号</span>
        </div>
      </div>
    </div>

    <!-- 3. Frequency Section: 频率设定与反馈双轨显示 -->
    <div class="vfd-section px-3 py-2 space-y-1.5">
      <div class="flex items-center justify-between text-xs">
        <span class="vfd-muted-text text-[10px] font-bold flex items-center gap-1">
          <Sliders class="w-3 h-3" style="color: var(--vfd-accent)" />
          频率调节
        </span>

        <!-- 设定值微调器 -->
        <div class="vfd-well flex items-center gap-1 rounded px-1 py-0.5">
          <span class="text-[9px] vfd-muted-text mr-0.5">设定SP:</span>
          <button @click="stepFrequency(-freqStep, $event)"
            :disabled="!isActiveMode || !hasWritePermission"
            class="vfd-step-btn p-0.5 rounded active:scale-95 disabled:opacity-40 cursor-pointer"
            title="步进减少频率">
            <Minus class="w-2.5 h-2.5" />
          </button>

          <!-- 点击弹窗输入 -->
          <span @click="openFreqModal($event)"
            class="font-mono text-xs font-bold cursor-pointer hover:underline px-1"
            style="color: var(--vfd-sp)"
            title="点击设定具体频率">
            {{ spFreq.toFixed(1) }}
          </span>

          <button @click="stepFrequency(freqStep, $event)"
            :disabled="!isActiveMode || !hasWritePermission"
            class="vfd-step-btn p-0.5 rounded active:scale-95 disabled:opacity-40 cursor-pointer"
            title="步进增加频率">
            <Plus class="w-2.5 h-2.5" />
          </button>
          <span class="text-[9px] vfd-faint-text">Hz</span>
        </div>
      </div>

      <!-- 反馈频率大字显示与双轨进度条 -->
      <div class="flex items-baseline justify-between pt-0.5">
        <span class="text-[10px] vfd-muted-text">运行反馈(PV)</span>
        <div class="flex items-baseline gap-1">
          <span class="font-mono text-base font-black tracking-tight" style="color: var(--vfd-accent)">
            {{ pvFreq.toFixed(1) }}
          </span>
          <span class="text-[10px] font-mono vfd-faint-text">Hz</span>
        </div>
      </div>

      <!-- 频率对比柱条 -->
      <div class="vfd-track relative w-full h-2 rounded-full overflow-hidden">
        <!-- PV 填充条 -->
        <div class="h-full rounded-full transition-all duration-300"
          :style="{ width: `${Math.min(100, Math.max(0, (pvFreq / (maxFreq || 50)) * 100))}%`, background: 'var(--vfd-pv-grad)' }"></div>
        <!-- SP 游标指示线 -->
        <div class="absolute top-0 bottom-0 w-1 -ml-0.5 pointer-events-none"
          :style="{ left: `${Math.min(100, Math.max(0, (spFreq / (maxFreq || 50)) * 100))}%`, backgroundColor: 'var(--vfd-sp)', boxShadow: '0 0 4px var(--vfd-sp)' }"
          title="SP 设定值位置"></div>
      </div>
    </div>

    <!-- 4. Current & Load Section: 负载电流监控 -->
    <div class="vfd-section px-3 py-1.5 space-y-1">
      <div class="flex items-center justify-between text-[11px]">
        <span class="vfd-muted-text text-[10px] font-bold flex items-center gap-1">
          <Zap class="w-3 h-3" style="color: var(--vfd-sp)" />
          运行电流
        </span>
        <div class="flex items-center gap-1.5 font-mono">
          <span class="font-bold text-xs" style="color: var(--vfd-title)">{{ motorCurrent.toFixed(1) }} A</span>
          <span class="text-[9px] vfd-faint-text">/ 额定 {{ ratedCurrent.toFixed(1) }}A</span>
        </div>
      </div>

      <!-- 负荷率进度条 -->
      <div class="w-full flex items-center gap-2">
        <div class="vfd-track flex-1 h-1.5 rounded-full overflow-hidden">
          <div class="h-full rounded-full transition-all duration-300"
            :style="{ width: `${Math.min(100, loadPercent)}%`, backgroundColor: loadStatus.color }"></div>
        </div>
        <span class="text-[9px] font-mono px-1 rounded shrink-0 font-semibold"
          :class="loadStatus.pulse ? 'animate-pulse' : ''"
          :style="loadStatus.badge">
          {{ loadPercent.toFixed(0) }}%
        </span>
      </div>
    </div>

    <!-- 5. Controls Section: 启、停、复位控制按钮（符合工业防误触互锁） -->
    <div class="vfd-footer p-2.5 grid grid-cols-3 gap-1.5 shrink-0">
      <!-- 启动按钮 -->
      <button @click="handleStart($event)"
        :disabled="!isActiveMode || isRunning || isFault || !isReady || !hasWritePermission"
        class="vfd-btn vfd-btn--start h-9 rounded-lg font-bold text-xs flex items-center justify-center gap-1 border transition-all cursor-pointer select-none"
        title="启动电机（就绪时有效）">
        <Power class="w-3.5 h-3.5" />
        <span>启动</span>
      </button>

      <!-- 停止按钮 -->
      <button @click="handleStop($event)"
        :disabled="!isActiveMode || !isRunning || !hasWritePermission"
        class="vfd-btn vfd-btn--stop h-9 rounded-lg font-bold text-xs flex items-center justify-center gap-1 border transition-all cursor-pointer select-none"
        title="停止电机运行">
        <Square class="w-3.5 h-3.5 fill-current" />
        <span>停止</span>
      </button>

      <!-- 复位按钮 -->
      <button @click="handleReset($event)"
        :disabled="!isActiveMode || !hasWritePermission"
        class="vfd-btn vfd-btn--reset h-9 rounded-lg font-bold text-xs flex items-center justify-center gap-1 border transition-all cursor-pointer select-none"
        :class="isFault ? 'vfd-btn--reset-fault animate-pulse' : ''"
        title="清除故障告警并复位状态">
        <RotateCcw class="w-3.5 h-3.5" />
        <span>复位</span>
      </button>
    </div>

    <!-- 频率直接输入浮动对话框 -->
    <div v-if="showFreqDialog" @click.stop
      class="vfd-overlay absolute inset-0 backdrop-blur-xs flex flex-col justify-center items-center p-4 z-30">
      <div class="vfd-dialog w-full p-3 rounded-xl shadow-2xl space-y-3 text-center">
        <div class="flex items-center justify-between text-xs font-bold" style="color: var(--vfd-title)">
          <span>设定电机频率</span>
          <span class="text-[10px] vfd-faint-text font-normal">{{ minFreq }} ~ {{ maxFreq }} Hz</span>
        </div>
        <div class="flex items-center justify-center gap-2">
          <input type="number" v-model="tempFreqInput" :min="minFreq" :max="maxFreq" step="0.1"
            class="vfd-input w-24 rounded px-2 py-1 text-center font-mono text-base font-bold focus:outline-none" />
          <span class="text-xs vfd-muted-text font-mono">Hz</span>
        </div>
        <div class="flex items-center gap-2 pt-1">
          <button @click="showFreqDialog = false"
            class="vfd-dialog-cancel flex-1 py-1 rounded text-xs cursor-pointer">
            取消
          </button>
          <button @click="confirmFreqModal"
            class="vfd-dialog-confirm flex-1 py-1 rounded text-xs font-bold text-white cursor-pointer">
            确定
          </button>
        </div>
      </div>
    </div>

  </div>
</template>

<style scoped>
/* ===== 风格主题（CSS 变量驱动，全部取值来自 --vfd-* 令牌）===== */
.vfd-root {
  background: var(--vfd-page);
  border: 1px solid var(--vfd-border);
  color: var(--vfd-body);
  backdrop-filter: var(--vfd-blur);
  -webkit-backdrop-filter: var(--vfd-blur);
}
.vfd-root.vfd--running {
  border-color: var(--vfd-accent);
  box-shadow: 0 0 12px var(--vfd-accent-glow);
}
.vfd-root.vfd--fault {
  border-color: rgba(239, 68, 68, 0.7);
  box-shadow: 0 0 16px rgba(239, 68, 68, 0.3);
}

/* Header */
.vfd-header {
  background: var(--vfd-header);
  border-bottom: 1px solid var(--vfd-divider);
}
.vfd-tag {
  background: var(--vfd-tag-bg);
  color: var(--vfd-tag-text);
  border: 1px solid var(--vfd-tag-border);
}
.vfd-title-text { color: var(--vfd-title); }
.vfd-pill--ok { background: var(--vfd-ok-soft); color: var(--vfd-ok); }
.vfd-pill--err { background: var(--vfd-err-soft); color: var(--vfd-err); }
.vfd-pill-dot--ok { background: var(--vfd-ok); box-shadow: 0 0 4px var(--vfd-ok); }
.vfd-pill-dot--err { background: var(--vfd-err); }

/* Sections & wells */
.vfd-section {
  background: var(--vfd-section);
  border-bottom: 1px solid var(--vfd-divider);
}
.vfd-well {
  background: var(--vfd-well);
  border: 1px solid var(--vfd-well-border);
}
.vfd-cell {
  background: var(--vfd-well-soft);
  border: 1px solid var(--vfd-well-border);
}
.vfd-muted-text { color: var(--vfd-muted); }
.vfd-faint-text { color: var(--vfd-faint); }
.vfd-pv-badge {
  background: var(--vfd-overlay);
  color: var(--vfd-accent);
}
.vfd-track {
  background: var(--vfd-well);
  border: 1px solid var(--vfd-well-border);
}

/* 频率微调按钮 */
.vfd-step-btn {
  color: var(--vfd-body);
}
.vfd-step-btn:hover:not(:disabled) {
  background: var(--vfd-hover);
}

/* 控制按钮（启/停为工业语义色，复位随主题） */
.vfd-btn:disabled {
  background: var(--vfd-btn-disabled-bg);
  color: var(--vfd-faint);
  border-color: var(--vfd-divider);
  cursor: not-allowed;
  opacity: 0.5;
}
.vfd-btn:not(:disabled):active { transform: scale(0.95); }

.vfd-btn--start:not(:disabled) {
  background: #059669;
  color: #fff;
  border-color: #10b981;
  box-shadow: 0 4px 6px -1px rgba(5, 150, 105, 0.35);
}
.vfd-btn--start:not(:disabled):hover { background: #10b981; }

.vfd-btn--stop:not(:disabled) {
  background: #e11d48;
  color: #fff;
  border-color: #f43f5e;
  box-shadow: 0 4px 6px -1px rgba(225, 29, 72, 0.35);
}
.vfd-btn--stop:not(:disabled):hover { background: #f43f5e; }

.vfd-btn--reset:not(:disabled) {
  background: var(--vfd-btn-bg);
  color: var(--vfd-btn-text);
  border-color: var(--vfd-btn-border);
}
.vfd-btn--reset:not(:disabled):hover { background: var(--vfd-btn-bg-hover); }
.vfd-btn--reset.vfd-btn--reset-fault {
  background: #d97706;
  color: #fff;
  border-color: #fbbf24;
  box-shadow: 0 0 10px rgba(245, 158, 11, 0.5);
}
.vfd-btn--reset.vfd-btn--reset-fault:hover { background: #f59e0b; }

/* 频率输入对话框 */
.vfd-overlay {
  background: var(--vfd-overlay);
}
.vfd-dialog {
  background: var(--vfd-dialog);
  border: 1px solid var(--vfd-dialog-border);
}
.vfd-input {
  background: var(--vfd-well);
  border: 1px solid var(--vfd-accent);
  color: var(--vfd-accent);
}
.vfd-input:focus {
  box-shadow: 0 0 0 1px var(--vfd-accent);
}
.vfd-dialog-cancel {
  background: var(--vfd-btn-bg);
  color: var(--vfd-btn-text);
}
.vfd-dialog-cancel:hover { background: var(--vfd-btn-bg-hover); }
.vfd-dialog-confirm {
  background: var(--vfd-accent);
}
.vfd-dialog-confirm:hover { filter: brightness(1.1); }
</style>
