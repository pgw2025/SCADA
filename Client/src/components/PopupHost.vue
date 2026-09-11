<script setup lang="ts">
// 运行态弹窗宿主：将页内组件以模态窗口呈现（openPopup 动作触发）。
// 复用 HMIWidget 渲染链（builtinRenderers 已注册 vfd-motor-panel 等面板组件），
// 因此面板的变量绑定 / 实时值 / 内部权限判断（hasWritePermission）全部自动继承。
import { computed, onMounted, onUnmounted } from 'vue';
import type { HMIComponent } from '../types';
import HMIWidget from './HMIWidget.vue';
import { X } from 'lucide-vue-next';

const props = defineProps<{
  /** 弹窗内容源组件（运行态解析为模态） */
  component: HMIComponent;
  /** 当前角色是否有写权限（用于控件锁标显示；面板自身 hasWritePermission 同步生效） */
  canControlWrite?: boolean;
}>();

const emit = defineEmits<{ (e: 'close'): void }>();

// 渲染快照：浅拷贝强制可见/未锁/无图层归属（对齐项目弹窗组件快照约束先例），
// 避免源组件隐藏状态与图层属性影响模态渲染，也隔离模态内交互对画布源对象的污染。
const descriptor = computed<HMIComponent>(() => ({
  ...props.component,
  visible: true,
  locked: false,
  layerId: undefined,
  props: { ...props.component.props },
}));

// ESC 关闭
const onKeydown = (e: KeyboardEvent) => { if (e.key === 'Escape') emit('close'); };
onMounted(() => window.addEventListener('keydown', onKeydown));
onUnmounted(() => window.removeEventListener('keydown', onKeydown));
</script>

<template>
  <!-- 对齐 SetValueDialog 既有约定：fixed inset-0 z-[100]，无 teleport；与 CanvasPanel 缩放容器同级，避免随画布缩放失真 -->
  <div class="fixed inset-0 z-[100] flex items-center justify-center bg-black/50 backdrop-blur-sm p-4"
    @click.self="emit('close')">
    <div class="w-[min(92vw,640px)] max-h-[88vh] rounded-xl shadow-2xl overflow-hidden flex flex-col
                bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700">
      <!-- 标题栏：源组件名（多电机页面可区分设备） -->
      <div class="flex items-center justify-between px-4 py-2.5 border-b border-slate-200 dark:border-slate-700 shrink-0">
        <span class="text-sm font-bold text-slate-800 dark:text-slate-100 truncate">
          {{ component.name || component.label || component.type }}
        </span>
        <button @click="emit('close')"
          class="p-1 rounded text-slate-400 hover:text-red-500 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
          title="关闭 (Esc)">
          <X class="w-4 h-4" />
        </button>
      </div>
      <!-- 内容区：value 传 0（面板组件自读 devices store，不消费该 prop）；isActiveMode=true 启用运行态交互 -->
      <div class="flex-1 overflow-auto p-2">
        <HMIWidget :component="descriptor" :value="0" :is-active-mode="true"
          :control-locked="!canControlWrite" />
      </div>
    </div>
  </div>
</template>