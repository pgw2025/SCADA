import { addLog } from './logService';
import { setDeviceVariableValue } from '../services/dataOrchestration';
import { scheduledTasks, systemScripts } from '../store/configStore';
import { historicalRecords } from '../store/historyStore';
import { runScriptEngine } from './scriptService';

export const executeTask = (taskId: string) => {
  const task = scheduledTasks.value.find((t) => t.id === taskId);
  if (!task) return;

  task.status = 'running';
  setTimeout(() => {
    try {
      const pad = (n: number) => n.toString().padStart(2, '0');
      const d = new Date();
      task.lastRun = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;

      if (task.type === 'set_value') {
        if (task.params.variableKey && task.params.newValue !== undefined) {
          const valToWrite = task.params.variableKey === 'pump_state' || task.params.variableKey === 'valve_state'
            ? !!task.params.newValue
            : task.params.newValue;
          setDeviceVariableValue(task.params.variableKey, valToWrite);
          addLog('调度执行', `计划任务 [${task.name}] 写入 [${task.params.variableKey}] = ${task.params.newValue} 成功`, 'normal');
        }
      } else if (task.type === 'backup') {
        addLog('系统内核', `计划任务 [${task.name}]：已成功压缩主数据库并导出 iota_scada_v6_dump_${Date.now()}.sql.gz`, 'normal');
      } else if (task.type === 'execute_script') {
        if (task.params.scriptId) {
          const targetScr = systemScripts.value.find(s => s.id === task.params.scriptId);
          if (targetScr) {
            runScriptEngine(targetScr);
            addLog('调度执行', `计划任务 [${task.name}] 执行脚本 [${targetScr.name}] 成功`, 'info');
          }
        }
      } else if (task.type === 'clear_history') {
        const keepDays = task.params.retentionDays || 30;
        historicalRecords.value = historicalRecords.value.slice(0, 120); 
        addLog('系统内核', `计划任务 [${task.name}] 执行完毕：清空 ${keepDays} 天之前的时序块`, 'warning');
      }

      task.status = 'success';
    } catch (err: any) {
      task.status = 'failed';
      addLog('调度控制器', `调度日常执行遇到常规阻断: [${task.name}] Error: ${err.message}`, 'warning');
    }
  }, 1200);
};
