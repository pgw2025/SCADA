<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;
</script>

<template>
<div
      class="w-full h-full flex flex-col items-center bg-slate-900 border border-slate-700 rounded p-1 font-mono text-[9px] text-slate-400">
      <div
        class="flex-1 w-full bg-slate-950 border border-slate-800 rounded relative overflow-hidden flex flex-col justify-end">
        <div class="w-full transition-all duration-300" :style="{
          height: `${normalizedPercent}%`,
          backgroundColor: alertColor,
          boxShadow: `0 0 12px ${alertColor}`,
        }" />
        <div
          class="absolute inset-0 flex flex-col justify-between p-1 opacity-50 pointer-events-none text-[8px] text-slate-300 font-mono">
          <span>H</span>
          <span>M</span>
          <span>L</span>
        </div>
      </div>
      <div class="mt-1 text-[10px] text-slate-100 font-bold truncate max-w-full">
        {{ numValue.toFixed(0) }}{{ unit ? ' ' + unit : '' }}
      </div>
    </div>
</template>
