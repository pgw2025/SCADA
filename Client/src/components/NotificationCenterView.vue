<script setup lang="ts">
import { onMounted, reactive, ref, computed } from 'vue';
import {
  MessageSquare,
  Mail,
  Bell,
  Save,
  Send,
  RefreshCw,
  Plus,
  Trash2,
  CheckCircle2,
  AlertTriangle,
  AlertCircle,
  Eye,
  EyeOff,
  Clock,
  Activity,
  Smartphone,
  Laptop,
  Check,
  RotateCw,
  Search,
  Filter,
  Layers,
  Sparkles,
  Sliders,
  ExternalLink,
  Code
} from 'lucide-vue-next';
import { addLog } from '../store/index';
import { showToast } from '../services/toastService';
import {
  fetchNotificationConfig,
  saveNotificationConfig,
  testDingTalk,
  testEmail,
  testWeCom,
  fetchNotificationLogs,
  clearNotificationLogs,
  retryNotificationLog,
  NotificationConfig,
  NotificationTemplates,
  NotificationLogItem
} from '../api/notificationApi';

// Active navigation tab: channels | policy | templates | logs
const activeTab = ref<'channels' | 'policy' | 'templates' | 'logs'>('channels');

const loading = ref(true);
const isSaving = ref(false);
const testingDing = ref(false);
const testingEmail = ref(false);
const testingWeCom = ref(false);
const saveSuccess = ref(false);

// Test results banner
const dingTestResult = ref<{ success: boolean; message: string; latencyMs?: number } | null>(null);
const emailTestResult = ref<{ success: boolean; message: string; latencyMs?: number } | null>(null);
const weComTestResult = ref<{ success: boolean; message: string; latencyMs?: number } | null>(null);

// Sensitive fields visibility toggle
const showDingSecret = ref(false);
const showEmailPassword = ref(false);

// Recipient input buffer
const newRecipientInput = ref('');

// Delivery Logs state
const logs = ref<NotificationLogItem[]>([]);
const logsLoading = ref(false);
const logFilterChannel = ref<'all' | 'dingTalk' | 'weCom' | 'email' | 'webPush'>('all');
const logFilterStatus = ref<'all' | 'Success' | 'Failed' | 'Retrying'>('all');
const logSearchQuery = ref('');
const selectedLogDetail = ref<NotificationLogItem | null>(null);
const retryingLogId = ref<number | string | null>(null);

// Template management
const activeTemplateKey = ref<keyof NotificationTemplates>('alarmTriggered');
const templateFormatMode = ref<'markdown' | 'html'>('markdown');
const previewMode = ref<'dingtalk' | 'email'>('dingtalk');

// Textarea ref for inserting variables
const markdownTextareaRef = ref<HTMLTextAreaElement | null>(null);
const htmlTextareaRef = ref<HTMLTextAreaElement | null>(null);

// Template metadata & variable descriptions
const templateMeta: {
  key: keyof NotificationTemplates;
  label: string;
  desc: string;
  placeholders: { tag: string; label: string }[];
}[] = [
  {
    key: 'alarmTriggered',
    label: '报警触发',
    desc: '遥测模拟量或开关量超出设定阈值时发出',
    placeholders: [
      { tag: 'deviceKey', label: '设备编码' },
      { tag: 'deviceId', label: '设备编号' },
      { tag: 'variableName', label: '监测变量' },
      { tag: 'variableKey', label: '变量标识' },
      { tag: 'ruleName', label: '报警规则' },
      { tag: 'level', label: '告警级别' },
      { tag: 'condition', label: '判定条件' },
      { tag: 'threshold', label: '设定阈值' },
      { tag: 'actualValue', label: '实测数据' },
      { tag: 'source', label: '数据源' },
      { tag: 'message', label: '告警描述' },
      { tag: 'time', label: '触发时间' }
    ]
  },
  {
    key: 'alarmRecovered',
    label: '报警恢复',
    desc: '遥测数值回落至安全区间，自动解除告警时发出',
    placeholders: [
      { tag: 'deviceKey', label: '设备编码' },
      { tag: 'deviceId', label: '设备编号' },
      { tag: 'variableName', label: '监测变量' },
      { tag: 'variableKey', label: '变量标识' },
      { tag: 'ruleName', label: '报警规则' },
      { tag: 'level', label: '告警级别' },
      { tag: 'condition', label: '判定条件' },
      { tag: 'threshold', label: '安全阈值' },
      { tag: 'actualValue', label: '当前实测' },
      { tag: 'source', label: '数据源' },
      { tag: 'message', label: '恢复说明' },
      { tag: 'time', label: '解除时间' }
    ]
  },
  {
    key: 'deviceStatus',
    label: '设备状态',
    desc: '现场控制器/PLC通信心跳上线或离线时发出',
    placeholders: [
      { tag: 'status', label: '通信状态' },
      { tag: 'deviceId', label: '设备编号' },
      { tag: 'deviceKey', label: '设备编码' },
      { tag: 'time', label: '状态变动时间' }
    ]
  },
  {
    key: 'systemAlarm',
    label: '系统报警',
    desc: 'SCADA采集引擎、总线或内部测点出现异常时发出',
    placeholders: [
      { tag: 'deviceId', label: '设备编号' },
      { tag: 'variableName', label: '变量名称' },
      { tag: 'variableKey', label: '变量标识' },
      { tag: 'level', label: '异常级别' },
      { tag: 'message', label: '异常报文' },
      { tag: 'time', label: '记录时间' }
    ]
  },
  {
    key: 'systemError',
    label: '系统故障',
    desc: '系统底层模块通信失败、驱动崩溃或网络中断',
    placeholders: [
      { tag: 'level', label: '故障严重度' },
      { tag: 'source', label: '故障源模块' },
      { tag: 'content', label: '异常堆栈' },
      { tag: 'time', label: '故障时间' }
    ]
  },
  {
    key: 'scriptExecution',
    label: '脚本异常',
    desc: '自动化逻辑或定时连锁控制脚本运行中断报错时发出',
    placeholders: [
      { tag: 'scriptId', label: '脚本编号' },
      { tag: 'scriptVersion', label: '脚本版本' },
      { tag: 'triggerSource', label: '触发源' },
      { tag: 'result', label: '执行结果' },
      { tag: 'error', label: '报错详情' },
      { tag: 'durationMs', label: '耗时(毫秒)' },
      { tag: 'time', label: '执行时间' }
    ]
  }
];

// Realistic industrial sample data for live simulator preview
const sampleData: Record<string, Record<string, string>> = {
  alarmTriggered: {
    deviceKey: 'DEV_PUMP_01',
    deviceId: '1',
    variableName: '出水总管压力',
    variableKey: 'Pressure',
    ruleName: '管网高压保护报警',
    level: 'Critical (严重超限)',
    condition: '>',
    threshold: '0.80 MPa',
    actualValue: '0.94 MPa',
    source: '西门子 S7-1500 PLC DB1.DBD2',
    message: '管网瞬时压力达到 0.94 MPa，已超出额定保护上限 0.80 MPa！',
    time: '2026-09-05 12:20:18'
  },
  alarmRecovered: {
    deviceKey: 'DEV_PUMP_01',
    deviceId: '1',
    variableName: '出水总管压力',
    variableKey: 'Pressure',
    ruleName: '管网高压保护报警',
    level: 'Normal',
    condition: '<=',
    threshold: '0.80 MPa',
    actualValue: '0.42 MPa',
    source: '西门子 S7-1500 PLC DB1.DBD2',
    message: '增压泵组变频卸荷完成，管网压力回落至 0.42 MPa 额定工况。',
    time: '2026-09-05 12:25:04'
  },
  deviceStatus: {
    deviceId: '1',
    deviceKey: 'DEV_PUMP_01',
    status: 'Offline (通讯心跳丢失)',
    time: '2026-09-05 12:28:10'
  },
  systemAlarm: {
    deviceId: '2',
    variableName: '反渗透储水罐液位计',
    variableKey: 'Level',
    level: 'Warning',
    message: '4-20mA 模拟量信号波动异常，存在接地噪声干扰',
    time: '2026-09-05 12:30:15'
  },
  systemError: {
    level: 'Fatal',
    source: 'OPC_UA_Client_Driver',
    content: 'SocketException: Connection refused at opc.tcp://192.168.2.20:4840 (TCP reset by peer)',
    time: '2026-09-05 12:32:00'
  },
  scriptExecution: {
    scriptId: '1',
    scriptVersion: '1.2.0',
    triggerSource: '周期调度 (每5秒)',
    result: 'Failed',
    error: 'ReferenceError: variables.Flow is undefined at line 3:5',
    durationMs: '18',
    time: '2026-09-05 12:35:40'
  }
};

const emptyTemplate = () => ({ title: '', markdown: '', htmlBody: '' });

