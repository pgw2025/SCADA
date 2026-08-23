<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import { 
  triggers, 
  devices, 
  dataModels, 
  addLog, 
  getDeviceVariableValue 
} from '../store/index';
import { 
  Bell, 
  Trash2, 
  Plus, 
  ToggleLeft, 
  ToggleRight, 
  Link2, 
  AlertTriangle, 
  Info, 
  Check, 
  X, 
  ShieldAlert,
  Activity
} from 'lucide-vue-next';
import { VariableTrigger } from '../types';

// Modal or add inline form variables
const showAddModal = ref(false);
const newTriggerName = ref('');
const selectedDeviceId = ref('');
const selectedVariableKey = ref('');
const selectedCondition = ref<'greater' | 'less' | 'equal'>('greater');
const thresholdValue = ref(50);
const actionTypeSelected = ref<'alarm' | 'linkage'>('alarm');
const alarmLevelSelected = ref<'info' | 'normal' | 'warning'>('warning');
const linkageVarSelected = ref('');
const linkageValSelected = ref('false');

// Get variables for selected device
const availableVariablesForSelectedDevice = computed(() => {
  const dev = devices.value.find(d => d.id === selectedDeviceId.value);
  if (!dev) return [];
  const model = dataModels.value.find(m => m.id === dev.modelId);
  return model ? model.variables : [];
});

// Watch device list to set initial state in form
watch(selectedDeviceId, () => {
  const vars = availableVariablesForSelectedDevice.value;
  if (vars.length > 0) {
    selectedVariableKey.value = vars[0].key;
  } else {
    selectedVariableKey.value = '';
  }
});

// List of all keys across all devices for linkage
const allAvailableVariablesKeys = computed(() => {
  const res: string[] = [];
  dataModels.value.forEach((m) => {
    m.variables.forEach((v) => {
      if (!res.includes(v.key)) res.push(v.key);
    });
  });
  return res;
});

// Format triggers with device names
const triggerRows = computed(() => {
  return triggers.value.map(trig => {
    const dev = devices.value.find(d => d.id === trig.deviceId);
    const curVal = dev ? dev.variables[trig.variableKey] : 'offline';
    return {
      ...trig,
      deviceName: dev ? dev.name : '未知设备',
      deviceCode: dev ? dev.key : 'UNKNOWN',
      currentValue: curVal
    };
  });
});

const handleCreateTrigger = () => {
  if (!newTriggerName.value.trim() || !selectedDeviceId.value || !selectedVariableKey.value) {
    alert('请完善触发器核心属性字段！');
    return;
  }

  const newTrig: VariableTrigger = {
    id: `trig-${Date.now()}`,
    name: newTriggerName.value.trim(),
    deviceId: selectedDeviceId.value,
    variableKey: selectedVariableKey.value,
    condition: selectedCondition.value,
    threshold: Number(thresholdValue.value),
    actionType: actionTypeSelected.value,
    alarmLevel: alarmLevelSelected.value,
    linkageVariableKey: actionTypeSelected.value === 'linkage' ? linkageVarSelected.value : undefined,
    linkageValue: actionTypeSelected.value === 'linkage' 
      ? (linkageValSelected.value === 'true' ? true : linkageValSelected.value === 'false' ? false : Number(linkageValSelected.value)) 
      : undefined,
    active: true
  };

  triggers.value.push(newTrig);
  addLog('触发器管理', `注册新触发规则: [${newTrig.name}] -> 配属设备 ${newTrig.deviceId}`, 'normal');

  // Reset Form
  newTriggerName.value = '';
  showAddModal.value = false;
};

const handleDeleteTrigger = (id: string, name: string) => {
  if (confirm(`确定要移除触发器 [${name}] 吗？`)) {
    triggers.value = triggers.value.filter(t => t.id !== id);
    addLog('触发器管理', `注销了保护触发点: [${name}]`, 'warning');
  }
};

const toggleTriggerActive = (trig: VariableTrigger) => {
  trig.active = !trig.active;
  addLog('触发器管理', `触发器 [${trig.name}] 已${trig.active ? '启用' : '禁用'}`, trig.active ? 'info' : 'warning');
};
</script>

