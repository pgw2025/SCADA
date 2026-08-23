<script setup lang="ts">
import { ref, computed } from 'vue';
import { 
  scheduledTasks, 
  executeTask, 
  systemScripts, 
  addLog, 
  dataModels 
} from '../store/index';
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
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent">
    
    <!-- Top info row -->
    <div class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Calendar class="w-5 h-5 text-indigo-500 dark:text-indigo-400" />
          任务调度管理
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          配置定时执行的自动化任务，支持数据备份、变量写入、脚本执行和历史清理。
        </p>
      </div>

      <button 
        @click="showAddModal = true; if(allVariables.length) selectedVarKey = allVariables[0]; if(systemScripts.length) selectedScriptId = systemScripts[0].id;"
        class="font-bold text-xs bg-indigo-600 dark:bg-indigo-500 text-white hover:bg-indigo-700 dark:hover:bg-indigo-600 px-4 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-sm active:translate-y-0.5"
      >
        <Plus class="w-4 h-4" />
        新建任务
      </button>
    </div>

    <!-- Active tasks Grid list -->
    <div class="flex-1 p-6 overflow-y-auto space-y-4 text-left">
      <div class="grid grid-cols-1 xl:grid-cols-2 gap-4">
        
        <div 
          v-for="task in scheduledTasks" 
          :key="task.id"
          class="bg-white dark:bg-slate-900 border rounded-xl p-5 shadow-xs flex flex-col justify-between gap-4 transition-all hover:shadow-md"
          :class="task.active ? 'border-slate-200/95 dark:border-slate-800' : 'border-slate-200/40 dark:border-slate-800/40 opacity-70 bg-slate-50/50 dark:bg-slate-900/50'"
        >
          <!-- Task Header -->
          <div class="flex items-start justify-between gap-4">
            <div class="flex items-start gap-3">
              <!-- Custom Icon based on TYPE -->
              <div 
                class="w-10 h-10 rounded-lg flex items-center justify-center shrink-0"
                :class="{
                  'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400': task.type === 'set_value',
                  'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400': task.type === 'backup',
                  'bg-indigo-50 dark:bg-indigo-950/60 text-indigo-600 dark:text-indigo-400': task.type === 'execute_script',
                  'bg-rose-50 dark:bg-rose-950/60 text-rose-600 dark:text-rose-400': task.type === 'clear_history'
                }"
              >
                <Clock v-if="task.type === 'set_value'" class="w-5 h-5" />
                <Database v-else-if="task.type === 'backup'" class="w-5 h-5" />
                <FileCode v-else-if="task.type === 'execute_script'" class="w-5 h-5" />
                <History v-else class="w-5 h-5" />
              </div>

              <div>
                <h3 class="font-bold text-xs text-slate-900 dark:text-white leading-tight">{{ task.name }}</h3>
                <div class="flex items-center gap-2 mt-1.5 flex-wrap">
                  <!-- Cron timing -->
                  <span class="inline-flex items-center gap-1 bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 text-[10px] font-bold font-mono px-2 py-0.5 rounded-md">
                    <Clock class="w-3 h-3" />
                    {{ task.cronExpression }}
                  </span>
                  
                  <!-- Type -->
                  <span class="text-[9px] uppercase tracking-wider font-bold px-1.5 py-0.5 rounded border"
                    :class="{
                      'border-sky-200 dark:border-sky-800 text-sky-600 dark:text-sky-400 bg-sky-50/20 dark:bg-sky-950/20': task.type === 'set_value',
                      'border-emerald-200 dark:border-emerald-800 text-emerald-600 dark:text-emerald-400 bg-emerald-50/20 dark:bg-emerald-950/20': task.type === 'backup',
                      'border-indigo-200 dark:border-indigo-800 text-indigo-600 dark:text-indigo-400 bg-indigo-50/20 dark:bg-indigo-950/20': task.type === 'execute_script',
                      'border-rose-200 dark:border-rose-800 text-rose-600 dark:text-rose-400 bg-rose-50/20 dark:bg-rose-950/20': task.type === 'clear_history'
                    }"
                  >
                    {{ task.type === 'backup' ? '数据备份' : task.type === 'set_value' ? '变量写入' : task.type === 'execute_script' ? '脚本执行' : '历史清理' }}
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
              <ToggleLeft v-else class="w-8 h-8 text-slate-300 dark:text-slate-600" />
            </button>
          </div>

          <!-- Parameter values print -->
          <div class="text-[11px] bg-slate-50 dark:bg-slate-950 border border-slate-100 dark:border-slate-800 p-2.5 rounded-lg text-slate-600 dark:text-slate-300">
            <span class="font-bold block mb-1 text-slate-400 dark:text-slate-500">任务配置:</span>
            <div v-if="task.type === 'set_value'" class="font-mono">
              变量: <span class="text-[#1890ff] font-bold">{{ task.params.variableKey }}</span> → 值: <mark class="bg-amber-100/60 dark:bg-amber-950/80 px-1 font-sans rounded font-bold text-slate-800 dark:text-amber-200">{{ task.params.newValue }}</mark>
            </div>
            <div v-else-if="task.type === 'backup'">
              导出系统配置和时序数据到备份文件
            </div>
            <div v-else-if="task.type === 'execute_script'" class="font-mono text-indigo-700 dark:text-indigo-400 font-bold">
              脚本 ID: {{ task.params.scriptId }}
            </div>
            <div v-else-if="task.type === 'clear_history'">
              保留 <b class="text-rose-600 dark:text-rose-400 px-1 font-bold font-sans">{{ task.params.retentionDays || 30 }}</b> 天内数据，超出部分自动清理
            </div>
          </div>

          <!-- Footer run actions -->
          <div class="flex items-center justify-between border-t border-slate-100 dark:border-slate-800 pt-3 text-[10px]">
            <div class="text-slate-400 dark:text-slate-500 font-mono flex items-center gap-1">
              <span>上次运行:</span>
              <span class="font-bold text-slate-600 dark:text-slate-300">{{ task.lastRun || '从未执行' }}</span>
            </div>

            <div class="flex items-center gap-2">
              <!-- Success status blinkers -->
              <div v-if="task.status === 'running'" class="inline-flex items-center gap-1 text-indigo-600 dark:text-indigo-400 font-bold font-sans">
                <Loader2 class="w-3.5 h-3.5 animate-spin" />
                <span>执行中</span>
              </div>
              <div v-else-if="task.status === 'success'" class="inline-flex items-center gap-1 text-emerald-600 dark:text-emerald-400 font-bold">
                <CheckCircle2 class="w-3.5 h-3.5" />
                <span>成功</span>
              </div>
              <div v-else-if="task.status === 'failed'" class="inline-flex items-center gap-1 text-rose-600 dark:text-rose-400 font-bold">
                <X class="w-3.5 h-3.5" />
                <span>失败</span>
              </div>

              <!-- Manual bypass trigger -->
              <button 
                @click="triggerExecuteNow(task)"
                :disabled="!task.active || task.status === 'running'"
                class="px-2.5 py-1 text-[10px] font-bold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 bg-white dark:bg-slate-900 text-slate-700 dark:text-slate-300 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all disabled:opacity-45 disabled:cursor-not-allowed select-none"
              >
                <Play class="w-3 h-3 text-slate-500 dark:text-slate-400" />
                立即执行
              </button>

              <!-- Deletion -->
              <button 
                @click="handleDeleteTask(task.id, task.name)"
                class="p-1 hover:bg-rose-50 dark:hover:bg-rose-950/40 text-slate-400 hover:text-rose-500 rounded-lg cursor-pointer transition-colors"
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
    <div v-if="showAddModal" class="fixed inset-0 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <!-- Header -->
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-2 font-bold text-xs uppercase tracking-widest text-indigo-400">
            <Clock class="w-4 h-4" />
            <span>新建调度任务</span>
          </div>
          <button @click="showAddModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4.5 h-4.5" /></button>
        </div>

        <!-- Content -->
        <div class="p-5 space-y-4 text-xs font-sans">
          
          <!-- Task Name -->
          <div>
            <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">任务名称</label>
            <input 
              v-model="newTaskName"
              type="text"
              placeholder="如: 每日数据备份"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white outline-none focus:border-[#1890ff]"
            />
          </div>

          <!-- Cron timing and types -->
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">执行周期</label>
              <select 
                v-model="cronInput"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white focus:outline-none font-sans font-bold"
              >
                <option value="每 5 秒自动触发">每 5 秒</option>
                <option value="每分钟间隔">每分钟</option>
                <option value="每 15 分钟">每 15 分钟</option>
                <option value="每天凌晨 02:00:00">每天 02:00</option>
                <option value="每周日 00:00:00">每周日 00:00</option>
              </select>
            </div>

            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">任务类型</label>
              <select 
                v-model="taskTypeSelected"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white focus:outline-none font-sans font-bold"
              >
                <option value="backup">数据备份</option>
                <option value="set_value">变量写入</option>
                <option value="execute_script">脚本执行</option>
                <option value="clear_history">历史清理</option>
              </select>
            </div>
          </div>

          <!-- Conditional param fields based on type selected -->
          <div v-if="taskTypeSelected === 'set_value'" class="bg-slate-50 dark:bg-slate-950 rounded-lg p-3 border border-slate-150 dark:border-slate-800 space-y-2">
            <h4 class="font-bold text-slate-600 dark:text-slate-300 block mb-1">变量配置</h4>
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-[10px] text-slate-400 dark:text-slate-500 block mb-0.5">目标变量</label>
                <select 
                  v-model="selectedVarKey"
                  class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-slate-800 dark:text-white p-1 rounded font-mono"
                >
                  <option v-for="v in allVariables" :key="v" :value="v">{{ v }}</option>
                </select>
              </div>
              <div>
                <label class="text-[10px] text-slate-400 dark:text-slate-500 block mb-0.5">写入值</label>
                <input 
                  v-model.number="targetWriteVal"
                  type="number"
                  class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-slate-800 dark:text-white p-1 rounded font-bold"
                />
              </div>
            </div>
          </div>

          <div v-else-if="taskTypeSelected === 'execute_script'" class="bg-indigo-50/50 dark:bg-indigo-950/40 rounded-lg p-3 border border-indigo-100 dark:border-indigo-900/40">
            <label class="font-bold text-indigo-700 dark:text-indigo-400 block mb-1">选择脚本</label>
            <select 
              v-model="selectedScriptId"
              class="w-full bg-white dark:bg-slate-900 border border-indigo-200 dark:border-indigo-800 text-slate-800 dark:text-white p-2 rounded-lg font-bold outline-none"
            >
              <option v-for="scr in systemScripts" :key="scr.id" :value="scr.id">
                {{ scr.name }} ({{ scr.triggerType === 'auto' ? '定时' : '手动' }})
              </option>
              <option v-if="systemScripts.length === 0" disabled>暂无可用脚本</option>
            </select>
          </div>

          <div v-else-if="taskTypeSelected === 'clear_history'" class="bg-rose-50/50 dark:bg-rose-950/40 rounded-lg p-3 border border-rose-100 dark:border-rose-900/40 flex items-center justify-between">
            <span class="font-bold text-rose-800 dark:text-rose-300">数据保留期限</span>
            <div class="flex items-center gap-1 text-slate-700 dark:text-slate-300 font-bold">
              <span>保留</span>
              <input 
                v-model.number="retentionDaysInput"
                type="number"
                class="w-14 bg-white dark:bg-slate-900 border border-rose-200 dark:border-rose-800 text-slate-800 dark:text-white rounded p-1 text-center font-mono font-bold"
              />
              <span>天</span>
            </div>
          </div>

          <div v-else-if="taskTypeSelected === 'backup'" class="text-[11px] leading-relaxed text-emerald-700 dark:text-emerald-400 bg-emerald-50/50 dark:bg-emerald-950/30 border border-emerald-100 dark:border-emerald-900/40 rounded-lg p-3">
            <b>备份内容：</b>系统配置、时序数据和设备点位信息将被打包导出。
          </div>

        </div>

        <!-- Footer -->
        <div class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800">
          <button 
            @click="showAddModal = false"
            class="px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleCreateTask"
            class="px-4 py-1.5 rounded-lg bg-slate-900 dark:bg-indigo-600 hover:bg-slate-800 dark:hover:bg-indigo-500 font-bold text-xs text-white cursor-pointer"
          >
            创建任务
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