const defaultTemplates: NotificationTemplates = {
  alarmTriggered: {
    title: '⚠️【SCADA告警】{deviceKey} 触发 {ruleName}',
    markdown: `### ⚠️ SCADA工业监控系统 - 阈值报警
**设备编码**：{deviceKey} (ID: #{deviceId})  
**监测指标**：{variableName} ({variableKey})  
**报警级别**：<font color="#ef4444">{level}</font>  
**报警判据**：实测值 **{actualValue}** {condition} 阈值 **{threshold}**  
**详细描述**：{message}  
**告警时间**：{time}  
> 🔔 请区域值班人员与自动化工程师尽快登录SCADA查看现场工况！`,
    htmlBody: `<div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; border: 1px solid #e2e8f0; border-radius: 8px; overflow: hidden; max-width: 600px; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.06);">
  <div style="background: #dc2626; color: #ffffff; padding: 14px 20px; font-weight: bold; font-size: 16px;">
    ⚠️ SCADA 工业设备越限报警通知
  </div>
  <div style="padding: 20px; background: #ffffff; color: #1e293b; font-size: 14px; line-height: 1.7;">
    <p><strong>设备名称/编码：</strong><span style="font-family: monospace; color: #0284c7;">{deviceKey}</span> (设备 #{deviceId})</p>
    <p><strong>监测参数：</strong>{variableName} ({variableKey})</p>
    <p><strong>告警级别：</strong><span style="color: #dc2626; font-weight: bold;">{level}</span></p>
    <p><strong>判定条件：</strong>实测值 <span style="color: #dc2626; font-weight: bold; font-size: 16px;">{actualValue}</span> {condition} 预设阈值 <strong>{threshold}</strong></p>
    <div style="background: #fef2f2; border-left: 4px solid #ef4444; padding: 10px 14px; margin: 14px 0; color: #991b1b; font-size: 13px;">
      <strong>现场诊断提示：</strong>{message}
    </div>
    <div style="color: #64748b; font-size: 12px; margin-top: 18px; border-top: 1px solid #f1f5f9; padding-top: 10px;">
      报警发生时间：{time} &nbsp;|&nbsp; 数据源：{source}
    </div>
  </div>
</div>`
  },
  alarmRecovered: {
    title: '✅【SCADA恢复】{deviceKey} 报警已解除',
    markdown: `### ✅ SCADA工业监控系统 - 告警自动恢复
**设备编码**：{deviceKey} (ID: #{deviceId})  
**恢复测点**：{variableName} ({variableKey})  
**当前实测**：<font color="#10b981">{actualValue}</font> (已脱离异常阈值 {threshold})  
**解除信息**：{message}  
**恢复时间**：{time}  
> 🟢 系统巡检检测到工况已恢复至设定安全区间。`,
    htmlBody: `<div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; border: 1px solid #e2e8f0; border-radius: 8px; overflow: hidden; max-width: 600px;">
  <div style="background: #10b981; color: #ffffff; padding: 14px 20px; font-weight: bold; font-size: 16px;">
    ✅ SCADA 工业设备告警解除通知
  </div>
  <div style="padding: 20px; background: #ffffff; color: #1e293b; font-size: 14px; line-height: 1.7;">
    <p><strong>设备名称/编码：</strong><span style="font-family: monospace; color: #0284c7;">{deviceKey}</span> (设备 #{deviceId})</p>
    <p><strong>恢复测点：</strong>{variableName} ({variableKey})</p>
    <p><strong>当前回测值：</strong><span style="color: #10b981; font-weight: bold;">{actualValue}</span> (安全上限 {threshold})</p>
    <p><strong>系统状态：</strong>{message}</p>
    <p style="color: #64748b; font-size: 12px; margin-top: 16px; border-top: 1px solid #f1f5f9; padding-top: 10px;">恢复时间：{time} &nbsp;|&nbsp; 来源：{source}</p>
  </div>
</div>`
  },
  deviceStatus: {
    title: '📡【通信状态】设备 {deviceId} 状态变更为 {status}',
    markdown: `### 📡 工业现场通信状态变动
**设备编号**：#{deviceId}  
**通信状态**：**{status}**  
**检测时间**：{time}  
> 💡 工业物联网网关检测到控制器链路心跳变化。`,
    htmlBody: `<div style="font-family: sans-serif; padding: 16px; border: 1px solid #e2e8f0; border-radius: 6px;">
  <h3 style="margin-top:0; color: #0284c7;">📡 设备通信状态报告</h3>
  <p>设备 <strong>#{deviceId}</strong> 通信状态已切换为：<strong>{status}</strong></p>
  <p style="color: #64748b; font-size: 12px;">时间戳：{time}</p>
</div>`
  },
  systemAlarm: {
    title: '🚨【系统报警】测点异常: {variableName}',
    markdown: `### 🚨 SCADA平台内核异常告警
**关联节点**：#{deviceId} - {variableName} ({variableKey})  
**告警级别**：{level}  
**告警报文**：{message}  
**时间**：{time}`,
    htmlBody: `<div style="font-family: sans-serif; padding: 16px; border: 1px solid #fca5a5; background: #fff1f2; border-radius: 6px;">
  <h3 style="color: #be123c; margin-top:0;">🚨 SCADA 系统内部告警</h3>
  <p><strong>节点：</strong>#{deviceId} - {variableName} ({variableKey})</p>
  <p><strong>级别：</strong>{level}</p>
  <p><strong>详情：</strong>{message}</p>
  <p style="color: #64748b; font-size: 12px;">记录时间：{time}</p>
</div>`
  },
  systemError: {
    title: '❌【系统故障】模块 {source} 发生异常',
    markdown: `### ❌ SCADA网关/通信驱动故障
**错误源模块**：{source}  
**严重程度**：{level}  
**错误堆栈/摘要**：{content}  
**发生时间**：{time}`,
    htmlBody: `<div style="font-family: monospace; padding: 16px; background: #1e293b; color: #f87171; border-radius: 6px;">
  <h4 style="margin: 0 0 8px; color: #fbbf24;">[SYSTEM_ERROR] {source}</h4>
  <p style="margin: 0; white-space: pre-wrap;">{content}</p>
  <div style="margin-top: 10px; color: #94a3b8; font-size: 12px;">Time: {time} | Level: {level}</div>
</div>`
  },
  scriptExecution: {
    title: '⚡【脚本控制】执行脚本 #{scriptId} 异常中断',
    markdown: `### ⚡ SCADA自动化策略脚本异常
**脚本编号**：#{scriptId} (版本: v{scriptVersion})  
**触发源**：{triggerSource}  
**执行耗时**：{durationMs}ms  
**执行结果**：{result}  
**捕获异常**：{error}  
**记录时间**：{time}`,
    htmlBody: `<div style="font-family: sans-serif; padding: 16px; border: 1px solid #fde047; background: #fefce8; border-radius: 6px;">
  <h3 style="color: #a16207; margin-top:0;">⚡ 自动化控制脚本异常报告</h3>
  <p><strong>脚本：</strong>#{scriptId} (v{scriptVersion}) &nbsp;|&nbsp; <strong>触发源：</strong>{triggerSource}</p>
  <p><strong>耗时：</strong>{durationMs} ms</p>
  <p style="color: #dc2626;"><strong>报错信息：</strong>{error}</p>
  <p style="color: #713f12; font-size: 12px;">时间：{time}</p>
</div>`
  }
};

const form = reactive<NotificationConfig>({
  dingTalk: {
    enabled: true,
    webhook: 'https://oapi.dingtalk.com/robot/send?access_token=6e9f5a7c2b3d1e4f8a0b9c8d7e6f5a4b3c2d1e0f',
    secret: '******',
    hasSecret: true
  },
  email: {
    enabled: true,
    smtpHost: 'smtp.qiye.aliyun.com',
    smtpPort: 465,
    useSsl: true,
    username: 'alert_service@iota-factory.com',
    password: '******',
    hasPassword: true,
    from: 'alert_service@iota-factory.com',
    fromName: '晋鑫SCADA工业监控中心',
    to: ['duty_engineer@iota-factory.com', 'workshop_supervisor@iota-factory.com']
  },
  weCom: {
    enabled: false,
    webhook: ''
  },
  push: {
    pushAlarm: true,
    pushDeviceOffline: true,
    pushDeviceOnline: true,
    deviceStatusDebounceMinutes: 5,
    pushSystemAlarm: true,
    pushSystemError: true,
    pushScript: true,
    maxPerMinutePerChannel: 15,
    maxAttempts: 3,
    retryBaseDelayMs: 1500,
    queueCapacity: 2048
  },
  templates: JSON.parse(JSON.stringify(defaultTemplates))
});

onMounted(async () => {
  loading.value = true;
  try {
    const res = await fetchNotificationConfig();
    if (res) {
      if (res.dingTalk) Object.assign(form.dingTalk, res.dingTalk);
      if (res.email) Object.assign(form.email, res.email);
      if (res.weCom) Object.assign(form.weCom, res.weCom);
      if (res.push) Object.assign(form.push, res.push);
      if (res.templates) {
        templateMeta.forEach(m => {
          if ((res.templates as any)?.[m.key]) {
            Object.assign((form.templates as any)[m.key], (res.templates as any)[m.key]);
          }
        });
      }
    }
  } catch {
    // 默认内置兜底
  } finally {
    loading.value = false;
  }
  loadDeliveryLogs();
});

const loadDeliveryLogs = async () => {
  logsLoading.value = true;
  try {
    const res = await fetchNotificationLogs();
    if (Array.isArray(res)) {
      logs.value = res;
    }
  } catch {
    // 忽略加载错误
  } finally {
    logsLoading.value = false;
  }
};

const handleClearLogs = async () => {
  try {
    await clearNotificationLogs();
    logs.value = [];
    showToast('投递历史记录已清空', 'success');
  } catch {
    showToast('清空失败', 'error');
  }
};

const handleRetryLog = async (logItem: NotificationLogItem) => {
  retryingLogId.value = logItem.id;
  try {
    const res = await retryNotificationLog(logItem.id);
    logItem.status = res.status;
    logItem.latencyMs = res.latencyMs;
    logItem.timestamp = res.timestamp;
    delete logItem.error;
    showToast(`消息 #${logItem.id} 重试成功`, 'success');
  } catch {
    showToast('重试投递失败', 'error');
  } finally {
    retryingLogId.value = null;
  }
};

// Recipient management
const addRecipientFromInput = () => {
  const val = newRecipientInput.value.trim();
  if (!val) return;
  // Support comma-separated emails
  const emails = val.split(/[,;，；\s]+/).filter(Boolean);
  for (const e of emails) {
    if (!form.email.to.includes(e)) {
      form.email.to.push(e);
    }
  }
  newRecipientInput.value = '';
};

const removeRecipient = (index: number) => {
  form.email.to.splice(index, 1);
};

// Save notification configuration
const handleSave = async () => {
  isSaving.value = true;
  saveSuccess.value = false;
  try {
    const payload: NotificationConfig = JSON.parse(JSON.stringify(form));
    await saveNotificationConfig(payload);
    saveSuccess.value = true;
    addLog('系统设置', '消息通知中心全局策略与模板配置已保存。', 'normal');
    showToast('消息通知配置已保存，重启后端服务后生效', 'success');
    setTimeout(() => { saveSuccess.value = false; }, 3000);
  } catch (err: any) {
    showToast('配置保存失败: ' + (err?.message || '网络连接异常'), 'error');
  } finally {
    isSaving.value = false;
  }
};

// Connectivity tests
const handleTestDing = async () => {
  testingDing.value = true;
  dingTestResult.value = null;
  try {
    const res = await testDingTalk({ ...form.dingTalk });
    dingTestResult.value = res;
    showToast(res.message, res.success ? 'success' : 'error');
    addLog('系统设置', `钉钉推送验证：${res.message}`, res.success ? 'normal' : 'warning');
    loadDeliveryLogs();
  } catch (err: any) {
    dingTestResult.value = { success: false, message: '测试请求超时或网络不可达: ' + err?.message };
    showToast('钉钉通道连通性测试失败', 'error');
  } finally {
    testingDing.value = false;
  }
};

const handleTestEmail = async () => {
  testingEmail.value = true;
  emailTestResult.value = null;
  try {
    const res = await testEmail({ ...form.email });
    emailTestResult.value = res;
    showToast(res.message, res.success ? 'success' : 'error');
    addLog('系统设置', `邮件推送验证：${res.message}`, res.success ? 'normal' : 'warning');
    loadDeliveryLogs();
  } catch (err: any) {
    emailTestResult.value = { success: false, message: 'SMTP 握手失败: ' + err?.message };
    showToast('SMTP 邮件通道连通性测试失败', 'error');
  } finally {
    testingEmail.value = false;
  }
};

