<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const describeGaugeArc = (cx: number, cy: number, r: number, startAngle: number, endAngle: number) => {
  if (endAngle <= startAngle) return '';
  const rad = (a: number) => (a * Math.PI) / 180;
  const x1 = cx + r * Math.sin(rad(startAngle));
  const y1 = cy - r * Math.cos(rad(startAngle));
  const x2 = cx + r * Math.sin(rad(endAngle));
  const y2 = cy - r * Math.cos(rad(endAngle));
  const diff = endAngle - startAngle;
  const largeArc = diff > 180 ? 1 : 0;
  return `M ${x1.toFixed(2)} ${y1.toFixed(2)} A ${r} ${r} 0 ${largeArc} 1 ${x2.toFixed(2)} ${y2.toFixed(2)}`;
};

const dialAngle = computed(() => {
  const minVal = minValue.value;
  const maxVal = maxValue.value;
  const range = maxVal - minVal;
  if (range <= 0) return -120;
  const boundedVal = Math.max(minVal, Math.min(maxVal, numValue.value));
  return -120 + ((boundedVal - minVal) / range) * 240;
});

const dialTrackArc = computed(() => describeGaugeArc(50, 50, 34, -120, 120));

// 低限预警黄色弧（minValue ~ thresholdMin）
const dialYellowArc = computed(() => {
  const minVal = minValue.value;
  const maxVal = maxValue.value;
  const range = maxVal - minVal;
  if (range <= 0 || thresholdMin.value <= minVal) return '';
  const lowRatio = Math.min(1, Math.max(0, (thresholdMin.value - minVal) / range));
  const lowAngle = -120 + lowRatio * 240;
  if (lowAngle <= -120) return '';
  return describeGaugeArc(50, 50, 34, -120, lowAngle);
});

// 正常安全绿色弧（thresholdMin ~ thresholdMax）
const dialGreenArc = computed(() => {
  const minVal = minValue.value;
  const maxVal = maxValue.value;
  const range = maxVal - minVal;
  if (range <= 0) return '';
  const lowRatio = Math.min(1, Math.max(0, (thresholdMin.value - minVal) / range));
  const highRatio = Math.min(1, Math.max(0, (thresholdMax.value - minVal) / range));
  const startAngle = -120 + lowRatio * 240;
  const endAngle = -120 + highRatio * 240;
  if (endAngle <= startAngle) return '';
  return describeGaugeArc(50, 50, 34, startAngle, endAngle);
});

// 高限危险红色弧（thresholdMax ~ maxValue）
const dialRedArc = computed(() => {
  const minVal = minValue.value;
  const maxVal = maxValue.value;
  const range = maxVal - minVal;
  if (range <= 0) return '';
  const warnRatio = Math.min(1, Math.max(0, (thresholdMax.value - minVal) / range));
  const warnAngle = -120 + warnRatio * 240;
  if (warnAngle >= 120) return '';
  return describeGaugeArc(50, 50, 34, warnAngle, 120);
});

const dialMajorTicks = computed(() => {
  const list = [];
  for (let i = 0; i <= 4; i++) {
    const angle = -120 + (i / 4) * 240;
    const rad = (angle * Math.PI) / 180;
    list.push({
      x1: (50 + 38 * Math.sin(rad)).toFixed(2),
      y1: (50 - 38 * Math.cos(rad)).toFixed(2),
      x2: (50 + 31 * Math.sin(rad)).toFixed(2),
      y2: (50 - 31 * Math.cos(rad)).toFixed(2),
    });
  }
  return list;
});

const dialMinorTicks = computed(() => {
  const list = [];
  for (let i = 0; i <= 16; i++) {
    if (i % 4 === 0) continue;
    const angle = -120 + (i / 16) * 240;
    const rad = (angle * Math.PI) / 180;
    list.push({
      x1: (50 + 37 * Math.sin(rad)).toFixed(2),
      y1: (50 - 37 * Math.cos(rad)).toFixed(2),
      x2: (50 + 34 * Math.sin(rad)).toFixed(2),
      y2: (50 - 34 * Math.cos(rad)).toFixed(2),
    });
  }
  return list;
});
</script>

<template>
<svg width="100%" height="100%" viewBox="0 0 100 100"
      preserveAspectRatio="xMidYMid meet" class="select-none">
      <!-- Bezel & Face -->
      <circle cx="50" cy="50" r="47" fill="#1e293b" stroke="#334155" stroke-width="2.5" />
      <circle cx="50" cy="50" r="43" fill="#0f172a" stroke="#1e293b" stroke-width="1" />

      <!-- Background Track Arc -->
      <path v-if="dialTrackArc" :d="dialTrackArc" stroke="#334155" stroke-width="3" fill="none"
        stroke-linecap="round" />

      <!-- Low Warning Zone Yellow Arc -->
      <path v-if="dialYellowArc" :d="dialYellowArc" stroke="#f59e0b" stroke-width="3" fill="none"
        stroke-linecap="round" />

      <!-- Normal/Safe Zone Green Arc -->
      <path v-if="dialGreenArc" :d="dialGreenArc" stroke="#10b981" stroke-width="3" fill="none"
        stroke-linecap="round" />

      <!-- High Warning/Danger Zone Red Arc -->
      <path v-if="dialRedArc" :d="dialRedArc" stroke="#ef4444" stroke-width="3" fill="none" stroke-linecap="round" />

      <!-- Minor Ticks -->
      <g stroke="#475569" stroke-width="1">
        <line v-for="(t, i) in dialMinorTicks" :key="'min-' + i" :x1="t.x1" :y1="t.y1" :x2="t.x2" :y2="t.y2" />
      </g>

      <!-- Major Ticks -->
      <g stroke="#94a3b8" stroke-width="1.5">
        <line v-for="(t, i) in dialMajorTicks" :key="'maj-' + i" :x1="t.x1" :y1="t.y1" :x2="t.x2" :y2="t.y2" />
      </g>

      <!-- Scale Min/Max labels -->
      <text x="21" y="74" text-anchor="middle" fill="#64748b" font-size="5.5" font-family="monospace">
        {{ minValue }}
      </text>
      <text x="79" y="74" text-anchor="middle" fill="#64748b" font-size="5.5" font-family="monospace">
        {{ maxValue }}
      </text>

      <!-- Center Label -->
      <text x="50" y="65" text-anchor="middle" fill="#94a3b8" font-size="7" font-weight="500">
        {{ component.label }}
      </text>

      <!-- Value & Unit Text -->
      <text x="50" y="79" text-anchor="middle" :fill="alertColor" font-size="9.5" font-weight="bold"
        font-family="monospace">
        {{ numValue.toFixed(1) }}<tspan font-size="6.5" fill="#64748b" dx="1">{{ unit || '' }}</tspan>
      </text>

      <!-- Needle -->
      <g :transform="`translate(50, 50) rotate(${dialAngle})`" class="transition-transform duration-300 ease-out">
        <!-- Shadow -->
        <path d="M -1.5 0 L 0 -34 L 1.5 0 Z" fill="#000000" opacity="0.3" transform="translate(0.5, 0.5)" />
        <!-- Needle Pointer -->
        <path d="M -2 0 L 0 -34 L 2 0 L 1.2 5 L -1.2 5 Z" :fill="alertColor" stroke="#0f172a" stroke-width="0.4" />
        <!-- Center Pivot Cap -->
        <circle cx="0" cy="0" r="4" fill="#334155" stroke="#64748b" stroke-width="1" />
        <circle cx="0" cy="0" r="1.8" fill="#f8fafc" />
      </g>
    </svg>
</template>
