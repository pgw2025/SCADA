<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const wavePath = computed(() => {
  const percentHeight = normalizedPercent.value;
  const fluidY = 10 + (100 - percentHeight);
  const waveOffset = props.isActiveMode ? (ticks.value * 0.15) % (2 * Math.PI) : 0;
  return `M 10 ${fluidY} Q 30 ${fluidY - 4 * Math.sin(waveOffset)}, 50 ${fluidY} T 90 ${fluidY} L 90 110 L 10 110 Z`;
});
</script>

<template>
<svg width="100%" height="100%" viewBox="0 0 100 120"
      preserveAspectRatio="none">
      <!-- Leg supports -->
      <line x1="20" y1="110" x2="15" y2="118" stroke="#475569" stroke-width="4" />
      <line x1="80" y1="110" x2="85" y2="118" stroke="#475569" stroke-width="4" />

      <!-- Main Glass Body container -->
      <rect x="8" y="8" width="84" height="104" rx="10" ry="10" fill="#1e293b" :stroke="strokeColor" stroke-width="3" />

      <!-- Wave flow surface -->
      <path v-if="numValue > 0" :d="normalizedPercent >= 99 ? 'M 10 10 L 90 10 L 90 110 L 10 110 Z' : wavePath"
        :fill="fillColor || '#3b82f6'" opacity="0.8" />

      <!-- Glossy Highlight -->
      <rect x="12" y="12" width="12" height="96" rx="4" fill="#ffffff" opacity="0.08" />

      <!-- Grid Overlay lines -->
      <g stroke="#ffffff" stroke-width="1" opacity="0.25">
        <line x1="10" y1="35" x2="25" y2="35" />
        <line x1="10" y1="60" x2="30" y2="60" />
        <line x1="10" y1="85" x2="25" y2="85" />

        <line x1="90" y1="35" x2="75" y2="35" />
        <line x1="90" y1="60" x2="70" y2="60" />
        <line x1="90" y1="85" x2="75" y2="85" />
      </g>

      <!-- Numeric Value Overlay（显示原始值+单位，而非强制百分比） -->
      <text x="50" y="65" text-anchor="middle" fill="#ffffff" font-size="11" font-weight="bold" stroke="#000"
        stroke-width="1" paint-order="stroke">
        {{ numValue.toFixed(1) }}{{ unit ? ' ' + unit : '' }}
      </text>
    </svg>
</template>