const handleTestWeCom = async () => {
  testingWeCom.value = true;
  weComTestResult.value = null;
  try {
    const res = await testWeCom({ ...form.weCom });
    weComTestResult.value = res;
    showToast(res.message, res.success ? 'success' : 'error');
    addLog('系统设置', `企业微信推送验证：${res.message}`, res.success ? 'normal' : 'warning');
    loadDeliveryLogs();
  } catch (err: any) {
    weComTestResult.value = { success: false, message: '测试请求超时或网络不可达: ' + err?.message };
    showToast('企业微信通道连通性测试失败', 'error');
  } finally {
    testingWeCom.value = false;
  }
};

// Current active template definition
const currentMeta = computed(() => {
  return templateMeta.find(m => m.key === activeTemplateKey.value) || templateMeta[0];
});

const currentTemplate = computed(() => {
  return (form.templates as any)[activeTemplateKey.value];
});

// Click-to-insert placeholder variable into the currently focused textarea
const insertPlaceholder = (tag: string) => {
  const token = `{${tag}}`;
  const targetTextarea = templateFormatMode.value === 'markdown' ? markdownTextareaRef.value : htmlTextareaRef.value;
  const modelField = templateFormatMode.value === 'markdown' ? 'markdown' : 'htmlBody';

  if (targetTextarea) {
    const start = targetTextarea.selectionStart ?? currentTemplate.value[modelField].length;
    const end = targetTextarea.selectionEnd ?? start;
    const text = currentTemplate.value[modelField];
    currentTemplate.value[modelField] = text.substring(0, start) + token + text.substring(end);
    // Set cursor after token
    setTimeout(() => {
      targetTextarea.focus();
      targetTextarea.setSelectionRange(start + token.length, start + token.length);
    }, 10);
  } else {
    currentTemplate.value[modelField] += token;
  }
  showToast(`已插入变量: ${token}`, 'info');
};

// Reset current template to system default
const handleResetTemplate = () => {
  const def = (defaultTemplates as any)[activeTemplateKey.value];
  if (def) {
    (form.templates as any)[activeTemplateKey.value] = JSON.parse(JSON.stringify(def));
    showToast(`已恢复「${currentMeta.value.label}」默认模板`, 'success');
  }
};

// Replace placeholders with sample simulation data for real-time rendering
const renderSimulatorText = (text: string) => {
  if (!text) return '';
  const samples = sampleData[activeTemplateKey.value] || {};
  return text.replace(/\{(\w+)\}/g, (match, tag) => {
    return samples[tag] !== undefined ? samples[tag] : match;
  });
};

const simulatedTitle = computed(() => {
  return renderSimulatorText(currentTemplate.value?.title || '');
});

const simulatedMarkdown = computed(() => {
  return renderSimulatorText(currentTemplate.value?.markdown || '');
});

const simulatedHtml = computed(() => {
  return renderSimulatorText(currentTemplate.value?.htmlBody || '');
});

// Filtered delivery logs
const filteredLogs = computed(() => {
  return logs.value.filter(log => {
    if (logFilterChannel.value !== 'all' && log.channel !== logFilterChannel.value) return false;
    if (logFilterStatus.value !== 'all' && log.status !== logFilterStatus.value) return false;
    if (logSearchQuery.value) {
      const q = logSearchQuery.value.toLowerCase();
      const matchTitle = log.title?.toLowerCase().includes(q);
      const matchRecipient = log.recipient?.toLowerCase().includes(q);
      const matchPreview = log.payloadPreview?.toLowerCase().includes(q);
      if (!matchTitle && !matchRecipient && !matchPreview) return false;
    }
    return true;
  });
});

// Delivery metrics calculation
const metrics = computed(() => {
  const total = logs.value.length;
  const successCount = logs.value.filter(l => l.status === 'Success').length;
  const failedCount = logs.value.filter(l => l.status === 'Failed').length;
  const avgLatency = total > 0
    ? Math.round(logs.value.reduce((acc, l) => acc + (l.latencyMs || 0), 0) / total)
    : 0;
  return { total, successCount, failedCount, avgLatency };
});
</script>

