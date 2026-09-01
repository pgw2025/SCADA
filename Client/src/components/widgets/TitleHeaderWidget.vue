<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

// ===== 大屏标题背景图元（title-header）5套简约大方风格（含浅色/深色/通透） × 桌面/移动 =====
// 所有内容从 props 读取（含注册表默认兜底），文案/风格/时钟/状态均可在属性面板编辑。
const headerStyle = computed(() =>
  (propOr('headerStyle', 'navy-midnight') as string));
const headerDevice = computed<'desktop' | 'mobile'>(() =>
  (propOr('headerDevice', 'desktop') as 'desktop' | 'mobile'));
const headerTitle = computed(() => propOr('headerTitle', '工业互联网智能监控大屏'));
const headerSubtitle = computed(() => propOr('headerSubtitle', ''));
const headerLogoText = computed(() => propOr('headerLogoText', 'SCADA'));
const headerShowClock = computed(() => propOr('headerShowClock', true));
const headerShowStatus = computed(() => propOr('headerShowStatus', true));
const headerStatusText = computed(() => propOr('headerStatusText', '系统运行正常'));
const headerGlowColor = computed(() => propOr('headerGlowColor', '#38bdf8'));

// 5 套风格主题（2 浅色 + 2 深色 + 1 通透悬浮）：极简亮白 / 工业钛灰 / 经典石板深灰 / 深海商务暗蓝 / 悬浮通透胶囊
const headerTheme = computed(() => {
  const glow = headerGlowColor.value;
  const style = headerStyle.value;

  // 1. 浅色系：极简亮白 (Pure Crisp White)
  if (style === 'pure-white') {
    return {
      background: '#ffffff',
      border: '1px solid #e2e8f0',
      borderRadius: '2px',
      backdropFilter: 'none',
      accent: glow && glow !== '#38bdf8' ? glow : '#2563eb',
      accentSoft: 'rgba(37,99,235,0.08)',
      text: '#0f172a',
      subText: '#64748b',
      isLight: true,
    };
  }

  // 2. 浅色系：工业钛灰浅色 (Titanium Light Grey)
  if (style === 'titanium-light') {
    return {
      background: 'linear-gradient(180deg, #f8fafc 0%, #f1f5f9 100%)',
      border: '1px solid #cbd5e1',
      borderRadius: '2px',
      backdropFilter: 'none',
      accent: glow && glow !== '#38bdf8' ? glow : '#0284c7',
      accentSoft: 'rgba(2,132,199,0.1)',
      text: '#1e293b',
      subText: '#475569',
      isLight: true,
    };
  }

  // 3. 深色系：经典石板深灰 (Classic Slate Dark)
  if (style === 'slate-dark') {
    return {
      background: 'linear-gradient(180deg, #1e293b 0%, #0f172a 100%)',
      border: '1px solid #334155',
      borderRadius: '2px',
      backdropFilter: 'none',
      accent: glow || '#38bdf8',
      accentSoft: 'rgba(56,189,248,0.15)',
      text: '#f8fafc',
      subText: '#94a3b8',
      isLight: false,
    };
  }

  // 4. 通透系：悬浮通透胶囊 (Adaptive Frost Capsule)
  if (style === 'translucent-frost') {
    return {
      background: 'rgba(15, 23, 42, 0.82)',
      border: '1px solid rgba(255,255,255,0.15)',
      borderRadius: '8px',
      backdropFilter: 'blur(8px)',
      accent: glow || '#38bdf8',
      accentSoft: 'rgba(56,189,248,0.18)',
      text: '#ffffff',
      subText: '#cbd5e1',
      isLight: false,
    };
  }

  // 兼容旧预设：生态绿 (Eco Green)
  if (style === 'eco-green') {
    return {
      background: 'linear-gradient(180deg, #073a26 0%, #052c1c 55%, #032015 100%)',
      border: '1px solid #064e3b',
      borderRadius: '2px',
      backdropFilter: 'none',
      accent: glow || '#34d399',
      accentSoft: 'rgba(52,211,153,0.16)',
      text: '#eafff5',
      subText: '#7fd9b8',
      isLight: false,
    };
  }

  // 兼容旧预设：机能碳纤橙 (Carbon Orange)
  if (style === 'carbon-orange') {
    return {
      background: 'linear-gradient(180deg, #2a1b0c 0%, #201407 50%, #170d04 100%)',
      border: '1px solid #78350f',
      borderRadius: '2px',
      backdropFilter: 'none',
      accent: glow || '#f59e0b',
      accentSoft: 'rgba(245,158,11,0.14)',
      text: '#fff3e0',
      subText: '#cfaa85',
      isLight: false,
    };
  }

  // 默认（第4种）：深海商务暗蓝 (Navy Midnight / tech-blue)
  return {
    background: 'linear-gradient(180deg, #0b172a 0%, #081a36 60%, #061426 100%)',
    border: '1px solid #1e293b',
    borderRadius: '2px',
    backdropFilter: 'none',
    accent: glow || '#38bdf8',
    accentSoft: 'rgba(56,189,248,0.16)',
    text: '#ffffff',
    subText: '#7dd3fc',
    isLight: false,
  };
});
</script>

