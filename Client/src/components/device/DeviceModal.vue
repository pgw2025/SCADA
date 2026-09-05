<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import { Device, Area, DataModel, ControllerOption, DeviceConnectionSummary, DeviceType, DEVICE_TYPES } from '../../types';
import { Cpu, X, Server, Link2, Settings2, Plus, Trash2, CheckCircle2 } from 'lucide-vue-next';

const props = defineProps<{
  show: boolean;
  isEditing: boolean;
  areas: Area[];
  dataModels: DataModel[];
  controllers: ControllerOption[];
  connections: DeviceConnectionSummary[];
  initialData: any;
  errors: Record<string, string>;
  errorMessage: string;
}>();

const emit = defineEmits<{
  (e: 'close'): void;
  (e: 'save', data: any): void;
}>();

const activeTab = ref<'basic' | 'model' | 'connection'>('basic');

// Form state
const name = ref('');
const key = ref('');
const areaId = ref<number | null>(null);
const isEnabled = ref(true);

// Models
const primaryModelId = ref<number | null>(null);
const secondaryModelIds = ref<number[]>([]);

// Connection mode
const connectionMode = ref<'quick' | 'advanced'>('quick');
const protocolKey = ref<string>('OPCUA');
const ipAddress = ref('127.0.0.1');
const port = ref<number | string>(4840);
const endpointUrl = ref('opc.tcp://127.0.0.1:4840');
const cpuType = ref('S7-1500');
const rack = ref(0);
const slot = ref(1);

// Advanced connection
const selectedControllerId = ref<number | null>(null);
const selectedConnectionId = ref<number | null>(null);

watch(
  () => props.show,
  (val) => {
    if (val) {
      activeTab.value = 'basic';
      const d = props.initialData || {};
      name.value = d.name || '';
      key.value = d.key || '';
      areaId.value = d.areaId ?? (props.areas[0]?.id || null);
      isEnabled.value = d.isEnabled ?? true;

      // Model
      primaryModelId.value = d.modelId ? Number(d.modelId) : (props.dataModels[0] ? Number(props.dataModels[0].id) : null);
      if (d.models && Array.isArray(d.models)) {
        secondaryModelIds.value = d.models
          .filter((m: any) => !m.isPrimary)
          .map((m: any) => Number(m.modelId));
      } else {
        secondaryModelIds.value = [];
      }

      // Connection Mode
      if (d.connectionId) {
        connectionMode.value = 'advanced';
        selectedConnectionId.value = d.connectionId;
        selectedControllerId.value = d.controllerId || null;
      } else {
        connectionMode.value = 'quick';
        selectedConnectionId.value = null;
        selectedControllerId.value = null;
      }

      protocolKey.value = d.protocolKey || (d.type ? d.type.toUpperCase() : 'OPCUA');
      ipAddress.value = d.ipAddress || '127.0.0.1';
      port.value = d.port || (protocolKey.value === 'S7' ? 102 : 4840);
      endpointUrl.value = d.endpointUrl || 'opc.tcp://127.0.0.1:4840';
      cpuType.value = d.cpuType || 'S7-1500';
      rack.value = d.rack ?? 0;
      slot.value = d.slot ?? 1;
    }
  },
  { immediate: true }
);

// Filter connections by controller
const filteredConnections = computed(() => {
  if (!selectedControllerId.value) return props.connections;
  return props.connections.filter(c => c.controllerId === selectedControllerId.value);
});

// Update default ports when protocol changes in quick mode
const handleProtocolChange = () => {
  if (protocolKey.value === 'S7') {
    port.value = 102;
  } else if (protocolKey.value === 'OPCUA') {
    port.value = 4840;
    endpointUrl.value = `opc.tcp://${ipAddress.value || '127.0.0.1'}:4840`;
  } else if (protocolKey.value === 'MODBUSTCP') {
    port.value = 502;
  }
};

const handleSave = () => {
  const payload: any = {
    name: name.value,
    key: key.value,
    areaId: areaId.value,
    isEnabled: isEnabled.value,
    modelId: primaryModelId.value,
    secondaryModelIds: secondaryModelIds.value,
    connectionMode: connectionMode.value
  };

  if (connectionMode.value === 'advanced') {
    payload.controllerId = selectedControllerId.value;
    payload.connectionId = selectedConnectionId.value;
  } else {
    payload.protocolKey = protocolKey.value;
    payload.ipAddress = ipAddress.value;
    payload.port = Number(port.value) || 0;
    payload.endpointUrl = endpointUrl.value;
    payload.cpuType = cpuType.value;
    payload.rack = rack.value;
    payload.slot = slot.value;
  }

  emit('save', payload);
};

