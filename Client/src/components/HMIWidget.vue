<script setup lang="ts">
// 瘦分发器：按 component.type 渲染对应图元 SFC，共享逻辑统一来自 widgets/useWidgetBase。
// 仅保留外层包裹、通用 showValue 浮签与“未知图元”兜底。
import type { HmiWidgetProps } from './widgets/useWidgetBase';
import { useWidgetBase } from './widgets/useWidgetBase';

import BoilerWidget from './widgets/BoilerWidget.vue';
import PumpWidget from './widgets/PumpWidget.vue';
import ValveWidget from './widgets/ValveWidget.vue';
import TankWidget from './widgets/TankWidget.vue';
import PipeHWidget from './widgets/PipeHWidget.vue';
import PipeVWidget from './widgets/PipeVWidget.vue';
import GaugeDialWidget from './widgets/GaugeDialWidget.vue';
import GaugeLevelWidget from './widgets/GaugeLevelWidget.vue';
import DigitalValWidget from './widgets/DigitalValWidget.vue';
import VarDisplayWidget from './widgets/VarDisplayWidget.vue';
import TrendChartWidget from './widgets/TrendChartWidget.vue';
import ConveyorWidget from './widgets/ConveyorWidget.vue';
import TextWidget from './widgets/TextWidget.vue';
import LedWidget from './widgets/LedWidget.vue';
import ButtonWidget from './widgets/ButtonWidget.vue';
import SwitchWidget from './widgets/SwitchWidget.vue';
import SysTimeWidget from './widgets/SysTimeWidget.vue';
import RoundedBtnWidget from './widgets/RoundedBtnWidget.vue';
import MotorWidget from './widgets/MotorWidget.vue';
import ImageWidget from './widgets/ImageWidget.vue';
import TitleHeaderWidget from './widgets/TitleHeaderWidget.vue';
import NavMenuWidget from './widgets/NavMenuWidget.vue';
import MultiVarDashboardWidget from './widgets/MultiVarDashboardWidget.vue';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
// 通用浮签需用到的共享派生（与子组件共用同一真相源）
const { numValue, boolValue, onText, offText, unit } = base;

const widgetMap: Record<string, any> = {
  boiler: BoilerWidget,
  pump: PumpWidget,
  valve: ValveWidget,
  tank: TankWidget,
  'pipe-h': PipeHWidget,
  'pipe-v': PipeVWidget,
  'gauge-dial': GaugeDialWidget,
  'gauge-level': GaugeLevelWidget,
  'digital-val': DigitalValWidget,
  'var-display': VarDisplayWidget,
  'trend-chart': TrendChartWidget,
  conveyor: ConveyorWidget,
  text: TextWidget,
  led: LedWidget,
  button: ButtonWidget,
  switch: SwitchWidget,
  'sys-time': SysTimeWidget,
  'rounded-btn': RoundedBtnWidget,
  motor: MotorWidget,
  image: ImageWidget,
  'title-header': TitleHeaderWidget,
  'nav-menu': NavMenuWidget,
  'multi-var-dashboard': MultiVarDashboardWidget,
};
</script>

<template>
  <div class="relative w-full h-full">
    <component
      :is="widgetMap[component.type]"
      v-if="widgetMap[component.type]"
      :component="component"
      :value="value"
      :is-active-mode="isActiveMode"
      :control-locked="controlLocked"
      :history="history"
      :current-page-id="currentPageId"
      :quality="quality"
    />
    <!-- ERROR -->
    <div v-else class="p-2 bg-slate-800 text-white rounded text-xs select-none">
      Unknown Widget: {{ component.type }}
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