<template>
<div class="relative w-full h-full overflow-hidden select-none"
      :style="{
        background: headerTheme.background,
        color: headerTheme.text,
        border: headerTheme.border,
        borderRadius: headerTheme.borderRadius || '2px',
        backdropFilter: headerTheme.backdropFilter || 'none',
      }">
      <!-- 装饰 SVG：随画布等比拉伸，极简线条设计 -->
      <svg class="absolute inset-0 w-full h-full pointer-events-none" viewBox="0 0 100 100" preserveAspectRatio="none">
        <!-- 极简亮白风格：极致清爽，底部 1px 细分界线 -->
        <template v-if="headerStyle === 'pure-white'">
          <line x1="0" y1="99.5" x2="100" y2="99.5" stroke="#e2e8f0" stroke-width="0.8" />
          <line x1="0" y1="0.5" x2="100" y2="0.5" stroke="#f1f5f9" stroke-width="0.5" />
        </template>

        <!-- 工业钛灰风格：底部 2px 浅蓝装饰线条 -->
        <template v-else-if="headerStyle === 'titanium-light'">
          <line x1="0" y1="0.5" x2="100" y2="0.5" stroke="#e2e8f0" stroke-width="0.5" />
          <rect x="0" y="97.5" width="100" height="2.5" :fill="headerTheme.accent" opacity="0.85" />
        </template>

        <!-- 经典石板深灰风格：沉稳严谨，底部 1.5px 纯净细线 -->
        <template v-else-if="headerStyle === 'slate-dark'">
          <line x1="0" y1="0.7" x2="100" y2="0.7" :stroke="headerTheme.accent" stroke-width="0.5" opacity="0.25" />
          <rect x="0" y="98" width="100" height="2" :fill="headerTheme.accent" opacity="0.6" />
        </template>

        <!-- 悬浮通透胶囊风格：轻量微边框 -->
        <template v-else-if="headerStyle === 'translucent-frost'">
          <line x1="10" y1="99" x2="90" y2="99" :stroke="headerTheme.accent" stroke-width="0.6" opacity="0.3" />
        </template>

        <!-- 生态绿：菱形光带 + 中心能效光环 -->
        <template v-else-if="headerStyle === 'eco-green'">
          <line x1="0" y1="0.7" x2="100" y2="0.7" :stroke="headerTheme.accent" stroke-width="0.5" opacity="0.35" />
          <rect x="0" y="97" width="100" height="3" :fill="headerTheme.accent" opacity="0.5" />
          <rect x="8" y="32" width="10" height="10" :fill="headerTheme.accent" opacity="0.25"
            transform="rotate(45 13 37)" />
          <rect x="82" y="32" width="10" height="10" :fill="headerTheme.accent" opacity="0.25"
            transform="rotate(45 87 37)" />
          <line x1="0" y1="50" x2="100" y2="50" :stroke="headerTheme.accent" stroke-width="0.6" opacity="0.22"
            stroke-dasharray="2 3" />
        </template>

        <!-- 机能碳纤橙：斜纹装饰 -->
        <template v-else-if="headerStyle === 'carbon-orange'">
          <line x1="0" y1="0.7" x2="100" y2="0.7" :stroke="headerTheme.accent" stroke-width="0.5" opacity="0.35" />
          <rect x="0" y="97" width="100" height="3" :fill="headerTheme.accent" opacity="0.5" />
          <g :stroke="headerTheme.accent" stroke-width="1.1" opacity="0.16" stroke-linecap="round">
            <line x1="0" y1="108" x2="108" y2="0" />
            <line x1="12" y1="112" x2="112" y2="12" />
            <line x1="-12" y1="92" x2="92" y2="-12" />
          </g>
        </template>

        <!-- 深海商务暗蓝（默认 / tech-blue）：顶部微光 + 底部科技线条 -->
        <template v-else>
          <line x1="0" y1="0.7" x2="100" y2="0.7" :stroke="headerTheme.accent" stroke-width="0.5" opacity="0.35" />
          <rect x="0" y="97.5" width="100" height="2.5" :fill="headerTheme.accent" opacity="0.7" />
          <polygon :points="'0,12 18,0 28,0 0,28'" :fill="headerTheme.accentSoft" />
          <polygon :points="'100,88 82,100 72,100 100,72'" :fill="headerTheme.accentSoft" />
        </template>
      </svg>

      <!-- 桌面大屏布局：Logo｜主标题+副标题两行｜右端时钟+状态 -->
      <div v-if="headerDevice === 'desktop'"
        class="relative z-10 w-full h-full flex flex-col justify-center px-5 gap-0.5"
        :style="{ fontWeight: bold ? '700' : '600' }">
        <div class="flex items-center gap-3 min-w-0">
          <div class="shrink-0 flex items-center gap-1.5 border-2 rounded-md px-2.5 h-7"
            :style="{ color: headerTheme.accent, borderColor: headerTheme.accent, fontSize: `${fontSize}px` }">
            <span class="w-1.5 h-1.5 rounded-full" :style="{ background: headerTheme.accent }" />
            <span class="font-mono tracking-wider">{{ headerLogoText }}</span>
          </div>
          <span class="min-w-0 truncate" :style="{ fontSize: `${fontSize + 3}px`, color: headerTheme.text }">{{
            headerTitle
            }}</span>
          <div class="ml-auto shrink-0 flex items-center gap-3">
            <span v-if="headerShowClock" class="font-mono"
              :style="{ fontSize: `${fontSize}px`, color: headerTheme.accent }">{{ timeString }}</span>
            <span v-if="headerShowStatus" class="flex items-center gap-1.5 rounded-full px-2.5 h-6"
              :style="{ background: headerTheme.accentSoft, color: headerTheme.text, fontSize: `${Math.max(10, fontSize - 3)}px` }">
              <span class="w-1.5 h-1.5 rounded-full animate-pulse" :style="{ background: headerTheme.accent }" />
              {{ headerStatusText }}
            </span>
          </div>
        </div>
        <div v-if="headerSubtitle" class="truncate"
          :style="{ fontSize: `${Math.max(10, fontSize - 2)}px`, color: headerTheme.subText, letterSpacing: '0.1em' }">
          {{ headerSubtitle }}
        </div>
      </div>

      <!-- 移动竖屏布局：Logo｜标题｜右端时钟+状态点（紧凑单行） -->
      <div v-else class="relative z-10 w-full h-full flex items-center gap-2 px-2"
        :style="{ fontSize: `${fontSize}px`, fontWeight: bold ? '700' : '600' }">
        <span class="shrink-0 font-mono tracking-wide" :style="{ color: headerTheme.accent }">{{ headerLogoText
          }}</span>
        <span class="min-w-0 truncate" :style="{ color: headerTheme.text }">{{ headerTitle }}</span>
        <span v-if="headerShowClock" class="ml-auto shrink-0 font-mono"
          :style="{ fontSize: `${Math.max(10, fontSize - 2)}px`, color: headerTheme.accent }">{{ timeString }}</span>
        <span v-if="headerShowStatus" class="shrink-0 w-2 h-2 rounded-full animate-pulse"
          :style="{ background: headerTheme.accent }" :title="headerStatusText" />
      </div>
    </div>
</template>
