<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { dataConversions, devices, addLog } from '../store/index';
import { checkCycleInConversions } from '../utils/algo';
import { DataConversion } from '../types';
import { 
  fetchDataConversions,
  createDataConversion,
  updateDataConversion,
  deleteDataConversion
} from '../api/dataConversionApi';
import { 
  Plus, 
  Trash2, 
  Settings, 
  X, 
  Shuffle, 
  ToggleLeft, 
  ToggleRight, 
  HelpCircle, 
  ArrowRight,
  ShieldAlert,
  Search
} from 'lucide-vue-next';

const showModal = ref(false);
const isEditing = ref(false);
const editingId = ref<number | null>(null);

// Form Fields
const linkageName = ref('');
const sourceDevId = ref(0);
const sourceVarKey = ref('');
const targetDevId = ref(0);
const targetVarKey = ref('');
const isActiveState = ref(true);

const filterQuery = ref('');

// 页面加载：从后端拉取全部规则，实现持久化
onMounted(async () => {
  try {
    dataConversions.value = await fetchDataConversions();
  } catch { /* 拦截器已弹 toast，此处静默 */ }
});

// Filtered conversions
const filteredConversions = computed(() => {
  const query = filterQuery.value.trim().toLowerCase();
  if (!query) return dataConversions.value;

  return dataConversions.value.filter(c => {
    const srcDev = devices.value.find(d => d.id === c.sourceDeviceId)?.name || '';
    const dstDev = devices.value.find(d => d.id === c.targetDeviceId)?.name || '';
    return (
      c.name.toLowerCase().includes(query) ||
      c.sourceVariableKey.toLowerCase().includes(query) ||
      c.targetVariableKey.toLowerCase().includes(query) ||
      srcDev.toLowerCase().includes(query) ||
      dstDev.toLowerCase().includes(query)
    );
  });
});

// Watch source device selection to update variables dropdown list
const sourceVariables = computed(() => {
  const dev = devices.value.find(d => d.id === sourceDevId.value);
  if (!dev) return [];
  return Object.keys(dev.variables);
});

// Watch target device selection to update variables dropdown list
const targetVariables = computed(() => {
  const dev = devices.value.find(d => d.id === targetDevId.value);
  if (!dev) return [];
  return Object.keys(dev.variables);
});

const openNewLinkageModal = () => {
  isEditing.value = false;
  editingId.value = null;
  linkageName.value = `联动规则-${Date.now().toString().slice(-4)}`;
  sourceDevId.value = devices.value[0]?.id ?? 0;
  sourceVarKey.value = sourceVariables.value[0] || '';
  targetDevId.value = devices.value[1]?.id ?? devices.value[0]?.id ?? 0;
  targetVarKey.value = targetVariables.value[0] || '';
  isActiveState.value = true;
  showModal.value = true;
};

// Handle source device change
const onSourceDeviceChange = () => {
  sourceVarKey.value = sourceVariables.value[0] || '';
};

// Handle target device change
const onTargetDeviceChange = () => {
  targetVarKey.value = targetVariables.value[0] || '';
};

