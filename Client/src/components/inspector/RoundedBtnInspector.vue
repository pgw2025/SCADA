<script setup lang="ts">
// rounded-btn 圆角按钮检查器：预设风格/控制模式/脚本绑定/操作变量绑定/多状态配置
// 从 InspectorPanel.vue 抽出（Phase 2c）；通信：component 下行 / updateProp、applyProps 上行
import { computed, ref, watch } from 'vue';
import { HMIComponent } from '../../types';
import { devices } from '../../store/deviceStore';
import { systemScripts } from '../../store/configStore';
import { loadSystemScripts } from '../../services/scriptService';
import { loginUser } from '../../store/userStore';
import { ROLE_ADMIN } from '../../constants/roles';

const props = defineProps<{
  component: HMIComponent;
  /** 导航目标候选（父组件按 currentPlatform 过滤后传入，与 nav-menu 共用同一计算） */
  navTargetOptions: { id: string; name: string }[];
}>();

const emit = defineEmits<{
  (e: 'updateProp', key: string, value: any): void;
  (e: 'applyProps', patch: Record<string, any>): void;
}>();

const componentProps = computed(() => props.component.props ?? {});

const updateProp = (key: string, value: any) => emit('updateProp', key, value);
const applyProps = (patch: Record<string, any>) => emit('applyProps', patch);

// 解析数值输入：合法（含 0）原样写入，非法（NaN/空）回退缺省值
const numInput = (raw: string, fallback: number): number => {
  const n = parseFloat(raw);
  return Number.isFinite(n) ? n : fallback;
};

// ===== 圆角按钮：5 种工业预设风格（启动/停止/复位/点动/急停）=====
// 预设只覆盖状态文案/配色/控制模式/边框，不改动绑定与尺寸；应用后仍可继续微调
const roundedBtnPresets: Record<string, Record<string, any>> = {
  start: {
    presetStyle: 'start', buttonMode: 'set-bit',
    state0Text: '待机', state0BgColor: '#334155', state0TextColor: '#94a3b8',
    state1Text: '运行中', state1BgColor: '#16a34a', state1TextColor: '#ffffff',
    strokeColor: '#16a34a', customStates: '',
  },
  stop: {
    presetStyle: 'stop', buttonMode: 'reset-bit',
    state0Text: '停止', state0BgColor: '#7f1d1d', state0TextColor: '#fca5a5',
    state1Text: '停止', state1BgColor: '#dc2626', state1TextColor: '#ffffff',
    strokeColor: '#dc2626', customStates: '',
  },
  reset: {
    presetStyle: 'reset', buttonMode: 'momentary',
    state0Text: '就绪', state0BgColor: '#1e3a8a', state0TextColor: '#93c5fd',
    state1Text: '复位中', state1BgColor: '#2563eb', state1TextColor: '#ffffff',
    strokeColor: '#2563eb', customStates: '',
  },
  jog: {
    presetStyle: 'jog', buttonMode: 'momentary',
    state0Text: '点动', state0BgColor: '#7c2d12', state0TextColor: '#fdba74',
    state1Text: '点动中', state1BgColor: '#ea580c', state1TextColor: '#ffffff',
    strokeColor: '#ea580c', customStates: '',
  },
  estop: {
    presetStyle: 'estop', buttonMode: 'set-bit', borderWidth: 3,
    state0Text: '急停', state0BgColor: '#991b1b', state0TextColor: '#fecaca',
    state1Text: '急停触发', state1BgColor: '#dc2626', state1TextColor: '#ffffff',
    strokeColor: '#f87171', customStates: '',
  },
};
const applyRoundedBtnPreset = (key: string) => {
  const preset = roundedBtnPresets[key];
  if (preset) applyProps(preset);
};

