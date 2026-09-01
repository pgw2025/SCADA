<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const textContent = computed(() => {
  const label = props.component.label ?? '';
  if (!label.includes('{value}')) return label;
  const val = typeof props.value === 'boolean'
    ? (props.value ? onText.value : offText.value)
    : numValue.value.toFixed(2) + (unit.value || '');
  return label.replaceAll('{value}', val);
});
</script>

<template>
<div class="w-full h-full flex items-center" :style="{
      justifyContent: align === 'center' ? 'center' : align === 'right' ? 'flex-end' : 'flex-start',
      fontSize: `${fontSize}px`,
      fontWeight: bold ? 'bold' : 'normal',
      color: activeColor || '#cbd5e1',
    }">
      {{ textContent }}
    </div>
</template>
