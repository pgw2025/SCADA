<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const flowSpeed = computed(() => 0.5 + Math.min(3, Math.abs(numValue.value) / 50));
const pipeScrollOffsetH = computed(() => numValue.value > 0 ? -(ticks.value * flowSpeed.value) % 30 : 0);
</script>

<template>
<div class="w-full h-full relative flex items-center">
      <div class="absolute inset-x-0 h-4 rounded-full border-t border-b overflow-hidden shadow-inner flex items-center"
        :style="{
          backgroundColor: numValue > 0 ? '#334155' : inactiveColor,
          borderColor: strokeColor,
        }">
        <div v-if="numValue > 0" class="w-[200%] h-1 opacity-70" :style="{
          backgroundImage: `repeating-linear-gradient(90deg, transparent, transparent 15px, ${activeColor} 15px, ${activeColor} 30px)`,
          transform: `translateX(${pipeScrollOffsetH}px)`,
        }" />
      </div>
      <div class="absolute left-0 top-0 bottom-0 w-2 bg-slate-700 rounded-sm border-r border-slate-500" />
      <div class="absolute right-0 top-0 bottom-0 w-2 bg-slate-700 rounded-sm border-l border-slate-500" />
    </div>
</template>
