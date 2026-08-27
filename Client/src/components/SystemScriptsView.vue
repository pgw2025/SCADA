<script setup lang="ts">
import { ref, computed } from 'vue';
import { 
  systemScripts, 
  runScriptEngine, 
  addLog, 
  devices 
} from '../store/index';
import { 
  FileCode, 
  Plus, 
  Trash2, 
  Play, 
  Clock, 
  AlertTriangle, 
  Terminal, 
  Save, 
  X, 
  RotateCcw,
  Check,
  Zap
} from 'lucide-vue-next';
import { SystemScript } from '../types';

const selectedScriptId = ref<string | null>(null);
const showAddModal = ref(false);

// Add form states
const newScriptName = ref('');
const newScriptTriggerType = ref<'auto' | 'manual'>('manual');
const newScriptInterval = ref(5);
const newScriptDeviceId = ref<number | null>(null);
const newScriptCode = ref(`// 双联PLC逻辑连锁脚本模板
let tempVal = getVal('boiler_temp');
if (tempVal > 92) {
  setVal('pump_state', true);
  log('【温度保护】排温风机已被自动开启');
} else {
  log('【参数巡查】当前温度：' + tempVal + '℃，在安全区间');
}`);

// Current selected script object
const currentScriptObj = computed(() => {
  return systemScripts.value.find(s => s.id === selectedScriptId.value) || systemScripts.value[0] || null;
});

// Set initial selection
if (systemScripts.value.length > 0 && !selectedScriptId.value) {
  selectedScriptId.value = systemScripts.value[0].id;
}

const handleCreateScript = () => {
  if (!newScriptName.value.trim() || !newScriptCode.value.trim()) {
    alert('请完善脚本大纲名及代码内容！');
    return;
  }

  const newScript: SystemScript = {
    id: `script-${Date.now()}`,
    name: newScriptName.value.trim(),
    code: newScriptCode.value,
    triggerType: newScriptTriggerType.value,
    intervalSeconds: newScriptTriggerType.value === 'auto' ? Number(newScriptInterval.value) : undefined,
    deviceId: newScriptDeviceId.value ?? undefined,
    executionStatus: 'idle',
    logOutput: '等待手工运行或内部时序轮询...'
  };

  systemScripts.value.push(newScript);
  selectedScriptId.value = newScript.id;
  addLog('脚本中心', `写入新脚本组模块: [${newScript.name}]`, 'normal');

  // Reset
  newScriptName.value = '';
  newScriptDeviceId.value = null;
  showAddModal.value = false;
};

const handleDeleteScript = (id: string, name: string) => {
  if (confirm(`确定要抛弃系统脚本: [${name}] 吗？`)) {
    systemScripts.value = systemScripts.value.filter(s => s.id !== id);
    if (selectedScriptId.value === id) {
      selectedScriptId.value = systemScripts.value[0]?.id || null;
    }
    addLog('脚本中心', `卸载了指令块: [${name}]`, 'warning');
  }
};

const handleManualExecute = () => {
  const scr = currentScriptObj.value;
  if (!scr) return;

  scr.executionStatus = 'running' as any;
  setTimeout(() => {
    runScriptEngine(scr);
    addLog('脚本物理层', `用户强制执行脚本 [${scr.name}] 逻辑成功`, 'info');
  }, 600);
};

