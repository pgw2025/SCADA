<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';
import { getWidgetDef, getMenuIcon } from '../../widgetRegistry';
import { isSamePageRef } from '../../utils/pageId';
import type { HmiMenuItem } from '../../types';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

// ===== 导航菜单图元（nav-menu）：桌面顶部横条 / 移动底部 Tab 栏 =====
// 数据全部来自 props.menuItems（Inspector 编辑，PropsJson 落库），此处仅渲染。
// 跳转不在本组件处理：菜单项带 data-nav-page 标记，由 CanvasPanel 统一分发 navigateToPage。
const menuStyle = computed(() => propOr('menuStyle', 'navy-midnight'));
const menuDevice = computed<'desktop' | 'mobile'>(() =>
  (propOr('menuDevice', 'desktop') as 'desktop' | 'mobile'));
const menuItems = computed<HmiMenuItem[]>(() => {
  const raw = props.component.props.menuItems;
  return Array.isArray(raw) && raw.length
    ? (raw as HmiMenuItem[])
    : (getWidgetDef('nav-menu')?.defaultProps().menuItems as HmiMenuItem[]);
});
const menuAccentColor = computed(() => propOr('menuAccentColor', '#38bdf8'));
const menuFontSize = computed(() => Number(propOr('menuFontSize', 14)));
// 归一化比较：targetPageId 可能是 srv-{serverId}（新配置）或本地 id，currentPageId 亦随会话双轨
const isCurrentMenuItem = (item: HmiMenuItem) =>
  !!item.targetPageId && isSamePageRef(item.targetPageId, props.currentPageId);

// 5 套风格主题计算：桌面顶部导航条 / 移动端底部标签栏
const navMenuTheme = computed(() => {
  const style = menuStyle.value;
  const customAccent = menuAccentColor.value;

  if (style === 'pure-white') {
    const accent = customAccent && customAccent !== '#38bdf8' ? customAccent : '#2563eb';
    return {
      background: '#ffffff',
      border: '1px solid #e2e8f0',
      backdropFilter: 'none',
      accent,
      accentSoft: 'rgba(37,99,235,0.08)',
      itemText: '#64748b',
      activeText: accent,
      isLight: true,
    };
  }

  if (style === 'titanium-light') {
    const accent = customAccent && customAccent !== '#38bdf8' ? customAccent : '#0284c7';
    return {
      background: 'linear-gradient(180deg, #f8fafc 0%, #f1f5f9 100%)',
      border: '1px solid #cbd5e1',
      backdropFilter: 'none',
      accent,
      accentSoft: 'rgba(2,132,199,0.1)',
      itemText: '#475569',
      activeText: accent,
      isLight: true,
    };
  }

  if (style === 'slate-dark') {
    const accent = customAccent || '#38bdf8';
    return {
      background: 'linear-gradient(180deg, #1e293b 0%, #0f172a 100%)',
      border: '1px solid #334155',
      backdropFilter: 'none',
      accent,
      accentSoft: 'rgba(56,189,248,0.15)',
      itemText: '#94a3b8',
      activeText: accent,
      isLight: false,
    };
  }

  if (style === 'translucent-frost') {
    const accent = customAccent || '#38bdf8';
    return {
      background: 'rgba(15, 23, 42, 0.82)',
      border: '1px solid rgba(255,255,255,0.15)',
      backdropFilter: 'blur(8px)',
      accent,
      accentSoft: 'rgba(56,189,248,0.18)',
      itemText: '#cbd5e1',
      activeText: accent,
      isLight: false,
    };
  }

  if (style === 'eco-green') {
    const accent = customAccent && customAccent !== '#38bdf8' ? customAccent : '#34d399';
    return {
      background: 'linear-gradient(180deg, #073a26 0%, #052c1c 55%, #032015 100%)',
      border: '1px solid #064e3b',
      backdropFilter: 'none',
      accent,
      accentSoft: 'rgba(52,211,153,0.16)',
      itemText: '#7fd9b8',
      activeText: accent,
      isLight: false,
    };
  }

  if (style === 'carbon-orange') {
    const accent = customAccent && customAccent !== '#38bdf8' ? customAccent : '#f59e0b';
    return {
      background: 'linear-gradient(180deg, #2a1b0c 0%, #201407 50%, #170d04 100%)',
      border: '1px solid #78350f',
      backdropFilter: 'none',
      accent,
      accentSoft: 'rgba(245,158,11,0.14)',
      itemText: '#cfaa85',
      activeText: accent,
      isLight: false,
    };
  }

  // 默认：深海商务暗蓝 (Navy Midnight)
  const accent = customAccent || '#38bdf8';
  return {
    background: 'linear-gradient(180deg, #0b172a 0%, #081a36 60%, #061426 100%)',
    border: '1px solid #1e293b',
    backdropFilter: 'none',
    accent,
    accentSoft: 'rgba(56,189,248,0.16)',
    itemText: '#9fb6cc',
    activeText: accent,
    isLight: false,
  };
});
</script>

