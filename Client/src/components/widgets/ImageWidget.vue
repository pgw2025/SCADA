<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';
import { ref, watch } from 'vue';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

// 图片图元：填充方式 → 尺寸样式（tile 无 object-fit 对应，按原尺寸平铺由容器裁切）
const imageFitStyle = computed(() => {
  const fit = props.component.props.imageFit ?? 'fill';
  if (fit === 'tile') {
    // 原尺寸平铺：用 background-repeat 实现真·平铺，替代原先「原尺寸左上对齐退化为裁切」的分支
    return { width: 'auto', height: 'auto', maxWidth: 'none', maxHeight: 'none' };
  }
  // 显式字面量收窄，避免 string 不可赋给 CSS ObjectFit 联合类型
  const objectFit: 'fill' | 'contain' | 'cover' =
    fit === 'contain' ? 'contain' : fit === 'cover' ? 'cover' : 'fill';
  return { width: '100%', height: '100%', objectFit };
});

// #12 image 图元状态：URL 加载失败兜底 + tile 背景平铺
const imgError = ref(false);
const resetImgError = () => (imgError.value = false);
watch(() => props.component.props.imageUrl, resetImgError);
const tileStyle = computed(() => ({
  width: '100%',
  height: '100%',
  backgroundImage: `url("${props.component.props.imageUrl}")`,
  backgroundRepeat: 'repeat',
  backgroundSize: 'auto',
}));
</script>

<template>
<div
      class="w-full h-full flex items-center justify-center overflow-hidden select-none">
      <!-- tile：background-repeat 真平铺 -->
      <div v-if="(component.props.imageFit === 'tile') && (component.props.imageUrl || '').trim() && !imgError"
        :style="tileStyle" class="w-full h-full" />
      <!-- 常规 fit：img 渲染 -->
      <img v-else-if="(component.props.imageUrl || '').trim() && !imgError" :src="component.props.imageUrl" alt=""
        draggable="false" @error="imgError = true" class="pointer-events-none max-w-full max-h-full"
        :style="imageFitStyle" />
      <div v-else class="flex flex-col items-center gap-1 text-slate-400 dark:text-slate-500">
        <svg class="w-6 h-6" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
          stroke-linecap="round" stroke-linejoin="round">
          <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
          <circle cx="8.5" cy="8.5" r="1.5" />
          <polyline points="21 15 16 10 5 21" />
        </svg>
        <span class="text-[10px]">{{ imgError ? '图片加载失败' : '未设置图片' }}</span>
      </div>
    </div>
</template>