// run-script 模式：管理员编辑器内懒加载脚本列表供下拉选择（列表接口 RequireAdmin，
// 非管理员不自动拉取，避免无谓 403 噪音；运行态触发走 /api/ScriptRuntime 不依赖此列表）
const scriptListRequested = ref(false);
watch(
  () => props.component?.type === 'rounded-btn' && props.component?.props.buttonMode === 'run-script',
  (need) => {
    if (need && !scriptListRequested.value && loginUser.value?.role === ROLE_ADMIN) {
      scriptListRequested.value = true;
      loadSystemScripts().catch(() => { scriptListRequested.value = false; });
    }
  },
  { immediate: true }
);

// ===== 圆角按钮：操作变量绑定（写入目标，可与显示/背景变量分离）=====
// 未配置时回落主绑定（bindDeviceId/bindVariableKey）
const opBindingVariableOptions = computed(() => {
  const dev = devices.value.find((d) => String(d.id) === String(componentProps.value.opDeviceId));
  if (dev && dev.variables) {
    return Object.keys(dev.variables).map((k) => ({ key: k }));
  }
  return [];
});

const onOpDeviceChange = (val: string) => {
  applyProps({ opDeviceId: val === '' ? null : Number(val), opVariableKey: '' });
};

const onOpVariableChange = (val: string) => {
  updateProp('opVariableKey', val);
};
</script>