<template>
  <div class="h-full flex flex-col text-slate-800 dark:text-slate-100 bg-slate-100/70 dark:bg-slate-950 select-none overflow-hidden">

    <!-- Top Unified Header Banner -->
    <header class="bg-white dark:bg-slate-900 border-b border-slate-200 dark:border-slate-800 px-6 py-4 shrink-0 shadow-sm transition-colors flex flex-col lg:flex-row lg:items-center justify-between gap-4">
      <div class="flex items-center gap-3">
        <div class="w-10 h-10 rounded-xl bg-sky-500/10 text-sky-600 dark:text-sky-400 flex items-center justify-center font-bold">
          <Bell class="w-5 h-5" />
        </div>
        <div>
          <div class="flex items-center gap-2">
            <h2 class="font-bold text-lg text-slate-900 dark:text-white tracking-tight">消息通知中心</h2>
            <span class="px-2 py-0.5 rounded text-[11px] font-mono bg-sky-100 text-sky-700 dark:bg-sky-950/60 dark:text-sky-400 border border-sky-200 dark:border-sky-800">
              Gateway v2.4
            </span>
          </div>
          <p class="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
            工业现场报警推送中继服务，支持钉钉群机器人、SMTP 邮件网关、触发策略矩阵与实时效果仿真。
          </p>
        </div>
      </div>

      <!-- Header status badges & Save action -->
      <div class="flex items-center flex-wrap gap-2.5">
        <div class="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-slate-50 dark:bg-slate-800/80 border border-slate-200/80 dark:border-slate-700/80 text-xs">
          <span class="text-slate-500 dark:text-slate-400">通道状态：</span>
          <span class="inline-flex items-center gap-1 font-medium" :class="form.dingTalk.enabled ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'">
            <span class="w-1.5 h-1.5 rounded-full" :class="form.dingTalk.enabled ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'"></span>
            钉钉
          </span>
          <span class="text-slate-300 dark:text-slate-600">|</span>
          <span class="inline-flex items-center gap-1 font-medium" :class="form.email.enabled ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'">
            <span class="w-1.5 h-1.5 rounded-full" :class="form.email.enabled ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'"></span>
            邮件
          </span>
          <span class="text-slate-300 dark:text-slate-600">|</span>
          <span class="inline-flex items-center gap-1 font-medium" :class="form.weCom.enabled ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'">
            <span class="w-1.5 h-1.5 rounded-full" :class="form.weCom.enabled ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'"></span>
            企微
          </span>
        </div>

        <button
          id="btn-save-notification-config"
          @click="handleSave"
          :disabled="isSaving"
          class="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg font-medium text-xs text-white bg-sky-600 hover:bg-sky-500 active:bg-sky-700 shadow-sm transition-all disabled:opacity-50 cursor-pointer"
        >
          <RotateCw v-if="isSaving" class="w-3.5 h-3.5 animate-spin" />
          <Check v-else-if="saveSuccess" class="w-3.5 h-3.5 text-emerald-300" />
          <Save v-else class="w-3.5 h-3.5" />
          <span>{{ isSaving ? '保存中…' : saveSuccess ? '已成功保存' : '保存全局配置' }}</span>
        </button>
      </div>
    </header>

    <!-- Navigation Tabs Bar -->
    <div class="bg-white dark:bg-slate-900 border-b border-slate-200 dark:border-slate-800 px-6 shrink-0 flex items-center justify-between overflow-x-auto">
      <nav class="flex space-x-1 py-2 text-xs font-medium">
        <button
          id="tab-channels"
          @click="activeTab = 'channels'"
          class="flex items-center gap-2 px-3.5 py-2 rounded-lg transition-all cursor-pointer"
          :class="activeTab === 'channels'
            ? 'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 font-semibold shadow-xs'
            : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800'"
        >
          <MessageSquare class="w-4 h-4" />
          <span>1. 推送通道矩阵</span>
          <span class="w-2 h-2 rounded-full" :class="(form.dingTalk.enabled || form.email.enabled || form.weCom.enabled) ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-600'"></span>
        </button>

        <button
          id="tab-policy"
          @click="activeTab = 'policy'"
          class="flex items-center gap-2 px-3.5 py-2 rounded-lg transition-all cursor-pointer"
          :class="activeTab === 'policy'
            ? 'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 font-semibold shadow-xs'
            : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800'"
        >
          <Sliders class="w-4 h-4" />
          <span>2. 触发策略与防抖</span>
        </button>

        <button
          id="tab-templates"
          @click="activeTab = 'templates'"
          class="flex items-center gap-2 px-3.5 py-2 rounded-lg transition-all cursor-pointer"
          :class="activeTab === 'templates'
            ? 'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 font-semibold shadow-xs'
            : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800'"
        >
          <Sparkles class="w-4 h-4 text-amber-500" />
          <span>3. 模板与实时模拟器</span>
        </button>

        <button
          id="tab-logs"
          @click="activeTab = 'logs'; loadDeliveryLogs()"
          class="flex items-center gap-2 px-3.5 py-2 rounded-lg transition-all cursor-pointer"
          :class="activeTab === 'logs'
            ? 'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 font-semibold shadow-xs'
            : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800'"
        >
          <Clock class="w-4 h-4" />
          <span>4. 投递历史与队列</span>
          <span v-if="logs.length" class="px-1.5 py-0.2 rounded-full text-[10px] bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300 font-mono">
            {{ logs.length }}
          </span>
        </button>
      </nav>
    </div>

    <!-- Main Tab Content Area -->
    <main class="flex-1 overflow-y-auto p-4 md:p-6">

      <!-- ========================================================================= -->
      <!-- TAB 1: PUSH CHANNELS (推送通道矩阵) -->
      <!-- ========================================================================= -->
      <section v-if="activeTab === 'channels'" class="max-w-6xl mx-auto space-y-6">

        <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">

          <!-- DingTalk WebHook Card -->
          <div class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs flex flex-col overflow-hidden transition-all">
            <!-- Card Header -->
            <div class="p-5 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between bg-slate-50/50 dark:bg-slate-900/50">
              <div class="flex items-center gap-3">
                <div class="w-9 h-9 rounded-lg bg-blue-500/10 text-blue-600 dark:text-blue-400 flex items-center justify-center font-bold">
                  <MessageSquare class="w-5 h-5" />
                </div>
                <div>
                  <h3 class="font-bold text-sm text-slate-900 dark:text-white flex items-center gap-2">
                    钉钉群机器人
                    <span
                      class="px-2 py-0.5 rounded text-[10px] font-semibold"
                      :class="form.dingTalk.enabled ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'"
                    >
                      {{ form.dingTalk.enabled ? '服务已启用' : '未开启' }}
                    </span>
                  </h3>
                  <p class="text-[11px] text-slate-400">支持加签安全验证的群自定义 WebHook 机器人</p>
                </div>
              </div>
              <label class="relative inline-flex items-center cursor-pointer">
                <input type="checkbox" v-model="form.dingTalk.enabled" class="sr-only peer" />
                <div class="w-11 h-6 bg-slate-200 peer-focus:outline-none rounded-full peer dark:bg-slate-700 peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-blue-600"></div>
              </label>
            </div>

            <!-- Card Body -->
            <div class="p-5 space-y-4 flex-1">
              <div>
                <label class="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5">
                  WebHook 目标地址 <span class="text-rose-500">*</span>
                </label>
                <input
                  v-model="form.dingTalk.webhook"
                  type="text"
                  placeholder="https://oapi.dingtalk.com/robot/send?access_token=..."
                  class="w-full px-3 py-2 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all font-mono"
                />
                <p class="text-[11px] text-slate-400 mt-1">钉钉客户端群设置中添加自定义机器人后生成的专属 Hook 完整路径。</p>
              </div>

              <div>
                <label class="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5">
                  加签密钥 (Secret)
                </label>
                <div class="relative">
                  <input
                    v-model="form.dingTalk.secret"
                    :type="showDingSecret ? 'text' : 'password'"
                    placeholder="SEC..."
                    class="w-full pl-3 pr-9 py-2 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all font-mono"
                  />
                  <button
                    type="button"
                    @click="showDingSecret = !showDingSecret"
                    class="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer"
                  >
                    <EyeOff v-if="showDingSecret" class="w-4 h-4" />
                    <Eye v-else class="w-4 h-4" />
                  </button>
                </div>
                <p class="text-[11px] text-slate-400 mt-1">以 SEC 开头。若输入框显示 ****** 表示后端已留存密钥，未变动时不予覆盖。</p>
              </div>

              <!-- Test Result Alert -->
              <div
                v-if="dingTestResult"
                class="p-3 rounded-lg text-xs flex items-start gap-2.5 transition-all"
                :class="dingTestResult.success ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950/40 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800' : 'bg-rose-50 text-rose-800 dark:bg-rose-950/40 dark:text-rose-300 border border-rose-200 dark:border-rose-800'"
              >
                <CheckCircle2 v-if="dingTestResult.success" class="w-4 h-4 text-emerald-500 shrink-0 mt-0.5" />
                <AlertCircle v-else class="w-4 h-4 text-rose-500 shrink-0 mt-0.5" />
                <div class="flex-1">
                  <div class="font-semibold">{{ dingTestResult.success ? '连通性验证成功' : '通道测试失败' }}</div>
                  <div class="text-[11px] mt-0.5 opacity-90">{{ dingTestResult.message }}</div>
                  <div v-if="dingTestResult.latencyMs" class="text-[10px] mt-1 font-mono opacity-80">响应耗时: {{ dingTestResult.latencyMs }}ms</div>
                </div>
              </div>
            </div>

            <!-- Card Footer -->
            <div class="p-4 bg-slate-50/70 dark:bg-slate-900/70 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between">
              <span class="text-[11px] text-slate-400">群机器人建议开启安全加签</span>
              <button
                type="button"
                @click="handleTestDing"
                :disabled="testingDing || !form.dingTalk.webhook"
                class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium text-blue-700 dark:text-blue-300 bg-blue-50 dark:bg-blue-950/60 border border-blue-200 dark:border-blue-800/80 hover:bg-blue-100 dark:hover:bg-blue-900 transition-colors disabled:opacity-40 cursor-pointer"
              >
                <RotateCw v-if="testingDing" class="w-3.5 h-3.5 animate-spin" />
                <Send v-else class="w-3.5 h-3.5" />
                <span>{{ testingDing ? '握手测试中…' : '发送钉钉测试消息' }}</span>
              </button>
            </div>
          </div>

          <!-- SMTP Email Card -->
          <div class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs flex flex-col overflow-hidden transition-all">
            <!-- Card Header -->
            <div class="p-5 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between bg-slate-50/50 dark:bg-slate-900/50">
              <div class="flex items-center gap-3">
                <div class="w-9 h-9 rounded-lg bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 flex items-center justify-center font-bold">
                  <Mail class="w-5 h-5" />
                </div>
                <div>
                  <h3 class="font-bold text-sm text-slate-900 dark:text-white flex items-center gap-2">
                    SMTP 邮件服务
                    <span
                      class="px-2 py-0.5 rounded text-[10px] font-semibold"
                      :class="form.email.enabled ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'"
                    >
                      {{ form.email.enabled ? '服务已启用' : '未开启' }}
                    </span>
                  </h3>
                  <p class="text-[11px] text-slate-400">标准 SMTP 协议外部邮件告警分发通道</p>
                </div>
              </div>
              <label class="relative inline-flex items-center cursor-pointer">
                <input type="checkbox" v-model="form.email.enabled" class="sr-only peer" />
                <div class="w-11 h-6 bg-slate-200 peer-focus:outline-none rounded-full peer dark:bg-slate-700 peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-emerald-600"></div>
              </label>
            </div>

            <!-- Card Body -->
            <div class="p-5 space-y-4 flex-1">
              <div class="grid grid-cols-1 md:grid-cols-3 gap-3">
                <div class="md:col-span-2">
                  <label class="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5">
                    SMTP 主机 <span class="text-rose-500">*</span>
                  </label>
                  <input
                    v-model="form.email.smtpHost"
                    type="text"
                    placeholder="smtp.qiye.aliyun.com"
                    class="w-full px-3 py-2 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-emerald-500/20 focus:border-emerald-500 transition-all font-mono"
                  />
                </div>
                <div>
                  <label class="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5">
                    端口
                  </label>
                  <input
                    v-model.number="form.email.smtpPort"
                    type="number"
                    placeholder="465"
                    class="w-full px-3 py-2 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-emerald-500/20 focus:border-emerald-500 transition-all font-mono"
                  />
                </div>
              </div>

              <div class="flex items-center justify-between py-1 px-1">
                <span class="text-xs text-slate-600 dark:text-slate-400">启用 SSL / TLS 安全加密</span>
                <label class="relative inline-flex items-center cursor-pointer">
                  <input type="checkbox" v-model="form.email.useSsl" class="sr-only peer" />
                  <div class="w-9 h-5 bg-slate-200 peer-focus:outline-none rounded-full peer dark:bg-slate-700 peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-emerald-600"></div>
                </label>
              </div>

              <div class="grid grid-cols-1 md:grid-cols-2 gap-3">
                <div>
                  <label class="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5">
                    发信账号 / 用户名
                  </label>
                  <input
                    v-model="form.email.username"
                    type="text"
                    placeholder="alert_service@iota-factory.com"
                    class="w-full px-3 py-2 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-emerald-500/20 focus:border-emerald-500 transition-all font-mono"
                  />
                </div>
                <div>
                  <label class="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5">
                    授权码 / 密码
                  </label>
                  <div class="relative">
                    <input
                      v-model="form.email.password"
                      :type="showEmailPassword ? 'text' : 'password'"
                      placeholder="******"
                      class="w-full pl-3 pr-9 py-2 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-emerald-500/20 focus:border-emerald-500 transition-all font-mono"
                    />
                    <button
                      type="button"
                      @click="showEmailPassword = !showEmailPassword"
                      class="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer"
                    >
                      <EyeOff v-if="showEmailPassword" class="w-4 h-4" />
                      <Eye v-else class="w-4 h-4" />
                    </button>
                  </div>
                </div>
              </div>

              <!-- From Details & Recipients Management -->
              <div class="grid grid-cols-1 md:grid-cols-2 gap-3">
                <div>
                  <label class="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5">
                    发件人别名 (From Name)
                  </label>
                  <input
                    v-model="form.email.fromName"
                    type="text"
                    placeholder="晋鑫SCADA工业监控中心"
                    class="w-full px-3 py-2 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-emerald-500/20 focus:border-emerald-500 transition-all"
                  />
                </div>
                <div>
                  <label class="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5">
                    发件人地址 (From Email)
                  </label>
                  <input
                    v-model="form.email.from"
                    type="text"
                    placeholder="alert_service@iota-factory.com"
                    class="w-full px-3 py-2 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-emerald-500/20 focus:border-emerald-500 transition-all font-mono"
                  />
                </div>
              </div>

              <!-- Recipients Tag Manager -->
              <div>
                <label class="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5">
                  接收通知邮箱列表 ({{ form.email.to.length }}个)
                </label>
                <div class="flex flex-wrap gap-1.5 p-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50/70 dark:bg-slate-800/50 min-h-[44px]">
                  <span
                    v-for="(recipient, i) in form.email.to"
                    :key="i"
                    class="inline-flex items-center gap-1 px-2.5 py-1 rounded-md text-xs font-mono bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-slate-800 dark:text-slate-200 shadow-2xs"
                  >
                    <Mail class="w-3 h-3 text-slate-400" />
                    <span>{{ recipient }}</span>
                    <button
                      type="button"
                      @click="removeRecipient(i)"
                      class="text-slate-400 hover:text-rose-500 ml-1 transition-colors cursor-pointer"
                    >
                      <Trash2 class="w-3 h-3" />
                    </button>
                  </span>
                  <div class="flex-1 min-w-[160px] flex items-center">
                    <input
                      v-model="newRecipientInput"
                      @keydown.enter.prevent="addRecipientFromInput"
                      placeholder="输入邮箱后回车或按分号添加…"
                      class="w-full bg-transparent px-2 py-1 text-xs focus:outline-none text-slate-800 dark:text-slate-200 placeholder-slate-400 font-mono"
                    />
                  </div>
                </div>
                <div class="flex items-center justify-between text-[11px] text-slate-400 mt-1">
                  <span>支持粘贴多个邮箱（以逗号或分号分隔）自动录入</span>
                  <button
                    type="button"
                    @click="addRecipientFromInput"
                    class="text-emerald-600 dark:text-emerald-400 font-medium hover:underline cursor-pointer"
                  >
                    + 添加到列表
                  </button>
                </div>
              </div>

              <!-- Test Result Alert -->
              <div
                v-if="emailTestResult"
                class="p-3 rounded-lg text-xs flex items-start gap-2.5 transition-all"
                :class="emailTestResult.success ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950/40 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800' : 'bg-rose-50 text-rose-800 dark:bg-rose-950/40 dark:text-rose-300 border border-rose-200 dark:border-rose-800'"
              >
                <CheckCircle2 v-if="emailTestResult.success" class="w-4 h-4 text-emerald-500 shrink-0 mt-0.5" />
                <AlertCircle v-else class="w-4 h-4 text-rose-500 shrink-0 mt-0.5" />
                <div class="flex-1">
                  <div class="font-semibold">{{ emailTestResult.success ? 'SMTP 握手成功' : '邮件服务测试失败' }}</div>
                  <div class="text-[11px] mt-0.5 opacity-90">{{ emailTestResult.message }}</div>
                  <div v-if="emailTestResult.latencyMs" class="text-[10px] mt-1 font-mono opacity-80">投递往返: {{ emailTestResult.latencyMs }}ms</div>
                </div>
              </div>
            </div>

            <!-- Card Footer -->
            <div class="p-4 bg-slate-50/70 dark:bg-slate-900/70 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between">
              <span class="text-[11px] text-slate-400">推荐使用 465 (SSL) 工业邮箱端口</span>
              <button
                type="button"
                @click="handleTestEmail"
                :disabled="testingEmail || !form.email.smtpHost"
                class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium text-emerald-700 dark:text-emerald-300 bg-emerald-50 dark:bg-emerald-950/60 border border-emerald-200 dark:border-emerald-800/80 hover:bg-emerald-100 dark:hover:bg-emerald-900 transition-colors disabled:opacity-40 cursor-pointer"
              >
                <RotateCw v-if="testingEmail" class="w-3.5 h-3.5 animate-spin" />
                <Send v-else class="w-3.5 h-3.5" />
                <span>{{ testingEmail ? '发送测试邮件中…' : '发送 SMTP 测试邮件' }}</span>
              </button>
            </div>
          </div>

          <!-- WeCom WebHook Card -->
          <div class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs flex flex-col overflow-hidden transition-all">
            <div class="p-5 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between bg-slate-50/50 dark:bg-slate-900/50">
              <div class="flex items-center gap-3">
                <div class="w-9 h-9 rounded-lg bg-teal-500/10 text-teal-600 dark:text-teal-400 flex items-center justify-center font-bold">
                  <Bell class="w-5 h-5" />
                </div>
                <div>
                  <h3 class="font-bold text-sm text-slate-900 dark:text-white flex items-center gap-2">
                    企业微信群机器人
                    <span
                      class="px-2 py-0.5 rounded text-[10px] font-semibold"
                      :class="form.weCom.enabled ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'"
                    >
                      {{ form.weCom.enabled ? '服务已启用' : '未开启' }}
                    </span>
                  </h3>
                  <p class="text-[11px] text-slate-400">企业微信群 WebHook 机器人，markdown 告警直达</p>
                </div>
              </div>
              <label class="relative inline-flex items-center cursor-pointer">
                <input type="checkbox" v-model="form.weCom.enabled" class="sr-only peer" />
                <div class="w-11 h-6 bg-slate-200 peer-focus:outline-none rounded-full peer dark:bg-slate-700 peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-teal-600"></div>
              </label>
            </div>

            <div class="p-5 space-y-4 flex-1">
              <div>
                <label class="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5">
                  WebHook 目标地址 <span class="text-rose-500">*</span>
                </label>
                <input
                  v-model="form.weCom.webhook"
                  type="text"
                  placeholder="https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=..."
                  class="w-full px-3 py-2 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-500 transition-all font-mono"
                />
                <p class="text-[11px] text-slate-400 mt-1">企业微信群「添加群机器人」后生成的 Webhook 完整路径。</p>
              </div>

              <div
                v-if="weComTestResult"
                class="p-3 rounded-lg text-xs flex items-start gap-2.5 transition-all"
                :class="weComTestResult.success ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950/40 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800' : 'bg-rose-50 text-rose-800 dark:bg-rose-950/40 dark:text-rose-300 border border-rose-200 dark:border-rose-800'"
              >
                <CheckCircle2 v-if="weComTestResult.success" class="w-4 h-4 text-emerald-500 shrink-0 mt-0.5" />
                <AlertCircle v-else class="w-4 h-4 text-rose-500 shrink-0 mt-0.5" />
                <div class="flex-1">
                  <div class="font-semibold">{{ weComTestResult.success ? '连通性验证成功' : '通道测试失败' }}</div>
                  <div class="text-[11px] mt-0.5 opacity-90">{{ weComTestResult.message }}</div>
                  <div v-if="weComTestResult.latencyMs" class="text-[10px] mt-1 font-mono opacity-80">响应耗时: {{ weComTestResult.latencyMs }}ms</div>
                </div>
              </div>
            </div>

            <div class="p-4 bg-slate-50/70 dark:bg-slate-900/70 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between">
              <span class="text-[11px] text-slate-400">建议在机器人设置中开启关键词校验与 IP 白名单</span>
              <button
                type="button"
                @click="handleTestWeCom"
                :disabled="testingWeCom || !form.weCom.webhook"
                class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium text-teal-700 dark:text-teal-300 bg-teal-50 dark:bg-teal-950/60 border border-teal-200 dark:border-teal-800/80 hover:bg-teal-100 dark:hover:bg-teal-900 transition-colors disabled:opacity-40 cursor-pointer"
              >
                <RotateCw v-if="testingWeCom" class="w-3.5 h-3.5 animate-spin" />
                <Send v-else class="w-3.5 h-3.5" />
                <span>{{ testingWeCom ? '握手测试中…' : '发送企微测试消息' }}</span>
              </button>
            </div>
          </div>

        </div>

        <!-- Channel Roadmap / Extension preview banner -->
        <div class="bg-gradient-to-r from-slate-50 to-slate-100/60 dark:from-slate-900 dark:to-slate-850 p-4 rounded-xl border border-dashed border-slate-300 dark:border-slate-800 flex flex-col md:flex-row items-center justify-between gap-4">
          <div class="flex items-center gap-3">
            <div class="w-8 h-8 rounded-lg bg-sky-500/10 text-sky-600 dark:text-sky-400 flex items-center justify-center font-bold">
              <Layers class="w-4 h-4" />
            </div>
            <div>
              <div class="text-xs font-bold text-slate-800 dark:text-slate-200">更多工业消息中继接入扩展</div>
              <div class="text-[11px] text-slate-500 dark:text-slate-400">系统已内置 企业微信群机器人 (WeCom)，后续可扩展 飞书多维协作机器人 (Feishu) 与 通用 HTTP WebHook 接口。</div>
            </div>
          </div>
          <span class="px-3 py-1 rounded-full text-[11px] font-medium bg-slate-200/80 dark:bg-slate-800 text-slate-600 dark:text-slate-400">
            标准化驱动插拔就绪
          </span>
        </div>

      </section>

      <!-- ========================================================================= -->
      <!-- TAB 2: TRIGGER POLICY & THROTTLING (触发策略与防抖) -->
      <!-- ========================================================================= -->
      <section v-if="activeTab === 'policy'" class="max-w-6xl mx-auto space-y-6">

        <!-- Subscription Matrix Table -->
        <div class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs overflow-hidden">
          <div class="p-5 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
            <div>
              <h3 class="font-bold text-sm text-slate-900 dark:text-white flex items-center gap-2">
                <Sliders class="w-4 h-4 text-sky-500" />
                工业事件与通道订阅矩阵
              </h3>
              <p class="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                自由勾选各类 SCADA 工艺与系统事件的分发渠道，避免无关信息干扰主操作人员。
              </p>
            </div>
          </div>

          <div class="overflow-x-auto">
            <table class="w-full text-left text-xs">
              <thead class="bg-slate-50 dark:bg-slate-850/60 border-b border-slate-200/80 dark:border-slate-800 text-slate-600 dark:text-slate-400 font-semibold">
                <tr>
                  <th class="py-3 px-5">工业告警事件类型</th>
                  <th class="py-3 px-5">事件业务说明</th>
                  <th class="py-3 px-4 text-center">钉钉群机器人</th>
                  <th class="py-3 px-4 text-center">SMTP 邮件</th>
                  <th class="py-3 px-4 text-center">全局分发使能</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-100 dark:divide-slate-800/80">
                <!-- 1. Alarm Triggered -->
                <tr class="hover:bg-slate-50/60 dark:hover:bg-slate-850/40 transition-colors">
                  <td class="py-3.5 px-5 font-semibold text-slate-900 dark:text-white flex items-center gap-2">
                    <span class="w-2 h-2 rounded-full bg-rose-500"></span>
                    遥测超限报警 (Alarm Triggered)
                  </td>
                  <td class="py-3.5 px-5 text-slate-500 dark:text-slate-400">
                    压力、温度、液位等模拟量越界或开关量故障触发时广播
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushAlarm" class="w-4 h-4 rounded text-sky-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushAlarm" class="w-4 h-4 rounded text-emerald-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <span class="px-2 py-0.5 rounded text-[10px] font-bold" :class="form.push.pushAlarm ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400' : 'bg-slate-100 text-slate-400 dark:bg-slate-800'">
                      {{ form.push.pushAlarm ? '已订阅' : '已静音' }}
                    </span>
                  </td>
                </tr>

                <!-- 2. Device Offline -->
                <tr class="hover:bg-slate-50/60 dark:hover:bg-slate-850/40 transition-colors">
                  <td class="py-3.5 px-5 font-semibold text-slate-900 dark:text-white flex items-center gap-2">
                    <span class="w-2 h-2 rounded-full bg-amber-500"></span>
                    控制器离线警告 (PLC Offline)
                  </td>
                  <td class="py-3.5 px-5 text-slate-500 dark:text-slate-400">
                    S7 / Modbus / OPC UA 心跳丢失且超过防抖静默期时触发
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushDeviceOffline" class="w-4 h-4 rounded text-sky-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushDeviceOffline" class="w-4 h-4 rounded text-emerald-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <span class="px-2 py-0.5 rounded text-[10px] font-bold" :class="form.push.pushDeviceOffline ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400' : 'bg-slate-100 text-slate-400 dark:bg-slate-800'">
                      {{ form.push.pushDeviceOffline ? '已订阅' : '已静音' }}
                    </span>
                  </td>
                </tr>

                <!-- 3. Device Online -->
                <tr class="hover:bg-slate-50/60 dark:hover:bg-slate-850/40 transition-colors">
                  <td class="py-3.5 px-5 font-semibold text-slate-900 dark:text-white flex items-center gap-2">
                    <span class="w-2 h-2 rounded-full bg-blue-500"></span>
                    设备重新上线 (PLC Online)
                  </td>
                  <td class="py-3.5 px-5 text-slate-500 dark:text-slate-400">
                    控制器链路重新建立握手，状态恢复运行通知
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushDeviceOnline" class="w-4 h-4 rounded text-sky-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushDeviceOnline" class="w-4 h-4 rounded text-emerald-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <span class="px-2 py-0.5 rounded text-[10px] font-bold" :class="form.push.pushDeviceOnline ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400' : 'bg-slate-100 text-slate-400 dark:bg-slate-800'">
                      {{ form.push.pushDeviceOnline ? '已订阅' : '已静音' }}
                    </span>
                  </td>
                </tr>

                <!-- 4. System Alarm -->
                <tr class="hover:bg-slate-50/60 dark:hover:bg-slate-850/40 transition-colors">
                  <td class="py-3.5 px-5 font-semibold text-slate-900 dark:text-white flex items-center gap-2">
                    <span class="w-2 h-2 rounded-full bg-purple-500"></span>
                    系统内部报警 (System Alarm)
                  </td>
                  <td class="py-3.5 px-5 text-slate-500 dark:text-slate-400">
                    SCADA平台采集引擎、数据库连接池或内部测点状态异常
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushSystemAlarm" class="w-4 h-4 rounded text-sky-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushSystemAlarm" class="w-4 h-4 rounded text-emerald-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <span class="px-2 py-0.5 rounded text-[10px] font-bold" :class="form.push.pushSystemAlarm ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400' : 'bg-slate-100 text-slate-400 dark:bg-slate-800'">
                      {{ form.push.pushSystemAlarm ? '已订阅' : '已静音' }}
                    </span>
                  </td>
                </tr>

                <!-- 5. System Error -->
                <tr class="hover:bg-slate-50/60 dark:hover:bg-slate-850/40 transition-colors">
                  <td class="py-3.5 px-5 font-semibold text-slate-900 dark:text-white flex items-center gap-2">
                    <span class="w-2 h-2 rounded-full bg-rose-600"></span>
                    网关通信故障 (System Error)
                  </td>
                  <td class="py-3.5 px-5 text-slate-500 dark:text-slate-400">
                    底层驱动调用崩溃、网络套接字重置或致命异常日志
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushSystemError" class="w-4 h-4 rounded text-sky-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushSystemError" class="w-4 h-4 rounded text-emerald-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <span class="px-2 py-0.5 rounded text-[10px] font-bold" :class="form.push.pushSystemError ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400' : 'bg-slate-100 text-slate-400 dark:bg-slate-800'">
                      {{ form.push.pushSystemError ? '已订阅' : '已静音' }}
                    </span>
                  </td>
                </tr>

                <!-- 6. Script Execution -->
                <tr class="hover:bg-slate-50/60 dark:hover:bg-slate-850/40 transition-colors">
                  <td class="py-3.5 px-5 font-semibold text-slate-900 dark:text-white flex items-center gap-2">
                    <span class="w-2 h-2 rounded-full bg-yellow-500"></span>
                    策略脚本异常 (Script Fault)
                  </td>
                  <td class="py-3.5 px-5 text-slate-500 dark:text-slate-400">
                    自动化控制逻辑脚本执行失败、死循环超时或语法异常
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushScript" class="w-4 h-4 rounded text-sky-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <input type="checkbox" v-model="form.push.pushScript" class="w-4 h-4 rounded text-emerald-600 cursor-pointer" />
                  </td>
                  <td class="py-3.5 px-4 text-center">
                    <span class="px-2 py-0.5 rounded text-[10px] font-bold" :class="form.push.pushScript ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400' : 'bg-slate-100 text-slate-400 dark:bg-slate-800'">
                      {{ form.push.pushScript ? '已订阅' : '已静音' }}
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Throttling, Debounce & Reliability Parameters Grid -->
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">

          <!-- Debounce Card -->
          <div class="bg-white dark:bg-slate-900 p-5 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs space-y-3">
            <div class="flex items-center gap-2.5">
              <div class="w-8 h-8 rounded-lg bg-amber-500/10 text-amber-600 dark:text-amber-400 flex items-center justify-center font-bold">
                <Clock class="w-4 h-4" />
              </div>
              <h4 class="font-bold text-xs text-slate-900 dark:text-white">设备状态防抖静默</h4>
            </div>
            <p class="text-[11px] text-slate-500 dark:text-slate-400">
              同一控制器频繁在线/离线闪烁时，在此时间段内仅发送首次变动，防止信息轰炸。
            </p>
            <div class="flex items-center gap-2">
              <input
                v-model.number="form.push.deviceStatusDebounceMinutes"
                type="number"
                min="1"
                max="60"
                class="w-24 px-3 py-1.5 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 font-mono focus:outline-none focus:ring-1 focus:ring-sky-500"
              />
              <span class="text-xs text-slate-500">分钟 (推荐 3 ~ 10)</span>
            </div>
          </div>

          <!-- Rate Limiting Card -->
          <div class="bg-white dark:bg-slate-900 p-5 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs space-y-3">
            <div class="flex items-center gap-2.5">
              <div class="w-8 h-8 rounded-lg bg-sky-500/10 text-sky-600 dark:text-sky-400 flex items-center justify-center font-bold">
                <Activity class="w-4 h-4" />
              </div>
              <h4 class="font-bold text-xs text-slate-900 dark:text-white">单通道频次上限 (限流)</h4>
            </div>
            <p class="text-[11px] text-slate-500 dark:text-slate-400">
              单通道（如钉钉机器人）每分钟允许发送的最大告警数量，避免触发服务商封禁。
            </p>
            <div class="flex items-center gap-2">
              <input
                v-model.number="form.push.maxPerMinutePerChannel"
                type="number"
                min="1"
                max="60"
                class="w-24 px-3 py-1.5 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 font-mono focus:outline-none focus:ring-1 focus:ring-sky-500"
              />
              <span class="text-xs text-slate-500">条 / 分钟 (推荐 15 ~ 20)</span>
            </div>
          </div>

          <!-- Retry Strategy Card -->
          <div class="bg-white dark:bg-slate-900 p-5 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs space-y-3">
            <div class="flex items-center gap-2.5">
              <div class="w-8 h-8 rounded-lg bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 flex items-center justify-center font-bold">
                <RotateCw class="w-4 h-4" />
              </div>
              <h4 class="font-bold text-xs text-slate-900 dark:text-white">失败重试与延迟退避</h4>
            </div>
            <p class="text-[11px] text-slate-500 dark:text-slate-400">
              网络偶发丢包或超时时自动重试次数与基础退避时延。
            </p>
            <div class="flex items-center gap-3">
              <div class="flex items-center gap-1.5">
                <span class="text-xs text-slate-400">最多</span>
                <input
                  v-model.number="form.push.maxAttempts"
                  type="number"
                  min="1"
                  max="5"
                  class="w-16 px-2 py-1 text-xs rounded border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 font-mono"
                />
                <span class="text-xs text-slate-400">次</span>
              </div>
              <div class="flex items-center gap-1.5">
                <span class="text-xs text-slate-400">延迟</span>
                <input
                  v-model.number="form.push.retryBaseDelayMs"
                  type="number"
                  min="500"
                  step="500"
                  class="w-20 px-2 py-1 text-xs rounded border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 font-mono"
                />
                <span class="text-xs text-slate-400">ms</span>
              </div>
            </div>
          </div>

        </div>

      </section>

      <!-- ========================================================================= -->
      <!-- TAB 3: TEMPLATES & LIVE SIMULATOR (模板与实时模拟器) -->
      <!-- ========================================================================= -->
      <section v-if="activeTab === 'templates'" class="max-w-7xl mx-auto space-y-5">

        <!-- Template Selector Nav Pills -->
        <div class="flex items-center gap-2 overflow-x-auto pb-1">
          <button
            v-for="meta in templateMeta"
            :key="meta.key"
            @click="activeTemplateKey = meta.key"
            class="flex items-center gap-2 px-3 py-2 rounded-xl text-xs font-semibold whitespace-nowrap transition-all cursor-pointer"
            :class="activeTemplateKey === meta.key
              ? 'bg-sky-600 text-white shadow-sm'
              : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-850'"
          >
            <span>{{ meta.label }}</span>
            <span class="text-[10px] opacity-75 font-mono">({{ meta.key }})</span>
          </button>
        </div>

        <!-- 2-Column Split: Left Editor vs Right Simulator -->
        <div class="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">

          <!-- Left Column: Template Editor -->
          <div class="lg:col-span-7 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs flex flex-col overflow-hidden">

            <!-- Editor Header -->
            <div class="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between bg-slate-50/50 dark:bg-slate-900/50">
              <div>
                <h3 class="font-bold text-sm text-slate-900 dark:text-white flex items-center gap-2">
                  <span>{{ currentMeta.label }}模板配置</span>
                  <span class="px-2 py-0.5 rounded text-[10px] font-mono bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400">
                    {{ currentMeta.key }}
                  </span>
                </h3>
                <p class="text-[11px] text-slate-400 mt-0.5">{{ currentMeta.desc }}</p>
              </div>

              <!-- Format Switcher: Markdown vs HTML -->
              <div class="flex items-center bg-slate-200/80 dark:bg-slate-800 p-0.5 rounded-lg text-xs">
                <button
                  type="button"
                  @click="templateFormatMode = 'markdown'"
                  class="px-3 py-1 rounded-md transition-all cursor-pointer"
                  :class="templateFormatMode === 'markdown' ? 'bg-white dark:bg-slate-700 text-slate-900 dark:text-white font-semibold shadow-xs' : 'text-slate-500 hover:text-slate-800 dark:hover:text-slate-200'"
                >
                  钉钉 Markdown
                </button>
                <button
                  type="button"
                  @click="templateFormatMode = 'html'"
                  class="px-3 py-1 rounded-md transition-all cursor-pointer"
                  :class="templateFormatMode === 'html' ? 'bg-white dark:bg-slate-700 text-slate-900 dark:text-white font-semibold shadow-xs' : 'text-slate-500 hover:text-slate-800 dark:hover:text-slate-200'"
                >
                  邮件 HTML
                </button>
              </div>
            </div>

            <div class="p-5 space-y-4">
              <!-- Title Field -->
              <div>
                <div class="flex items-center justify-between mb-1.5">
                  <label class="text-xs font-semibold text-slate-700 dark:text-slate-300">
                    通知消息标题 (Subject / Title)
                  </label>
                  <span class="text-[10px] text-slate-400 font-mono">{{ currentTemplate.title?.length || 0 }} 字符</span>
                </div>
                <input
                  v-model="currentTemplate.title"
                  type="text"
                  class="w-full px-3 py-2 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-sky-500/20 focus:border-sky-500 transition-all font-mono"
                  placeholder="例如：⚠️【SCADA告警】{deviceKey} 触发 {ruleName}"
                />
              </div>

              <!-- Interactive Variables Tag Toolbar (Click to Insert) -->
              <div>
                <div class="flex items-center justify-between mb-1.5">
                  <span class="text-xs font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-1.5">
                    <Sparkles class="w-3.5 h-3.5 text-amber-500" />
                    可用变量占位符 (点击一键插入到光标处)
                  </span>
                </div>
                <div class="flex flex-wrap gap-1.5 p-2 rounded-lg bg-slate-50 dark:bg-slate-800/40 border border-slate-200/80 dark:border-slate-800">
                  <button
                    v-for="ph in currentMeta.placeholders"
                    :key="ph.tag"
                    type="button"
                    @click="insertPlaceholder(ph.tag)"
                    class="group inline-flex items-center gap-1 px-2.5 py-1 rounded-md text-[11px] font-mono bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-300 hover:border-sky-500 hover:text-sky-600 dark:hover:text-sky-400 transition-all cursor-pointer shadow-2xs"
                    :title="`插入 {${ph.tag}} (${ph.label})`"
                  >
                    <Plus class="w-3 h-3 text-slate-400 group-hover:text-sky-500" />
                    <span class="font-semibold text-sky-600 dark:text-sky-400">{{"{" + ph.tag + "}"}}</span>
                    <span class="text-[10px] text-slate-400 font-sans">({{ ph.label }})</span>
                  </button>
                </div>
              </div>

              <!-- Markdown Editor -->
              <div v-if="templateFormatMode === 'markdown'" class="space-y-1.5">
                <div class="flex items-center justify-between">
                  <label class="text-xs font-semibold text-slate-700 dark:text-slate-300">
                    钉钉 Markdown 正文模板
                  </label>
                  <span class="text-[11px] text-slate-400">支持标题 ###、加粗 **、颜色 &lt;font&gt;、引用 &gt;</span>
                </div>
                <textarea
                  ref="markdownTextareaRef"
                  v-model="currentTemplate.markdown"
                  rows="11"
                  class="w-full p-3 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-sky-500/20 focus:border-sky-500 transition-all font-mono leading-relaxed"
                  placeholder="输入 Markdown 模板内容…"
                ></textarea>
              </div>

              <!-- HTML Editor -->
              <div v-else class="space-y-1.5">
                <div class="flex items-center justify-between">
                  <label class="text-xs font-semibold text-slate-700 dark:text-slate-300">
                    SMTP 邮件 HTML 正文模板
                  </label>
                  <span class="text-[11px] text-slate-400">支持标准行内样式（如 style="color: #dc2626;"）</span>
                </div>
                <textarea
                  ref="htmlTextareaRef"
                  v-model="currentTemplate.htmlBody"
                  rows="11"
                  class="w-full p-3 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-sky-500/20 focus:border-sky-500 transition-all font-mono leading-relaxed"
                  placeholder="输入 HTML 模板代码…"
                ></textarea>
              </div>
            </div>

            <!-- Editor Footer -->
            <div class="p-4 bg-slate-50/70 dark:bg-slate-900/70 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between">
              <span class="text-[11px] text-slate-400">右侧模拟器实时预览已绑定当前工业虚拟数据</span>
              <button
                type="button"
                @click="handleResetTemplate"
                class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium text-slate-600 dark:text-slate-300 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
              >
                <RotateCw class="w-3.5 h-3.5 text-slate-400" />
                <span>恢复本事件默认官方模板</span>
              </button>
            </div>

          </div>

          <!-- Right Column: Realtime Visual Simulator -->
          <div class="lg:col-span-5 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs flex flex-col overflow-hidden">

            <!-- Simulator Header -->
            <div class="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between bg-slate-50/50 dark:bg-slate-900/50">
              <div class="flex items-center gap-2">
                <div class="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse"></div>
                <h3 class="font-bold text-sm text-slate-900 dark:text-white">实景推送仿真器</h3>
              </div>

              <!-- Switch Simulator Mode -->
              <div class="flex items-center bg-slate-200/80 dark:bg-slate-800 p-0.5 rounded-lg text-xs">
                <button
                  type="button"
                  @click="previewMode = 'dingtalk'"
                  class="flex items-center gap-1.5 px-3 py-1 rounded-md transition-all cursor-pointer"
                  :class="previewMode === 'dingtalk' ? 'bg-white dark:bg-slate-700 text-slate-900 dark:text-white font-semibold shadow-xs' : 'text-slate-500 hover:text-slate-800 dark:hover:text-slate-200'"
                >
                  <Smartphone class="w-3.5 h-3.5" />
                  <span>钉钉群效果</span>
                </button>
                <button
                  type="button"
                  @click="previewMode = 'email'"
                  class="flex items-center gap-1.5 px-3 py-1 rounded-md transition-all cursor-pointer"
                  :class="previewMode === 'email' ? 'bg-white dark:bg-slate-700 text-slate-900 dark:text-white font-semibold shadow-xs' : 'text-slate-500 hover:text-slate-800 dark:hover:text-slate-200'"
                >
                  <Laptop class="w-3.5 h-3.5" />
                  <span>邮件收件箱</span>
                </button>
              </div>
            </div>

            <!-- Simulator Body Screen -->
            <div class="p-5 bg-slate-100/80 dark:bg-slate-950/70 min-h-[460px] flex flex-col justify-start">

              <!-- DINGTALK APP SIMULATION -->
              <div v-if="previewMode === 'dingtalk'" class="space-y-4 max-w-sm mx-auto w-full">
                <!-- Group Chat Context -->
                <div class="text-center">
                  <span class="px-2.5 py-0.5 rounded-full text-[10px] bg-slate-200/70 dark:bg-slate-800 text-slate-500">
                    今天 12:20 (车间动力与水务运行群)
                  </span>
                </div>

                <!-- DingTalk Bot Bubble -->
                <div class="flex items-start gap-2.5">
                  <div class="w-8 h-8 rounded-full bg-blue-600 text-white flex items-center justify-center font-bold text-xs shrink-0 shadow-xs">
                    SC
                  </div>
                  <div class="flex-1 space-y-1">
                    <div class="flex items-center gap-1.5">
                      <span class="text-xs font-semibold text-slate-700 dark:text-slate-300">SCADA 智能报警机器人</span>
                      <span class="px-1.5 py-0.2 rounded text-[9px] bg-blue-100 text-blue-700 dark:bg-blue-950 dark:text-blue-300 font-bold">
                        BOT
                      </span>
                    </div>

                    <!-- DingTalk Message Card -->
                    <div class="bg-white dark:bg-slate-850 p-4 rounded-2xl rounded-tl-none border border-slate-200/80 dark:border-slate-800 shadow-xs space-y-2.5 text-left">
                      <!-- Simulated Card Title -->
                      <h4 class="font-bold text-sm text-slate-900 dark:text-white border-b border-slate-100 dark:border-slate-800 pb-2">
                        {{ simulatedTitle || '（未设置标题）' }}
                      </h4>

                      <!-- Simulated Card Body Text (pre-wrap) -->
                      <div class="text-xs text-slate-700 dark:text-slate-300 whitespace-pre-wrap leading-relaxed font-sans select-text">
                        {{ simulatedMarkdown || '（尚未编写 Markdown 正文）' }}
                      </div>

                      <div class="pt-2 border-t border-slate-100 dark:border-slate-800/80 flex items-center justify-between text-[10px] text-slate-400">
                        <span>由 晋鑫工业SCADA 系统自动下发</span>
                        <span class="text-blue-500 font-medium cursor-pointer">查看大屏详情 &gt;</span>
                      </div>
                    </div>
                  </div>
                </div>
              </div>

              <!-- EMAIL CLIENT SIMULATION -->
              <div v-else class="w-full bg-white dark:bg-slate-850 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs overflow-hidden flex flex-col text-left">
                <!-- Email Header meta -->
                <div class="p-3.5 bg-slate-50 dark:bg-slate-900 border-b border-slate-200 dark:border-slate-800 space-y-1.5 text-xs">
                  <div class="font-bold text-sm text-slate-900 dark:text-white truncate">
                    主题: {{ simulatedTitle || '（未命名邮件主题）' }}
                  </div>
                  <div class="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1">
                    <span>发件人:</span>
                    <span class="text-slate-800 dark:text-slate-200 font-medium">
                      {{ form.email.fromName || 'SCADA 监控中心' }} &lt;{{ form.email.from || form.email.username || 'alert@scada.local' }}&gt;
                    </span>
                  </div>
                  <div class="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1">
                    <span>收件人:</span>
                    <span class="text-slate-800 dark:text-slate-200 font-mono">
                      {{ form.email.to[0] || 'duty_engineer@iota-factory.com' }}
                    </span>
                  </div>
                </div>

                <!-- Email HTML Body Renderer -->
                <div class="p-4 overflow-x-auto select-text">
                  <div v-if="simulatedHtml" v-html="simulatedHtml"></div>
                  <div v-else class="text-xs text-slate-400 italic py-6 text-center">
                    HTML 模板内容为空，请在左侧编辑器输入
                  </div>
                </div>
              </div>

            </div>

            <!-- Simulator Bottom Note -->
            <div class="p-3.5 bg-slate-50/70 dark:bg-slate-900/70 border-t border-slate-100 dark:border-slate-800 text-[11px] text-slate-500 flex items-center justify-between">
              <span>模拟数据源：DEV_PUMP_01 出水压力遥测点 (S7-1500)</span>
              <span class="text-emerald-600 dark:text-emerald-400 font-medium">动态变量注入就绪</span>
            </div>

          </div>

        </div>

      </section>

      <!-- ========================================================================= -->
      <!-- TAB 4: DELIVERY AUDIT LOGS & QUEUE (投递历史与队列) -->
      <!-- ========================================================================= -->
      <section v-if="activeTab === 'logs'" class="max-w-6xl mx-auto space-y-6">

        <!-- Top Metric Counters -->
        <div class="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <div class="bg-white dark:bg-slate-900 p-4 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs">
            <div class="text-xs text-slate-500">累计投递流水</div>
            <div class="text-2xl font-bold text-slate-900 dark:text-white mt-1 font-mono">{{ metrics.total }}</div>
          </div>
          <div class="bg-white dark:bg-slate-900 p-4 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs">
            <div class="text-xs text-slate-500">投递成功</div>
            <div class="text-2xl font-bold text-emerald-600 dark:text-emerald-400 mt-1 font-mono">{{ metrics.successCount }}</div>
          </div>
          <div class="bg-white dark:bg-slate-900 p-4 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs">
            <div class="text-xs text-slate-500">投递异常 / 失败</div>
            <div class="text-2xl font-bold text-rose-600 dark:text-rose-400 mt-1 font-mono">{{ metrics.failedCount }}</div>
          </div>
          <div class="bg-white dark:bg-slate-900 p-4 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs">
            <div class="text-xs text-slate-500">平均往返耗时</div>
            <div class="text-2xl font-bold text-sky-600 dark:text-sky-400 mt-1 font-mono">{{ metrics.avgLatency }} <span class="text-xs font-sans text-slate-400">ms</span></div>
          </div>
        </div>

        <!-- Filter & Control Toolbar -->
        <div class="bg-white dark:bg-slate-900 p-4 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs flex flex-col md:flex-row md:items-center justify-between gap-3">
          <div class="flex items-center flex-wrap gap-2.5">
            <!-- Channel Filter -->
            <select
              v-model="logFilterChannel"
              class="px-3 py-1.5 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none cursor-pointer"
            >
              <option value="all">全部推送渠道</option>
              <option value="dingTalk">钉钉群机器人</option>
              <option value="weCom">企业微信群机器人</option>
              <option value="email">SMTP 邮件</option>
              <option value="webPush">Web 推送</option>
            </select>

            <!-- Status Filter -->
            <select
              v-model="logFilterStatus"
              class="px-3 py-1.5 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none cursor-pointer"
            >
              <option value="all">全部状态</option>
              <option value="Success">投递成功 (Success)</option>
              <option value="Failed">异常失败 (Failed)</option>
              <option value="Retrying">重试中 (Retrying)</option>
            </select>

            <!-- Keyword Search -->
            <div class="relative">
              <input
                v-model="logSearchQuery"
                type="text"
                placeholder="搜索标题、目标或详情…"
                class="pl-8 pr-3 py-1.5 text-xs rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 focus:outline-none w-56 font-mono"
              />
              <Search class="w-3.5 h-3.5 text-slate-400 absolute left-2.5 top-1/2 -translate-y-1/2" />
            </div>
          </div>

          <div class="flex items-center gap-2">
            <button
              type="button"
              @click="loadDeliveryLogs"
              :disabled="logsLoading"
              class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium text-slate-700 dark:text-slate-300 bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors cursor-pointer"
            >
              <RotateCw class="w-3.5 h-3.5" :class="{ 'animate-spin': logsLoading }" />
              <span>刷新流水</span>
            </button>
            <button
              type="button"
              @click="handleClearLogs"
              :disabled="logs.length === 0"
              class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium text-rose-600 dark:text-rose-400 bg-rose-50 dark:bg-rose-950/50 hover:bg-rose-100 dark:hover:bg-rose-900/50 transition-colors disabled:opacity-40 cursor-pointer"
            >
              <Trash2 class="w-3.5 h-3.5" />
              <span>清空流水</span>
            </button>
          </div>
        </div>

        <!-- Logs Table -->
        <div class="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 shadow-xs overflow-hidden">
          <div class="overflow-x-auto">
            <table class="w-full text-left text-xs">
              <thead class="bg-slate-50 dark:bg-slate-850/60 border-b border-slate-200/80 dark:border-slate-800 text-slate-600 dark:text-slate-400 font-semibold">
                <tr>
                  <th class="py-3 px-4">投递时间</th>
                  <th class="py-3 px-3">渠道</th>
                  <th class="py-3 px-3">事件类型</th>
                  <th class="py-3 px-4">通知主题与概要</th>
                  <th class="py-3 px-3">接收目标</th>
                  <th class="py-3 px-3 text-right">响应耗时</th>
                  <th class="py-3 px-3 text-center">状态</th>
                  <th class="py-3 px-4 text-center">操作</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-100 dark:divide-slate-800/80">
                <tr v-if="filteredLogs.length === 0">
                  <td colspan="8" class="py-8 text-center text-slate-400 text-xs italic">
                    暂无符合条件的通知投递记录
                  </td>
                </tr>
                <tr
                  v-for="item in filteredLogs"
                  :key="item.id"
                  class="hover:bg-slate-50/60 dark:hover:bg-slate-850/40 transition-colors"
                >
                  <td class="py-3 px-4 text-slate-500 dark:text-slate-400 font-mono whitespace-nowrap">
                    {{ new Date(item.timestamp).toLocaleString() }}
                  </td>
                  <td class="py-3 px-3 whitespace-nowrap">
                    <span
                      class="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[11px] font-semibold"
                      :class="{
                        'bg-blue-50 text-blue-700 dark:bg-blue-950/60 dark:text-blue-400 border border-blue-200/60 dark:border-blue-900': item.channel === 'dingTalk',
                        'bg-teal-50 text-teal-700 dark:bg-teal-950/60 dark:text-teal-400 border border-teal-200/60 dark:border-teal-900': item.channel === 'weCom',
                        'bg-emerald-50 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400 border border-emerald-200/60 dark:border-emerald-900': item.channel === 'email',
                        'bg-violet-50 text-violet-700 dark:bg-violet-950/60 dark:text-violet-400 border border-violet-200/60 dark:border-violet-900': item.channel === 'webPush'
                      }"
                    >
                      <MessageSquare v-if="item.channel === 'dingTalk'" class="w-3 h-3" />
                      <Bell v-else-if="item.channel === 'weCom'" class="w-3 h-3" />
                      <Mail v-else-if="item.channel === 'email'" class="w-3 h-3" />
                      <Smartphone v-else class="w-3 h-3" />
                      {{ item.channel === 'dingTalk' ? '钉钉群' : (item.channel === 'weCom' ? '企微群' : (item.channel === 'email' ? 'SMTP邮件' : 'Web推送')) }}
                    </span>
                  </td>
                  <td class="py-3 px-3 text-slate-700 dark:text-slate-300 font-mono whitespace-nowrap">
                    {{ item.eventType }}
                  </td>
                  <td class="py-3 px-4 font-medium text-slate-900 dark:text-white max-w-xs truncate">
                    <div class="truncate" :title="item.title">{{ item.title }}</div>
                    <div v-if="item.error" class="text-[10px] text-rose-500 truncate mt-0.5">错误: {{ item.error }}</div>
                  </td>
                  <td class="py-3 px-3 text-slate-600 dark:text-slate-400 font-mono max-w-[140px] truncate">
                    {{ item.recipient }}
                  </td>
                  <td class="py-3 px-3 text-right font-mono text-slate-500 whitespace-nowrap">
                    {{ item.latencyMs }}ms
                  </td>
                  <td class="py-3 px-3 text-center whitespace-nowrap">
                    <span
                      class="px-2 py-0.5 rounded text-[10px] font-bold inline-flex items-center gap-1"
                      :class="{
                        'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400': item.status === 'Success',
                        'bg-amber-100 text-amber-700 dark:bg-amber-950/60 dark:text-amber-400': item.status === 'Retrying',
                        'bg-rose-100 text-rose-700 dark:bg-rose-950/60 dark:text-rose-400': item.status !== 'Success' && item.status !== 'Retrying'
                      }"
                    >
                      <CheckCircle2 v-if="item.status === 'Success'" class="w-3 h-3" />
                      <RotateCw v-else-if="item.status === 'Retrying'" class="w-3 h-3 animate-spin" />
                      <AlertCircle v-else class="w-3 h-3" />
                      {{ item.status === 'Success' ? '投递成功' : (item.status === 'Retrying' ? '重试中' : '投递失败') }}
                    </span>
                  </td>
                  <td class="py-3 px-4 text-center whitespace-nowrap space-x-2">
                    <button
                      type="button"
                      @click="selectedLogDetail = item"
                      class="text-sky-600 dark:text-sky-400 hover:underline text-[11px] font-medium cursor-pointer"
                    >
                      详情
                    </button>
                    <button
                      v-if="item.status === 'Failed'"
                      type="button"
                      @click="handleRetryLog(item)"
                      :disabled="retryingLogId === item.id"
                      class="text-amber-600 dark:text-amber-400 hover:underline text-[11px] font-medium cursor-pointer"
                    >
                      {{ retryingLogId === item.id ? '重试中…' : '一键重发' }}
                    </button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

      </section>

    </main>

    <!-- Detail Dialog Modal for Notification Log Item -->
    <div
      v-if="selectedLogDetail"
      class="fixed inset-0 z-50 bg-black/50 backdrop-blur-xs flex items-center justify-center p-4 animate-fade-in"
      @click.self="selectedLogDetail = null"
    >
      <div class="bg-white dark:bg-slate-900 rounded-2xl max-w-lg w-full border border-slate-200 dark:border-slate-800 shadow-xl overflow-hidden text-left">
        <div class="p-5 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
          <h3 class="font-bold text-sm text-slate-900 dark:text-white flex items-center gap-2">
            <span>通知投递流水详情</span>
            <span class="px-2 py-0.5 rounded text-[10px] font-mono bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400">
              #{{ selectedLogDetail.id }}
            </span>
          </h3>
          <button
            type="button"
            @click="selectedLogDetail = null"
            class="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer"
          >
            ✕
          </button>
        </div>

        <div class="p-5 space-y-3.5 text-xs">
          <div>
            <span class="text-slate-400">消息标题：</span>
            <span class="font-semibold text-slate-900 dark:text-white">{{ selectedLogDetail.title }}</span>
          </div>

          <div class="grid grid-cols-2 gap-3 py-2 border-y border-slate-100 dark:border-slate-800">
            <div>
              <span class="text-slate-400">分发渠道：</span>
              <span class="font-medium text-slate-800 dark:text-slate-200">{{ selectedLogDetail.channel === 'dingTalk' ? '钉钉群机器人' : (selectedLogDetail.channel === 'weCom' ? '企业微信群机器人' : (selectedLogDetail.channel === 'email' ? 'SMTP 邮件服务' : 'Web Push 推送')) }}</span>
            </div>
            <div>
              <span class="text-slate-400">投递状态：</span>
              <span class="font-bold" :class="selectedLogDetail.status === 'Success' ? 'text-emerald-600' : 'text-rose-600'">{{ selectedLogDetail.status }}</span>
            </div>
            <div>
              <span class="text-slate-400">投递耗时：</span>
              <span class="font-mono text-slate-800 dark:text-slate-200">{{ selectedLogDetail.latencyMs }} ms</span>
            </div>
            <div>
              <span class="text-slate-400">时间戳：</span>
              <span class="font-mono text-slate-800 dark:text-slate-200">{{ new Date(selectedLogDetail.timestamp).toLocaleString() }}</span>
            </div>
          </div>

          <div>
            <span class="text-slate-400">接收方地址 / 群组：</span>
            <div class="mt-1 p-2 rounded bg-slate-50 dark:bg-slate-800 font-mono text-slate-700 dark:text-slate-300">
              {{ selectedLogDetail.recipient }}
            </div>
          </div>

          <div v-if="selectedLogDetail.payloadPreview">
            <span class="text-slate-400">报文概览 / 内容摘录：</span>
            <div class="mt-1 p-2.5 rounded bg-slate-50 dark:bg-slate-800 text-slate-700 dark:text-slate-300 whitespace-pre-wrap leading-relaxed">
              {{ selectedLogDetail.payloadPreview }}
            </div>
          </div>

          <div v-if="selectedLogDetail.error" class="p-3 rounded-lg bg-rose-50 dark:bg-rose-950/50 border border-rose-200 dark:border-rose-900 text-rose-700 dark:text-rose-300">
            <div class="font-semibold mb-0.5">第三方服务器异常回执：</div>
            <div class="font-mono text-[11px] break-all">{{ selectedLogDetail.error }}</div>
          </div>
        </div>

        <div class="p-4 bg-slate-50 dark:bg-slate-900/60 border-t border-slate-100 dark:border-slate-800 flex justify-end">
          <button
            type="button"
            @click="selectedLogDetail = null"
            class="px-4 py-2 rounded-lg text-xs font-medium bg-slate-200/80 dark:bg-slate-800 hover:bg-slate-300 dark:hover:bg-slate-700 transition-colors cursor-pointer"
          >
            关闭详情
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
