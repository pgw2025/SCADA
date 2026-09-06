<script setup lang="ts">
import { computed, ref } from 'vue';
import { HMIComponent } from '../../types';
import { devices } from '../../store/deviceStore';
import { showToast } from '../../services/toastService';
import {
  Sliders,
  Cpu,
  Activity,
  Zap,
  CheckCircle2,
  AlertTriangle,
  Sparkles,
  RefreshCw,
  PlusCircle,
  ExternalLink,
  ChevronRight,
  HelpCircle,
  Radio,
  SlidersHorizontal,
  Server,
  Palette
} from 'lucide-vue-next';

const props = defineProps<{
  component: HMIComponent;
}>();

const emit = defineEmits<{
  (e: 'updateProp', key: string, value: any): void;
}>();

const componentProps = computed(() => props.component.props ?? {});
const updateProp = (key: string, value: any) => emit('updateProp', key, value);

// 主设备选择（优先取 opDeviceId，其次取组件顶层的 bindDeviceId，再默认取第一台设备）
const primaryDeviceId = computed<number | null>(() => {
  const op = componentProps.value.opDeviceId;
  if (op !== undefined && op !== null && op !== '') return Number(op);
  if (props.component.bindDeviceId !== undefined && props.component.bindDeviceId !== null) {
    return Number(props.component.bindDeviceId);
  }
  return devices.value[0]?.id ?? null;
});

const primaryDevice = computed(() => {
  if (primaryDeviceId.value == null) return null;
  return devices.value.find(d => Number(d.id) === Number(primaryDeviceId.value)) ?? null;
});

// 解析指定设备下的所有变量选项（参考多变量看板）
const getDeviceVariableOptions = (devId?: number | null) => {
  const targetId = devId != null ? devId : primaryDeviceId.value;
  const dev = devices.value.find(d => Number(d.id) === Number(targetId)) || primaryDevice.value;
  if (!dev || !dev.variables) return [];

  return Object.keys(dev.variables).map(k => {
    const meta = dev.variableMeta?.[k];
    const val = dev.variables[k];
    const isNum = typeof val === 'number';
    return {
      key: k,
      name: meta?.name || k,
      unit: meta?.unit || '',
      type: isNum ? 'analog' : 'digital',
      value: val
    };
  });
};

// 辅助：获取某个点位当前的实时数值或状态
const getPointCurrentValue = (varKey?: string, devIdOverride?: number | null) => {
  if (!varKey) return null;
  const targetId = devIdOverride != null ? devIdOverride : primaryDeviceId.value;
  const dev = devices.value.find(d => Number(d.id) === Number(targetId));
  if (!dev || !dev.variables || dev.variables[varKey] === undefined) return null;
  return dev.variables[varKey];
};

// 记录展开“独立设备指定”的点位
const expandedDevPoints = ref<Record<string, boolean>>({});
const toggleDevOverride = (pointKey: string) => {
  expandedDevPoints.value[pointKey] = !expandedDevPoints.value[pointKey];
};

// 记录各点位是否处于“自定义手动输入”模式
const customInputModes = ref<Record<string, boolean>>({});
const toggleCustomInput = (pointKey: string) => {
  customInputModes.value[pointKey] = !customInputModes.value[pointKey];
};

// 解析数值
const numInput = (raw: string, fallback: number): number => {
  const n = parseFloat(raw);
  return Number.isFinite(n) ? n : fallback;
};

// ===== 一键向当前设备注入变频电机 10 项标准点位 =====
const injectStandardMotorVariables = () => {
  const dev = primaryDevice.value;
  if (!dev) {
    showToast('请先选择一个关联的目标设备', 'warning');
    return;
  }
  if (!dev.variables) dev.variables = {};
  if (!dev.variableMeta) dev.variableMeta = {};

  const standardPoints = [
    { key: 'cmd_start', name: '电机启动指令', val: false, unit: '' },
    { key: 'cmd_stop', name: '电机停止指令', val: false, unit: '' },
    { key: 'cmd_reset', name: '故障复位指令', val: false, unit: '' },
    { key: 'st_ready', name: '电机就绪状态', val: true, unit: '' },
    { key: 'st_running', name: '电机运行状态', val: false, unit: '' },
    { key: 'st_fault', name: '电机故障报警', val: false, unit: '' },
    { key: 'comm_ok', name: '驱动通信正常', val: true, unit: '' },
    { key: 'sp_freq', name: '变频设定频率', val: 30.0, unit: 'Hz' },
    { key: 'pv_freq', name: '变频反馈频率', val: 0.0, unit: 'Hz' },
    { key: 'pv_current', name: '电机运行电流', val: 0.0, unit: 'A' },
  ];

  standardPoints.forEach(pt => {
    if (dev.variables[pt.key] === undefined) {
      dev.variables[pt.key] = pt.val;
    }
    if (!dev.variableMeta![pt.key]) {
      dev.variableMeta![pt.key] = {
        key: pt.key,
        name: pt.name,
        unit: pt.unit,
        quality: 'Good'
      } as any;
    }
  });

  // 立即将控制面板各点位绑定至注入的点位
  updateProp('startVar', 'cmd_start');
  updateProp('stopVar', 'cmd_stop');
  updateProp('resetVar', 'cmd_reset');
  updateProp('readyVar', 'st_ready');
  updateProp('runningVar', 'st_running');
  updateProp('faultVar', 'st_fault');
  updateProp('signalVar', 'comm_ok');
  updateProp('spFreqVar', 'sp_freq');
  updateProp('pvFreqVar', 'pv_freq');
  updateProp('currentVar', 'pv_current');

  showToast(`已成功为设备 [${dev.name}] 注入 10 项标准变频电机点位并完成自动绑定`, 'success');
};

