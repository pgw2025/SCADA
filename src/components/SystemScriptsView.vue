<script setup lang="ts">
import { ref, computed } from 'vue';
import { 
  systemScripts, 
  runScriptEngine, 
  addLog, 
  devices 
} from '../store';
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
    executionStatus: 'idle',
    logOutput: '等待手工运行或内部时序轮询...'
  };

  systemScripts.value.push(newScript);
  selectedScriptId.value = newScript.id;
  addLog('脚本中心', `写入新脚本组模块: [${newScript.name}]`, 'normal');

  // Reset
  newScriptName.value = '';
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
  <div class="h-full flex flex-col text-[#1e293b] select-none bg-slate-50">
    
    <!-- Header banner -->
    <div class="bg-white p-5 border-b border-slate-200 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 tracking-tight flex items-center gap-2">
          <FileCode class="w-5 h-5 text-indigo-600" />
          系统逻辑控制与自治脚本工作台 (Sandbox Editor)
        </h2>
        <p class="text-xs text-slate-500 font-sans">
          内置浏览器端轻量安全执行引擎。支持读取、改写底层寄存器，并能编写基于时间段及联动逻辑的安全自动化程序。
        </p>
      </div>

      <button 
        @click="showAddModal = true"
        class="font-bold text-xs bg-slate-900 text-white hover:bg-slate-800 px-4 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-sm active:translate-y-0.5"
      >
        <Plus class="w-4 h-4" />
        编写新业务脚本
      </button>
    </div>

    <!-- Active workspace dashboard layout -->
    <div class="flex-1 flex flex-col lg:flex-row min-h-0 overflow-hidden">
      
      <!-- Left sidebar selector -->
      <div class="w-full lg:w-72 bg-white border-r border-slate-200 flex flex-col shrink-0">
        <div class="p-4 border-b border-slate-100 font-bold text-[10px] text-slate-400 uppercase tracking-widest text-left">
          脚本库目录 ({{ systemScripts.length }})
        </div>

        <div class="flex-1 overflow-y-auto divide-y divide-slate-100 text-left">
          <div 
            v-for="scr in systemScripts" 
            :key="scr.id"
            @click="selectedScriptId = scr.id"
            class="p-4 cursor-pointer hover:bg-slate-50/50 transition-all space-y-2 relative"
            :class="selectedScriptId === scr.id ? 'bg-indigo-50/20 text-indigo-600 border-r-4 border-r-indigo-600' : 'text-slate-700'"
          >
            <div class="flex items-start justify-between gap-1">
              <span class="font-bold text-xs leading-snug tracking-tight block max-w-[180px] break-words">
                {{ scr.name }}
              </span>

              <button 
                @click.stop="handleDeleteScript(scr.id, scr.name)"
                class="text-slate-400 hover:text-rose-600 p-0.5"
                title="删除脚本"
              >
                <Trash2 class="w-3.5 h-3.5" />
              </button>
            </div>

            <div class="flex items-center gap-2">
              <span class="text-[9px] font-bold px-1.5 py-0.5 rounded border"
                :class="scr.triggerType === 'auto' ? 'bg-emerald-50 text-emerald-600 border-emerald-100' : 'bg-slate-50 text-slate-500 border-slate-150'"
              >
                {{ scr.triggerType === 'auto' ? '自动运行 (' + scr.intervalSeconds + 's)' : '事件/手动触发' }}
              </span>

              <span class="text-[9px] font-bold px-1.5 py-0.5 rounded border"
                :class="scr.executionStatus === 'success' ? 'bg-emerald-500 text-white border-emerald-500' : scr.executionStatus === 'error' ? 'bg-rose-500 text-white border-rose-500' : scr.executionStatus === 'running' ? 'bg-indigo-500 text-white border-indigo-500 animate-pulse' : 'bg-slate-100 text-slate-400 border-slate-200'"
              >
                {{ scr.executionStatus === 'success' ? '执行正常' : scr.executionStatus === 'error' ? '常规异常' : scr.executionStatus === 'running' ? '编译解调' : '空闲中' }}
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- Right Side: Live code workspace and Terminal -->
      <div v-if="currentScriptObj" class="flex-1 flex flex-col min-w-0 bg-slate-900 border-l border-slate-950 relative">
        
        <!-- Editor Top Header status actions -->
        <div class="bg-slate-950/80 px-5 py-3 border-b border-slate-950 flex items-center justify-between text-white text-xs">
          <div class="flex items-center gap-2">
            <span class="w-2.5 h-2.5 rounded-full bg-rose-500" />
            <b class="font-sans text-slate-300">活跃编辑器:</b>
            <span class="font-bold text-[#1890ff] font-mono select-all">{{ currentScriptObj.name }}</span>
          </div>

          <div class="flex items-center gap-2 font-sans">
            <span class="text-[10px] text-slate-500 font-mono hidden sm:inline-block">Last: {{ currentScriptObj.lastExecuted || '无运行印记' }}</span>
            <button 
              @click="handleManualExecute"
              :disabled="currentScriptObj.executionStatus === 'running'"
              class="px-4 py-1 rounded bg-indigo-600 hover:bg-indigo-500 text-white font-bold inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 font-sans shadow-sm disabled:opacity-40"
            >
              <Play class="w-3.5 h-3.5" />
              立即运行验证
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
          <div class="h-48 border-t border-slate-950 bg-slate-950 flex flex-col min-h-[140px] shrink-0 text-left">
            <div class="bg-slate-900 border-b border-slate-950 px-4 py-1.5 flex items-center justify-between text-slate-400 font-mono text-[10px] select-none">
              <div class="flex items-center gap-1 text-[#1890ff] font-bold">
                <Terminal class="w-3.5 h-3.5" />
                <span>实时逻辑解调控制台 (Script Diagnostic Terminal)</span>
              </div>
              <span>Lines: {{ currentScriptObj.logOutput ? currentScriptObj.logOutput.split('\n').length : 0 }}</span>
            </div>

            <!-- Scroll output -->
            <div class="flex-1 overflow-y-auto p-4 space-y-1 font-mono text-[10.5px] text-slate-300 leading-relaxed max-w-full break-all select-all">
              <div v-for="(line, idx) in currentScriptObj.logOutput?.split('\n')" :key="idx" class="whitespace-pre-wrap">
                <span class="text-indigo-400 font-bold mr-1.5">>>></span> {{ line }}
              </div>
              <div v-if="!currentScriptObj.logOutput" class="text-slate-600 text-center py-6">
                等待点击 "立即运行验证" 捕获控制台输出重叠段...
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

      <div v-else class="flex-1 flex flex-col items-center justify-center text-slate-400 py-16 gap-3">
        <FileCode class="w-10 h-10 text-slate-300 animate-pulse" />
        <span>目录已被排空。创建您的第一段工业整编脚本。</span>
      </div>

    </div>

    <!-- ADD SCRIPT POPUP WINDOW -->
    <div v-if="showAddModal" class="fixed inset-0 bg-slate-900/40 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white rounded-xl shadow-xl border border-slate-100 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 text-white p-4 flex items-center justify-between">
          <div class="flex items-center gap-2 font-bold text-xs uppercase tracking-widest text-indigo-400">
            <Zap class="w-4 h-4" />
            <span>编写全新逻辑控制程序模块</span>
          </div>
          <button @click="showAddModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4.5 h-4.5" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs font-sans">
          
          <div>
            <label class="font-bold text-slate-500 block mb-1">控制脚本别称</label>
            <input 
              v-model="newScriptName"
              type="text"
              placeholder="如: 分拣履带主轴安全倾倒保护程序"
              class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 outline-none focus:border-[#1890ff]"
            />
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="font-bold text-slate-500 block mb-1">触发加载机制</label>
              <select 
                v-model="newScriptTriggerType"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 focus:outline-none font-bold"
              >
                <option value="manual">仅手动 / 计划任务调度</option>
                <option value="auto">后台自动间隔运行</option>
              </select>
            </div>

            <div v-if="newScriptTriggerType === 'auto'">
              <label class="font-bold text-slate-500 block mb-1">轮询时间间隔 (秒)</label>
              <input 
                v-model.number="newScriptInterval"
                type="number"
                min="1"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-1.5 focus:bg-white text-slate-800 font-mono outline-none font-bold"
              />
            </div>
          </div>

        </div>

        <div class="bg-slate-50 p-3 flex justify-end gap-2 border-t border-slate-100">
          <button 
            @click="showAddModal = false"
            class="px-3 py-1.5 rounded-lg border border-slate-200 bg-white hover:bg-slate-50 font-bold text-xs text-slate-600 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleCreateScript"
            class="px-4 py-1.5 rounded-lg bg-slate-900 hover:bg-slate-800 font-bold text-xs text-white cursor-pointer"
          >
            初始化代码模块
          </button>
        </div>

      </div>
    </div>

  </div>
</template>
