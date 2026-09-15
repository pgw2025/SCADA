<script setup lang="ts">
import { defineProps, computed, ref, watch, onMounted, onBeforeUnmount } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';
import { useVfdPanelTheme } from './useVfdPanelTheme';
import { getWidgetDef, getMenuIcon } from '../../widgetRegistry';
import { isSamePageRef } from '../../utils/pageId';
import type { HmiMenuItem, HmiSubMenuItem } from '../../types';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

// ===== 导航菜单图元（nav-menu）：桌面顶部横条 / 移动底部 Tab 栏 =====
// 数据全部来自 props.menuItems（Inspector 编辑，PropsJson 落库），此处仅渲染。
// 一级项跳转不在本组件处理：无子项菜单项带 data-nav-page 标记，由 CanvasPanel 统一分发 navigateToPage。
// 二级菜单（children）：一级项为纯文件夹，点击/hover 仅展开；子项同样带 data-nav-page 由 CanvasPanel 分发跳转。
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

// ===== 二级菜单（children）：展开状态与子项判定 =====
const rootEl = ref<HTMLElement | null>(null);
/** 当前展开的二级菜单一级项索引（null=全部收起；桌面端 hover / 移动端点击） */
const openIndex = ref<number | null>(null);

const hasChildren = (item: HmiMenuItem) => Array.isArray(item.children) && item.children.length > 0;
/** 子项是否命中当前画面 */
const subActive = (sub: HmiSubMenuItem) =>
  !!sub.targetPageId && isSamePageRef(sub.targetPageId, props.currentPageId);
/** 一级项命中：自身 targetPageId 或任一子项 targetPageId 命中当前画面（归一化双轨比较） */
const isCurrentMenuItem = (item: HmiMenuItem) =>
  (!!item.targetPageId && isSamePageRef(item.targetPageId, props.currentPageId))
  || (item.children ?? []).some(subActive);

const closeMenu = () => { openIndex.value = null; };
const toggleOpen = (idx: number) => { openIndex.value = openIndex.value === idx ? null : idx; };

// 桌面端：hover 展开 / 移出收起（含下拉区，避免移入子项时闪烁）；仅运行态生效
const onDesktopHover = (item: HmiMenuItem, idx: number) => {
  if (!props.isActiveMode) return;
  openIndex.value = hasChildren(item) ? idx : null;
};
const onDesktopLeave = () => {
  if (!props.isActiveMode) return;
  openIndex.value = null;
};
// 移动端：点击纯文件夹一级项切换展开（stopPropagation 仅拦 click，pointerdown 分发逻辑不受影响）
const onItemClick = (e: Event, item: HmiMenuItem, idx: number) => {
  if (!props.isActiveMode || menuDevice.value !== 'mobile') return;
  if (hasChildren(item)) {
    e.stopPropagation();
    toggleOpen(idx);
  }
};

// 外部点击 / Esc / 页面切换后统一收起
const onDocPointerDown = (e: PointerEvent) => {
  if (rootEl.value && !rootEl.value.contains(e.target as Node)) closeMenu();
};
const onDocKeydown = (e: KeyboardEvent) => {
  if (e.key === 'Escape') closeMenu();
};

