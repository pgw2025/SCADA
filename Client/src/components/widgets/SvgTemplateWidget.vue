<script setup lang="ts">
// SVG 轨通用渲染器（P4）：sanitize + bind 后 v-html 渲染。
// - 绑定上下文来自 useWidgetBase（与 builtin 轨同一真相源）
// - 前端二次清洗（sanitizeSvg）与后端入库清洗构成双保险（D7）
// - 纯展示轨：pointer-events-none；模板约定根节点 width="100%" height="100%" + viewBox
import { computed } from 'vue';
import { useWidgetBase, type HmiWidgetProps } from './useWidgetBase';
import { getWidgetDef } from '../../widgetRegistry';
import { bindSvgTemplate, sanitizeSvg, type SvgBindingContext } from '../../utils/svgTemplate';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);

const svgHtml = computed(() => {
  const def = getWidgetDef(props.component.type);
  const tpl = def?.renderKind === 'svg' ? def.svgTemplate : '';
  if (!tpl || !tpl.trim()) {
    return '<div style="padding:8px;font-size:12px;color:#94a3b8">SVG 模板为空</div>';
  }
  const ctx: SvgBindingContext = {
    value: props.value,
    numValue: base.numValue.value,
    boolValue: base.boolValue.value,
    normalizedPercent: base.normalizedPercent.value,
    state: base.boolValue.value ? base.onText.value : base.offText.value,
    unit: base.unit.value,
    label: props.component.label ?? '',
    activeColor: base.activeColor.value,
    inactiveColor: base.inactiveColor.value,
    alertColor: base.alertColor.value,
    thresholdMin: base.thresholdMin.value,
    thresholdMax: base.thresholdMax.value,
    fontSize: base.fontSize.value,
    quality: props.quality ?? '',
  };
  return bindSvgTemplate(sanitizeSvg(tpl), ctx);
});
</script>

<template>
  <!-- 纯展示轨：pointer-events-none 避免拦截画布操作；SVG 根节点约定 width/height 100% + viewBox 等比适配 -->
  <div class="relative w-full h-full pointer-events-none select-none" v-html="svgHtml" />
</template>
