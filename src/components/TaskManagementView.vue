<script setup lang="ts">
import { ref, computed } from 'vue';
import { 
  scheduledTasks, 
  executeTask, 
  systemScripts, 
  addLog, 
  dataModels 
} from '../store';
import { 
  Calendar, 
  Plus, 
  Trash2, 
  Play, 
  ToggleLeft, 
  ToggleRight, 
  Layers, 
  Database, 
  FileCode, 
  X, 
  CheckCircle2, 
  Loader2, 
  History,
  Clock
} from 'lucide-vue-next';
import { ScheduledTask } from '../types';

// Popup dialog form bindings
const showAddModal = ref(false);
const newTaskName = ref('');
const taskTypeSelected = ref<'set_value' | 'backup' | 'execute_script' | 'clear_history'>('backup');
const cronInput = ref('每分钟');
const selectedVarKey = ref('');
const targetWriteVal = ref(0);
const selectedScriptId = ref('');
const retentionDaysInput = ref(30);

// Get list of variables
const allVariables = computed(() => {
  const list: string[] = [];
  dataModels.value.forEach(m => {
    m.variables.forEach(v => {
      if (!list.includes(v.key)) list.push(v.key);
    });
  });
  return list;
});

const handleCreateTask = () => {
  if (!newTaskName.value.trim() || !cronInput.value.trim()) {
    alert('请完善调度任务基本信息！');
    return;
  }

  const newTask: ScheduledTask = {
    id: `task-${Date.now()}`,
    name: newTaskName.value.trim(),
    type: taskTypeSelected.value,
    cronExpression: cronInput.value.trim(),
    params: {
      variableKey: taskTypeSelected.value === 'set_value' ? selectedVarKey.value : undefined,
      newValue: taskTypeSelected.value === 'set_value' ? Number(targetWriteVal.value) : undefined,
      scriptId: taskTypeSelected.value === 'execute_script' ? selectedScriptId.value : undefined,
      retentionDays: taskTypeSelected.value === 'clear_history' ? Number(retentionDaysInput.value) : undefined
    },
    status: 'idle',
    active: true
  };

  scheduledTasks.value.push(newTask);
  addLog('任务调度', `成功部署新调度批处理任务: [${newTask.name}]`, 'normal');

  // Reset
  newTaskName.value = '';
  showAddModal.value = false;
};

const handleDeleteTask = (id: string, name: string) => {
  if (confirm(`确定要移除调度任务 [${name}] 吗？`)) {
    scheduledTasks.value = scheduledTasks.value.filter(t => t.id !== id);
    addLog('任务调度', `删除了后台任务: [${name}]`, 'warning');
  }
};

const triggerExecuteNow = (task: ScheduledTask) => {
  addLog('任务调度', `人工强制激活定时计划: [${task.name}]`, 'info');
  executeTask(task.id);
};

