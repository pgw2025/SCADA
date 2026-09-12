<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';
import { useVfdPanelTheme } from './useVfdPanelTheme';

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

// 复用共享主题体系（与 VFD 面板/多变量看板同源），收敛掉旧的 headerTheme 平行实现
const { panelTheme } = useVfdPanelTheme({ panelStyle: headerStyle, panelAccentColor: headerGlowColor });

// 供模板消费的标题头主题：字段结构保持不变
const headerTheme = computed(() => {
  const t = panelTheme.value;
  return {
    background: t.page,
    border: `1px solid ${t.border}`,
    borderRadius: headerStyle.value === 'translucent-frost' ? '8px' : '2px',
    backdropFilter: t.blur && t.blur !== 'none' ? t.blur : 'none',
    accent: t.accent,
    accentSoft: t.btnBg,
    text: t.title,
    subText: t.muted,
    isLight: t.isLight,
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
