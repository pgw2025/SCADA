<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';
import { useVfdPanelTheme } from './useVfdPanelTheme';
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
// 字号硬兜底按端型区分：移动端底部标签栏文字更小，清空配置时不回退到桌面偏大的 14px
const menuFontSize = computed(() => Number(propOr('menuFontSize', menuDevice.value === 'mobile' ? 12 : 14)));
// 归一化比较：targetPageId 可能是 srv-{serverId}（新配置）或本地 id，currentPageId 亦随会话双轨
const isCurrentMenuItem = (item: HmiMenuItem) =>
  !!item.targetPageId && isSamePageRef(item.targetPageId, props.currentPageId);

// 复用共享主题体系（与 VFD 面板/多变量看板同源），收敛掉旧的 navMenuTheme 平行实现
const { panelTheme } = useVfdPanelTheme({ panelStyle: menuStyle, panelAccentColor: menuAccentColor });

// 供模板消费的导航条主题：字段结构保持不变（background/border/accent/itemText/activeText/isLight/backdropFilter）
const navMenuTheme = computed(() => {
  const t = panelTheme.value;
  return {
    background: t.page,
    border: `1px solid ${t.border}`,
    backdropFilter: t.blur && t.blur !== 'none' ? t.blur : 'none',
    accent: t.accent,
    accentSoft: t.btnBg,
    itemText: t.muted,
    activeText: t.accent,
    isLight: t.isLight,
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
        <div v-for="(item, idx) in menuItems" :key="idx"
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
        <div v-for="(item, idx) in menuItems" :key="idx"
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
