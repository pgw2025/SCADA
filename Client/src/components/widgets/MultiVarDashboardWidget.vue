<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';
import { devices } from '../../store/deviceStore';
import { LayoutDashboard } from 'lucide-vue-next';
import type { HmiDashboardItem } from '../../types';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const dashboardTitle = computed(() => propOr('dashboardTitle', '实时参数监控看板'));
const showDashboardTitle = computed(() => propOr('showDashboardTitle', true));
const dashboardTitleBgColor = computed(() => propOr('dashboardTitleBgColor', ''));
const dashboardTitleColor = computed(() => propOr('dashboardTitleColor', ''));
const dashboardLayout = computed<'grid' | 'table' | 'compact'>(() => propOr('dashboardLayout', 'grid'));
const dashboardColumns = computed(() => Number(propOr('dashboardColumns', 2)));
const dashboardGap = computed(() => Number(propOr('dashboardGap', 8)));
const dashboardShowItemBorder = computed(() => propOr('dashboardShowItemBorder', true));
const dashboardItemBorderColor = computed(() => propOr('dashboardItemBorderColor', '#e2e8f0'));
const dashboardItemBgColor = computed(() => propOr('dashboardItemBgColor', '#f8fafc'));
const dashboardValueFontSize = computed(() => Number(propOr('dashboardValueFontSize', 16)));
const dashboardLabelFontSize = computed(() => Number(propOr('dashboardLabelFontSize', 11)));
const dashboardZebra = computed(() => propOr('dashboardZebra', false));

const dashboardItems = computed<HmiDashboardItem[]>(() => {
  const items = props.component.props.dashboardItems;
  if (Array.isArray(items) && items.length > 0) return items;
  const def = defDefaults.value.dashboardItems;
  return Array.isArray(def) ? def : [];
});

// 解析每个多变量子项的实时数据、元数据与报警状态
const dashboardResolvedItems = computed(() => {
  return dashboardItems.value.map((item, idx) => {
    // 优先取 item 本身指定的 deviceId，若无则取组件全局 bindDeviceId，否则取第一个设备
    const devId = item.deviceId != null ? item.deviceId : props.component.bindDeviceId;
    const dev = devId != null
      ? devices.value.find(d => String(d.id) === String(devId))
      : devices.value[0];

    const rawVal = dev?.variables?.[item.variableKey];
    const meta = dev?.variableMeta?.[item.variableKey];
    const label = item.label?.trim() || meta?.name || item.variableKey || `变量 ${idx + 1}`;
    const unit = item.unit !== undefined && item.unit !== '' ? item.unit : (meta?.unit || '');
    const quality = meta?.quality || (dev?.runtimeStatus === 'Offline' ? 'Offline' : 'Good');
    const isQualityBad = quality !== 'Good';

    const isBool = typeof rawVal === 'boolean';
    const isNum = typeof rawVal === 'number';

    let displayVal = '--';
    if (isQualityBad) {
      displayVal = '--';
    } else if (isBool) {
      displayVal = rawVal ? onText.value : offText.value;
    } else if (isNum) {
      const prec = item.precision != null && item.precision !== undefined && item.precision >= 0
        ? Math.min(4, Math.max(0, Math.round(Number(item.precision))))
        : null;
      displayVal = prec !== null ? rawVal.toFixed(prec) : `${rawVal}`;
    } else if (rawVal !== undefined && rawVal !== null) {
      displayVal = String(rawVal);
    }

    const isHigh = !isQualityBad && isNum && item.thresholdMax != null && item.thresholdMax !== undefined && rawVal >= item.thresholdMax;
    const isLow = !isQualityBad && isNum && item.thresholdMin != null && item.thresholdMin !== undefined && rawVal <= item.thresholdMin;
    const isAlarm = isHigh || isLow;

    let statusColor = '#10b981'; // 正常绿
    let statusText = '正常';
    if (isQualityBad) {
      statusColor = '#94a3b8';
      statusText = '离线';
    } else if (isHigh) {
      statusColor = '#ef4444'; // 高限红
      statusText = '高限报警';
    } else if (isLow) {
      statusColor = '#f59e0b'; // 低限黄
      statusText = '低限预警';
    } else if (isBool) {
      statusColor = rawVal ? '#10b981' : '#94a3b8';
      statusText = rawVal ? '运行' : '停止';
    }

    return {
      id: item.id || `item-${idx}`,
      variableKey: item.variableKey,
      label,
      unit,
      rawVal,
      displayVal,
      isBool,
      isNum,
      isQualityBad,
      isHigh,
      isLow,
      isAlarm,
      statusColor,
      statusText,
      showStatusDot: item.showStatusDot !== false,
      devName: dev?.name || '',
    };
  });
});