onMounted(() => {
  document.addEventListener('pointerdown', onDocPointerDown, true);
  document.addEventListener('keydown', onDocKeydown);
});
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocPointerDown, true);
  document.removeEventListener('keydown', onDocKeydown);
});
// 子项点击后由 CanvasPanel 分发 navigateToPage → currentPageId 变化即收起
watch(() => props.currentPageId, () => closeMenu());

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
<div ref="rootEl"
      class="relative w-full h-full overflow-visible select-none flex items-stretch" :style="{
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

      <!-- 桌面端：横向均分导航项（图标+文字水平排列，当前项底部高亮条；二级菜单 hover 下拉） -->
      <div v-if="menuDevice === 'desktop'" class="relative z-10 flex w-full h-full">
        <div v-for="(item, idx) in menuItems" :key="idx"
          class="relative flex-1 h-full transition-colors duration-200"
          :class="isActiveMode && hasChildren(item) ? 'cursor-pointer' : ''"
          @mouseenter="onDesktopHover(item, idx)" @mouseleave="onDesktopLeave">
          <!-- 一级项：无子项可跳转（带 data-nav-page）；有子项为纯文件夹，仅 hover 展开 -->
          <div
            class="relative flex items-center justify-center gap-2 h-full transition-colors duration-200"
            :class="isActiveMode && !hasChildren(item) && item.targetPageId ? (navMenuTheme.isLight ? 'cursor-pointer hover:bg-black/5' : 'cursor-pointer hover:bg-white/5') : ''"
            :data-nav-page="(!hasChildren(item) ? item.targetPageId : null) || undefined" :style="{
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
            <!-- 文件夹指示箭头（有二级菜单时展示，展开翻转） -->
            <span v-if="hasChildren(item)" class="text-[9px] shrink-0 transition-transform duration-200"
              :style="{ transform: openIndex === idx ? 'rotate(180deg)' : 'none', color: navMenuTheme.itemText }">▾</span>
            <!-- 当前项底部高亮条 -->
            <div v-if="isCurrentMenuItem(item)" class="absolute bottom-0 left-0 right-0 h-[3px]" :style="{
              background: navMenuTheme.accent,
              boxShadow: !navMenuTheme.isLight ? `0 0 10px ${navMenuTheme.accent}` : 'none'
            }" />
          </div>
          <!-- 二级菜单下拉：贴一级项下方，hover 保持展开，子项带 data-nav-page 由 CanvasPanel 分发 -->
          <div v-if="openIndex === idx" class="absolute top-full left-0 right-0 z-[999] pt-1">
            <div class="rounded-md overflow-hidden py-1"
              :style="{ background: navMenuTheme.background, border: navMenuTheme.border, boxShadow: '0 10px 28px rgba(0,0,0,0.35)' }">
              <div v-for="(sub, si) in (item.children ?? [])" :key="si"
                class="flex items-center gap-2 px-3 py-1.5 transition-colors duration-150 truncate"
                :class="isActiveMode ? (navMenuTheme.isLight ? 'hover:bg-black/5' : 'hover:bg-white/10') : ''"
                :data-nav-page="sub.targetPageId || undefined" @click="closeMenu" :style="{
                  color: subActive(sub) ? navMenuTheme.activeText : navMenuTheme.itemText,
                  fontWeight: subActive(sub) ? '600' : '400',
                }">
                <component v-if="sub.icon" :is="getMenuIcon(sub.icon)" class="w-3.5 h-3.5 shrink-0" :style="{
                  color: subActive(sub) ? navMenuTheme.accent : navMenuTheme.itemText,
                }" />
                <span class="truncate" :style="{ fontSize: `${menuFontSize - 2}px` }">{{ sub.text }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 移动端：底部 Tab 栏（图标在上文字在下，当前项整体提亮；二级菜单向上弹出面板） -->
      <div v-else class="relative z-10 flex w-full h-full">
        <!-- 二级菜单遮罩 + 上弹面板（浮于 Tab 栏上方；遮罩点按收起，子项 pointerdown 由 CanvasPanel 分发跳转） -->
        <template v-if="openIndex !== null">
          <div class="absolute" :style="{
            left: '-12px', right: '-12px', top: '-800px', bottom: 'calc(100% + 6px)',
            background: 'rgba(0,0,0,0.4)', zIndex: 30,
          }" @click.self="closeMenu" />
          <div class="absolute" :style="{
            left: '8px', right: '8px', bottom: 'calc(100% + 6px)',
            background: navMenuTheme.background,
            border: navMenuTheme.border,
            borderRadius: '10px',
            boxShadow: '0 -8px 28px rgba(0,0,0,0.35)',
            zIndex: 31,
            overflow: 'hidden',
          }">
            <div class="flex items-center justify-between px-3 pt-2 pb-1" :style="{ color: navMenuTheme.itemText }">
              <span class="text-[10px] font-semibold tracking-wide uppercase truncate">
                {{ menuItems[openIndex]?.text }}
              </span>
              <button type="button" @click="closeMenu"
                class="shrink-0 ml-2 px-1 rounded transition-colors"
                :class="navMenuTheme.isLight ? 'hover:bg-black/5' : 'hover:bg-white/10'"
                :style="{ color: navMenuTheme.itemText }" title="收起">✕</button>
            </div>
            <div v-for="(sub, si) in (menuItems[openIndex]?.children ?? [])" :key="si"
              class="flex items-center gap-2 px-3 py-2.5 transition-colors duration-150"
              :class="isActiveMode ? (navMenuTheme.isLight ? 'active:bg-black/5' : 'active:bg-white/10') : ''"
              :data-nav-page="sub.targetPageId || undefined" @click="closeMenu" :style="{
                color: subActive(sub) ? navMenuTheme.activeText : navMenuTheme.itemText,
                fontWeight: subActive(sub) ? '600' : '400',
              }">
              <component v-if="sub.icon" :is="getMenuIcon(sub.icon)" class="w-4 h-4 shrink-0" :style="{
                color: subActive(sub) ? navMenuTheme.accent : navMenuTheme.itemText,
              }" />
              <span class="truncate" :style="{ fontSize: `${menuFontSize + 1}px` }">{{ sub.text }}</span>
              <span v-if="subActive(sub)" class="ml-auto w-1.5 h-1.5 shrink-0 rounded-full"
                :style="{ background: navMenuTheme.accent, boxShadow: !navMenuTheme.isLight ? `0 0 6px ${navMenuTheme.accent}` : 'none' }" />
            </div>
          </div>
        </template>

        <div v-for="(item, idx) in menuItems" :key="idx"
          class="relative flex-1 flex flex-col items-center justify-center gap-0.5 h-full min-w-0 transition-colors duration-200"
          :class="isActiveMode && !hasChildren(item) && item.targetPageId ? (navMenuTheme.isLight ? 'cursor-pointer active:bg-black/5' : 'cursor-pointer active:bg-white/10') : ''"
          :data-nav-page="(!hasChildren(item) ? item.targetPageId : null) || undefined" @click="onItemClick($event, item, idx)" :style="{
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
          <!-- 文件夹指示箭头（有二级菜单时展示，展开翻转）：
               绝对定位挂 Tab 容器、不参与 flex 流，保证所有 Tab 都只有 图标+文字 两个子项，justify-center 结果一致 -->
          <span v-if="hasChildren(item)"
            class="absolute left-1/2 bottom-0.5 text-[8px] leading-none pointer-events-none transition-transform duration-200"
            :style="{ transform: openIndex === idx ? 'translateX(-50%) rotate(180deg)' : 'translateX(-50%)', color: navMenuTheme.itemText }">▾</span>
          <!-- 当前项顶部高亮条 -->
          <div v-if="isCurrentMenuItem(item)" class="absolute top-0 left-0 right-0 h-[3px]" :style="{
            background: navMenuTheme.accent,
            boxShadow: !navMenuTheme.isLight ? `0 0 10px ${navMenuTheme.accent}` : 'none'
          }" />
        </div>
      </div>
    </div>
</template>