// ===== 一键根据规则自动扫描并配对当前设备中的变量 =====
const autoMatchVariables = () => {
  const dev = primaryDevice.value;
  if (!dev?.variables) {
    showToast('当前设备未配置任何变量，请先创建或注入变量', 'warning');
    return;
  }

  const vars = Object.keys(dev.variables);
  let matchedCount = 0;

  const findMatch = (candidates: string[]) => {
    return vars.find(v => {
      const metaName = dev.variableMeta?.[v]?.name || '';
      const lowerKey = v.toLowerCase();
      const lowerName = metaName.toLowerCase();
      return candidates.some(c => lowerKey.includes(c) || lowerName.includes(c));
    });
  };

  const tryMatch = (propKey: string, candidates: string[]) => {
    const matched = findMatch(candidates);
    if (matched) {
      updateProp(propKey, matched);
      matchedCount++;
    }
  };

  tryMatch('startVar', ['start', 'run_cmd', 'cmd_start', '启动', '开机']);
  tryMatch('stopVar', ['stop', 'cmd_stop', '停止', '关机', 'halt']);
  tryMatch('resetVar', ['reset', 'cmd_reset', '复位', 'rst', 'ack']);
  tryMatch('readyVar', ['ready', 'st_ready', '就绪', '备妥', 'auto_ready']);
  tryMatch('runningVar', ['running', 'st_running', '运行', 'is_running', 'run']);
  tryMatch('faultVar', ['fault', 'st_fault', '故障', 'alarm', '报警', 'trip', 'error']);
  tryMatch('signalVar', ['comm', 'signal', 'link', '通信', '信号', 'online']);
  tryMatch('spFreqVar', ['sp_freq', 'freq_sp', '设定频率', '频率设定', 'speed_sp', 'hz_set']);
  tryMatch('pvFreqVar', ['pv_freq', 'freq_pv', '反馈频率', '实际频率', 'speed_pv', 'freq', 'hz']);
  tryMatch('currentVar', ['current', 'pv_current', '电流', 'amp', 'cur', 'load_current']);

  if (matchedCount > 0) {
    showToast(`智能配对成功：已自动匹配并绑定 ${matchedCount} 个点位`, 'success');
  } else {
    showToast('未在当前设备中找到名称匹配的常见点位，可使用「注入标准点位」快速初始化', 'info');
  }
};
</script>