// 看板整体容器样式
const dashboardContainerStyle = computed(() => {
  const p = props.component.props;
  const hasBorder = p.showBorder !== false;
  const hasBg = p.showBackground !== false;

  const borderWidth = hasBorder ? `${p.borderWidth ?? 1.5}px` : '0px';
  const borderColor = hasBorder ? (p.borderColor || '#cbd5e1') : 'transparent';
  const borderStyle = hasBorder ? (p.borderStyle || 'solid') : 'none';
  const borderRadius = p.borderRadius !== undefined ? `${p.borderRadius}px` : '8px';
  const backgroundColor = hasBg ? (p.bgColor || '#ffffff') : 'transparent';

  return {
    borderWidth,
    borderStyle,
    borderColor,
    borderRadius,
    backgroundColor,
  };
});

// 网格列数样式
const dashboardGridStyle = computed(() => {
  const cols = dashboardColumns.value;
  const gap = `${dashboardGap.value}px`;
  if (cols === 0) {
    return {
      display: 'grid',
      gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))',
      gap,
    };
  }
  return {
    display: 'grid',
    gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))`,
    gap,
  };
});
</script>

<template>
<div
      class="w-full h-full flex flex-col relative overflow-hidden select-none transition-all duration-150"
      :style="dashboardContainerStyle">

      <!-- 标题栏（可选显示） -->
      <div v-if="showDashboardTitle"
        class="shrink-0 flex items-center justify-between px-3 py-1.5 border-b transition-colors" :style="{
          backgroundColor: dashboardTitleBgColor || 'rgba(241, 245, 249, 0.75)',
          borderColor: dashboardShowItemBorder ? dashboardItemBorderColor : 'rgba(226, 232, 240, 0.8)',
          color: dashboardTitleColor || '#1e293b'
        }">
        <div class="flex items-center gap-1.5 min-w-0">
          <div class="w-2 h-2 rounded-full bg-[#1890ff] shadow-sm shadow-sky-400/50" />
          <span class="text-xs font-bold tracking-wide truncate font-sans">
            {{ dashboardTitle }}
          </span>
        </div>
        <div class="flex items-center gap-1.5 shrink-0 text-[10px] font-mono opacity-75">
          <span class="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse" />
          <span>{{ dashboardResolvedItems.length }} 点位</span>
        </div>
      </div>

      <!-- 看板主体内容区域 -->
      <div class="flex-1 p-2.5 overflow-y-auto overflow-x-hidden">
        <!-- 空状态提示 -->
        <div v-if="dashboardResolvedItems.length === 0"
          class="w-full h-full min-h-[80px] flex flex-col items-center justify-center text-slate-400 gap-1.5 text-center p-3">
          <LayoutDashboard class="w-6 h-6 stroke-1 text-slate-300 dark:text-slate-600" />
          <span class="text-xs">暂无监控变量点位</span>
          <span class="text-[10px] text-slate-400 dark:text-slate-500">请在右侧属性面板添加或一键导入变量</span>
        </div>

        <!-- 模式1：卡片网格 (grid) -->
        <div v-else-if="dashboardLayout === 'grid'" :style="dashboardGridStyle">
          <div v-for="item in dashboardResolvedItems" :key="item.id"
            class="flex flex-col justify-between p-2 rounded transition-all relative overflow-hidden" :style="{
              borderWidth: dashboardShowItemBorder ? '1px' : '0px',
              borderStyle: 'solid',
              borderColor: item.isAlarm ? item.statusColor : dashboardItemBorderColor,
              backgroundColor: item.isAlarm ? (item.isHigh ? 'rgba(239, 68, 68, 0.06)' : 'rgba(245, 158, 11, 0.06)') : (dashboardItemBgColor || '#f8fafc'),
              borderRadius: '6px',
            }">

            <!-- 卡片头部：标签与指示灯 -->
            <div class="flex items-center justify-between gap-1 mb-1">
              <div class="flex items-center gap-1 min-w-0 flex-1">
                <span v-if="item.showStatusDot" class="w-2 h-2 rounded-full shrink-0 transition-colors"
                  :class="item.isAlarm ? 'animate-pulse' : ''" :style="{ backgroundColor: item.statusColor }" />
                <span class="font-medium truncate leading-tight text-slate-700 dark:text-slate-200"
                  :style="{ fontSize: `${dashboardLabelFontSize}px` }" :title="`${item.label} (${item.variableKey})`">
                  {{ item.label }}
                </span>
              </div>
              <span v-if="item.isAlarm" class="text-[9px] px-1 py-0.2 rounded font-bold shrink-0 font-sans" :style="{
                backgroundColor: item.isHigh ? '#fee2e2' : '#fef3c7',
                color: item.isHigh ? '#dc2626' : '#d97706'
              }">
                {{ item.statusText }}
              </span>
            </div>

            <!-- 卡片数值主体 -->
            <div class="flex items-baseline justify-between gap-1 font-mono mt-0.5">
              <span class="font-bold tracking-tight tabular-nums truncate" :style="{
                fontSize: `${dashboardValueFontSize}px`,
                color: item.isAlarm ? item.statusColor : (item.isQualityBad ? '#94a3b8' : (activeColor || '#0f172a'))
              }">
                {{ item.displayVal }}
              </span>
              <span v-if="item.unit" class="text-[10px] text-slate-400 font-sans shrink-0 font-normal">
                {{ item.unit }}
              </span>
            </div>
          </div>
        </div>

        <!-- 模式2：列表表格 (table) -->
        <div v-else-if="dashboardLayout === 'table'" class="w-full">
          <table class="w-full text-left border-collapse text-xs">
            <thead>
              <tr class="border-b text-[10px] font-semibold text-slate-400"
                :style="{ borderColor: dashboardItemBorderColor }">
                <th class="py-1 px-1.5">变量/点位</th>
                <th class="py-1 px-1.5 text-right">实时数值</th>
                <th class="py-1 px-1.5 text-center">单位</th>
                <th class="py-1 px-1.5 text-center">状态</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(item, idx) in dashboardResolvedItems" :key="item.id" class="border-b transition-colors"
                :style="{
                  borderColor: dashboardItemBorderColor,
                  backgroundColor: dashboardZebra && idx % 2 === 1 ? 'rgba(0,0,0,0.02)' : 'transparent'
                }">
                <td class="py-1 px-1.5 truncate max-w-[120px]">
                  <div class="flex items-center gap-1">
                    <span v-if="item.showStatusDot" class="w-1.5 h-1.5 rounded-full shrink-0"
                      :style="{ backgroundColor: item.statusColor }" />
                    <span class="font-medium text-slate-700 dark:text-slate-200 truncate"
                      :style="{ fontSize: `${dashboardLabelFontSize}px` }" :title="item.label">
                      {{ item.label }}
                    </span>
                  </div>
                </td>
                <td class="py-1 px-1.5 text-right font-mono font-bold tabular-nums" :style="{
                  fontSize: `${dashboardValueFontSize}px`,
                  color: item.isAlarm ? item.statusColor : (item.isQualityBad ? '#94a3b8' : '#0f172a')
                }">
                  {{ item.displayVal }}
                </td>
                <td class="py-1 px-1.5 text-center text-[10px] text-slate-400 font-sans">
                  {{ item.unit || '-' }}
                </td>
                <td class="py-1 px-1.5 text-center">
                  <span class="text-[9px] px-1.5 py-0.5 rounded-full font-medium" :style="{
                    backgroundColor: item.isAlarm ? (item.isHigh ? '#fee2e2' : '#fef3c7') : '#dcfce7',
                    color: item.isAlarm ? (item.isHigh ? '#dc2626' : '#b45309') : '#15803d'
                  }">
                    {{ item.statusText }}
                  </span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- 模式3：紧凑标签 (compact) -->
        <div v-else-if="dashboardLayout === 'compact'" class="flex flex-wrap gap-1.5">
          <div v-for="item in dashboardResolvedItems" :key="item.id"
            class="flex items-center gap-1.5 px-2 py-1 rounded text-xs transition-all border" :style="{
              borderColor: item.isAlarm ? item.statusColor : dashboardItemBorderColor,
              backgroundColor: dashboardItemBgColor || '#f8fafc'
            }">
            <span v-if="item.showStatusDot" class="w-2 h-2 rounded-full shrink-0"
              :style="{ backgroundColor: item.statusColor }" />
            <span class="text-slate-600 dark:text-slate-300 font-medium"
              :style="{ fontSize: `${dashboardLabelFontSize}px` }">{{ item.label }}:</span>
            <span class="font-mono font-bold tabular-nums"
              :style="{ fontSize: `${dashboardValueFontSize}px`, color: item.statusColor }">{{ item.displayVal }}</span>
            <span v-if="item.unit" class="text-[10px] text-slate-400">{{ item.unit }}</span>
          </div>
        </div>
      </div>
    </div>
</template>