<template>
  <div class="h-full overflow-y-auto md:overflow-y-hidden flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">
    
    <!-- Top info cards layout -->
    <div class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <ShieldAlert class="w-5 h-5 text-amber-500 animate-pulse" />
          触发器管理
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          配置变量监控规则，当变量值满足条件时触发告警或执行联动控制。
        </p>
      </div>

      <button 
        @click="showAddModal = true; selectedDeviceId = devices[0]?.id || ''"
        class="font-bold text-xs bg-slate-900 dark:bg-sky-600 text-white hover:bg-slate-800 dark:hover:bg-sky-500 px-4 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center active:scale-95 transition-all shadow-sm"
      >
        <Plus class="w-4 h-4" />
        新建触发器
      </button>
    </div>

    <!-- Active indicators panel -->
    <div class="p-6 grid grid-cols-1 sm:grid-cols-3 gap-4 shrink-0">
      <div class="bg-white dark:bg-slate-900 border border-slate-200/80 dark:border-slate-800 rounded-xl p-4 flex items-center gap-3.5 text-left transition-colors">
        <div class="w-10 h-10 rounded-lg bg-rose-50 dark:bg-rose-950/60 flex items-center justify-center text-rose-600 dark:text-rose-400 shrink-0">
          <Bell class="w-5 h-5" />
        </div>
        <div>
          <span class="text-[10px] text-slate-400 dark:text-slate-500 font-bold uppercase tracking-wider">触发器总数</span>
          <h3 class="text-xl font-bold font-mono text-slate-900 dark:text-white leading-none mt-1">
            {{ triggers.length }} <span class="text-xs font-sans text-slate-400 dark:text-slate-500">个</span>
          </h3>
        </div>
      </div>

      <div class="bg-white dark:bg-slate-900 border border-slate-200/80 dark:border-slate-800 rounded-xl p-4 flex items-center gap-3.5 text-left transition-colors">
        <div class="w-10 h-10 rounded-lg bg-emerald-50 dark:bg-emerald-950/60 flex items-center justify-center text-emerald-600 dark:text-emerald-400 shrink-0">
          <ToggleRight class="w-5 h-5" />
        </div>
        <div>
          <span class="text-[10px] text-slate-400 dark:text-slate-500 font-bold uppercase tracking-wider">已启用</span>
          <h3 class="text-xl font-bold font-mono text-emerald-600 dark:text-emerald-400 leading-none mt-1">
            {{ triggers.filter(t => t.active).length }} <span class="text-xs font-sans text-slate-400 dark:text-slate-500">个</span>
          </h3>
        </div>
      </div>

      <div class="bg-white dark:bg-slate-900 border border-slate-200/80 dark:border-slate-800 rounded-xl p-4 flex items-center gap-3.5 text-left transition-colors">
        <div class="w-10 h-10 rounded-lg bg-indigo-50 dark:bg-indigo-950/60 flex items-center justify-center text-indigo-600 dark:text-indigo-400 shrink-0">
          <Link2 class="w-5 h-5" />
        </div>
        <div>
          <span class="text-[10px] text-slate-400 dark:text-slate-500 font-bold uppercase tracking-wider">联动规则</span>
          <h3 class="text-xl font-bold font-mono text-indigo-600 dark:text-indigo-400 leading-none mt-1">
            {{ triggers.filter(t => t.actionType === 'linkage').length }} <span class="text-xs font-sans text-slate-400 dark:text-slate-500">条</span>
          </h3>
        </div>
      </div>
    </div>

    <!-- Main dataset table -->
    <div class="flex-1 px-4 md:px-6 pb-6 md:overflow-hidden overflow-visible flex flex-col">
      <div class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-xs overflow-hidden flex flex-col transition-colors">
        <div class="overflow-x-auto flex-1">
          <table class="w-full text-left text-xs font-sans border-collapse">
            <thead class="bg-slate-50 dark:bg-slate-950 text-slate-500 dark:text-slate-400 font-bold uppercase tracking-wider text-[10px] border-b border-slate-100 dark:border-slate-800 select-none">
              <tr>
                <th class="p-4 pl-5">规则名称</th>
                <th class="p-4">关联设备</th>
                <th class="p-4">触发条件</th>
                <th class="p-4">当前值</th>
                <th class="p-4">响应动作</th>
                <th class="p-4 font-center text-center">状态</th>
                <th class="p-4 pr-5 text-right w-24">操作</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100 dark:divide-slate-800 text-slate-700 dark:text-slate-300">
              <tr v-for="trig in triggerRows" :key="trig.id" class="hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-colors">
                <!-- Name with condition icon -->
                <td class="p-4 pl-5 font-bold text-slate-900 dark:text-white">
                  <div class="flex items-center gap-2">
                    <span class="w-1.5 h-1.5 rounded-full" :class="trig.active ? 'bg-emerald-500 shadow-[0_0_5px_#10b981]' : 'bg-slate-300 dark:bg-slate-600'" />
                    <div>
                      <span>{{ trig.name }}</span>
                      <p class="text-[9px] font-mono font-medium text-slate-400 dark:text-slate-500 mt-0.5">ID: {{ trig.id }}</p>
                    </div>
                  </div>
                </td>

                <!-- Device Binding -->
                <td class="p-4">
                  <span class="font-bold text-[#1890ff] dark:text-sky-400 bg-sky-50 dark:bg-sky-950/60 text-[10px] px-2 py-1 rounded-md border border-sky-100/60 dark:border-sky-800/60 font-mono">
                    {{ trig.deviceCode }}
                  </span>
                  <span class="ml-2 text-slate-500 dark:text-slate-400 text-[11px] font-sans truncate block md:inline-block max-w-[140px] align-middle">
                    {{ trig.deviceName }}
                  </span>
                </td>

                <!-- Logical expression -->
                <td class="p-4 font-bold font-mono">
                  <span class="text-slate-500 dark:text-slate-400">{{ trig.variableKey }}</span>
                  <span class="mx-1 text-slate-800 dark:text-slate-200">
                    {{ trig.condition === 'greater' ? '>' : trig.condition === 'less' ? '<' : '==' }}
                  </span>
                  <span class="text-indigo-600 dark:text-indigo-400 bg-indigo-50 dark:bg-indigo-950/60 px-1.5 py-0.5 rounded text-[11px]">
                    {{ trig.threshold }}
                  </span>
                </td>

                <!-- Current value simulation tracer -->
                <td class="p-4 font-mono font-bold">
                  <div class="flex items-center gap-1.5">
                    <Activity class="w-3.5 h-3.5 text-slate-400 dark:text-slate-500" />
                    <span :class="trig.currentValue === 'offline' ? 'text-slate-400 dark:text-slate-500' : 'text-slate-800 dark:text-slate-200'">
                      {{ trig.currentValue }}
                    </span>
                  </div>
                </td>

                <!-- Trigger actions badge -->
                <td class="p-4">
                  <div v-if="trig.actionType === 'alarm'" class="inline-flex items-center gap-1.5 font-bold text-[10px]">
                    <span 
                      class="px-2 py-0.5 rounded font-sans tracking-wide uppercase text-white"
                      :class="trig.alarmLevel === 'warning' ? 'bg-rose-500' : trig.alarmLevel === 'normal' ? 'bg-amber-500' : 'bg-sky-500'"
                    >
                      {{ trig.alarmLevel === 'warning' ? '严重告警' : trig.alarmLevel === 'normal' ? '一般告警' : '提示信息' }}
                    </span>
                  </div>
                  
                  <div v-else class="inline-flex items-center gap-1 font-bold text-[10px] bg-indigo-50 dark:bg-indigo-950/60 text-indigo-700 dark:text-indigo-300 border border-indigo-100 dark:border-indigo-800 px-2 py-1 rounded-lg">
                    <Link2 class="w-3 h-3 text-indigo-500 dark:text-indigo-400" />
                    <span>联动: {{ trig.linkageVariableKey }} = {{ trig.linkageValue }}</span>
                  </div>
                </td>

                <!-- Action state toggles -->
                <td class="p-4 text-center">
                  <button 
                    @click="toggleTriggerActive(trig)"
                    class="focus:outline-none cursor-pointer transition-transform active:scale-90"
                  >
                    <ToggleRight v-if="trig.active" class="w-8 h-8 text-emerald-500" />
                    <ToggleLeft v-else class="w-8 h-8 text-slate-300 dark:text-slate-600" />
                  </button>
                </td>

                <!-- Trash trigger actions -->
                <td class="p-4 pr-5 text-right">
                  <button 
                    @click="handleDeleteTrigger(trig.id, trig.name)"
                    class="p-1 px-2 rounded-lg hover:bg-rose-50 dark:hover:bg-rose-950/40 text-rose-500 hover:text-rose-600 font-bold transition-colors cursor-pointer"
                    title="移除警报拦截"
                  >
                    <Trash2 class="w-4 h-4" />
                  </button>
                </td>
              </tr>

              <tr v-if="triggers.length === 0">
                <td colspan="7" class="text-center py-16 text-slate-400 dark:text-slate-500">
                  <AlertTriangle class="w-8 h-8 mx-auto text-slate-300 dark:text-slate-600 mb-2 animate-bounce" />
                  <span>暂无触发器。点击右上角创建新规则。</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- DIALOG POPUP: CREATE SAFETY ALARM RULE -->
    <div v-if="showAddModal" class="fixed inset-0 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <!-- Title bar -->
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-2 font-bold text-xs uppercase tracking-widest text-[#1890ff]">
            <ShieldAlert class="w-4 h-4" />
            <span>新建触发器</span>
          </div>
          <button @click="showAddModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4.5 h-4.5" /></button>
        </div>

        <!-- Form content -->
        <div class="p-5 space-y-4 text-xs font-sans">
          <!-- Trigger Name -->
          <div>
            <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">规则名称</label>
            <input 
              v-model="newTriggerName"
              type="text"
              placeholder="如: 温度超限告警"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-semibold focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white outline-none focus:border-[#1890ff]"
            />
          </div>

          <!-- Target Device -->
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">监控设备</label>
              <select 
                v-model="selectedDeviceId"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-bold focus:outline-none"
              >
                <option v-for="d in devices" :key="d.id" :value="d.id">
                  {{ d.name }} ({{ d.code }})
                </option>
              </select>
            </div>

            <!-- Target Variable Key -->
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">监控变量</label>
              <select 
                v-model="selectedVariableKey"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-mono font-bold focus:outline-none"
              >
                <option v-for="v in availableVariablesForSelectedDevice" :key="v.key" :value="v.key">
                  {{ v.name }} ({{ v.key }})
                </option>
                <option v-if="availableVariablesForSelectedDevice.length === 0" disabled>设备离线或无模型</option>
              </select>
            </div>
          </div>

          <!-- Condition & threshold -->
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">比较条件</label>
              <select 
                v-model="selectedCondition"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white focus:outline-none"
              >
                <option value="greater">大于 (>)</option>
                <option value="less">小于 (&lt;)</option>
                <option value="equal">等于 (==)</option>
              </select>
            </div>
            
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">阈值</label>
              <input 
                v-model.number="thresholdValue"
                type="number"
                step="0.1"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-bold outline-none"
              />
            </div>
          </div>

          <!-- Action block type -->
          <div class="border-t border-slate-100 dark:border-slate-800 pt-3">
            <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1.5">响应动作</label>
            <div class="flex gap-4">
              <label class="flex items-center gap-1.5 cursor-pointer font-bold text-slate-700 dark:text-slate-300">
                <input 
                  type="radio" 
                  value="alarm" 
                  v-model="actionTypeSelected"
                  class="accent-slate-900 dark:accent-sky-500" 
                />
                触发告警
              </label>
              <label class="flex items-center gap-1.5 cursor-pointer font-bold text-slate-700 dark:text-slate-300">
                <input 
                  type="radio" 
                  value="linkage" 
                  v-model="actionTypeSelected"
                  class="accent-slate-900 dark:accent-sky-500" 
                />
                变量联动
              </label>
            </div>
          </div>

          <!-- Conditional details Form views -->
          <div v-if="actionTypeSelected === 'alarm'" class="bg-amber-50/50 dark:bg-amber-950/40 rounded-lg p-3 border border-amber-100 dark:border-amber-900/40 flex items-center justify-between">
            <span class="font-bold text-amber-800 dark:text-amber-300">告警级别</span>
            <select 
              v-model="alarmLevelSelected"
              class="bg-white dark:bg-slate-900 border border-amber-200 dark:border-amber-800 text-amber-900 dark:text-amber-200 font-bold rounded p-1 outline-none font-sans"
            >
              <option value="info">提示信息</option>
              <option value="normal">一般告警</option>
              <option value="warning">严重告警</option>
            </select>
          </div>

          <div v-else class="bg-indigo-50/50 dark:bg-indigo-950/40 rounded-lg p-3 border border-indigo-100 dark:border-indigo-900/40 space-y-3">
            <h4 class="font-bold text-indigo-800 dark:text-indigo-300">联动配置</h4>
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-[10px] text-indigo-600 dark:text-indigo-400 font-bold block mb-0.5">目标变量</label>
                <select 
                  v-model="linkageVarSelected"
                  class="w-full bg-white dark:bg-slate-900 border border-indigo-100 dark:border-indigo-800 text-slate-800 dark:text-white font-mono text-[11px] p-1.5 rounded focus:outline-none"
                >
                  <option v-for="key in allAvailableVariablesKeys" :key="key" :value="key">
                    {{ key }}
                  </option>
                </select>
              </div>
              
              <div>
                <label class="text-[10px] text-indigo-600 dark:text-indigo-400 font-bold block mb-0.5">设置值</label>
                <select 
                  v-model="linkageValSelected"
                  class="w-full bg-white dark:bg-slate-900 border border-indigo-100 dark:border-indigo-800 text-slate-800 dark:text-white text-[11px] p-1.5 rounded focus:outline-none"
                >
                  <option value="true">开启 (true)</option>
                  <option value="false">关闭 (false)</option>
                  <option value="50">中等 (50)</option>
                  <option value="0">零值 (0)</option>
                  <option value="120">高值 (120)</option>
                </select>
              </div>
            </div>
          </div>

        </div>

        <!-- Footer -->
        <div class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800">
          <button 
            @click="showAddModal = false; newTriggerName = ''"
            class="px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleCreateTrigger"
            class="px-4 py-1.5 rounded-lg bg-slate-900 dark:bg-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white cursor-pointer"
          >
            创建触发器
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