<template>
<div
      class="relative w-full h-full overflow-hidden select-none flex items-stretch" :style="{
        background: navMenuTheme.background,
        border: navMenuTheme.border,
        backdropFilter: navMenuTheme.backdropFilter,
        WebkitBackdropFilter: navMenuTheme.backdropFilter,
      }">
      <!-- 顶部/底部流光刻线（与主题风格呼应） -->
      <div v-if="menuDevice === 'desktop'" class="absolute inset-x-0 bottom-0 h-[1.5px]"
        :style="{ background: navMenuTheme.accent, opacity: navMenuTheme.isLight ? 0.3 : 0.6 }" />
      <div v-else class="absolute inset-x-0 top-0 h-[1.5px]"
        :style="{ background: navMenuTheme.accent, opacity: navMenuTheme.isLight ? 0.3 : 0.6 }" />

      <!-- 桌面端：横向均分导航项（图标+文字水平排列，当前项底部高亮条） -->
      <div v-if="menuDevice === 'desktop'" class="relative z-10 flex w-full h-full">
        <div v-for="item in menuItems" :key="item.text + item.targetPageId"
          class="relative flex-1 flex items-center justify-center gap-2 h-full transition-colors duration-200"
          :class="isActiveMode && item.targetPageId ? (navMenuTheme.isLight ? 'cursor-pointer hover:bg-black/5' : 'cursor-pointer hover:bg-white/5') : ''"
          :data-nav-page="item.targetPageId || undefined" :style="{
            color: isCurrentMenuItem(item) ? navMenuTheme.activeText : navMenuTheme.itemText,
            fontWeight: isCurrentMenuItem(item) ? '600' : '500',
          }">
          <component :is="getMenuIcon(item.icon)" class="w-4 h-4 shrink-0"
            :style="{ color: isCurrentMenuItem(item) ? navMenuTheme.accent : navMenuTheme.itemText }" />
          <span class="truncate tracking-wide" :style="{
            fontSize: `${menuFontSize}px`,
            textShadow: isCurrentMenuItem(item) && !navMenuTheme.isLight ? `0 0 8px ${navMenuTheme.accent}` : 'none'
          }">
            {{ item.text }}
          </span>
          <!-- 当前项底部高亮条 -->
          <div v-if="isCurrentMenuItem(item)" class="absolute bottom-0 left-0 right-0 h-[3px]" :style="{
            background: navMenuTheme.accent,
            boxShadow: !navMenuTheme.isLight ? `0 0 10px ${navMenuTheme.accent}` : 'none'
          }" />
        </div>
      </div>

      <!-- 移动端：底部 Tab 栏（图标在上文字在下，当前项整体提亮） -->
      <div v-else class="relative z-10 flex w-full h-full">
        <div v-for="item in menuItems" :key="item.text + item.targetPageId"
          class="relative flex-1 flex flex-col items-center justify-center gap-0.5 h-full min-w-0 transition-colors duration-200"
          :class="isActiveMode && item.targetPageId ? (navMenuTheme.isLight ? 'cursor-pointer active:bg-black/5' : 'cursor-pointer active:bg-white/10') : ''"
          :data-nav-page="item.targetPageId || undefined" :style="{
            color: isCurrentMenuItem(item) ? navMenuTheme.activeText : navMenuTheme.itemText,
          }">
          <component :is="getMenuIcon(item.icon)" class="w-[18px] h-[18px] shrink-0" :style="{
            color: isCurrentMenuItem(item) ? navMenuTheme.accent : navMenuTheme.itemText,
            filter: isCurrentMenuItem(item) && !navMenuTheme.isLight ? `drop-shadow(0 0 6px ${navMenuTheme.accent})` : 'none',
          }" />
          <span class="truncate max-w-full px-0.5 leading-none"
            :style="{ fontSize: `${menuFontSize}px`, fontWeight: isCurrentMenuItem(item) ? '600' : '400' }">
            {{ item.text }}
          </span>
          <!-- 当前项顶部高亮条 -->
          <div v-if="isCurrentMenuItem(item)" class="absolute top-0 left-0 right-0 h-[3px]" :style="{
            background: navMenuTheme.accent,
            boxShadow: !navMenuTheme.isLight ? `0 0 10px ${navMenuTheme.accent}` : 'none'
          }" />
        </div>
      </div>
    </div>
</template>
