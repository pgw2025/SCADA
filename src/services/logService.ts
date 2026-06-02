import { ref } from 'vue';
import { SystemLog } from '../types';

export const logs = ref<SystemLog[]>([]);

export const addLog = (source: string, content: string, level: 'info' | 'warning' | 'normal' = 'info') => {
  const pad = (n: number) => n.toString().padStart(2, '0');
  const d = new Date();
  const timeStr = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;

  logs.value.unshift({
    id: `log-${Date.now()}`,
    timestamp: timeStr,
    level,
    source,
    content
  });

  if (logs.value.length > 200) {
    logs.value.pop();
  }
};