const handleUpdateCode = (evt: Event) => {
  const codeVal = (evt.target as HTMLTextAreaElement).value;
  if (currentScriptObj.value) {
    currentScriptObj.value.code = codeVal;
  }
};
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">
    
    <!-- Header banner -->
    <div class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <FileCode class="w-5 h-5 text-indigo-600 dark:text-indigo-400" />
          系统脚本管理
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          编写和管理自动化控制脚本，支持定时执行和手动触发。
        </p>
      </div>

      <button 
        @click="showAddModal = true"
        class="font-bold text-xs bg-slate-900 dark:bg-sky-600 text-white hover:bg-slate-800 dark:hover:bg-sky-500 px-4 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-sm active:translate-y-0.5"
      >
        <Plus class="w-4 h-4" />
        新建脚本
      </button>
    </div>

    <!-- Active workspace dashboard layout -->
    <div class="flex-1 flex flex-col lg:flex-row min-h-0 overflow-hidden">
      
      <!-- Left sidebar selector -->
      <div class="w-full lg:w-72 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex flex-col shrink-0 transition-colors">
        <div class="p-4 border-b border-slate-100 dark:border-slate-800 font-bold text-[10px] text-slate-400 dark:text-slate-500 uppercase tracking-widest text-left">
          脚本列表 ({{ systemScripts.length }})
        </div>

        <div class="flex-1 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800 text-left">
          <div 
            v-for="scr in systemScripts" 
            :key="scr.id"
            @click="selectedScriptId = scr.id"
            class="p-4 cursor-pointer hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-all space-y-2 relative"
            :class="selectedScriptId === scr.id ? 'bg-indigo-50/20 dark:bg-indigo-950/40 text-indigo-600 dark:text-indigo-400 border-r-4 border-r-indigo-600 dark:border-r-indigo-400' : 'text-slate-700 dark:text-slate-300'"
          >
            <div class="flex items-start justify-between gap-1">
              <span class="font-bold text-xs leading-snug tracking-tight block max-w-[180px] break-words">
                {{ scr.name }}
              </span>

              <button 
                @click.stop="handleDeleteScript(scr.id, scr.name)"
                class="text-slate-400 dark:text-slate-500 hover:text-rose-600 dark:hover:text-rose-400 p-0.5"
                title="删除脚本"
              >
                <Trash2 class="w-3.5 h-3.5" />
              </button>
            </div>

            <div class="flex items-center gap-2">
              <span class="text-[9px] font-bold px-1.5 py-0.5 rounded border"
                :class="scr.triggerType === 'auto' ? 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400 border-emerald-100 dark:border-emerald-800' : 'bg-slate-50 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-150 dark:border-slate-700'"
              >
                {{ scr.triggerType === 'auto' ? '定时执行 (' + scr.intervalSeconds + 's)' : '手动触发' }}
              </span>

              <span class="text-[9px] font-bold px-1.5 py-0.5 rounded border"
                :class="scr.executionStatus === 'success' ? 'bg-emerald-500 text-white border-emerald-500' : scr.executionStatus === 'error' ? 'bg-rose-500 text-white border-rose-500' : scr.executionStatus === 'running' ? 'bg-indigo-500 text-white border-indigo-500 animate-pulse' : 'bg-slate-100 dark:bg-slate-800 text-slate-400 dark:text-slate-500 border-slate-200 dark:border-slate-700'"
              >
                {{ scr.executionStatus === 'success' ? '执行成功' : scr.executionStatus === 'error' ? '执行失败' : scr.executionStatus === 'running' ? '执行中' : '空闲' }}
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- Right Side: Live code workspace and Terminal -->
      <div v-if="currentScriptObj" class="flex-1 flex flex-col min-w-0 bg-slate-900 dark:bg-slate-950 border-l border-slate-950 dark:border-slate-800 relative">
        
        <!-- Editor Top Header status actions -->
        <div class="bg-slate-950/80 px-5 py-3 border-b border-slate-950 dark:border-slate-800 flex items-center justify-between text-white text-xs">
          <div class="flex items-center gap-2">
            <span class="w-2.5 h-2.5 rounded-full bg-rose-500" />
            <b class="font-sans text-slate-300">当前脚本:</b>
            <span class="font-bold text-[#1890ff] font-mono select-all">{{ currentScriptObj.name }}</span>
          </div>

          <div class="flex items-center gap-2 font-sans">
            <span class="text-[10px] text-slate-500 font-mono hidden sm:inline-block">上次执行: {{ currentScriptObj.lastExecuted || '未执行' }}</span>
            <button 
              @click="handleManualExecute"
              :disabled="currentScriptObj.executionStatus === 'running'"
              class="px-4 py-1 rounded bg-indigo-600 hover:bg-indigo-500 text-white font-bold inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 font-sans shadow-sm disabled:opacity-40"
            >
              <Play class="w-3.5 h-3.5" />
              运行脚本
            </button>
          </div>
        </div>

        <!-- Split area: Code Area (top half) / Console output logs (bottom half) -->
        <div class="flex-1 flex flex-col min-h-0">
          
          <!-- Code Block input view -->
          <div class="flex-1 flex flex-col min-h-[220px] relative">
            <span class="absolute top-2.5 right-4 pointer-events-none select-none uppercase tracking-widest text-[#1890ff] font-bold text-[9px] font-mono">
              JAVASCRIPT ES6 (MOCK SANDBOX CONTEXT)
            </span>
            <div class="absolute left-0 top-0 bottom-0 w-11 bg-slate-950 text-slate-600 font-mono text-[10px] text-right pr-2 py-4 select-none border-r border-slate-800">
              <div v-for="n in 14" :key="n" class="leading-relaxed h-5">{{ n }}</div>
            </div>
            <textarea 
              :value="currentScriptObj.code"
              @input="handleUpdateCode"
              class="flex-1 w-full bg-[#181824] pl-14 pr-4 py-4 font-mono text-[11.5px] leading-relaxed text-emerald-400 outline-none resize-none overflow-y-auto"
              spellcheck="false"
              placeholder="// 输入控制代码..."
            />
          </div>

          <!-- Console log output -->
          <div class="h-48 border-t border-slate-950 dark:border-slate-800 bg-slate-950 flex flex-col min-h-[140px] shrink-0 text-left">
            <div class="bg-slate-900 dark:bg-slate-950 border-b border-slate-950 dark:border-slate-800 px-4 py-1.5 flex items-center justify-between text-slate-400 font-mono text-[10px] select-none">
            <div class="flex items-center gap-1 text-[#1890ff] font-bold">
              <Terminal class="w-3.5 h-3.5" />
              <span>控制台输出</span>
            </div>
            <span>行数: {{ currentScriptObj.logOutput ? currentScriptObj.logOutput.split('\n').length : 0 }}</span>
          </div>

            <!-- Scroll output -->
            <div class="flex-1 overflow-y-auto p-4 space-y-1 font-mono text-[10.5px] text-slate-300 leading-relaxed max-w-full break-all select-all">
              <div v-for="(line, idx) in currentScriptObj.logOutput?.split('\n')" :key="idx" class="whitespace-pre-wrap">
                <span class="text-indigo-400 font-bold mr-1.5">>>></span> {{ line }}
              </div>
              <div v-if="!currentScriptObj.logOutput" class="text-slate-600 text-center py-6">
                点击 "运行脚本" 执行并查看输出...
              </div>
            </div>
          </div>

        </div>

        <!-- Float Help Panel -->
        <div class="absolute top-12 right-4 bg-slate-950/90 border border-slate-800/80 rounded-lg p-3 text-left max-w-xs space-y-2 pointer-events-auto select-none mt-2 shadow-lg text-[9px] font-mono leading-relaxed text-slate-400">
          <b class="text-slate-200 block border-b border-slate-800 pb-1 text-[10px] font-sans">内挂系统调用 API 说明</b>
          <div class="space-y-1">
            <p><b class="text-yellow-400">getVal(key)</b>: 读取某个关联的PLC寄存器。返回数字/布尔值。</p>
            <p><b class="text-yellow-400">setVal(key, val)</b>: 物理写入改写某个绑定的寄存器值。</p>
            <p><b class="text-yellow-400">log(message)</b>: 输出一条打印字符串至解调窗口。</p>
          </div>
        </div>

      </div>

      <div v-else class="flex-1 flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 py-16 gap-3">
        <FileCode class="w-10 h-10 text-slate-300 dark:text-slate-600 animate-pulse" />
        <span>暂无脚本。点击右上角按钮创建新脚本。</span>
      </div>

    </div>

    <!-- ADD SCRIPT POPUP WINDOW -->
    <div v-if="showAddModal" class="fixed inset-0 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-2 font-bold text-xs uppercase tracking-widest text-indigo-400">
            <Zap class="w-4 h-4" />
            <span>新建脚本</span>
          </div>
          <button @click="showAddModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4.5 h-4.5" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs font-sans">
          
          <div>
            <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">脚本名称</label>
            <input 
              v-model="newScriptName"
              type="text"
              placeholder="如: 温度保护脚本"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white outline-none focus:border-[#1890ff]"
            />
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">触发方式</label>
              <select 
                v-model="newScriptTriggerType"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white focus:outline-none font-bold"
              >
                <option value="manual">手动触发</option>
                <option value="auto">定时执行</option>
              </select>
            </div>

            <div v-if="newScriptTriggerType === 'auto'">
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">执行间隔 (秒)</label>
              <input 
                v-model.number="newScriptInterval"
                type="number"
                min="1"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-mono outline-none font-bold"
              />
            </div>

            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">目标设备（写操作必填）</label>
              <select
                v-model="newScriptDeviceId"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white focus:outline-none font-mono"
              >
                <option :value="null">-- 未指定（脚本写操作将报错）--</option>
                <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }} ({{ d.key }})</option>
              </select>
            </div>
          </div>

        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800">
          <button 
            @click="showAddModal = false"
            class="px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleCreateScript"
            class="px-4 py-1.5 rounded-lg bg-slate-900 dark:bg-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white cursor-pointer"
          >
            创建脚本
          </button>
        </div>

      </div>
    </div>

  </div>
</template>