// Save custom linkage
const handleSaveLinkage = async () => {
  if (!linkageName.value.trim() || !sourceDevId.value || !sourceVarKey.value || !targetDevId.value || !targetVarKey.value) {
    alert('请填写完整所有的设备变量映射字段。');
    return;
  }

  // Prevent self loop A:Temp -> A:Temp
  if (sourceDevId.value === targetDevId.value && sourceVarKey.value === targetVarKey.value) {
    alert('错误: 无法关联相同设备的相同变量 (这会直接构成死循环回路)。');
    return;
  }

  // Build tentative array of conversions
  const tentative = JSON.parse(JSON.stringify(dataConversions.value)) as DataConversion[];
  
  if (isEditing.value && editingId.value) {
    const idx = tentative.findIndex(c => c.id === editingId.value);
    if (idx !== -1) {
      tentative[idx] = {
        id: editingId.value,
        name: linkageName.value,
        sourceDeviceId: sourceDevId.value,
        sourceVariableKey: sourceVarKey.value,
        targetDeviceId: targetDevId.value,
        targetVariableKey: targetVarKey.value,
        active: isActiveState.value
      };
    }
  } else {
    tentative.push({
      id: 0,
      name: linkageName.value,
      sourceDeviceId: sourceDevId.value,
      sourceVariableKey: sourceVarKey.value,
      targetDeviceId: targetDevId.value,
      targetVariableKey: targetVarKey.value,
      active: isActiveState.value
    });
  }

  // 1. CYCLE DETECTION & LOOP PREVENTION CHECK!
  const hasCycle = checkCycleInConversions(tentative);
  if (hasCycle) {
    alert('🛑 安全联动控制中心警报：\n\n检测到此配置在网络拓扑结构中形成了数据回环依赖 (即 A -> B -> A 等导致无限流转的死循环回路) ！\n\nSCADA 已自动拦截此配置，防止整个时序系统崩溃。请调整输入或输出路径后再试。');
    return;
  }

  try {
    // Persist changes if no cycles detected
    if (isEditing.value && editingId.value) {
      const idx = dataConversions.value.findIndex(c => c.id === editingId.value);
      if (idx !== -1) {
        await updateDataConversion({
          ...dataConversions.value[idx],
          name: linkageName.value,
          sourceDeviceId: sourceDevId.value,
          sourceVariableKey: sourceVarKey.value,
          targetDeviceId: targetDevId.value,
          targetVariableKey: targetVarKey.value,
          active: isActiveState.value
        });
        dataConversions.value[idx] = {
          ...dataConversions.value[idx],
          name: linkageName.value,
          sourceDeviceId: sourceDevId.value,
          sourceVariableKey: sourceVarKey.value,
          targetDeviceId: targetDevId.value,
          targetVariableKey: targetVarKey.value,
          active: isActiveState.value
        };
        addLog('数据转换', `修改了数据转换规则 [${linkageName.value}]`, 'normal');
      }
    } else {
      const created = await createDataConversion({
        name: linkageName.value,
        sourceDeviceId: sourceDevId.value,
        sourceVariableKey: sourceVarKey.value,
        targetDeviceId: targetDevId.value,
        targetVariableKey: targetVarKey.value,
        active: isActiveState.value
      });
      dataConversions.value.push(created);
      addLog('数据转换', `创建了数据联动转换规则 [${linkageName.value}]`, 'normal');
    }

    showModal.value = false;
  } catch { /* 拦截器已弹 toast，失败保留弹窗供重试 */ }
};

const handleDeleteLinkage = async (id: number, name: string) => {
  if (!confirm(`确定删除数据联动转换规则 [${name}] 吗？`)) return;
  try {
    await deleteDataConversion(id, name);
    dataConversions.value = dataConversions.value.filter(c => c.id !== id);
    addLog('数据转换', `删除了数据转换规则 [${name}]`, 'warning');
  } catch { /* 失败不删内存，保持与库一致 */ }
};

const toggleLinkStatus = async (c: DataConversion) => {
  const next = !c.active;
  // 若为启用：按翻转后的状态构造临时数组做环检，通过后才提交
  if (next) {
    const tentative = dataConversions.value.map(x =>
      x.id === c.id ? { ...x, active: true } : x);
    if (checkCycleInConversions(tentative)) {
      alert('无法激活此关联: 启动后会形成数据回环死循环！已自动拦截并关闭。');
      return;
    }
  }
  try {
    await updateDataConversion({ ...c, active: next });
    c.active = next;
    addLog('数据转换', `联动规则 [${c.name}] 切换为 ${next ? '已启用 (Active)' : '已停用 (Passive)'}`, next ? 'normal' : 'warning');
  } catch { /* 拦截器已弹 toast，状态保持不变 */ }
};
</script>