<template>
  <!-- INDUSTRIAL ROUNDED BUTTON SPECIFIC CONTROLS (圆角按钮专属配置) -->
  <div
    class="space-y-3 text-xs border border-emerald-200/80 dark:border-emerald-900/60 p-3 rounded-lg bg-emerald-50/40 dark:bg-emerald-950/20">
    <div class="flex items-center justify-between">
      <p class="font-bold text-emerald-600 dark:text-emerald-400 text-[11px] uppercase tracking-wider">圆角按钮与多状态配置
      </p>
      <span
        class="text-[9px] font-mono bg-emerald-100 dark:bg-emerald-900/60 text-emerald-700 dark:text-emerald-300 px-1.5 py-0.5 rounded">Rounded
        Button</span>
    </div>

    <!-- 预设风格一键应用（启动/停止/复位/点动/急停） -->
    <div>
      <label class="text-[10px] font-semibold text-gray-700 dark:text-slate-300">预设按钮风格 (Preset Styles)</label>
      <div class="grid grid-cols-5 gap-1 mt-1">
        <button type="button" @click="applyRoundedBtnPreset('start')"
          class="rounded px-1 py-1.5 text-[10px] font-bold text-white bg-[#16a34a] hover:brightness-110 active:scale-95 transition-all cursor-pointer">
          启动
        </button>
        <button type="button" @click="applyRoundedBtnPreset('stop')"
          class="rounded px-1 py-1.5 text-[10px] font-bold text-white bg-[#dc2626] hover:brightness-110 active:scale-95 transition-all cursor-pointer">
          停止
        </button>
        <button type="button" @click="applyRoundedBtnPreset('reset')"
          class="rounded px-1 py-1.5 text-[10px] font-bold text-white bg-[#2563eb] hover:brightness-110 active:scale-95 transition-all cursor-pointer">
          复位
        </button>
        <button type="button" @click="applyRoundedBtnPreset('jog')"
          class="rounded px-1 py-1.5 text-[10px] font-bold text-white bg-[#ea580c] hover:brightness-110 active:scale-95 transition-all cursor-pointer">
          点动
        </button>
        <button type="button" @click="applyRoundedBtnPreset('estop')"
          class="rounded px-1 py-1.5 text-[10px] font-bold text-white bg-[#991b1b] border border-red-400 hover:brightness-110 active:scale-95 transition-all cursor-pointer">
          急停
        </button>
      </div>
      <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-1 leading-snug">
        一键套用工业标准配色与控制模式（启动=置位/停止=复位清零/复位=脉冲/点动=按1送0/急停=置位+粗边框），应用后可继续微调。
      </p>
    </div>

    <!-- 控制模式选择 -->
    <div>
      <label class="text-[10px] font-semibold text-gray-700 dark:text-slate-300">控制动作模式 (Action Mode)</label>
      <select :value="componentProps.buttonMode || 'toggle'"
        @change="updateProp('buttonMode', ($event.target as HTMLSelectElement).value)"
        class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 focus:outline-none focus:border-emerald-500 mt-1 text-xs text-[#262626] dark:text-white font-medium">
        <option value="toggle">取反 (Toggle - 0变1，1变0)</option>
        <option value="set-bit">置位 (SetBit - 强制写入1 / True)</option>
        <option value="reset-bit">复位 (ResetBit - 强制写入0 / False)</option>
        <option value="momentary">按1送0 (Momentary - 按下写1松开写0)</option>
        <option value="set-value">恒定设值 (SetValue - 写入指定数值)</option>
        <option value="navigate">画面跳转 (Navigate - 跳转同端画面)</option>
        <option value="run-script">执行脚本 (RunScript - 触发服务端系统脚本)</option>
      </select>
    </div>

    <!-- 模式角标显隐开关 -->
    <div class="flex items-center gap-2">
      <input type="checkbox" id="showModeBadgeDef" :checked="componentProps.showModeBadge !== false"
        @change="updateProp('showModeBadge', ($event.target as HTMLInputElement).checked)"
        class="rounded border-[#d9d9d9] dark:border-slate-700 text-emerald-600 focus:ring-0" />
      <label for="showModeBadgeDef" class="text-xs text-gray-700 dark:text-slate-300 select-none cursor-pointer">
        显示模式角标文字（[取反]/[置位1]/[脚本] 等）
      </label>
    </div>

    <div v-if="componentProps.buttonMode === 'set-value'">
      <label class="text-[10px] text-gray-500 dark:text-slate-400">设值写入数值</label>
      <input type="number" :value="componentProps.clickValue ?? 1"
        @input="updateProp('clickValue', numInput(($event.target as HTMLInputElement).value, 1))"
        class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-emerald-500 mt-0.5 text-xs" />
    </div>

    <div v-if="componentProps.buttonMode === 'navigate'">
      <label class="text-[10px] text-gray-500 dark:text-slate-400">跳转目标画面（仅同端）</label>
      <select :value="componentProps.targetPageId ?? ''"
        @change="updateProp('targetPageId', ($event.target as HTMLSelectElement).value)"
        class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-emerald-500 mt-0.5 text-xs text-[#262626] dark:text-white">
        <option value="">-- 请选择目标画面 --</option>
        <option v-for="opt in navTargetOptions" :key="opt.id" :value="opt.id">{{ opt.name }}</option>
      </select>
    </div>

    <div v-if="componentProps.buttonMode === 'run-script'" class="space-y-1.5">
      <label class="text-[10px] text-gray-500 dark:text-slate-400">触发执行的系统脚本</label>
      <!-- 脚本列表（管理员自动加载；无权限/为空时回退手填 ID） -->
      <select v-if="systemScripts.length > 0" :value="componentProps.targetScriptId ?? ''"
        @change="updateProp('targetScriptId', ($event.target as HTMLSelectElement).value === '' ? null : Number(($event.target as HTMLSelectElement).value))"
        class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-emerald-500 mt-0.5 text-xs text-[#262626] dark:text-white">
        <option value="">-- 请选择脚本 --</option>
        <option v-for="s in systemScripts" :key="s.id" :value="s.id">#{{ s.id }} {{ s.name }}{{ s.active ? '' :
          '（已停用）' }}</option>
      </select>
      <template v-else>
        <input type="number" :value="componentProps.targetScriptId ?? ''"
          @input="updateProp('targetScriptId', numInput(($event.target as HTMLInputElement).value, 0) || null)"
          class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 text-xs text-[#262626] dark:text-white focus:outline-none focus:border-emerald-500"
          placeholder="输入系统脚本 ID" />
        <p class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug">
          脚本列表仅管理员可加载；可直接填写脚本 ID。运行态点击按钮将触发服务端沙箱执行。
        </p>
      </template>
    </div>

    <!-- 操作变量绑定（写入目标）：可与上方「数据绑定」（背景/显示变量）分离 -->
    <div v-if="!['navigate', 'run-script'].includes(componentProps.buttonMode)"
      class="space-y-2 pt-2 border-t border-emerald-100 dark:border-emerald-900/40">
      <p class="font-bold text-gray-700 dark:text-slate-300 text-[10px]">操作变量绑定（写入目标）</p>
      <p class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug">
        不配置时与「数据绑定」一致；配置后点击写入此变量，按钮背景状态仍由数据绑定变量驱动。
      </p>
      <div>
        <label class="text-[10px] text-gray-500 dark:text-slate-400">操作设备</label>
        <select :value="componentProps.opDeviceId ?? ''"
          @change="onOpDeviceChange(($event.target as HTMLSelectElement).value)"
          class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 mt-0.5 text-xs text-[#262626] dark:text-white focus:outline-none focus:border-emerald-500">
          <option value="">-- 跟随数据绑定设备 --</option>
          <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }} ({{ d.key }})</option>
        </select>
      </div>
      <div>
        <label class="text-[10px] text-gray-500 dark:text-slate-400">操作变量</label>
        <select :value="componentProps.opVariableKey ?? ''"
          @change="onOpVariableChange(($event.target as HTMLSelectElement).value)"
          class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 mt-0.5 text-xs text-[#262626] dark:text-white focus:outline-none focus:border-emerald-500">
          <option value="">-- 跟随数据绑定变量 --</option>
          <option v-for="v in opBindingVariableOptions" :key="v.key" :value="v.key">{{ v.key }}</option>
        </select>
      </div>
    </div>

    <!-- 圆角与边框精细调节 -->
    <div class="grid grid-cols-2 gap-2 text-xs pt-1 border-t border-emerald-100 dark:border-emerald-900/40">
      <div>
        <label class="text-[10px] text-gray-500 dark:text-slate-400">圆角弧度 (Radius: {{ componentProps.borderRadius
          ?? 10 }}px)</label>
        <input type="range" min="0" max="40" step="1" :value="componentProps.borderRadius ?? 10"
          @input="updateProp('borderRadius', parseInt(($event.target as HTMLInputElement).value) || 0)"
          class="w-full mt-1 accent-emerald-600 dark:accent-emerald-400" />
      </div>
      <div>
        <label class="text-[10px] text-gray-500 dark:text-slate-400">边框粗细 (Border: {{ componentProps.borderWidth
          ?? 1 }}px)</label>
        <input type="range" min="0" max="6" step="1" :value="componentProps.borderWidth ?? 1"
          @input="updateProp('borderWidth', parseInt(($event.target as HTMLInputElement).value) || 0)"
          class="w-full mt-1 accent-emerald-600 dark:accent-emerald-400" />
      </div>
    </div>

    <div>
      <label class="text-[10px] text-gray-500 dark:text-slate-400">边框轮廓颜色</label>
      <div class="flex items-center gap-1.5 mt-1">
        <input type="color" :value="componentProps.strokeColor || '#38bdf8'"
          @input="updateProp('strokeColor', ($event.target as HTMLInputElement).value)"
          class="w-6 h-6 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
        <input type="text" :value="componentProps.strokeColor || '#38bdf8'"
          @input="updateProp('strokeColor', ($event.target as HTMLInputElement).value)"
          class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
      </div>
    </div>

    <!-- 双态/多态配置：状态0 (OFF/停止) 与 状态1 (ON/运行) -->
    <div class="space-y-2 pt-2 border-t border-emerald-100 dark:border-emerald-900/40">
      <p class="font-bold text-gray-700 dark:text-slate-300 text-[10px]">基础状态样式定义 (状态 0 / 1)</p>

      <!-- 状态0 (值=0/false) -->
      <div
        class="bg-white dark:bg-slate-900 p-2 rounded border border-slate-200 dark:border-slate-800 space-y-1.5">
        <div class="flex items-center justify-between">
          <span class="text-[10px] font-bold text-slate-500 dark:text-slate-400">● 状态 0 (关/停止/0)</span>
        </div>
        <div class="grid grid-cols-3 gap-1.5">
          <div class="col-span-1">
            <label class="text-[9px] text-slate-400">显示文本</label>
            <input type="text" :value="componentProps.state0Text ?? 'OFF 停止'"
              @input="updateProp('state0Text', ($event.target as HTMLInputElement).value)"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[11px] text-slate-800 dark:text-white" />
          </div>
          <div>
            <label class="text-[9px] text-slate-400">背景色</label>
            <div class="flex items-center gap-1 mt-0.5">
              <input type="color" :value="componentProps.state0BgColor || '#1e293b'"
                @input="updateProp('state0BgColor', ($event.target as HTMLInputElement).value)"
                class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
              <input type="text" :value="componentProps.state0BgColor || '#1e293b'"
                @input="updateProp('state0BgColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
            </div>
          </div>
          <div>
            <label class="text-[9px] text-slate-400">文字颜色</label>
            <div class="flex items-center gap-1 mt-0.5">
              <input type="color" :value="componentProps.state0TextColor || '#94a3b8'"
                @input="updateProp('state0TextColor', ($event.target as HTMLInputElement).value)"
                class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
              <input type="text" :value="componentProps.state0TextColor || '#94a3b8'"
                @input="updateProp('state0TextColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
            </div>
          </div>
        </div>
      </div>

      <!-- 状态1 (值=1/true) -->
      <div
        class="bg-white dark:bg-slate-900 p-2 rounded border border-slate-200 dark:border-slate-800 space-y-1.5">
        <div class="flex items-center justify-between">
          <span class="text-[10px] font-bold text-emerald-600 dark:text-emerald-400">● 状态 1 (开/运行/1)</span>
        </div>
        <div class="grid grid-cols-3 gap-1.5">
          <div class="col-span-1">
            <label class="text-[9px] text-slate-400">显示文本</label>
            <input type="text" :value="componentProps.state1Text ?? 'ON 运行'"
              @input="updateProp('state1Text', ($event.target as HTMLInputElement).value)"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[11px] text-slate-800 dark:text-white" />
          </div>
          <div>
            <label class="text-[9px] text-slate-400">背景色</label>
            <div class="flex items-center gap-1 mt-0.5">
              <input type="color" :value="componentProps.state1BgColor || '#0284c7'"
                @input="updateProp('state1BgColor', ($event.target as HTMLInputElement).value)"
                class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
              <input type="text" :value="componentProps.state1BgColor || '#0284c7'"
                @input="updateProp('state1BgColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
            </div>
          </div>
          <div>
            <label class="text-[9px] text-slate-400">文字颜色</label>
            <div class="flex items-center gap-1 mt-0.5">
              <input type="color" :value="componentProps.state1TextColor || '#ffffff'"
                @input="updateProp('state1TextColor', ($event.target as HTMLInputElement).value)"
                class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
              <input type="text" :value="componentProps.state1TextColor || '#ffffff'"
                @input="updateProp('state1TextColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
            </div>
          </div>
        </div>
      </div>

      <!-- 高级多状态自定义 (Custom States) -->
      <div class="space-y-1 pt-1">
        <label class="text-[10px] text-gray-500 dark:text-slate-400 flex justify-between">
          <span>高级自定义多状态规则 (值:文本:背景色:字色)</span>
        </label>
        <textarea rows="2" :value="componentProps.customStates ?? ''"
          @input="updateProp('customStates', ($event.target as HTMLTextAreaElement).value)"
          class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 mt-0.5 focus:outline-none focus:border-emerald-500 text-[10px] font-mono text-gray-700 dark:text-slate-300 leading-relaxed"
          placeholder="0:停止:#334155:#94a3b8;1:运行:#0284c7:#ffffff;2:报警:#dc2626:#ffffff" />
        <p class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug">
          支持任意数值状态映射，例如 <code
            class="bg-slate-100 dark:bg-slate-800 px-1 rounded">0:停止:#1e293b:#94a3b8;1:运行:#10b981:#ffffff;2:过载:#f59e0b:#ffffff;3:紧急故障:#ef4444:#ffffff</code>
        </p>
      </div>
    </div>
  </div>
</template>
