import { addLog } from './logService';
import { getDeviceVariableValue, setDeviceVariableValue } from '../services/dataOrchestration';
import { SystemScript } from '../types';

export const runScriptEngine = (script: SystemScript) => {
  script.executionStatus = 'running' as any;
  const executionLogs: string[] = [];
  const logFormatter = (msg: string) => {
    executionLogs.push(`[${new Date().toLocaleTimeString()}] ${msg}`);
  };

  const sandbox = {
    getVal: (key: string) => {
      const val = getDeviceVariableValue(null, key);
      logFormatter(`读取绑定键 [${key}] = ${val}`);
      return val;
    },
    setVal: (key: string, val: any) => {
      setDeviceVariableValue(null, key, val);
      logFormatter(`命令写入 [${key}] = ${val}`);
    },
    log: (msg: string) => {
      logFormatter(`[用户输出] ${msg}`);
    }
  };

  try {
    const executor = new Function('sandbox', `
      with (sandbox) {
        ${script.code}
      }
    `);
    executor(sandbox);
    script.executionStatus = 'success';
    script.logOutput = executionLogs.join('\n');
    const pad = (n: number) => n.toString().padStart(2, '0');
    const d = new Date();
    script.lastExecuted = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
  } catch (e: any) {
    script.executionStatus = 'error';
    executionLogs.push(`[编译执行故障] -> ${e.message}`);
    script.logOutput = executionLogs.join('\n');
  }
};
