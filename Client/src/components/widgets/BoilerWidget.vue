<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;
</script>

<template>
<svg width="100%" height="100%" viewBox="0 0 100 120" preserveAspectRatio="none">
      <!-- Boiler Outer Shell -->
      <rect x="10" y="20" width="80" height="90" rx="8" ry="8" fill="#334155" :stroke="strokeColor" stroke-width="3" />
      <!-- Top Chimney -->
      <rect x="40" y="5" width="20" height="15" fill="#475569" :stroke="strokeColor" stroke-width="2" />
      <line x1="35" y1="5" x2="65" y2="5" stroke="#1e293b" stroke-width="3" />

      <!-- Glowing Furnace Window -->
      <circle cx="50" cy="70" r="22" fill="#1e293b" stroke="#475569" stroke-width="2" />

      <!-- Flame Animation when hot or turned on -->
      <path v-if="boolValue" :d="`M 38 78 Q 42 55, 50 ${50 + (ticks % 3) * 2} Q 58 55, 62 78 Q 50 84, 38 78`"
        :fill="isHighAlert ? '#ef4444' : '#f97316'" :opacity="0.8 + Math.sin(ticks * 0.1) * 0.15" />
      <path v-if="boolValue" :d="`M 44 78 Q 46 62, 50 ${60 + (ticks % 2) * 2} Q 54 62, 56 78 Q 50 82, 44 78`"
        fill="#eab308" opacity="0.9" />

      <!-- Analog temperature indicator mini-bar（按量程归一化，maxValue 已防除零） -->
      <rect x="18" y="30" width="6" height="30" rx="3" fill="#1e293b" />
      <rect x="19" :y="30 + (30 - (normalizedPercent / 100) * 30)" width="4" :height="(normalizedPercent / 100) * 30"
        rx="2" :fill="alertColor" />

      <!-- Pressure release outlet -->
      <path d="M 85 40 L 95 40 L 95 48 M 90 40 L 90 35" stroke="#475569" stroke-width="2" fill="none" />
    </svg>
</template>
