/**
 * 编译期静态资产（组件库动态化后仅剩一类）：
 *  ① 内置渲染器：renderType → SFC（23 项 = 22 个注册类型 + legacy button，审查 A2）
 * lucide 图标字典已抽至 lucideIcons.ts（叶子模块，环终结者），此处仅转发 getLucideIcon。
 * 组件元数据（名称/分类/默认值/排序）一律来自运行时模板源（widgetTemplates.ts）。
 */
import type { Component } from 'vue';
export { getLucideIcon } from './lucideIcons';
import BoilerWidget from './components/widgets/BoilerWidget.vue';
import PumpWidget from './components/widgets/PumpWidget.vue';
import ValveWidget from './components/widgets/ValveWidget.vue';
import TankWidget from './components/widgets/TankWidget.vue';
import PipeHWidget from './components/widgets/PipeHWidget.vue';
import PipeVWidget from './components/widgets/PipeVWidget.vue';
import GaugeDialWidget from './components/widgets/GaugeDialWidget.vue';
import GaugeLevelWidget from './components/widgets/GaugeLevelWidget.vue';
import DigitalValWidget from './components/widgets/DigitalValWidget.vue';
import VarDisplayWidget from './components/widgets/VarDisplayWidget.vue';
import TrendChartWidget from './components/widgets/TrendChartWidget.vue';
import ConveyorWidget from './components/widgets/ConveyorWidget.vue';
import TextWidget from './components/widgets/TextWidget.vue';
import LedWidget from './components/widgets/LedWidget.vue';
import ButtonWidget from './components/widgets/ButtonWidget.vue';   // legacy：registry 无注册但存量页面在用（审查 A2）
import SwitchWidget from './components/widgets/SwitchWidget.vue';
import SysTimeWidget from './components/widgets/SysTimeWidget.vue';
import RoundedBtnWidget from './components/widgets/RoundedBtnWidget.vue';
import MotorWidget from './components/widgets/MotorWidget.vue';
import ImageWidget from './components/widgets/ImageWidget.vue';
import TitleHeaderWidget from './components/widgets/TitleHeaderWidget.vue';
import NavMenuWidget from './components/widgets/NavMenuWidget.vue';
import MultiVarDashboardWidget from './components/widgets/MultiVarDashboardWidget.vue';

/** 内置渲染器：renderType → SFC。23 项 = 22 个注册类型 + legacy button。 */
export const builtinRenderers: Record<string, Component> = {
  boiler: BoilerWidget, pump: PumpWidget, valve: ValveWidget, tank: TankWidget,
  'pipe-h': PipeHWidget, 'pipe-v': PipeVWidget,
  'gauge-dial': GaugeDialWidget, 'gauge-level': GaugeLevelWidget,
  'digital-val': DigitalValWidget, 'var-display': VarDisplayWidget,
  'trend-chart': TrendChartWidget, conveyor: ConveyorWidget, text: TextWidget,
  led: LedWidget, button: ButtonWidget, switch: SwitchWidget,
  'sys-time': SysTimeWidget, 'rounded-btn': RoundedBtnWidget, motor: MotorWidget,
  image: ImageWidget, 'title-header': TitleHeaderWidget, 'nav-menu': NavMenuWidget,
  'multi-var-dashboard': MultiVarDashboardWidget,
};
