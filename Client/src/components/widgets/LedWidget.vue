<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;
</script>

<template>
<div class="w-full h-full flex flex-col items-center justify-center">
      <div class="rounded-full transition-all duration-300" :style="{
        width: `${Math.min(width, height) - 12}px`,
        height: `${Math.min(width, height) - 12}px`,
        backgroundColor: boolValue ? activeColor : inactiveColor,
        boxShadow: boolValue ? `0 0 16px ${activeColor}, inset 0 2px 4px rgba(255,255,255,0.4)` : 'inset 0 2px 4px rgba(0,0,0,0.4)',
        border: '3px solid #334155',
      }" />
      <span class="text-[9px] text-slate-300 font-mono mt-1 text-center truncate max-w-full">
        {{ component.label }}
      </span>
      <span class="text-[8px] text-slate-500 dark:text-slate-400 font-mono text-center truncate max-w-full"
        v-if="component.bindVariableKey || component.bindField">
        {{ boolValue ? onText : offText }}
      </span>
    </div>
</template>
