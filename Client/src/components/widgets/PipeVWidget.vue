<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const flowSpeed = computed(() => 0.5 + Math.min(3, Math.abs(numValue.value) / 50));
const pipeScrollOffsetH = computed(() => numValue.value > 0 ? -(ticks.value * flowSpeed.value) % 30 : 0);
const pipeScrollOffsetV = computed(() => numValue.value > 0 ? (ticks.value * flowSpeed.value) % 30 : 0);
</script>

<template>
<div class="w-full h-full relative flex justify-center">
      <div
        class="absolute inset-y-0 w-4 rounded-full border-l border-r overflow-hidden shadow-inner flex justify-center"
        :style="{
          backgroundColor: numValue > 0 ? '#334155' : inactiveColor,
          borderColor: strokeColor,
        }">
        <div v-if="numValue > 0" class="h-[200%] w-1 opacity-70" :style="{
          backgroundImage: `repeating-linear-gradient(180deg, transparent, transparent 15px, ${activeColor} 15px, ${activeColor} 30px)`,
          transform: `translateY(${pipeScrollOffsetV}px)`,
        }" />
      </div>
      <div class="absolute top-0 left-0 right-0 h-2 bg-slate-700 rounded-sm border-b border-slate-500" />
      <div class="absolute bottom-0 left-0 right-0 h-2 bg-slate-700 rounded-sm border-t border-slate-500" />
    </div>
</template>
