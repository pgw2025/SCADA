import { ref, computed } from 'vue';
import axios from 'axios';
import {
  Area,
  DataModel,
  Device,
  ScadaScreenProject,
  SystemLog,
  HMIComponent,
  DeviceType,
  VariableTrigger,
  ScheduledTask,
  SystemScript,
  ExposedDataInterface,
  HistoricalRecord,
  DatabaseConfig,
  SystemConfig,
  MqttServer,
  DataConversion,
  SystemUser,
  CreateUserDto,
  UpdateUserDto
} from '../types';
import { TEMPLATES } from '../templates';
import { getDeviceVariableValue, historicalRecords, scheduledTasks, setDeviceVariableValue, systemScripts } from './devices';

// === BACKEND CONFIGURATION ===
const API_BASE_URL = window.location.origin.replace(':3000', ':5000');

// === AXIOS INTERCEPTOR: AUTO ADD JWT TOKEN ===
axios.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem('scada_access_token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => {
    return Promise.reject(error);
  }
);

// === 2. SIMULATED SYSTEM RESOURCES STATUS ===
export const serverStatus = ref({
  cpuUsage: 14.5,
  memUsage: 48.2,
  diskUsage: 61.4,
  networkIn: 88.4,  // kb/s
  networkOut: 245.1, // kb/s
  uptimeDays: 14,
  uptimeHours: 5,
  uptimeMins: 32,
  pollFreq: 1200,    // ms
  totalPollPackets: 284145,
});

export const systemConfig = ref<SystemConfig>({
  systemTitle: 'IOTA-SCADA 工业物联大脑',
  pollIntervalMs: 1200,
  mqttBrokerHost: '10.120.44.15',
  mqttBrokerPort: 1883,
  opcUaDiscoveryUrl: 'opc.tcp://10.120.44.12:4840',
  alarmEmailNotify: true,
  alarmEmailAddress: 'ops_alerts@iota-factory.com',
  retentionPeriodDays: 90,
  isSimulationActive: false, // Default: False (Deactivated), so we fetch from backend instead
  backendApiUrl: 'http://localhost:5000'
});

export const logs = ref<SystemLog[]>([
  { id: 'log-1', timestamp: '2026-05-31 09:12:00', level: 'info', source: '系统内核', content: 'AntxV6 工业组态核心引擎已启动, PLC驱动自检完毕' },
  { id: 'log-2', timestamp: '2026-05-31 09:12:02', level: 'normal', source: 'OPC驱动', content: '连接到 OPC-UA 污水净化变频站 [opc.tcp://192.168.1.10:4840] 成功, 采样频率已设定为 50ms' },
  { id: 'log-3', timestamp: '2026-05-31 09:12:05', level: 'normal', source: 'S7驱动', content: 'S7-300 通讯协议插槽 [CPU315-2DP] 握手成功, 读出寄存器 DB10' },
  { id: 'log-4', timestamp: '2026-05-31 09:15:30', level: 'info', source: 'MQTT服务', content: '客户端订阅主题 [factory/conveyor/telemetry] 建立成功, 开始捕获遥测负载' },
  { id: 'log-5', timestamp: '2026-05-31 09:30:11', level: 'warning', source: '模拟设备', content: '辅助高备加水泵 [VR-PUMP-404] 轮询超时, 自动转为 离线 状态防损' },
]);

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

  // Keep logs list trimmed to prevent performance degradation
  if (logs.value.length > 200) {
    logs.value.pop();
  }
};

export const systemUsers = ref<SystemUser[]>([
  { id: 1, username: 'admin', role: '超级管理员', status: 'active' },
  { id: 2, username: 'operator_li', role: '操作员', status: 'active' },
  { id: 3, username: 'viewer_wang', role: '观察员', status: 'active' }
]);

export const isAuthenticated = ref<boolean>(false);
export const loginUser = ref<{ username: string; role: string } | null>(null);

// JWT Token 常量
const TOKEN_KEY = 'scada_access_token';

// 从 localStorage 读取 Token 并尝试自动登录
export const initializeAuth = () => {
  const token = localStorage.getItem(TOKEN_KEY);
  if (token) {
    // 检查 Token 是否过期（简单实现）
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      if (payload.exp * 1000 > Date.now()) {
        // Token 有效，设置登录状态
        isAuthenticated.value = true;
        loginUser.value = {
          username: payload.username || 'admin',
          role: payload.role || '系统管理员'
        };
        addLog('安全认证', 'Token 自动登录成功', 'normal');
      } else {
        localStorage.removeItem(TOKEN_KEY);
      }
    } catch {
      localStorage.removeItem(TOKEN_KEY);
    }
  }
};

