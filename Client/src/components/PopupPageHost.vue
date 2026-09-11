<script setup lang="ts">
// 弹窗画面宿主：将一张 platform='Popup' 的画面以模态方式呈现（openPagePopup 动作触发）。
// 复用 ModalShell 壳 + 嵌套 CanvasPanel：画布组件/图层/背景/适配缩放/交互全部复用。
// 事件逐项上抛、不接主画面处理器——宿主负责弹窗作用域路由（方案 P1 / D5）。
import type { ScadaPage } from '../types';
import CanvasPanel from './CanvasPanel.vue';
import ModalShell from './ModalShell.vue';
import type { HmiEventWriteMode } from '../types';

const props = defineProps<{
  /** 弹窗画面（platform='Popup'） */
  page: ScadaPage;
  /** 当前角色是否有写权限 */
  canControlWrite?: boolean;
  /** 弹窗画面组件实时值（严格复合键） */
  componentValues?: Record<string, number | boolean>;
  /** 弹窗画面组件质量标记 */
  componentQualities?: Record<string, string>;
}>();

const emit = defineEmits<{
  (e: 'close'): void;
  (e: 'triggerToggleValue', deviceId: number | null, variableKey: string, legacyKey: string, actionType?: string, val?: any): void;
  (e: 'navigateToPage', pageId: string): void;
  (e: 'triggerRunScript', scriptId: number): void;
  (e: 'componentEvent', component: any, eventType: string): void;
  (e: 'requestSetValue', component: any): void;
}>();
</script>

<template>
  <ModalShell :title="page.name" :can-control-write="canControlWrite" @close="emit('close')">
    <!-- CanvasPanel 为只读运行态渲染（隐藏编辑器工具条）；selectedId/selectedIds 固定空 -->
    <CanvasPanel
      class="h-full w-full"
      :components="page.components" :selected-id="null" :selected-ids="[]"
      :is-active-mode="true"
      :component-values="componentValues ?? {}" :component-qualities="componentQualities"
      :canvas-width="page.width" :canvas-height="page.height"
      :can-control-write="canControlWrite" :readonly="true"
      :background="page.background" :adapt-mode="page.adaptMode"
      :layers="page.layers" :current-page-id="page.id"
      @trigger-toggle-value="(...a: any[]) => emit('triggerToggleValue', ...a)"
      @navigate-to-page="(id: string) => emit('navigateToPage', id)"
      @trigger-run-script="(id: number) => emit('triggerRunScript', id)"
      @component-event="(c: any, t: string) => emit('componentEvent', c, t)"
      @request-set-value="(c: any) => emit('requestSetValue', c)"
    />
  </ModalShell>
</template>