<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;
</script>

<template>
<div class="w-full h-full p-0.5">
      <div
        class="w-full h-full rounded border-2 shadow flex flex-col items-center justify-center transition-all select-none relative overflow-hidden"
        :class="[
          isActiveMode ? 'active:translate-y-0.5 active:shadow-inner cursor-pointer' : '',
          boolValue ? 'shadow-inner' : 'shadow-md border-t-white border-l-white border-b-slate-900 border-r-slate-900'
        ]" :style="{
          backgroundColor: boolValue ? activeColor : fillColor || '#cbd5e1',
          borderColor: boolValue ? (strokeColor || '#0284c7') : '#94a3b8',
          color: boolValue ? '#ffffff' : '#1e293b'
        }">
        <!-- Led Indicator inside the button -->
        <div class="absolute top-1 right-2 w-1.5 h-1.5 rounded-full border border-slate-600/30" :style="{
          backgroundColor: boolValue ? '#22c55e' : '#dc2626',
          boxShadow: boolValue ? '0 0 6px #22c55e' : 'none'
        }" />
        <!-- 阶段6-2：运行模式无写权限时，绑定按钮显示只读锁标记 -->
        <span v-if="isLockedControl"
          class="absolute bottom-1 left-1.5 text-[8px] text-amber-500 flex items-center gap-0.5 leading-none"
          title="当前角色无写权限，控件为只读">
          <svg width="8" height="8" viewBox="0 0 24 24" fill="currentColor">
            <path
              d="M12 1a5 5 0 0 0-5 5v3H6a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2h-1V6a5 5 0 0 0-5-5zm3 8H9V6a3 3 0 0 1 6 0v3z" />
          </svg>
          只读
        </span>
        <!-- Label -->
        <span class="text-center font-mono pointer-events-none px-1 truncate max-w-full" :style="{
          fontSize: `${fontSize}px`,
          fontWeight: bold ? 'bold' : 'normal'
        }">
          {{ component.props.buttonText || component.label || '命令键' }}
        </span>
        <span class="text-[8px] opacity-60 font-sans pointer-events-none mt-0.5 select-none"
          v-if="component.bindVariableKey || component.bindField">
          {{ component.props.buttonMode === 'momentary' ? '[点动]' :
            component.props.buttonMode === 'set-bit' ? '[置位]' :
              component.props.buttonMode === 'reset-bit' ? '[复位]' :
                component.props.buttonMode === 'set-value' ? `[设值:${component.props.clickValue ?? 0}]` :
                  component.props.buttonMode === 'navigate' ? '[跳转]' : '[自锁]' }}
        </span>
      </div>
    </div>
</template>
