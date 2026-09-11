<script setup lang="ts">
// 运行态弹窗宿主：将页内组件以模态窗口呈现（openPopup 动作触发）。
// 复用 ModalShell 壳（标题栏 + X/ESC/遮罩三路关闭），复用 HMIWidget 渲染链
// （builtinRenderers 已注册 vfd-motor-panel 等面板组件），面板的变量绑定 / 实时值 /
// 内部权限判断（hasWritePermission）全部自动继承。
import { computed } from 'vue';
import type { HMIComponent } from '../types';
import HMIWidget from './HMIWidget.vue';
import ModalShell from './ModalShell.vue';

const props = defineProps<{
  /** 弹窗内容源组件（运行态解析为模态） */
  component: HMIComponent;
  /** 当前角色是否有写权限（用于控件锁标显示；面板自身 hasWritePermission 同步生效） */
  canControlWrite?: boolean;
}>();

const emit = defineEmits<{
  (e: 'close'): void;
  /** var-display 设值请求：弹窗内可设定控件点击设值时上抛，由宿主弹数字键盘写值 */
  (e: 'requestSetValue', component: HMIComponent): void;
}>();

// 渲染快照：浅拷贝强制可见/未锁/无图层归属（对齐项目弹窗组件快照约束先例），
// 避免源组件隐藏状态与图层属性影响模态渲染，也隔离模态内交互对画布源对象的污染。
const descriptor = computed<HMIComponent>(() => ({
  ...props.component,
  visible: true,
  locked: false,
  layerId: undefined,
  props: { ...props.component.props },
}));
</script>

<template>
  <ModalShell :title="component.name || component.label || component.type"
    :can-control-write="canControlWrite" @close="emit('close')">
    <!-- value 传 0（面板组件自读 devices store，不消费该 prop）；isActiveMode=true 启用运行态交互；
         @request-set-value 补齐 var-display 设值上抛（修复弹窗内点设值无反应缺陷） -->
    <div class="min-w-[420px] p-2">
      <HMIWidget :component="descriptor" :value="0" :is-active-mode="true"
        :control-locked="!canControlWrite"
        @request-set-value="(c) => emit('requestSetValue', c)" />
    </div>
  </ModalShell>
</template>