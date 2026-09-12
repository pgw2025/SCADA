<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import { Area, DataModel, ControllerOption, DeviceConnectionSummary } from '../../types';
import { Cpu, X, Server, Link2 } from 'lucide-vue-next';

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

// Connection
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

      // Connection
      selectedConnectionId.value = d.connectionId || null;
      selectedControllerId.value = d.controllerId || null;
    }
  },
  { immediate: true }
);

// Filter connections by controller
const filteredConnections = computed(() => {
  if (!selectedControllerId.value) return props.connections;
  return props.connections.filter(c => c.controllerId === selectedControllerId.value);
});

const handleSave = () => {
  const payload: any = {
    name: name.value,
    key: key.value,
    areaId: areaId.value,
    isEnabled: isEnabled.value,
    modelId: primaryModelId.value,
    controllerId: selectedControllerId.value,
    connectionId: selectedConnectionId.value
  };

  emit('save', payload);
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
        </div>

        <!-- TAB 3: CONNECTION -->
        <div v-if="activeTab === 'connection'" class="space-y-4">
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
