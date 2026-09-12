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
      class="w-full h-full border-2 rounded-lg flex flex-col justify-center items-center px-2 py-1 shadow-inner relative overflow-hidden"
      :style="{ backgroundColor: 'var(--vfd-metal-900)', borderColor: isHighAlert ? 'var(--vfd-err)' : 'var(--vfd-metal-700)' }">
      <div class="absolute top-1 left-2 text-[8px] uppercase tracking-widest font-mono"
        :style="{ color: 'var(--vfd-muted)' }">
        {{ component.label || '数字监测' }}
      </div>
      <div class="text-xl md:text-2xl font-black mt-2 font-mono tracking-widest"
        :style="{ color: isHighAlert ? 'var(--vfd-err)' : 'var(--vfd-ok)' }">
        {{ typeof value === 'boolean' ? (boolValue ? onText : offText) : `${numValue.toFixed(2)}` }}
        <span v-if="typeof value !== 'boolean' && unit" class="text-xs font-normal ml-0.5" :style="{ color: 'var(--vfd-faint)' }">{{ unit
        }}</span>
      </div>
      <div class="absolute bottom-1 right-2 w-1.5 h-1.5 rounded-full" :style="{
        backgroundColor: boolValue ? 'var(--vfd-ok)' : 'var(--vfd-err)',
        animation: boolValue ? 'pulse 1.2s infinite' : 'none',
      }" />
    </div>
</template>

<style>
@keyframes pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.4;
  }
}
</style>
