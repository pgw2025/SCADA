<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const roundedBtnState = computed<StateStyleConfig>(() => {
  const p = props.component.props;
  const rawVal = props.value;
  const strVal = String(rawVal).toLowerCase();
  const isTrueOrNonZero = typeof rawVal === 'boolean' ? rawVal : Number(rawVal) !== 0;

  // 1. 如果配置了 customStates (格式: "0:停止:#334155:#ffffff;1:运行:#0284c7:#ffffff;2:报警:#dc2626:#ffffff")
  if (p.customStates && p.customStates.trim()) {
    try {
      const entries = p.customStates.split(/[;；]/);
      for (const entry of entries) {
        const parts = entry.split(':').map(s => s.trim());
        if (parts.length >= 2) {
          const matchKey = parts[0].toLowerCase();
          if (matchKey === strVal || (matchKey === '1' && strVal === 'true') || (matchKey === '0' && strVal === 'false')) {
            return {
              text: parts[1] || p.buttonText || props.component.label || '按键',
              bgColor: parts[2] || (isTrueOrNonZero ? (p.activeColor || '#0284c7') : (p.inactiveColor || '#1e293b')),
              textColor: parts[3] || '#ffffff',
              borderColor: parts[4] || p.strokeColor || 'transparent',
            };
          }
        }
      }
    } catch (e) {
      console.error('Failed to parse customStates for rounded-btn', e);
    }
  }

  // 2. 如果配置了状态0 / 状态1 的精细配置
  if (isTrueOrNonZero) {
    return {
      text: p.state1Text || p.buttonText || props.component.label || 'ON 运行',
      bgColor: p.state1BgColor || p.activeColor || '#0284c7',
      textColor: p.state1TextColor || '#ffffff',
      borderColor: p.strokeColor || '#38bdf8',
    };
  } else {
    return {
      text: p.state0Text || p.buttonText || props.component.label || 'OFF 停止',
      bgColor: p.state0BgColor || p.inactiveColor || '#1e293b',
      textColor: p.state0TextColor || '#94a3b8',
      borderColor: p.strokeColor || '#475569',
    };
  }
});
</script>

<template>
<div class="w-full h-full p-0.5">
      <div
        class="w-full h-full shadow flex flex-col items-center justify-center transition-all select-none relative overflow-hidden group"
        :class="[
          isActiveMode ? 'active:scale-95 active:brightness-90 cursor-pointer' : '',
          boolValue ? 'shadow-md' : 'shadow-xs'
        ]" :style="{
          borderRadius: `${component.props.borderRadius ?? 10}px`,
          borderWidth: `${component.props.borderWidth ?? 1}px`,
          borderColor: roundedBtnState.borderColor || component.props.strokeColor || '#38bdf8',
          backgroundColor: roundedBtnState.bgColor,
          color: roundedBtnState.textColor,
        }">
        <!-- Subtle top gloss highlight for industrial tactility -->
        <div class="absolute inset-x-0 top-0 h-1/2 bg-gradient-to-b from-white/20 to-transparent pointer-events-none"
          :style="{ borderTopLeftRadius: `${component.props.borderRadius ?? 10}px`, borderTopRightRadius: `${component.props.borderRadius ?? 10}px` }" />

        <!-- Status LED dot -->
        <div class="absolute top-1.5 right-2 w-2 h-2 rounded-full border border-black/20" :style="{
          backgroundColor: boolValue ? '#22c55e' : '#64748b',
          boxShadow: boolValue ? '0 0 8px #22c55e' : 'none'
        }" />

        <!-- 只读锁标记 -->
        <span v-if="isLockedControl"
          class="absolute bottom-1 left-2 text-[8px] text-amber-300 flex items-center gap-0.5 leading-none bg-black/40 px-1 py-0.5 rounded"
          title="当前角色无写权限，控件为只读">
          <svg width="8" height="8" viewBox="0 0 24 24" fill="currentColor">
            <path
              d="M12 1a5 5 0 0 0-5 5v3H6a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2h-1V6a5 5 0 0 0-5-5zm3 8H9V6a3 3 0 0 1 6 0v3z" />
          </svg>
          只读
        </span>

        <!-- Primary Label / Dynamic text -->
        <span class="text-center font-mono pointer-events-none px-2 truncate max-w-full z-10 drop-shadow-xs" :style="{
          fontSize: `${fontSize}px`,
          fontWeight: bold ? 'bold' : '600'
        }">
          {{ roundedBtnState.text }}
        </span>

        <!-- Mode badge / hint（可配置隐藏：props.showModeBadge === false 时不渲染） -->
        <span v-if="component.props.showModeBadge !== false"
          class="text-[8px] opacity-75 font-sans pointer-events-none mt-0.5 select-none z-10">
          {{ component.props.buttonMode === 'momentary' ? '[按1送0]' : component.props.buttonMode === 'set-bit' ? '[置位1]'
            :
            component.props.buttonMode === 'reset-bit' ? '[复位0]' : component.props.buttonMode === 'set-value' ?
              `[设值:${component.props.clickValue ?? 0}]` : component.props.buttonMode === 'navigate' ? '[跳转]' :
                component.props.buttonMode === 'run-script' ? '[脚本]' : '[取反]' }}
        </span>
      </div>
    </div>
</template>