<template>
  <div class="h-full overflow-y-auto space-y-6 text-[#1e293b] dark:text-slate-100 select-none p-4 sm:p-6 bg-slate-50/50 dark:bg-transparent">
    
    <!-- Top banner -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between border-b border-slate-200 dark:border-slate-800 pb-5 gap-4 text-left">
      <div>
        <h1 class="text-xl font-bold font-sans text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Shuffle class="w-5 h-5 text-indigo-500" />
          <span>数据转换</span>
        </h1>
        <p class="text-xs text-slate-500 dark:text-slate-400 mt-1">
          配置设备间的数据映射和值跟随规则。
        </p>
      </div>

      <button 
        @click="openNewLinkageModal"
        class="bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white px-3.5 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 shadow-sm"
      >
        <Plus class="w-4 h-4" />
        新建转换规则
      </button>
    </div>

    <!-- Middle bar with search & status stats -->
    <div class="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 bg-white dark:bg-slate-900 p-4 rounded-xl border border-slate-200 dark:border-slate-800 shadow-sm text-left transition-colors">
      <div class="flex items-center gap-4 text-xs font-sans font-semibold text-slate-500 dark:text-slate-400 shrink-0">
        <span class="inline-flex items-center gap-1">
          规则总计: <b class="text-indigo-600 dark:text-indigo-400 text-sm font-mono">{{ dataConversions.length }}</b> 条
        </span>
        <span class="inline-flex items-center gap-1 border-l border-slate-200 dark:border-slate-800 pl-4">
          已启用: <b class="text-emerald-600 dark:text-emerald-400 text-sm font-mono">{{ dataConversions.filter(c => c.active).length }}</b> 条
        </span>
        <span class="inline-flex items-center gap-1 border-l border-slate-200 dark:border-slate-800 pl-4 text-[10px] text-slate-400 dark:text-slate-500 font-mono">
          🛡️ 环路保护: 已启用
        </span>
      </div>

      <!-- Live search -->
      <div class="relative w-full sm:w-72 select-none">
        <input 
          v-model="filterQuery"
          type="text"
          placeholder="检索规则名称、变量 Key、对应设备名..."
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg py-1.5 pl-8 pr-3 text-xs placeholder-slate-400 dark:placeholder-slate-500 text-slate-900 dark:text-white focus:bg-white dark:focus:bg-slate-900 focus:outline-none focus:border-[#1890ff]"
        />
        <Search class="absolute left-2.5 top-2.5 w-3.5 h-3.5 text-slate-400" />
        <button 
          v-if="filterQuery" 
          @click="filterQuery = ''" 
          class="absolute right-2 top-2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 focus:outline-none"
        >
          <X class="w-3.5 h-3.5" />
        </button>
      </div>
    </div>

    <!-- Grid / list of connections -->
    <div class="grid grid-cols-1 xl:grid-cols-2 gap-4 text-left">
      <div 
        v-for="c in filteredConversions" 
        :key="c.id"
        class="bg-white dark:bg-slate-900 border rounded-xl overflow-hidden shadow-sm hover:shadow-md transition-all flex flex-col justify-between"
        :class="c.active ? 'border-slate-200 dark:border-slate-800' : 'border-slate-200/50 dark:border-slate-800/50 opacity-75'"
      >
        <!-- Top bar details -->
        <div class="p-4 sm:p-5 space-y-3 flex-1">
          <div class="flex items-start justify-between gap-4">
            <div class="space-y-1">
              <h4 class="font-bold text-sm text-slate-900 dark:text-slate-100 leading-snug">{{ c.name }}</h4>
              <span class="text-[9px] text-slate-400 dark:text-slate-500 font-mono">转换通道 ID: {{ c.id }}</span>
            </div>

            <!-- Active / Inactive switch -->
            <button 
              @click="toggleLinkStatus(c)"
              class="rounded-full cursor-pointer transition-colors"
              :title="c.active ? '点击停用联动桥' : '点击启用联动桥'"
            >
              <ToggleRight v-if="c.active" class="w-8 h-8 text-emerald-500" />
              <ToggleLeft v-else class="w-8 h-8 text-slate-300 dark:text-slate-600" />
            </button>
          </div>

          <!-- Mapping layout diagram -->
          <div class="grid grid-cols-5 gap-2 items-center p-3 bg-slate-50 dark:bg-slate-950/60 rounded-xl border border-slate-100 dark:border-slate-800/80">
            <!-- Source Node -->
            <div class="col-span-2 space-y-1 text-left select-text">
              <span class="text-[9px] font-bold text-indigo-500 dark:text-indigo-400 uppercase font-mono">源</span>
              <h5 class="text-xs font-bold text-slate-800 dark:text-slate-200 truncate">
                {{ devices.find(d => d.id === c.sourceDeviceId)?.name || '未知源设备' }}
              </h5>
              <div class="font-mono text-[10px] text-slate-600 dark:text-slate-300 font-bold bg-[#1890ff]/10 dark:bg-sky-950/60 px-2 py-0.5 rounded truncate inline-block border border-[#1890ff]/20 dark:border-sky-800">
                {{ c.sourceVariableKey }}
              </div>
              <p class="text-[10px] text-slate-400 dark:text-slate-500 font-mono">
                当前值: <span class="text-slate-700 dark:text-slate-300 font-bold font-mono">{{ devices.find(d => d.id === c.sourceDeviceId)?.variables[c.sourceVariableKey] }}</span>
              </p>
            </div>

            <!-- Connection icon arrow -->
            <div class="flex flex-col items-center justify-center space-y-1">
              <span class="text-[8px] font-extrabold text-slate-400 dark:text-slate-500 tracking-wider">映射</span>
              <ArrowRight class="w-4 h-4 text-slate-400 dark:text-slate-500" />
            </div>

            <!-- Target Node -->
            <div class="col-span-2 space-y-1 text-left select-text">
              <span class="text-[9px] font-bold text-emerald-600 dark:text-emerald-400 uppercase font-mono">目标</span>
              <h5 class="text-xs font-bold text-slate-800 dark:text-slate-200 truncate">
                {{ devices.find(d => d.id === c.targetDeviceId)?.name || '未知目标设备' }}
              </h5>
              <div class="font-mono text-[10px] text-emerald-700 dark:text-emerald-300 font-bold bg-emerald-50 dark:bg-emerald-950/60 px-2 py-0.5 rounded truncate inline-block border border-emerald-100 dark:border-emerald-800">
                {{ c.targetVariableKey }}
              </div>
              <p class="text-[10px] text-slate-400 dark:text-slate-500 font-mono">
                当前值: <span class="text-slate-700 dark:text-slate-300 font-bold font-mono">{{ devices.find(d => d.id === c.targetDeviceId)?.variables[c.targetVariableKey] }}</span>
              </p>
            </div>
          </div>
        </div>

        <!-- Footer actions -->
        <div class="bg-slate-50/70 dark:bg-slate-950/40 p-3 flex justify-between items-center text-[10px] text-slate-400 dark:text-slate-500 border-t border-slate-100 dark:border-slate-800 shrink-0 font-mono">
          <span>🛡️ 环路检查已通过</span>
          <button 
            @click="handleDeleteLinkage(c.id, c.name)"
            class="text-rose-500 hover:text-rose-700 dark:hover:text-rose-400 font-bold inline-flex items-center gap-0.5 cursor-pointer font-sans"
          >
            <Trash2 class="w-3.5 h-3.5" />
            删除规则
          </button>
        </div>
      </div>

      <div 
        v-if="filteredConversions.length === 0" 
        class="col-span-full py-16 bg-white dark:bg-slate-900 border border-dashed border-slate-200 dark:border-slate-800 rounded-xl flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 text-center space-y-2 transition-colors"
      >
        <Shuffle class="w-8 h-8 text-indigo-300 dark:text-indigo-600 animate-pulse" />
        <div class="text-xs">
          <p class="font-bold text-slate-500 dark:text-slate-400">暂无转换规则</p>
          <p class="text-[11px] text-slate-400 dark:text-slate-500 mt-1">点击右上角按钮新建规则</p>
        </div>
      </div>
    </div>

    <!-- MODAL: DEFINE NEW CASCADE DATA LINKAGE -->
    <div v-if="showModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        
        <div class="bg-[#1e1b4b] dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Shuffle class="w-4 h-4 text-indigo-400" />
            <span>配置转换规则</span>
          </div>
          <button @click="showModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div>
            <label class="text-slate-500 dark:text-slate-400 block mb-1 font-bold">规则名称</label>
            <input 
              v-model="linkageName"
              type="text"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] text-xs font-semibold"
            />
          </div>

          <!-- Source dropdown selection box -->
          <div class="p-3 bg-slate-50/50 dark:bg-slate-950/50 rounded-xl border border-slate-100 dark:border-slate-800 space-y-2.5">
            <span class="text-[9px] font-bold text-indigo-500 dark:text-indigo-400 font-mono tracking-wider block">源</span>
            
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-slate-400 dark:text-slate-400 block mb-0.5 text-[10px]">源设备</label>
                <select 
                  v-model="sourceDevId"
                  @change="onSourceDeviceChange"
                  class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded p-1.5 focus:outline-none font-bold text-slate-900 dark:text-white"
                >
                  <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
                </select>
              </div>
              <div>
                <label class="text-slate-400 dark:text-slate-400 block mb-0.5 text-[10px]">源变量</label>
                <select 
                  v-model="sourceVarKey"
                  class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded p-1.5 focus:outline-none font-mono text-[11px] text-slate-900 dark:text-white"
                >
                  <option v-for="k in sourceVariables" :key="k" :value="k">{{ k }}</option>
                </select>
              </div>
            </div>
            
            <p v-if="sourceVarKey" class="text-[10px] text-slate-400 dark:text-slate-500 text-left font-mono leading-none">
              当前值: <b class="text-slate-600 dark:text-slate-300 font-bold">{{ devices.find(d => d.id === sourceDevId)?.variables[sourceVarKey] }}</b>
            </p>
          </div>

          <!-- Icon Separator -->
          <div class="flex justify-center select-none text-[10px] text-slate-400 dark:text-slate-500 uppercase tracking-widest font-bold">
            ▼ 值跟随传导 ▼
          </div>

          <!-- Target dropdown selection box -->
          <div class="p-3 bg-slate-50/50 dark:bg-slate-950/50 rounded-xl border border-slate-100 dark:border-slate-800 space-y-2.5">
            <span class="text-[9px] font-bold text-emerald-600 dark:text-emerald-400 font-mono tracking-wider block">目标</span>
            
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-slate-400 dark:text-slate-400 block mb-0.5 text-[10px]">目标设备</label>
                <select 
                  v-model="targetDevId"
                  @change="onTargetDeviceChange"
                  class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded p-1.5 focus:outline-none font-bold text-slate-900 dark:text-white"
                >
                  <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
                </select>
              </div>
              <div>
                <label class="text-slate-400 dark:text-slate-400 block mb-0.5 text-[10px]">目标变量</label>
                <select 
                  v-model="targetVarKey"
                  class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded p-1.5 focus:outline-none font-mono text-[11px] text-slate-900 dark:text-white"
                >
                  <option v-for="k in targetVariables" :key="k" :value="k">{{ k }}</option>
                </select>
              </div>
            </div>

            <p v-if="targetVarKey" class="text-[10px] text-slate-400 dark:text-slate-500 text-left font-mono leading-none">
              当前值: <b class="text-slate-600 dark:text-slate-300 font-bold">{{ devices.find(d => d.id === targetDevId)?.variables[targetVarKey] }}</b>
            </p>
          </div>

          <!-- Enable state toggle on save -->
          <div class="flex items-center justify-between py-1">
            <span class="text-slate-500 dark:text-slate-400 font-bold">保存并启用</span>
            <button 
              @click="isActiveState = !isActiveState"
              class="rounded-full cursor-pointer transition-colors"
            >
              <ToggleRight v-if="isActiveState" class="w-8 h-8 text-indigo-500" />
              <ToggleLeft v-else class="w-8 h-8 text-slate-300 dark:text-slate-600" />
            </button>
          </div>
        </div>

        <!-- Submit actions -->
        <div class="bg-slate-50 dark:bg-slate-950 p-4 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800 shrink-0">
          <button 
            @click="showModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleSaveLinkage"
            class="px-4 py-1.5 bg-indigo-600 hover:bg-indigo-700 font-bold text-xs text-white cursor-pointer rounded-lg"
          >
            保存规则
          </button>
        </div>

      </div>
    </div>

  </div>
</template>
