/**
 * lucide 图标字典（叶子模块）：图标名 → 组件（值域 = 原 widgetRegistry 的 19 个图标 import）。
 * 只依赖第三方 lucide-vue-next，无任何应用内 import —— 作为环终结者，
 * 供 builtinSeeds / builtinRenderers / widgetTemplates 共同引用，避免循环依赖（TDZ）。
 */
import type { Component } from 'vue';
import {
  BatteryCharging, Cpu, ToggleLeft, Layers, Workflow, RefreshCw, Gauge, Thermometer,
  Tv, Hash, LayoutDashboard, Activity, Clock, Type, Sparkles, ToggleRight,
  Image as ImageIcon, Monitor, Smartphone, PanelTop,
} from 'lucide-vue-next';

export const LUCIDE_ICON_MAP: Record<string, Component> = {
  'battery-charging': BatteryCharging, cpu: Cpu, 'toggle-left': ToggleLeft,
  layers: Layers, workflow: Workflow, 'refresh-cw': RefreshCw, gauge: Gauge,
  thermometer: Thermometer, tv: Tv, hash: Hash, 'layout-dashboard': LayoutDashboard,
  activity: Activity, clock: Clock, type: Type, sparkles: Sparkles,
  'toggle-right': ToggleRight, image: ImageIcon, monitor: Monitor,
  smartphone: Smartphone, 'panel-top': PanelTop,
};

export const getLucideIcon = (name: string): Component | undefined =>
  LUCIDE_ICON_MAP[name];
