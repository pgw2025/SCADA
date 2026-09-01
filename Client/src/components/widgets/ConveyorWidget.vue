<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const conveyorBeltStep = computed(() => numValue.value > 0 ? (ticks.value * (numValue.value / 40)) % 80 : 0);
</script>

<template>
<svg width="100%" height="100%" viewBox="0 0 300 40"
      preserveAspectRatio="none">
      <rect x="5" y="12" width="290" height="16" rx="8" fill="#1e293b" :stroke="strokeColor" stroke-width="2.5" />
      <circle cx="20" cy="20" r="6" fill="#64748b" stroke="#334155" />
      <circle cx="20" cy="20" r="2" fill="#1e293b" />
      <circle cx="105" cy="20" r="6" fill="#64748b" stroke="#334155" />
      <circle cx="105" cy="20" r="2" fill="#1e293b" />
      <circle cx="195" cy="20" r="6" fill="#64748b" stroke="#334155" />
      <circle cx="195" cy="20" r="2" fill="#1e293b" />
      <circle cx="280" cy="20" r="6" fill="#64748b" stroke="#334155" />
      <circle cx="280" cy="20" r="2" fill="#1e293b" />

      <g v-if="numValue > 0">
        <rect :x="20 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
        <rect :x="100 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
        <rect :x="180 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
        <rect :x="260 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
      </g>
      <line x1="8" y1="31" x2="292" y2="31" stroke="#475569" stroke-width="2" stroke-dasharray="4 4" />
    </svg>
</template>
