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
      class="w-full h-full flex flex-col items-center justify-center p-1 font-mono text-[9px] select-none">
      <div
        class="w-full h-full border rounded p-1.5 flex flex-col items-center justify-between shadow-md relative"
        :style="{ backgroundColor: 'var(--vfd-metal-800)', borderColor: 'var(--vfd-metal-700)' }">
        <!-- 阶段6-2：运行模式无写权限时，绑定开关显示只读锁标记 -->
        <span v-if="isLockedControl"
          class="absolute top-1 right-1.5 text-[8px] text-amber-500 flex items-center gap-0.5 leading-none"
          title="当前角色无写权限，控件为只读">
          <svg width="8" height="8" viewBox="0 0 24 24" fill="currentColor">
            <path
              d="M12 1a5 5 0 0 0-5 5v3H6a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2h-1V6a5 5 0 0 0-5-5zm3 8H9V6a3 3 0 0 1 6 0v3z" />
          </svg>
          只读
        </span>
        <!-- Top State Label -->
        <span class="font-bold uppercase text-[8px] tracking-tight text-center truncate max-w-full"
          :style="{ color: 'var(--vfd-muted)' }">
          {{ boolValue ? onText : offText }}
        </span>

        <!-- Slot slider & Lever knob style-->
        <div
          class="w-6 h-10 rounded-full border relative flex items-center justify-center overflow-hidden shadow-inner cursor-pointer"
          :style="{ backgroundColor: 'var(--vfd-metal-900)', borderColor: 'var(--vfd-metal-800)' }">
          <div
            class="w-5 h-5 rounded-full border shadow-md transition-all duration-300 flex items-center justify-center"
            :style="{
              transform: boolValue ? 'translateY(-10px)' : 'translateY(10px)',
              backgroundColor: boolValue ? 'var(--vfd-ok)' : 'var(--vfd-err)',
              boxShadow: boolValue ? '0 2px 4px rgba(16,185,129,0.4)' : '0 2px 4px rgba(239,68,68,0.4)',
            }">
            <div class="w-1.5 h-1.5 rounded-full bg-white/60" />
          </div>
        </div>

        <!-- Bottom Label text -->
        <span class="text-[8px] font-bold text-center truncate max-w-full"
          :style="{ color: 'var(--vfd-faint)' }">
          {{ component.label }}
        </span>
      </div>
    </div>
</template>