// Add secondary model
const addSecondaryModel = (modelId: number) => {
  if (!secondaryModelIds.value.includes(modelId) && modelId !== primaryModelId.value) {
    secondaryModelIds.value.push(modelId);
  }
};
const removeSecondaryModel = (index: number) => {
  secondaryModelIds.value.splice(index, 1);
};
</script>

<template>
  <div v-if="show" class="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-4 text-left select-none">
    <!-- Backdrop -->
    <div class="absolute inset-0 bg-slate-900/60 backdrop-blur-xs transition-opacity" @click="emit('close')" />

    <!-- Modal Dialog -->
    <div class="relative w-full max-w-xl bg-white dark:bg-slate-900 rounded-2xl shadow-2xl border border-slate-200 dark:border-slate-800 overflow-hidden flex flex-col max-h-[92vh] animate-in fade-in zoom-in-95 duration-150">
      <!-- Header -->
      <div class="px-5 py-4 bg-slate-900 text-white flex items-center justify-between border-b border-slate-800 shrink-0">
        <div class="flex items-center gap-2.5">
          <div class="w-8 h-8 rounded-lg bg-[#1890ff]/20 text-sky-400 flex items-center justify-center font-bold">
            <Cpu class="w-4 h-4" />
          </div>
          <div>
            <h3 class="font-bold text-sm tracking-tight">{{ isEditing ? '编辑工业设备资产' : '接入新工业设备' }}</h3>
            <div class="text-[10px] text-slate-400">配置设备标识、数据模型映射及硬件驱动通信</div>
          </div>
        </div>
        <button type="button" @click="emit('close')" class="text-slate-400 hover:text-white cursor-pointer p-1">
          <X class="w-4 h-4" />
        </button>
      </div>

      <!-- Navigation Tabs -->
      <div class="px-5 border-b border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-950/60 flex gap-4 shrink-0">
        <button
          type="button"
          @click="activeTab = 'basic'"
          class="py-2.5 text-xs font-bold border-b-2 transition-colors cursor-pointer flex items-center gap-1.5"
          :class="activeTab === 'basic' ? 'border-[#1890ff] text-[#1890ff]' : 'border-transparent text-slate-500 hover:text-slate-800 dark:hover:text-slate-200'"
        >
          <Cpu class="w-3.5 h-3.5" />
          <span>基础标识</span>
        </button>
        <button
          type="button"
          @click="activeTab = 'model'"
          class="py-2.5 text-xs font-bold border-b-2 transition-colors cursor-pointer flex items-center gap-1.5"
          :class="activeTab === 'model' ? 'border-[#1890ff] text-[#1890ff]' : 'border-transparent text-slate-500 hover:text-slate-800 dark:hover:text-slate-200'"
        >
          <Link2 class="w-3.5 h-3.5" />
          <span>数据模型绑定</span>
        </button>
        <button
          type="button"
          @click="activeTab = 'connection'"
          class="py-2.5 text-xs font-bold border-b-2 transition-colors cursor-pointer flex items-center gap-1.5"
          :class="activeTab === 'connection' ? 'border-[#1890ff] text-[#1890ff]' : 'border-transparent text-slate-500 hover:text-slate-800 dark:hover:text-slate-200'"
        >
          <Server class="w-3.5 h-3.5" />
          <span>通信驱动连接</span>
        </button>
      </div>

      <!-- Form Body -->
      <div class="p-5 overflow-y-auto space-y-4 text-xs">
        <div v-if="errorMessage" class="p-3 rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 text-rose-600 dark:text-rose-400 font-medium">
          {{ errorMessage }}
        </div>

        <!-- TAB 1: BASIC -->
        <div v-if="activeTab === 'basic'" class="space-y-4">
          <div>
            <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">
              设备名称 <span class="text-rose-500">*</span>
            </label>
            <input
              v-model="name"
              type="text"
              placeholder="例如: 1号冷却塔循环水泵"
              class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-medium focus:outline-none focus:border-[#1890ff]"
            />
            <p v-if="errors.Name" class="text-rose-500 text-[10px] mt-1">{{ errors.Name }}</p>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">
                设备识别键 (Key) <span class="text-rose-500">*</span>
              </label>
              <input
                v-model="key"
                type="text"
                placeholder="例如: PUMP-CIRC-01"
                class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-mono uppercase font-bold focus:outline-none focus:border-[#1890ff]"
              />
              <p v-if="errors.Key" class="text-rose-500 text-[10px] mt-1">{{ errors.Key }}</p>
            </div>

            <div>
              <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">
                所属工艺区域 <span class="text-rose-500">*</span>
              </label>
              <select
                v-model="areaId"
                class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-medium focus:outline-none focus:border-[#1890ff]"
              >
                <option v-for="a in areas" :key="a.id" :value="a.id">{{ a.name }}</option>
              </select>
            </div>
          </div>

          <div class="pt-2">
            <label class="flex items-center gap-2 font-bold text-slate-700 dark:text-slate-300 cursor-pointer">
              <input type="checkbox" v-model="isEnabled" class="rounded text-[#1890ff] focus:ring-0" />
              <span>启用采集服务（设备就绪后自动启动轮询）</span>
            </label>
          </div>
        </div>

        <!-- TAB 2: MODELS -->
        <div v-if="activeTab === 'model'" class="space-y-4">
          <div>
            <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">
              主数据模型 <span class="text-rose-500">*</span>
            </label>
            <p class="text-[11px] text-slate-400 mb-2">主模型决定该设备的核心变量与类型归属：</p>
            <select
              v-model="primaryModelId"
              class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-medium focus:outline-none focus:border-[#1890ff]"
            >
              <option v-for="m in dataModels" :key="m.id" :value="Number(m.id)">
                {{ m.name }} ({{ m.code || '无编码' }} · {{ m.variables?.length || 0 }} 点位)
              </option>
            </select>
          </div>

          <!-- Secondary Models -->
          <div class="pt-2 border-t border-slate-100 dark:border-slate-800">
            <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">
              附加绑定数据模型 (多对多拓展)
            </label>
            <p class="text-[11px] text-slate-400 mb-2">设备可同时继承多个通用模版（如：能耗计量模版、振动监测模版）：</p>

            <div class="space-y-2">
              <div
                v-for="(mid, idx) in secondaryModelIds"
                :key="mid"
                class="flex items-center justify-between p-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800"
              >
                <span class="font-medium text-slate-800 dark:text-slate-200">
                  {{ dataModels.find(m => Number(m.id) === mid)?.name || `模型 #${mid}` }}
                </span>
                <button
                  type="button"
                  @click="removeSecondaryModel(idx)"
                  class="text-rose-500 hover:text-rose-700 cursor-pointer p-1"
                >
                  <Trash2 class="w-3.5 h-3.5" />
                </button>
              </div>

              <!-- Selector to add -->
              <div class="flex items-center gap-2 pt-1">
                <select
                  id="add-secondary-select"
                  class="flex-1 px-3 py-1.5 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white text-xs"
                >
                  <option value="">-- 选择要附加的模型 --</option>
                  <option
                    v-for="m in dataModels.filter(m => Number(m.id) !== primaryModelId && !secondaryModelIds.includes(Number(m.id)))"
                    :key="m.id"
                    :value="m.id"
                  >
                    {{ m.name }}
                  </option>
                </select>
                <button
                  type="button"
                  @click="() => {
                    const sel = document.getElementById('add-secondary-select') as HTMLSelectElement;
                    if (sel && sel.value) {
                      addSecondaryModel(Number(sel.value));
                      sel.value = '';
                    }
                  }"
                  class="px-3 py-1.5 rounded-lg bg-slate-800 text-white hover:bg-slate-700 dark:bg-slate-700 dark:hover:bg-slate-600 font-bold text-xs cursor-pointer inline-flex items-center gap-1"
                >
                  <Plus class="w-3.5 h-3.5" />
                  <span>添加绑定</span>
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- TAB 3: CONNECTION -->
        <div v-if="activeTab === 'connection'" class="space-y-4">
          <!-- Mode Switcher -->
          <div class="grid grid-cols-2 gap-2 p-1 rounded-xl bg-slate-100 dark:bg-slate-800/80">
            <button
              type="button"
              @click="connectionMode = 'quick'"
              class="py-2 text-center rounded-lg font-bold text-xs transition-all cursor-pointer"
              :class="connectionMode === 'quick' ? 'bg-white dark:bg-slate-900 text-slate-900 dark:text-white shadow-xs' : 'text-slate-500 hover:text-slate-800'"
            >
              直接配置模式 (快速)
            </button>
            <button
              type="button"
              @click="connectionMode = 'advanced'"
              class="py-2 text-center rounded-lg font-bold text-xs transition-all cursor-pointer"
              :class="connectionMode === 'advanced' ? 'bg-white dark:bg-slate-900 text-slate-900 dark:text-white shadow-xs' : 'text-slate-500 hover:text-slate-800'"
            >
              挂载独立连接资产 (高级)
            </button>
          </div>

          <!-- Quick Mode Fields -->
          <div v-if="connectionMode === 'quick'" class="space-y-3">
            <div>
              <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">通信协议类型</label>
              <select
                v-model="protocolKey"
                @change="handleProtocolChange"
                class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-medium focus:outline-none focus:border-[#1890ff]"
              >
                <option value="OPCUA">OPC UA 工业协议</option>
                <option value="S7">西门子 S7 (S7-1200/1500/300/400)</option>
                <option value="MODBUSTCP">Modbus TCP 工业总线</option>
                <option value="MQTT">MQTT 物联网网关</option>
                <option value="VIRTUAL">虚拟测试设备 (无须物理硬件)</option>
              </select>
            </div>

            <div v-if="protocolKey === 'OPCUA'">
              <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">OPC UA 端点 URL</label>
              <input
                v-model="endpointUrl"
                type="text"
                placeholder="opc.tcp://192.168.1.100:4840"
                class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-mono text-xs focus:outline-none focus:border-[#1890ff]"
              />
            </div>

            <div v-else-if="protocolKey === 'S7'" class="space-y-3">
              <div class="grid grid-cols-2 gap-3">
                <div>
                  <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">PLC IP 地址</label>
                  <input
                    v-model="ipAddress"
                    type="text"
                    placeholder="192.168.0.1"
                    class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-mono"
                  />
                </div>
                <div>
                  <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">端口</label>
                  <input
                    v-model="port"
                    type="number"
                    placeholder="102"
                    class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-mono"
                  />
                </div>
              </div>
              <div class="grid grid-cols-3 gap-3">
                <div>
                  <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">CPU 型号</label>
                  <select
                    v-model="cpuType"
                    class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white"
                  >
                    <option value="S7-1500">S7-1500</option>
                    <option value="S7-1200">S7-1200</option>
                    <option value="S7-300">S7-300</option>
                    <option value="S7-400">S7-400</option>
                  </select>
                </div>
                <div>
                  <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">机架 (Rack)</label>
                  <input v-model.number="rack" type="number" min="0" class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-mono" />
                </div>
                <div>
                  <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">插槽 (Slot)</label>
                  <input v-model.number="slot" type="number" min="0" class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-mono" />
                </div>
              </div>
            </div>

            <div v-else-if="protocolKey === 'MODBUSTCP'" class="grid grid-cols-2 gap-3">
              <div>
                <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">主机 IP</label>
                <input v-model="ipAddress" type="text" placeholder="192.168.1.50" class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-mono" />
              </div>
              <div>
                <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">端口</label>
                <input v-model="port" type="number" placeholder="502" class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-mono" />
              </div>
            </div>

            <div v-else-if="protocolKey === 'VIRTUAL'" class="p-3 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 text-slate-500">
              虚拟设备使用内置数学发生器产生运行数据，无需配置物理网络。
            </div>
          </div>

          <!-- Advanced Mode Fields -->
          <div v-else class="space-y-3">
            <div>
              <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">所属控制器</label>
              <select
                v-model="selectedControllerId"
                class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white"
              >
                <option :value="null">-- 选择物理控制器 --</option>
                <option v-for="c in controllers" :key="c.id" :value="c.id">
                  {{ c.name }} ({{ c.code }}) - {{ c.protocolName }}
                </option>
              </select>
            </div>

            <div>
              <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">已建立的连接通道</label>
              <select
                v-model="selectedConnectionId"
                class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white"
              >
                <option :value="null">-- 选择连接通道 --</option>
                <option v-for="conn in filteredConnections" :key="conn.id" :value="conn.id">
                  #{{ conn.id }} ({{ conn.protocolName }}) - {{ conn.host }}:{{ conn.port }}
                </option>
              </select>
            </div>
          </div>
        </div>
      </div>

      <!-- Footer -->
      <div class="px-5 py-3 bg-slate-50 dark:bg-slate-950 border-t border-slate-200 dark:border-slate-800 flex justify-between items-center shrink-0">
        <div class="text-[11px] text-slate-400">
          <span v-if="activeTab === 'basic'">步骤 1/3: 基础标识</span>
          <span v-else-if="activeTab === 'model'">步骤 2/3: 模型绑定</span>
          <span v-else>步骤 3/3: 驱动通信</span>
        </div>
        <div class="flex gap-2">
          <button
            type="button"
            @click="emit('close')"
            class="px-4 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 text-slate-600 dark:text-slate-300 font-bold text-xs cursor-pointer"
          >
            取消
          </button>
          <button
            v-if="activeTab !== 'connection'"
            type="button"
            @click="activeTab = activeTab === 'basic' ? 'model' : 'connection'"
            class="px-4 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-white font-bold text-xs cursor-pointer"
          >
            下一步
          </button>
          <button
            v-else
            type="button"
            @click="handleSave"
            class="px-5 py-2 rounded-lg bg-[#1890ff] hover:bg-sky-600 text-white font-bold text-xs cursor-pointer shadow-xs"
          >
            完成并保存
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