<template>
  <div class="space-y-4">
    <!-- 头部横幅与智能操作工具栏 -->
    <div class="border border-amber-500/30 dark:border-amber-500/20 p-3 rounded-lg bg-amber-50/40 dark:bg-amber-950/20 space-y-3">
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-1.5">
          <Sliders class="w-4 h-4 text-amber-500 shrink-0" />
          <div>
            <p class="font-bold text-amber-700 dark:text-amber-400 text-xs">变频电机控制面板设置</p>
            <p class="text-[10px] text-amber-600/80 dark:text-amber-500/70">多点位变量绑定与电气参数配置</p>
          </div>
        </div>

        <div class="flex items-center gap-1.5">
          <!-- 智能配对按钮 -->
          <button type="button" @click="autoMatchVariables"
            class="flex items-center gap-1 px-2 py-1 rounded bg-amber-500/20 hover:bg-amber-500/30 text-amber-700 dark:text-amber-300 text-[10px] font-medium transition-colors cursor-pointer"
            title="根据命名规则自动扫描并配对当前设备点位">
            <Sparkles class="w-3 h-3 text-amber-500" />
            <span>智能配对</span>
          </button>

          <!-- 一键注入标准点位 -->
          <button type="button" @click="injectStandardMotorVariables"
            class="flex items-center gap-1 px-2 py-1 rounded bg-emerald-600 hover:bg-emerald-500 text-white text-[10px] font-medium transition-colors shadow-sm cursor-pointer"
            title="快速在当前设备中生成 10 项标准变频电机测试点位">
            <PlusCircle class="w-3 h-3" />
            <span>注入标准点位</span>
          </button>
        </div>
      </div>

      <!-- 全局主设备绑定选择器 -->
      <div class="pt-2 border-t border-amber-200/50 dark:border-amber-900/40">
        <div class="flex items-center justify-between mb-1">
          <label class="text-[10px] font-semibold text-gray-700 dark:text-slate-300 flex items-center gap-1">
            <Server class="w-3 h-3 text-amber-500" />
            关联主 PLC / 驱动器设备
          </label>
          <span v-if="primaryDevice" class="text-[9px] font-mono px-1.5 py-0.2 rounded bg-amber-100 dark:bg-amber-900/50 text-amber-700 dark:text-amber-300">
            {{ getDeviceVariableOptions().length }} 个可用变量
          </span>
        </div>
        <select :value="primaryDeviceId ?? ''"
          @change="updateProp('opDeviceId', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)"
          class="w-full bg-white dark:bg-slate-950 border border-amber-300 dark:border-amber-800 rounded px-2 py-1.5 focus:outline-none focus:border-amber-500 text-xs text-gray-800 dark:text-white">
          <option value="">-- 未指定设备（禁止裸 key 写入）--</option>
          <option v-for="dev in devices" :key="dev.id" :value="dev.id">
            {{ dev.name }} ({{ dev.key || 'PLC' }}) - {{ dev.ipAddress || '127.0.0.1' }}
          </option>
        </select>
        <p class="text-[9px] text-gray-500 dark:text-slate-400 mt-1 leading-normal">
          各点位默认继承此设备。如需跨设备联动（如电流来自独立电表），可在下方点位项中切换独立设备。
        </p>
      </div>
    </div>

    <!-- 模块零：外观风格主题（与导航菜单 nav-menu 同套预设） -->
    <div class="border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/40 dark:bg-sky-950/20 space-y-2.5 text-xs">
      <p class="font-bold text-sky-600 dark:text-sky-400 text-[11px] uppercase tracking-wider flex items-center gap-1.5">
        <Palette class="w-3.5 h-3.5" />
        外观风格主题 (Style Preset)
      </p>

      <div>
        <label class="text-[10px] text-gray-500 dark:text-slate-400">风格主题</label>
        <select :value="componentProps.panelStyle || 'slate-dark'"
          @change="updateProp('panelStyle', ($event.target as HTMLSelectElement).value)"
          class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
          <optgroup label="☀️ 浅色大方系列">
            <option value="pure-white">极简亮白 (Pure Crisp White · 浅色)</option>
            <option value="titanium-light">工业钛灰 (Titanium Light · 浅色)</option>
          </optgroup>
          <optgroup label="🌙 深色稳健系列">
            <option value="slate-dark">经典石板深灰 (Classic Slate · 深色)</option>
            <option value="navy-midnight">深海商务暗蓝 (Navy Midnight · 深色)</option>
          </optgroup>
          <optgroup label="🌿 轻量通透系列">
            <option value="translucent-frost">悬浮通透胶囊 (Adaptive Frost · 通透)</option>
          </optgroup>
          <optgroup label="⚙️ 经典特色预设">
            <option value="eco-green">生态翡翠绿 (Eco Green)</option>
            <option value="carbon-orange">机能碳纤橙 (Carbon Orange)</option>
            <option value="tech-blue">科技蓝 (Tech Blue)</option>
          </optgroup>
        </select>
      </div>

      <!-- 主题微调：强调色 -->
      <div>
        <label class="text-[10px] text-gray-500 dark:text-slate-400">强调色（运行光晕 / PV 反馈 / 信号灯）</label>
        <input type="color" :value="componentProps.panelAccentColor ?? '#38bdf8'"
          @input="updateProp('panelAccentColor', ($event.target as HTMLInputElement).value)"
          class="w-full h-7 bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded cursor-pointer" />
        <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-1 leading-normal">
          保持默认色即为各主题推荐强调色；启停/故障等安全语义色不随主题与强调色变化。
        </p>
      </div>
    </div>

    <!-- 模块一：控制指令点位绑定 (PLC DO / Coils) -->
    <div class="border border-emerald-200/80 dark:border-emerald-900/60 p-3 rounded-lg bg-emerald-50/30 dark:bg-emerald-950/10 space-y-2.5 text-xs">
      <div class="flex items-center justify-between pb-1.5 border-b border-emerald-100 dark:border-emerald-900/40">
        <div class="flex items-center gap-1.5">
          <Cpu class="w-3.5 h-3.5 text-emerald-600 dark:text-emerald-400" />
          <p class="font-bold text-emerald-700 dark:text-emerald-400 text-[11px] uppercase tracking-wider">
            控制指令点位 (PLC DO / 写入)
          </p>
        </div>
        <span class="text-[9px] font-mono text-emerald-600 dark:text-emerald-400 bg-emerald-100 dark:bg-emerald-900/40 px-1 rounded">布尔开关量</span>
      </div>

      <!-- 启动指令点位 -->
      <div class="p-2 rounded bg-white dark:bg-slate-900 border border-emerald-100 dark:border-emerald-900/30 space-y-1.5">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-1">
            <span class="w-2 h-2 rounded-full bg-emerald-500 inline-block"></span>
            <span class="font-medium text-gray-800 dark:text-slate-200 text-[11px]">启动指令点位 (Start)</span>
          </div>
          <!-- 实时数值微标 -->
          <div class="flex items-center gap-1 text-[10px]">
            <span v-if="getPointCurrentValue(componentProps.startVar, componentProps.startDeviceId) !== null"
              class="font-mono px-1 rounded text-[9px]"
              :class="getPointCurrentValue(componentProps.startVar, componentProps.startDeviceId) ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900 dark:text-emerald-300' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'">
              当前: {{ getPointCurrentValue(componentProps.startVar, componentProps.startDeviceId) ? 'ON' : 'OFF' }}
            </span>
            <button type="button" @click="toggleDevOverride('startVar')"
              class="text-emerald-600 dark:text-emerald-400 hover:underline cursor-pointer text-[9px]">
              {{ expandedDevPoints['startVar'] ? '收起设备' : '设备跨选' }}
            </button>
            <button type="button" @click="toggleCustomInput('startVar')"
              class="text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 cursor-pointer text-[9px]">
              {{ customInputModes['startVar'] ? '选择模式' : '手输模式' }}
            </button>
          </div>
        </div>

        <div v-if="expandedDevPoints['startVar']" class="grid grid-cols-1 gap-1 pb-1">
          <label class="text-[9px] text-gray-400">指定设备</label>
          <select :value="componentProps.startDeviceId ?? ''"
            @change="updateProp('startDeviceId', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px]">
            <option value="">-- 继承默认主设备 --</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
          </select>
        </div>

        <!-- 变量输入/下拉 -->
        <input v-if="customInputModes['startVar']" type="text"
          :value="componentProps.startVar ?? 'cmd_start'"
          @input="updateProp('startVar', ($event.target as HTMLInputElement).value)"
          placeholder="变量键名，如 cmd_start"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] font-mono text-gray-800 dark:text-white focus:outline-none focus:border-emerald-500" />
        <select v-else :value="componentProps.startVar ?? ''"
          @change="updateProp('startVar', ($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] text-gray-800 dark:text-white focus:outline-none focus:border-emerald-500">
          <option value="">-- 请选择启动指令变量 --</option>
          <option v-for="v in getDeviceVariableOptions(componentProps.startDeviceId)" :key="v.key" :value="v.key">
            {{ v.name }} ({{ v.key }})
          </option>
        </select>
      </div>

      <!-- 停止指令点位 -->
      <div class="p-2 rounded bg-white dark:bg-slate-900 border border-emerald-100 dark:border-emerald-900/30 space-y-1.5">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-1">
            <span class="w-2 h-2 rounded-full bg-rose-500 inline-block"></span>
            <span class="font-medium text-gray-800 dark:text-slate-200 text-[11px]">停止指令点位 (Stop)</span>
          </div>
          <div class="flex items-center gap-1 text-[10px]">
            <span v-if="getPointCurrentValue(componentProps.stopVar, componentProps.stopDeviceId) !== null"
              class="font-mono px-1 rounded text-[9px]"
              :class="getPointCurrentValue(componentProps.stopVar, componentProps.stopDeviceId) ? 'bg-rose-100 text-rose-700 dark:bg-rose-900 dark:text-rose-300' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'">
              当前: {{ getPointCurrentValue(componentProps.stopVar, componentProps.stopDeviceId) ? 'ON' : 'OFF' }}
            </span>
            <button type="button" @click="toggleDevOverride('stopVar')"
              class="text-emerald-600 dark:text-emerald-400 hover:underline cursor-pointer text-[9px]">
              {{ expandedDevPoints['stopVar'] ? '收起设备' : '设备跨选' }}
            </button>
            <button type="button" @click="toggleCustomInput('stopVar')"
              class="text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 cursor-pointer text-[9px]">
              {{ customInputModes['stopVar'] ? '选择模式' : '手输模式' }}
            </button>
          </div>
        </div>

        <div v-if="expandedDevPoints['stopVar']" class="grid grid-cols-1 gap-1 pb-1">
          <label class="text-[9px] text-gray-400">指定设备</label>
          <select :value="componentProps.stopDeviceId ?? ''"
            @change="updateProp('stopDeviceId', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px]">
            <option value="">-- 继承默认主设备 --</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
          </select>
        </div>

        <input v-if="customInputModes['stopVar']" type="text"
          :value="componentProps.stopVar ?? 'cmd_stop'"
          @input="updateProp('stopVar', ($event.target as HTMLInputElement).value)"
          placeholder="变量键名，如 cmd_stop"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] font-mono text-gray-800 dark:text-white focus:outline-none focus:border-emerald-500" />
        <select v-else :value="componentProps.stopVar ?? ''"
          @change="updateProp('stopVar', ($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] text-gray-800 dark:text-white focus:outline-none focus:border-emerald-500">
          <option value="">-- 请选择停止指令变量 --</option>
          <option v-for="v in getDeviceVariableOptions(componentProps.stopDeviceId)" :key="v.key" :value="v.key">
            {{ v.name }} ({{ v.key }})
          </option>
        </select>
      </div>

      <!-- 复位指令点位 -->
      <div class="p-2 rounded bg-white dark:bg-slate-900 border border-emerald-100 dark:border-emerald-900/30 space-y-1.5">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-1">
            <span class="w-2 h-2 rounded-full bg-amber-500 inline-block"></span>
            <span class="font-medium text-gray-800 dark:text-slate-200 text-[11px]">故障复位点位 (Reset)</span>
          </div>
          <div class="flex items-center gap-1 text-[10px]">
            <span v-if="getPointCurrentValue(componentProps.resetVar, componentProps.resetDeviceId) !== null"
              class="font-mono px-1 rounded text-[9px]"
              :class="getPointCurrentValue(componentProps.resetVar, componentProps.resetDeviceId) ? 'bg-amber-100 text-amber-700 dark:bg-amber-900 dark:text-amber-300' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'">
              当前: {{ getPointCurrentValue(componentProps.resetVar, componentProps.resetDeviceId) ? 'ON' : 'OFF' }}
            </span>
            <button type="button" @click="toggleDevOverride('resetVar')"
              class="text-emerald-600 dark:text-emerald-400 hover:underline cursor-pointer text-[9px]">
              {{ expandedDevPoints['resetVar'] ? '收起设备' : '设备跨选' }}
            </button>
            <button type="button" @click="toggleCustomInput('resetVar')"
              class="text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 cursor-pointer text-[9px]">
              {{ customInputModes['resetVar'] ? '选择模式' : '手输模式' }}
            </button>
          </div>
        </div>

        <div v-if="expandedDevPoints['resetVar']" class="grid grid-cols-1 gap-1 pb-1">
          <label class="text-[9px] text-gray-400">指定设备</label>
          <select :value="componentProps.resetDeviceId ?? ''"
            @change="updateProp('resetDeviceId', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px]">
            <option value="">-- 继承默认主设备 --</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
          </select>
        </div>

        <input v-if="customInputModes['resetVar']" type="text"
          :value="componentProps.resetVar ?? 'cmd_reset'"
          @input="updateProp('resetVar', ($event.target as HTMLInputElement).value)"
          placeholder="变量键名，如 cmd_reset"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] font-mono text-gray-800 dark:text-white focus:outline-none focus:border-emerald-500" />
        <select v-else :value="componentProps.resetVar ?? ''"
          @change="updateProp('resetVar', ($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] text-gray-800 dark:text-white focus:outline-none focus:border-emerald-500">
          <option value="">-- 请选择复位指令变量 --</option>
          <option v-for="v in getDeviceVariableOptions(componentProps.resetDeviceId)" :key="v.key" :value="v.key">
            {{ v.name }} ({{ v.key }})
          </option>
        </select>
      </div>
    </div>

    <!-- 模块二：状态信号点位反馈 (PLC DI / Discrete 读取) -->
    <div class="border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/30 dark:bg-sky-950/10 space-y-2.5 text-xs">
      <div class="flex items-center justify-between pb-1.5 border-b border-sky-100 dark:border-sky-900/40">
        <div class="flex items-center gap-1.5">
          <Activity class="w-3.5 h-3.5 text-sky-600 dark:text-sky-400" />
          <p class="font-bold text-sky-700 dark:text-sky-400 text-[11px] uppercase tracking-wider">
            状态信号反馈 (PLC DI / 读取)
          </p>
        </div>
        <span class="text-[9px] font-mono text-sky-600 dark:text-sky-400 bg-sky-100 dark:bg-sky-900/40 px-1 rounded">遥信状态</span>
      </div>

      <!-- 就绪信号 (Ready) -->
      <div class="p-2 rounded bg-white dark:bg-slate-900 border border-sky-100 dark:border-sky-900/30 space-y-1.5">
        <div class="flex items-center justify-between">
          <span class="font-medium text-gray-800 dark:text-slate-200 text-[11px]">就绪状态信号 (Ready)</span>
          <div class="flex items-center gap-1 text-[10px]">
            <span v-if="getPointCurrentValue(componentProps.readyVar, componentProps.readyDeviceId) !== null"
              class="font-mono px-1 rounded text-[9px]"
              :class="getPointCurrentValue(componentProps.readyVar, componentProps.readyDeviceId) ? 'bg-sky-100 text-sky-700 dark:bg-sky-900 dark:text-sky-300' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'">
              {{ getPointCurrentValue(componentProps.readyVar, componentProps.readyDeviceId) ? '就绪' : '未就绪' }}
            </span>
            <button type="button" @click="toggleDevOverride('readyVar')"
              class="text-sky-600 dark:text-sky-400 hover:underline cursor-pointer text-[9px]">
              {{ expandedDevPoints['readyVar'] ? '收起' : '设备' }}
            </button>
            <button type="button" @click="toggleCustomInput('readyVar')"
              class="text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 cursor-pointer text-[9px]">
              {{ customInputModes['readyVar'] ? '下拉' : '手输' }}
            </button>
          </div>
        </div>

        <div v-if="expandedDevPoints['readyVar']" class="grid grid-cols-1 gap-1 pb-1">
          <select :value="componentProps.readyDeviceId ?? ''"
            @change="updateProp('readyDeviceId', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px]">
            <option value="">-- 继承默认主设备 --</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
          </select>
        </div>

        <input v-if="customInputModes['readyVar']" type="text"
          :value="componentProps.readyVar ?? 'st_ready'"
          @input="updateProp('readyVar', ($event.target as HTMLInputElement).value)"
          placeholder="如 st_ready"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] font-mono text-gray-800 dark:text-white" />
        <select v-else :value="componentProps.readyVar ?? ''"
          @change="updateProp('readyVar', ($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] text-gray-800 dark:text-white">
          <option value="">-- 选择就绪变量 (st_ready) --</option>
          <option v-for="v in getDeviceVariableOptions(componentProps.readyDeviceId)" :key="v.key" :value="v.key">
            {{ v.name }} ({{ v.key }})
          </option>
        </select>
      </div>

      <!-- 运行信号 (Running) -->
      <div class="p-2 rounded bg-white dark:bg-slate-900 border border-sky-100 dark:border-sky-900/30 space-y-1.5">
        <div class="flex items-center justify-between">
          <span class="font-medium text-gray-800 dark:text-slate-200 text-[11px]">运行状态信号 (Running)</span>
          <div class="flex items-center gap-1 text-[10px]">
            <span v-if="getPointCurrentValue(componentProps.runningVar, componentProps.runningDeviceId) !== null"
              class="font-mono px-1 rounded text-[9px]"
              :class="getPointCurrentValue(componentProps.runningVar, componentProps.runningDeviceId) ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900 dark:text-emerald-300' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'">
              {{ getPointCurrentValue(componentProps.runningVar, componentProps.runningDeviceId) ? '运行中' : '已停止' }}
            </span>
            <button type="button" @click="toggleDevOverride('runningVar')"
              class="text-sky-600 dark:text-sky-400 hover:underline cursor-pointer text-[9px]">
              {{ expandedDevPoints['runningVar'] ? '收起' : '设备' }}
            </button>
            <button type="button" @click="toggleCustomInput('runningVar')"
              class="text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 cursor-pointer text-[9px]">
              {{ customInputModes['runningVar'] ? '下拉' : '手输' }}
            </button>
          </div>
        </div>

        <div v-if="expandedDevPoints['runningVar']" class="grid grid-cols-1 gap-1 pb-1">
          <select :value="componentProps.runningDeviceId ?? ''"
            @change="updateProp('runningDeviceId', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px]">
            <option value="">-- 继承默认主设备 --</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
          </select>
        </div>

        <input v-if="customInputModes['runningVar']" type="text"
          :value="componentProps.runningVar ?? 'st_running'"
          @input="updateProp('runningVar', ($event.target as HTMLInputElement).value)"
          placeholder="如 st_running"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] font-mono text-gray-800 dark:text-white" />
        <select v-else :value="componentProps.runningVar ?? ''"
          @change="updateProp('runningVar', ($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] text-gray-800 dark:text-white">
          <option value="">-- 选择运行状态变量 (st_running) --</option>
          <option v-for="v in getDeviceVariableOptions(componentProps.runningDeviceId)" :key="v.key" :value="v.key">
            {{ v.name }} ({{ v.key }})
          </option>
        </select>
      </div>

      <!-- 故障报警信号 (Fault) -->
      <div class="p-2 rounded bg-white dark:bg-slate-900 border border-sky-100 dark:border-sky-900/30 space-y-1.5">
        <div class="flex items-center justify-between">
          <span class="font-medium text-gray-800 dark:text-slate-200 text-[11px]">故障报警信号 (Fault)</span>
          <div class="flex items-center gap-1 text-[10px]">
            <span v-if="getPointCurrentValue(componentProps.faultVar, componentProps.faultDeviceId) !== null"
              class="font-mono px-1 rounded text-[9px]"
              :class="getPointCurrentValue(componentProps.faultVar, componentProps.faultDeviceId) ? 'bg-red-100 text-red-700 dark:bg-red-900 dark:text-red-300 font-bold' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'">
              {{ getPointCurrentValue(componentProps.faultVar, componentProps.faultDeviceId) ? '报警中' : '正常' }}
            </span>
            <button type="button" @click="toggleDevOverride('faultVar')"
              class="text-sky-600 dark:text-sky-400 hover:underline cursor-pointer text-[9px]">
              {{ expandedDevPoints['faultVar'] ? '收起' : '设备' }}
            </button>
            <button type="button" @click="toggleCustomInput('faultVar')"
              class="text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 cursor-pointer text-[9px]">
              {{ customInputModes['faultVar'] ? '下拉' : '手输' }}
            </button>
          </div>
        </div>

        <div v-if="expandedDevPoints['faultVar']" class="grid grid-cols-1 gap-1 pb-1">
          <select :value="componentProps.faultDeviceId ?? ''"
            @change="updateProp('faultDeviceId', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px]">
            <option value="">-- 继承默认主设备 --</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
          </select>
        </div>

        <input v-if="customInputModes['faultVar']" type="text"
          :value="componentProps.faultVar ?? 'st_fault'"
          @input="updateProp('faultVar', ($event.target as HTMLInputElement).value)"
          placeholder="如 st_fault"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] font-mono text-gray-800 dark:text-white" />
        <select v-else :value="componentProps.faultVar ?? ''"
          @change="updateProp('faultVar', ($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] text-gray-800 dark:text-white">
          <option value="">-- 选择故障报警变量 (st_fault) --</option>
          <option v-for="v in getDeviceVariableOptions(componentProps.faultDeviceId)" :key="v.key" :value="v.key">
            {{ v.name }} ({{ v.key }})
          </option>
        </select>
      </div>

      <!-- 通信链路信号 (Signal) -->
      <div class="p-2 rounded bg-white dark:bg-slate-900 border border-sky-100 dark:border-sky-900/30 space-y-1.5">
        <div class="flex items-center justify-between">
          <span class="font-medium text-gray-800 dark:text-slate-200 text-[11px]">通讯在线信号 (Signal / Link)</span>
          <div class="flex items-center gap-1 text-[10px]">
            <span v-if="getPointCurrentValue(componentProps.signalVar, componentProps.signalDeviceId) !== null"
              class="font-mono px-1 rounded text-[9px]"
              :class="getPointCurrentValue(componentProps.signalVar, componentProps.signalDeviceId) ? 'bg-sky-100 text-sky-700 dark:bg-sky-900 dark:text-sky-300' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'">
              {{ getPointCurrentValue(componentProps.signalVar, componentProps.signalDeviceId) ? '在线' : '离线' }}
            </span>
            <button type="button" @click="toggleDevOverride('signalVar')"
              class="text-sky-600 dark:text-sky-400 hover:underline cursor-pointer text-[9px]">
              {{ expandedDevPoints['signalVar'] ? '收起' : '设备' }}
            </button>
            <button type="button" @click="toggleCustomInput('signalVar')"
              class="text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 cursor-pointer text-[9px]">
              {{ customInputModes['signalVar'] ? '下拉' : '手输' }}
            </button>
          </div>
        </div>

        <div v-if="expandedDevPoints['signalVar']" class="grid grid-cols-1 gap-1 pb-1">
          <select :value="componentProps.signalDeviceId ?? ''"
            @change="updateProp('signalDeviceId', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px]">
            <option value="">-- 继承默认主设备 --</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
          </select>
        </div>

        <input v-if="customInputModes['signalVar']" type="text"
          :value="componentProps.signalVar ?? 'comm_ok'"
          @input="updateProp('signalVar', ($event.target as HTMLInputElement).value)"
          placeholder="如 comm_ok"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] font-mono text-gray-800 dark:text-white" />
        <select v-else :value="componentProps.signalVar ?? ''"
          @change="updateProp('signalVar', ($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] text-gray-800 dark:text-white">
          <option value="">-- 选择通信在线变量 (comm_ok) --</option>
          <option v-for="v in getDeviceVariableOptions(componentProps.signalDeviceId)" :key="v.key" :value="v.key">
            {{ v.name }} ({{ v.key }})
          </option>
        </select>
      </div>
    </div>

    <!-- 模块三：频率与电流遥测点位 (PLC AI / AO / Registers 读写) -->
    <div class="border border-amber-300/80 dark:border-amber-900/60 p-3 rounded-lg bg-amber-50/30 dark:bg-amber-950/10 space-y-2.5 text-xs">
      <div class="flex items-center justify-between pb-1.5 border-b border-amber-200/50 dark:border-amber-900/40">
        <div class="flex items-center gap-1.5">
          <Zap class="w-3.5 h-3.5 text-amber-500" />
          <p class="font-bold text-amber-700 dark:text-amber-400 text-[11px] uppercase tracking-wider">
            频率与电流模拟量 (PLC AI / AO)
          </p>
        </div>
        <span class="text-[9px] font-mono text-amber-600 dark:text-amber-400 bg-amber-100 dark:bg-amber-900/40 px-1 rounded">连续模拟量</span>
      </div>

      <!-- 设定频率 (SP, 写入) -->
      <div class="p-2 rounded bg-white dark:bg-slate-900 border border-amber-200/60 dark:border-amber-900/30 space-y-1.5">
        <div class="flex items-center justify-between">
          <span class="font-medium text-gray-800 dark:text-slate-200 text-[11px]">设定频率点位 (SP, 写入 Hz)</span>
          <div class="flex items-center gap-1 text-[10px]">
            <span v-if="getPointCurrentValue(componentProps.spFreqVar, componentProps.spFreqDeviceId) !== null"
              class="font-mono font-bold text-amber-600 dark:text-amber-400 text-[10px]">
              {{ Number(getPointCurrentValue(componentProps.spFreqVar, componentProps.spFreqDeviceId)).toFixed(1) }} Hz
            </span>
            <button type="button" @click="toggleDevOverride('spFreqVar')"
              class="text-amber-600 dark:text-amber-400 hover:underline cursor-pointer text-[9px]">
              {{ expandedDevPoints['spFreqVar'] ? '收起' : '设备' }}
            </button>
            <button type="button" @click="toggleCustomInput('spFreqVar')"
              class="text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 cursor-pointer text-[9px]">
              {{ customInputModes['spFreqVar'] ? '下拉' : '手输' }}
            </button>
          </div>
        </div>

        <div v-if="expandedDevPoints['spFreqVar']" class="grid grid-cols-1 gap-1 pb-1">
          <select :value="componentProps.spFreqDeviceId ?? ''"
            @change="updateProp('spFreqDeviceId', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px]">
            <option value="">-- 继承默认主设备 --</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
          </select>
        </div>

        <input v-if="customInputModes['spFreqVar']" type="text"
          :value="componentProps.spFreqVar ?? 'sp_freq'"
          @input="updateProp('spFreqVar', ($event.target as HTMLInputElement).value)"
          placeholder="如 sp_freq"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] font-mono text-gray-800 dark:text-white" />
        <select v-else :value="componentProps.spFreqVar ?? ''"
          @change="updateProp('spFreqVar', ($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] text-gray-800 dark:text-white">
          <option value="">-- 选择设定频率变量 (sp_freq) --</option>
          <option v-for="v in getDeviceVariableOptions(componentProps.spFreqDeviceId)" :key="v.key" :value="v.key">
            {{ v.name }} ({{ v.key }}) - {{ v.value ?? '-' }} Hz
          </option>
        </select>
      </div>

      <!-- 反馈频率 (PV, 读取) -->
      <div class="p-2 rounded bg-white dark:bg-slate-900 border border-amber-200/60 dark:border-amber-900/30 space-y-1.5">
        <div class="flex items-center justify-between">
          <span class="font-medium text-gray-800 dark:text-slate-200 text-[11px]">反馈频率点位 (PV, 读取 Hz)</span>
          <div class="flex items-center gap-1 text-[10px]">
            <span v-if="getPointCurrentValue(componentProps.pvFreqVar, componentProps.pvFreqDeviceId) !== null"
              class="font-mono font-bold text-sky-600 dark:text-sky-400 text-[10px]">
              {{ Number(getPointCurrentValue(componentProps.pvFreqVar, componentProps.pvFreqDeviceId)).toFixed(1) }} Hz
            </span>
            <button type="button" @click="toggleDevOverride('pvFreqVar')"
              class="text-amber-600 dark:text-amber-400 hover:underline cursor-pointer text-[9px]">
              {{ expandedDevPoints['pvFreqVar'] ? '收起' : '设备' }}
            </button>
            <button type="button" @click="toggleCustomInput('pvFreqVar')"
              class="text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 cursor-pointer text-[9px]">
              {{ customInputModes['pvFreqVar'] ? '下拉' : '手输' }}
            </button>
          </div>
        </div>

        <div v-if="expandedDevPoints['pvFreqVar']" class="grid grid-cols-1 gap-1 pb-1">
          <select :value="componentProps.pvFreqDeviceId ?? ''"
            @change="updateProp('pvFreqDeviceId', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px]">
            <option value="">-- 继承默认主设备 --</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
          </select>
        </div>

        <input v-if="customInputModes['pvFreqVar']" type="text"
          :value="componentProps.pvFreqVar ?? 'pv_freq'"
          @input="updateProp('pvFreqVar', ($event.target as HTMLInputElement).value)"
          placeholder="如 pv_freq"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] font-mono text-gray-800 dark:text-white" />
        <select v-else :value="componentProps.pvFreqVar ?? ''"
          @change="updateProp('pvFreqVar', ($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] text-gray-800 dark:text-white">
          <option value="">-- 选择反馈频率变量 (pv_freq) --</option>
          <option v-for="v in getDeviceVariableOptions(componentProps.pvFreqDeviceId)" :key="v.key" :value="v.key">
            {{ v.name }} ({{ v.key }}) - {{ v.value ?? '-' }} Hz
          </option>
        </select>
      </div>

      <!-- 运行电流 (Current, 读取 A) -->
      <div class="p-2 rounded bg-white dark:bg-slate-900 border border-amber-200/60 dark:border-amber-900/30 space-y-1.5">
        <div class="flex items-center justify-between">
          <span class="font-medium text-gray-800 dark:text-slate-200 text-[11px]">运行电流点位 (读取 A)</span>
          <div class="flex items-center gap-1 text-[10px]">
            <span v-if="getPointCurrentValue(componentProps.currentVar, componentProps.currentDeviceId) !== null"
              class="font-mono font-bold text-amber-500 text-[10px]">
              {{ Number(getPointCurrentValue(componentProps.currentVar, componentProps.currentDeviceId)).toFixed(1) }} A
            </span>
            <button type="button" @click="toggleDevOverride('currentVar')"
              class="text-amber-600 dark:text-amber-400 hover:underline cursor-pointer text-[9px]">
              {{ expandedDevPoints['currentVar'] ? '收起' : '设备' }}
            </button>
            <button type="button" @click="toggleCustomInput('currentVar')"
              class="text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 cursor-pointer text-[9px]">
              {{ customInputModes['currentVar'] ? '下拉' : '手输' }}
            </button>
          </div>
        </div>

        <div v-if="expandedDevPoints['currentVar']" class="grid grid-cols-1 gap-1 pb-1">
          <select :value="componentProps.currentDeviceId ?? ''"
            @change="updateProp('currentDeviceId', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px]">
            <option value="">-- 继承默认主设备（或选电表） --</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
          </select>
        </div>

        <input v-if="customInputModes['currentVar']" type="text"
          :value="componentProps.currentVar ?? 'pv_current'"
          @input="updateProp('currentVar', ($event.target as HTMLInputElement).value)"
          placeholder="如 pv_current"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] font-mono text-gray-800 dark:text-white" />
        <select v-else :value="componentProps.currentVar ?? ''"
          @change="updateProp('currentVar', ($event.target as HTMLSelectElement).value)"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[11px] text-gray-800 dark:text-white">
          <option value="">-- 选择运行电流变量 (pv_current) --</option>
          <option v-for="v in getDeviceVariableOptions(componentProps.currentDeviceId)" :key="v.key" :value="v.key">
            {{ v.name }} ({{ v.key }}) - {{ v.value ?? '-' }} A
          </option>
        </select>
      </div>
    </div>

    <!-- 模块四：电机机械规格与电气参数 -->
    <div class="border border-slate-200 dark:border-slate-800 p-3 rounded-lg bg-slate-50/50 dark:bg-slate-900/30 space-y-2.5 text-xs">
      <p class="font-bold text-gray-700 dark:text-slate-300 text-[11px] uppercase tracking-wider flex items-center gap-1.5">
        <SlidersHorizontal class="w-3.5 h-3.5 text-slate-500" />
        电机机械铭牌与运行阈值
      </p>

      <div class="grid grid-cols-2 gap-2">
        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">电机名称</label>
          <input type="text" :value="componentProps.motorName ?? '1# 变频主循环泵'"
            @input="updateProp('motorName', ($event.target as HTMLInputElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-amber-500 mt-0.5 text-xs" />
        </div>
        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">设备 TAG 编号</label>
          <input type="text" :value="componentProps.motorTag ?? 'M101'"
            @input="updateProp('motorTag', ($event.target as HTMLInputElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white font-mono focus:outline-none focus:border-amber-500 mt-0.5 text-xs" />
        </div>
      </div>

      <div class="grid grid-cols-3 gap-1.5">
        <div>
          <label class="text-[9px] text-gray-500 dark:text-slate-400">额定电流 (A)</label>
          <input type="number" step="0.5" :value="componentProps.ratedCurrent ?? 25"
            @input="updateProp('ratedCurrent', numInput(($event.target as HTMLInputElement).value, 25))"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-gray-800 dark:text-white font-mono focus:outline-none focus:border-amber-500 mt-0.5 text-xs" />
        </div>
        <div>
          <label class="text-[9px] text-gray-500 dark:text-slate-400">最大频率 (Hz)</label>
          <input type="number" step="1" :value="componentProps.maxFreq ?? 50"
            @input="updateProp('maxFreq', numInput(($event.target as HTMLInputElement).value, 50))"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-gray-800 dark:text-white font-mono focus:outline-none focus:border-amber-500 mt-0.5 text-xs" />
        </div>
        <div>
          <label class="text-[9px] text-gray-500 dark:text-slate-400">步进幅度 (Hz)</label>
          <input type="number" step="0.5" :value="componentProps.freqStep ?? 1"
            @input="updateProp('freqStep', numInput(($event.target as HTMLInputElement).value, 1))"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-1.5 py-1 text-gray-800 dark:text-white font-mono focus:outline-none focus:border-amber-500 mt-0.5 text-xs" />
        </div>
      </div>

      <!-- 显隐开关 -->
      <div class="grid grid-cols-3 gap-2 pt-1 border-t border-slate-200/60 dark:border-slate-800">
        <label class="flex items-center gap-1.5 cursor-pointer select-none text-[10px] text-gray-700 dark:text-slate-300">
          <input type="checkbox" :checked="componentProps.showControls !== false"
            @change="updateProp('showControls', ($event.target as HTMLInputElement).checked)"
            class="rounded border-gray-300 text-amber-500 focus:ring-0" />
          <span>启停按钮</span>
        </label>
        <label class="flex items-center gap-1.5 cursor-pointer select-none text-[10px] text-gray-700 dark:text-slate-300">
          <input type="checkbox" :checked="componentProps.showFrequency !== false"
            @change="updateProp('showFrequency', ($event.target as HTMLInputElement).checked)"
            class="rounded border-gray-300 text-amber-500 focus:ring-0" />
          <span>频率仪表</span>
        </label>
        <label class="flex items-center gap-1.5 cursor-pointer select-none text-[10px] text-gray-700 dark:text-slate-300">
          <input type="checkbox" :checked="componentProps.showCurrent !== false"
            @change="updateProp('showCurrent', ($event.target as HTMLInputElement).checked)"
            class="rounded border-gray-300 text-amber-500 focus:ring-0" />
          <span>电流监控</span>
        </label>
      </div>
    </div>
  </div>
</template>

