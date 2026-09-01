<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const decimals = computed(() => {
  const d = Number(props.component.props.decimals);
  if (!Number.isFinite(d)) return 2;
  return Math.min(4, Math.max(0, Math.round(d)));
});
// 是否可设定：点击弹数字键盘写值（点击分发在 CanvasPanel，此处仅控制角标/光标）
const isSettable = computed(() => props.component.props.settable === true);
// 变量质量非 Good 时显示 -- 而非旧值（配合 CanvasPanel 质量角标）
const qualityBad = computed(() => !!props.quality && props.quality !== 'Good');
const varDisplayText = computed(() => {
  if (qualityBad.value) return '--';
  if (typeof props.value === 'boolean') return props.value ? onText.value : offText.value;
  return numValue.value.toFixed(decimals.value);
});

const varDisplayContainerStyle = computed(() => {
  const p = props.component.props || {};
  const hasBorder = p.showBorder === true || p.showBorder === 'true' as any;
  const hasBg = p.showBackground === true || p.showBackground === 'true' as any;
  const enableAlarm = p.enableAlarmBorder !== false;

  // 检查是否发生报警且启用了报警变色联动（仅当显式配置了有效阈值且产生超限时才触发报警边框）
  const isAlarm = enableAlarm && (
    (hasExplicitThresholdMax.value && isHighAlert.value) ||
    (hasExplicitThresholdMin.value && isLowAlert.value)
  );

  let borderColor = 'transparent';
  let borderWidth = '0px';
  let borderStyle = (p.borderStyle as string) || 'solid';

  if (isAlarm) {
    const bw = p.borderWidth !== undefined && p.borderWidth !== null ? Math.max(2, Number(p.borderWidth)) : 2;
    borderWidth = `${bw}px`;
    borderColor = isHighAlert.value ? '#ef4444' : '#f59e0b';
    borderStyle = 'solid';
  } else if (hasBorder) {
    const bw = p.borderWidth !== undefined && p.borderWidth !== null ? Number(p.borderWidth) : 1.5;
    borderWidth = `${bw}px`;
    borderColor = p.borderColor || p.strokeColor || '#cbd5e1';
    borderStyle = (p.borderStyle as string) || 'solid';
  }

  const borderRadius = p.borderRadius !== undefined && p.borderRadius !== null ? `${p.borderRadius}px` : '8px';
  const backgroundColor = hasBg
    ? (p.bgColor || '#ffffff')
    : 'transparent';

  return {
    borderWidth,
    borderStyle,
    borderColor,
    borderRadius,
    backgroundColor,
    boxSizing: 'border-box' as const,
  };
});
</script>

<template>
<div
      class="w-full h-full flex flex-col justify-center items-center px-3 py-1 relative overflow-hidden select-none transition-all duration-150"
      :class="[
        isActiveMode && isSettable && !isLockedControl ? 'cursor-pointer hover:shadow-md' : '',
        showBackground && !component.props.bgColor ? 'bg-white dark:bg-slate-950' : '',
      ]" :style="varDisplayContainerStyle">
      <div v-if="showInnerLabel"
        class="absolute top-1 left-2.5 text-[9px] text-slate-400 dark:text-slate-500 truncate max-w-[80%] font-mono pointer-events-none">
        {{ component.label || '变量' }}
      </div>
      <div class="font-mono font-bold tracking-wide leading-none tabular-nums" :class="showInnerLabel ? 'mt-1.5' : ''"
        :style="{
          fontSize: `${fontSize * 1.6}px`,
          color: (component.props.enableAlarmBorder !== false && hasExplicitThresholdMax && isHighAlert) ? '#ef4444' : ((component.props.enableAlarmBorder !== false && hasExplicitThresholdMin && isLowAlert) ? '#f59e0b' : (qualityBad ? '#94a3b8' : activeColor))
        }">
        {{ varDisplayText }}
        <span v-if="typeof value === 'number' && unit && !qualityBad"
          class="text-xs font-normal text-slate-400 dark:text-slate-500 ml-0.5">{{ unit }}</span>
      </div>
      <!-- 可设定角标：提示操作员可点击写值 -->
      <span v-if="isSettable && isActiveMode"
        class="absolute bottom-1 right-1.5 text-[9px] leading-none pointer-events-none"
        :class="isLockedControl ? 'text-amber-500' : 'text-sky-500 dark:text-sky-400'">
        {{ isLockedControl ? '只读' : '✎' }}
      </span>
      <!-- 运行模式无写权限：绑定显示组件显示只读锁标记（与按钮口径一致） -->
      <span v-if="isLockedControl && !isSettable"
        class="absolute bottom-1 left-1.5 text-[8px] text-amber-500 flex items-center gap-0.5 leading-none"
        title="当前角色无写权限，控件为只读">
        <svg width="8" height="8" viewBox="0 0 24 24" fill="currentColor">
          <path
            d="M12 1a5 5 0 0 0-5 5v3H6a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2h-1V6a5 5 0 0 0-5-5zm3 8H9V6a3 3 0 0 1 6 0v3z" />
        </svg>
        只读
      </span>
    </div>
</template>
