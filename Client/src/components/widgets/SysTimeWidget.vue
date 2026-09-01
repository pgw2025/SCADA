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
      class="w-full h-full bg-black/90 border-2 border-slate-800 rounded-lg flex flex-col justify-center items-center px-2 py-1 shadow-inner relative text-emerald-400 font-mono select-none">
      <div class="absolute top-1 left-2 text-[8px] text-slate-500 uppercase tracking-widest leading-none">
        {{ component.label || 'SYSTEM CLOCK' }}
      </div>
      <!-- Interactive time ticking -->
      <div class="text-[13px] sm:text-[14px] font-bold text-center mt-2.5 tracking-wider w-full truncate"
        v-text="timeString" />
      <div class="absolute bottom-1 right-2 flex items-center gap-1">
        <span class="w-1 h-1 rounded-full bg-emerald-500 animate-pulse" />
        <span class="text-[7px] text-slate-500">LIVE</span>
      </div>
    </div>
</template>