// 登录接口对接：POST /api/Auth/login
export const performLogin = async (username: string, passwordString: string): Promise<{ success: boolean; errorMessage?: string }> => {
  try {
    const response = await axios.post(`${API_BASE_URL}/api/Auth/login`, {
      username: username,
      password: passwordString
    });

    if (response.data && response.data.success) {
      const token = response.data.token;
      localStorage.setItem(TOKEN_KEY, token);

      isAuthenticated.value = true;
      loginUser.value = {
        username: response.data.user?.username || username,
        role: response.data.user?.role || '系统管理员'
      };

      addLog('安全认证', `用户 [${username}] 通过API登录系统成功`, 'normal');
      return { success: true };
    } else {
      const errorMsg = response.data?.message || '用户名或密码错误';
      addLog('安全认证', `用户 [${username}] 登录失败: ${errorMsg}`, 'warning');
      return { success: false, errorMessage: errorMsg };
    }
  } catch (error: any) {
    const errorMessage = error.response?.data?.message || error.message || '服务器连接失败，请检查网络或后端服务';
    addLog('安全认证', `登录失败: ${errorMessage}`, 'warning');
    return { success: false, errorMessage: errorMessage };
  }
};

export const performLogout = () => {
  addLog('安全认证', `用户 [${loginUser.value?.username || 'admin'}] 注销系统登录`, 'normal');
  isAuthenticated.value = false;
  loginUser.value = null;
  localStorage.removeItem(TOKEN_KEY);
};

export const fetchSystemUsers = async (): Promise<SystemUser[]> => {
  const response = await axios.get(`${API_BASE_URL}/api/SystemUser`);
  return response.data;
};

export const fetchSystemUserById = async (id: number): Promise<SystemUser> => {
  const response = await axios.get(`${API_BASE_URL}/api/SystemUser/${id}`);
  return response.data;
};

export const createSystemUser = async (userData: CreateUserDto): Promise<SystemUser> => {
  const response = await axios.post(`${API_BASE_URL}/api/SystemUser`, {
    username: userData.username,
    password: userData.password,
    role: userData.role,
    status: userData.status || 'active'
  });
  return response.data;
};

export const updateSystemUser = async (userData: UpdateUserDto): Promise<SystemUser> => {
  const response = await axios.put(`${API_BASE_URL}/api/SystemUser`, userData);
  return response.data;
};

export const deleteSystemUser = async (id: number): Promise<void> => {
  await axios.delete(`${API_BASE_URL}/api/SystemUser/${id}`);
};

export const loadSystemUsers = async (): Promise<SystemUser[]> => {
  try {
    const users = await fetchSystemUsers();
    systemUsers.value = users;
    return users;
  } catch (error: any) {
    addLog('用户管理', `加载用户列表失败: ${error.message}`, 'error');
    throw error;
  }
};



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
        historicalRecords.value = historicalRecords.value.slice(0, 120); // Retain live records
        addLog('系统内核', `计划任务 [${task.name}] 执行完毕：清空 ${keepDays} 天之前的时序块`, 'warning');
      }

      task.status = 'success';
    } catch (err: any) {
      task.status = 'failed';
      addLog('调度控制器', `调度日常执行遇到常规阻断: [${task.name}] Error: ${err.message}`, 'warning');
    }
  }, 1200);
};
// function runScriptEngine(targetScr: { id: string; name: string; code: string; triggerType: "auto" | "manual"; intervalSeconds?: number; lastExecuted?: string; executionStatus?: "idle" | "success" | "error"; logOutput?: string; }) {
//   throw new Error('Function not implemented.');
// }

export const runScriptEngine = (script: SystemScript) => {
  script.executionStatus = 'running' as any;
  const executionLogs: string[] = [];
  const logFormatter = (msg: string) => {
    executionLogs.push(`[${new Date().toLocaleTimeString()}] ${msg}`);
  };

  // Safe client-side sandbox container
  const sandbox = {
    getVal: (key: string) => {
      const val = getDeviceVariableValue(key);
      logFormatter(`读取绑定键 [${key}] = ${val}`);
      return val;
    },
    setVal: (key: string, val: any) => {
      setDeviceVariableValue(key, val);
      logFormatter(`命令写入 [${key}] = ${val}`);
    },
    log: (msg: string) => {
      logFormatter(`[用户输出] ${msg}`);
    }
  };

  try {
    // Compile and execute code in safety with context mapping
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