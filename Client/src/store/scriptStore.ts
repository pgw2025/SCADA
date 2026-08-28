import { ref } from 'vue';
import { ScriptExecutionEvent } from '../types';

/**
 * 脚本执行事件实时缓冲（SignalR ReceiveScriptExecution → push 到这里）。
 * 系统脚本页订阅最近事件以实时刷新状态角标与控制台；仅保留最近 200 条，防内存积压。
 */
export const scriptExecutionEvents = ref<ScriptExecutionEvent[]>([]);

export const pushScriptExecutionEvent = (e: ScriptExecutionEvent) => {
  if (!e || e.scriptId == null) return;
  scriptExecutionEvents.value.unshift(e);
  if (scriptExecutionEvents.value.length > 200) {
    scriptExecutionEvents.value.pop();
  }
};