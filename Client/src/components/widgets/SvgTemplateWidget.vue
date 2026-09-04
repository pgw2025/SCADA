<script setup lang="ts">
// SVG 轨通用渲染器（P4 占位壳）：完整实现（sanitize + bind + 运行态占位符替换）
// 将在 P4 阶段落地；当前仅渲染占位提示，保证 SVG 模板可落布、可选中、不报错。
import type { HmiWidgetProps } from './useWidgetBase';

defineProps<HmiWidgetProps>();
</script>

<template>
  <div
    class="relative w-full h-full flex items-center justify-center bg-slate-800/20 border border-dashed border-slate-500/40 rounded text-[10px] text-slate-400 dark:text-slate-500 select-none pointer-events-none">
    SVG 模板渲染将在后续版本提供
  </div>
</template>