const handleToggleActiveTask = (task: ScheduledTask) => {
  task.active = !task.active;
  addLog('任务调度', `任务 [${task.name}] 已${task.active ? '激活' : '挂起停止'}`, task.active ? 'info' : 'warning');
};
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] select-none bg-slate-50">
    
    <!-- Top info row -->
    <div class="bg-white p-5 border-b border-slate-200 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 tracking-tight flex items-center gap-2">
          <Calendar class="w-5 h-5 text-indigo-500" />
          自动化任务与计划调度管理器
        </h2>
        <p class="text-xs text-slate-500 font-sans">
          支持设定时序数据清理、工业参数巡回重写、系统内核定期脚本启动和实时数据库导出异地冷备自动化任务。
        </p>
      </div>

      <button 
        @click="showAddModal = true; if(allVariables.length) selectedVarKey = allVariables[0]; if(systemScripts.length) selectedScriptId = systemScripts[0].id;"
        class="font-bold text-xs bg-indigo-600 text-white hover:bg-indigo-700 px-4 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-sm active:translate-y-0.5"
      >
        <Plus class="w-4 h-4" />
        挂载新调度任务
      </button>
    </div>

    <!-- Active tasks Grid list -->
    <div class="flex-1 p-6 overflow-y-auto space-y-4 text-left">
      <div class="grid grid-cols-1 xl:grid-cols-2 gap-4">
        
        <div 
          v-for="task in scheduledTasks" 
          :key="task.id"
          class="bg-white border rounded-xl p-5 shadow-xs flex flex-col justify-between gap-4 transition-all hover:shadow-md"
          :class="task.active ? 'border-slate-200/95' : 'border-slate-200/40 opacity-70 bg-slate-50/50'"
        >
          <!-- Task Header -->
          <div class="flex items-start justify-between gap-4">
            <div class="flex items-start gap-3">
              <!-- Custom Icon based on TYPE -->
              <div 
                class="w-10 h-10 rounded-lg flex items-center justify-center shrink-0"
                :class="{
                  'bg-sky-50 text-sky-600': task.type === 'set_value',
                  'bg-emerald-50 text-emerald-600': task.type === 'backup',
                  'bg-indigo-50 text-indigo-600': task.type === 'execute_script',
                  'bg-rose-50 text-rose-600': task.type === 'clear_history'
                }"
              >
                <Clock v-if="task.type === 'set_value'" class="w-5 h-5" />
                <Database v-else-if="task.type === 'backup'" class="w-5 h-5" />
                <FileCode v-else-if="task.type === 'execute_script'" class="w-5 h-5" />
                <History v-else class="w-5 h-5" />
              </div>

              <div>
                <h3 class="font-bold text-xs text-slate-900 leading-tight">{{ task.name }}</h3>
                <div class="flex items-center gap-2 mt-1.5 flex-wrap">
                  <!-- Cron timing -->
                  <span class="inline-flex items-center gap-1 bg-slate-100 text-slate-600 text-[10px] font-bold font-mono px-2 py-0.5 rounded-md">
                    <Clock class="w-3 h-3" />
                    {{ task.cronExpression }}
                  </span>
                  
                  <!-- Type -->
                  <span class="text-[9px] uppercase tracking-wider font-bold px-1.5 py-0.5 rounded border"
                    :class="{
                      'border-sky-200 text-sky-600 bg-sky-50/20': task.type === 'set_value',
                      'border-emerald-200 text-emerald-600 bg-emerald-50/20': task.type === 'backup',
                      'border-indigo-200 text-indigo-600 bg-indigo-50/20': task.type === 'execute_script',
                      'border-rose-200 text-rose-600 bg-rose-50/20': task.type === 'clear_history'
                    }"
                  >
                    {{ task.type === 'backup' ? '数据冷备' : task.type === 'set_value' ? '参数自整' : task.type === 'execute_script' ? '脚本轮询' : '时序物理落盘清理' }}
                  </span>
                </div>
              </div>
            </div>

            <!-- Enable Toggle Button -->
            <button 
              @click="handleToggleActiveTask(task)"
              class="focus:outline-none cursor-pointer transition-transform active:scale-95 text-slate-500"
            >
              <ToggleRight v-if="task.active" class="w-8 h-8 text-emerald-500" />
              <ToggleLeft v-else class="w-8 h-8 text-slate-300" />
            </button>
          </div>

          <!-- Parameter values print -->
          <div class="text-[11px] bg-slate-50 border border-slate-100 p-2.5 rounded-lg text-slate-600">
            <span class="font-bold block mb-1 text-slate-400">调度详细配置:</span>
            <div v-if="task.type === 'set_value'" class="font-mono">
              寄存器下写: <span class="text-[#1890ff] font-bold">{{ task.params.variableKey }}</span> 写入值 -> <mark class="bg-amber-100/60 px-1 font-sans rounded font-bold text-slate-800">{{ task.params.newValue }}</mark>
            </div>
            <div v-else-if="task.type === 'backup'">
              备份归档策略: 工业组态项目节点拓扑与设备配置一键导出 (.sql 文件，保留结构)
            </div>
            <div v-else-if="task.type === 'execute_script'" class="font-mono text-indigo-700 font-bold">
              执行脚本 ID: {{ task.params.scriptId }}
            </div>
            <div v-else-if="task.type === 'clear_history'">
              清理深度: 保留时序日志记录时间跨度 <b class="text-rose-600 px-1 font-bold font-sans">{{ task.params.retentionDays || 30 }}</b> 天，早期溢出块作安全销毁。
            </div>
          </div>

          <!-- Footer run actions -->
          <div class="flex items-center justify-between border-t border-slate-100 pt-3 text-[10px]">
            <div class="text-slate-400 font-mono flex items-center gap-1">
              <span>上次运行:</span>
              <span class="font-bold text-slate-600">{{ task.lastRun || '从未执行' }}</span>
            </div>

            <div class="flex items-center gap-2">
              <!-- Success status blinkers -->
              <div v-if="task.status === 'running'" class="inline-flex items-center gap-1 text-indigo-600 font-bold font-sans">
                <Loader2 class="w-3.5 h-3.5 animate-spin" />
                <span>运行中...</span>
              </div>
              <div v-else-if="task.status === 'success'" class="inline-flex items-center gap-1 text-emerald-600 font-bold">
                <CheckCircle2 class="w-3.5 h-3.5" />
                <span>上次成功</span>
              </div>
              <div v-else-if="task.status === 'failed'" class="inline-flex items-center gap-1 text-rose-600 font-bold">
                <X class="w-3.5 h-3.5" />
                <span>执行故障</span>
              </div>

              <!-- Manual bypass trigger -->
              <button 
                @click="triggerExecuteNow(task)"
                :disabled="!task.active || task.status === 'running'"
                class="px-2.5 py-1 text-[10px] font-bold border border-slate-200 hover:bg-slate-50 bg-white rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all disabled:opacity-45 disabled:cursor-not-allowed select-none"
              >
                <Play class="w-3 h-3 text-slate-500" />
                强制触发
              </button>

              <!-- Deletion -->
              <button 
                @click="handleDeleteTask(task.id, task.name)"
                class="p-1 hover:bg-rose-50 text-slate-400 hover:text-rose-500 rounded-lg cursor-pointer"
                title="删除定时任务"
              >
                <Trash2 class="w-3.5 h-3.5" />
              </button>
            </div>
          </div>

        </div>

      </div>
    </div>

    <!-- ADD TIMING CRON JOB DIALOG MODAL -->
    <div v-if="showAddModal" class="fixed inset-0 bg-slate-900/40 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white rounded-xl shadow-xl border border-slate-100 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <!-- Header -->
        <div class="bg-slate-900 text-white p-4 flex items-center justify-between">
          <div class="flex items-center gap-2 font-bold text-xs uppercase tracking-widest text-indigo-400">
            <Clock class="w-4 h-4" />
            <span>挂载新调度批处理自动化任务</span>
          </div>
          <button @click="showAddModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4.5 h-4.5" /></button>
        </div>

        <!-- Content -->
        <div class="p-5 space-y-4 text-xs font-sans">
          
          <!-- Task Name -->
          <div>
            <label class="font-bold text-slate-500 block mb-1">计划任务名称</label>
            <input 
              v-model="newTaskName"
              type="text"
              placeholder="如: 食品车间每日污水溢流安全巡检备份"
              class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 outline-none focus:border-[#1890ff]"
            />
          </div>

          <!-- Cron timing and types -->
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="font-bold text-slate-500 block mb-1">执行机制 (Cron / Time)</label>
              <select 
                v-model="cronInput"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 focus:outline-none font-sans font-bold"
              >
                <option value="每 5 秒自动触发">每 5 秒自动触发</option>
                <option value="每分钟间隔">每分钟间隔</option>
                <option value="每 15 分钟">每 15 分钟</option>
                <option value="每天凌晨 02:00:00">每天热休 02:00:00</option>
                <option value="每周日 00:00:00">每周日凌晨 00:00</option>
              </select>
            </div>

            <div>
              <label class="font-bold text-slate-500 block mb-1">任务运作类型</label>
              <select 
                v-model="taskTypeSelected"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 focus:outline-none font-sans font-bold"
              >
                <option value="backup">全库异地备份 (Backup)</option>
                <option value="set_value">写寄存器设定 (Set Val)</option>
                <option value="execute_script">后台执行脚本 (Script)</option>
                <option value="clear_history">老旧时序清扫 (Prune)</option>
              </select>
            </div>
          </div>

          <!-- Conditional param fields based on type selected -->
          <div v-if="taskTypeSelected === 'set_value'" class="bg-slate-50 rounded-lg p-3 border border-slate-150 space-y-2">
            <h4 class="font-bold text-slate-600 block mb-1">下发变量参数值</h4>
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-[10px] text-slate-400 block mb-0.5">目标物理变量Key</label>
                <select 
                  v-model="selectedVarKey"
                  class="w-full bg-white border border-slate-200 p-1 rounded font-mono"
                >
                  <option v-for="v in allVariables" :key="v" :value="v">{{ v }}</option>
                </select>
              </div>
              <div>
                <label class="text-[10px] text-slate-400 block mb-0.5">需要修改写入的数值</label>
                <input 
                  v-model.number="targetWriteVal"
                  type="number"
                  class="w-full bg-white border border-slate-200 p-1 rounded font-bold"
                />
              </div>
            </div>
          </div>

          <div v-else-if="taskTypeSelected === 'execute_script'" class="bg-indigo-50/50 rounded-lg p-3 border border-indigo-100">
            <label class="font-bold text-indigo-700 block mb-1">关联的系统脚本</label>
            <select 
              v-model="selectedScriptId"
              class="w-full bg-white border border-indigo-200 text-slate-800 p-2 rounded-lg font-bold outline-none"
            >
              <option v-for="scr in systemScripts" :key="scr.id" :value="scr.id">
                {{ scr.name }} ({{ scr.triggerType === 'auto' ? '自动' : '手动' }})
              </option>
              <option v-if="systemScripts.length === 0" disabled>无任何可用系统脚本，请先前往脚本中心创建</option>
            </select>
          </div>

          <div v-else-if="taskTypeSelected === 'clear_history'" class="bg-rose-50/50 rounded-lg p-3 border border-rose-100 flex items-center justify-between">
            <span class="font-bold text-rose-800">数据库清理时间边界</span>
            <div class="flex items-center gap-1 text-slate-700 font-bold">
              <span>保留</span>
              <input 
                v-model.number="retentionDaysInput"
                type="number"
                class="w-14 bg-white border border-rose-200 rounded p-1 text-center font-mono font-bold"
              />
              <span>天内数据</span>
            </div>
          </div>

          <div v-else-if="taskTypeSelected === 'backup'" class="text-[11px] leading-relaxed text-emerald-700 bg-emerald-50/50 border border-emerald-100 rounded-lg p-3">
            <b>备份动作：</b>系统将在到达预定时间点后，自锁当前指令线，安全压缩写入历史时序表及全局SCADA拖曳点位数据库，输出异地灾备的 ZIP 打包数据。
          </div>

        </div>

        <!-- Footer -->
        <div class="bg-slate-50 p-3 flex justify-end gap-2 border-t border-slate-100">
          <button 
            @click="showAddModal = false"
            class="px-3 py-1.5 rounded-lg border border-slate-200 bg-white hover:bg-slate-50 font-bold text-xs text-slate-600 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleCreateTask"
            class="px-4 py-1.5 rounded-lg bg-slate-900 hover:bg-slate-800 font-bold text-xs text-white cursor-pointer"
          >
            保存上架计划
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
