<script setup lang="ts">
// 瘦分发器 v2（组件库动态化，D3）：builtin 轨走 builtinRenderers；svg 轨走 SvgTemplateWidget。
// 仅保留外层包裹、通用 showValue 浮签与“组件缺失”兜底。
import { computed } from 'vue';
import type { HmiWidgetProps } from './widgets/useWidgetBase';
import { useWidgetBase } from './widgets/useWidgetBase';
import { builtinRenderers } from '../builtinRenderers';
import { getWidgetDef } from '../widgetRegistry';
import SvgTemplateWidget from './widgets/SvgTemplateWidget.vue';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
// 通用浮签需用到的共享派生（与子组件共用同一真相源）
const { numValue, boolValue, onText, offText, unit } = base;

const renderComponent = computed(() => {
  const def = getWidgetDef(props.component.type);
  if (def?.renderKind === 'svg') return SvgTemplateWidget;
  return builtinRenderers[def?.type ?? props.component.type] ?? null;
});
</script>

<template>
  <div class="relative w-full h-full">
    <component
      :is="renderComponent"
      v-if="renderComponent"
      :component="component"
      :value="value"
      :is-active-mode="isActiveMode"
      :control-locked="controlLocked"
      :history="history"
      :current-page-id="currentPageId"
      :quality="quality"
    />
    <!-- 模板缺失兜底：可选中、可编辑属性，数据不丢（审查 B4：编辑态提供跳转入口） -->
    <div v-else class="p-2 bg-slate-800 text-white rounded text-xs select-none">
      组件缺失: {{ component.type }}
      <router-link v-if="isActiveMode" to="/widget-templates"
        class="underline ml-1">前往模板管理</router-link>
    </div>

    <!-- 通用变量值浮签：showValue=true 且组件自身无内嵌数值时，在底部覆盖层显示当前值（#6） -->
    <div
      v-if="component.props.showValue && !['gauge-dial', 'gauge-level', 'digital-val', 'trend-chart', 'tank', 'sys-time', 'rounded-btn', 'button', 'image', 'text', 'title-header', 'nav-menu', 'multi-var-dashboard'].includes(component.type)"
      class="absolute inset-x-0 bottom-0 text-center text-[9px] font-mono bg-black/60 text-white rounded-b px-1 truncate pointer-events-none z-20 select-none">
      {{ typeof value === 'boolean' ? (boolValue ? onText : offText) : numValue.toFixed(1) + (unit ? ' ' + unit :
        '')
      }}
    </div>
  </div>
</template>
