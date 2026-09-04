<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { MessageSquare, Mail, Bell, ShieldCheck, Save, Send, RefreshCw, Plus, X, FileText } from 'lucide-vue-next';
import { addLog } from '../store/index';
import { showToast } from '../services/toastService';
import {
  fetchNotificationConfig,
  saveNotificationConfig,
  testDingTalk,
  testEmail,
  NotificationConfig,
  NotificationTemplates
} from '../api/notificationApi';

const loading = ref(true);
const isSaving = ref(false);
const testingDing = ref(false);
const testingEmail = ref(false);
const saveSuccess = ref(false);

// 消息模板编辑：key 对应后端 Templates 的各个事件模板，附中文标签与占位符提示。
const templateMeta: { key: keyof NotificationTemplates; label: string; placeholders: string[] }[] = [
  { key: 'alarmTriggered', label: '报警触发', placeholders: ['deviceKey','deviceId','variableName','variableKey','ruleName','level','condition','threshold','actualValue','source','message','time'] },
  { key: 'alarmRecovered', label: '报警恢复', placeholders: ['deviceKey','deviceId','variableName','variableKey','ruleName','level','condition','threshold','actualValue','source','message','time'] },
  { key: 'deviceStatus', label: '设备状态', placeholders: ['status','deviceId','time'] },
  { key: 'systemAlarm', label: '系统报警', placeholders: ['deviceId','variableName','variableKey','level','message','time'] },
  { key: 'systemError', label: '系统异常', placeholders: ['level','source','time','content'] },
  { key: 'scriptExecution', label: '脚本异常', placeholders: ['scriptId','scriptVersion','triggerSource','result','error','durationMs','time'] },
];

const emptyTemplate = () => ({ title: '', markdown: '', htmlBody: '' });
// 把占位符数组格式化为「{a} {b}」提示串（避免模板字面量内出现 }} 干扰 Vue 插值解析）。
const fmtPlaceholders = (placeholders: string[]) => placeholders.map(p => '{' + p + '}').join(' ');
const defaultTemplates: NotificationTemplates = {
  alarmTriggered: emptyTemplate(),
  alarmRecovered: emptyTemplate(),
  deviceStatus: emptyTemplate(),
  systemAlarm: emptyTemplate(),
  systemError: emptyTemplate(),
  scriptExecution: emptyTemplate(),
};

const form = reactive<NotificationConfig>({
  dingTalk: { enabled: false, webhook: '', secret: '', hasSecret: false },
  email: {
    enabled: false, smtpHost: '', smtpPort: 465, useSsl: true,
    username: '', password: '', hasPassword: false,
    from: '', fromName: 'SCADA 报警中心', to: ['']
  },
  push: {
    pushAlarm: true, pushDeviceOffline: true, pushDeviceOnline: false,
    deviceStatusDebounceMinutes: 5, pushSystemAlarm: true, pushSystemError: true,
    pushScript: true, maxPerMinutePerChannel: 15, maxAttempts: 2,
    retryBaseDelayMs: 1000, queueCapacity: 2048
  },
  templates: defaultTemplates
});

const secretMask = '******';

onMounted(async () => {
  loading.value = true;
  try {
    const res = await fetchNotificationConfig();
    Object.assign(form.dingTalk, res.dingTalk);
    Object.assign(form.email, res.email);
    Object.assign(form.push, res.push);
    if (res.templates) {
      templateMeta.forEach(m => Object.assign((form.templates as any)[m.key], (res.templates as any)[m.key]));
    }
  } catch {
    // 错误由 http 拦截器统一提示
  } finally {
    loading.value = false;
  }
});

const addRecipient = () => { form.email.to.push(''); };
const removeRecipient = (i: number) => {
  if (form.email.to.length <= 1) return;
  form.email.to.splice(i, 1);
};

const handleSave = async () => {
  isSaving.value = true;
  saveSuccess.value = false;
  try {
    // 空值/掩码保持，敏感字段由后端解析；此处保证非空项不发送掩码之外的异常值
    const payload: NotificationConfig = JSON.parse(JSON.stringify(form));
    await saveNotificationConfig(payload);
    saveSuccess.value = true;
    addLog('系统设置', '消息通知配置已保存（重启后生效）。', 'normal');
    showToast('通知配置已保存，重启后生效', 'success');
    setTimeout(() => { saveSuccess.value = false; }, 2500);
  } catch {
    // 错误由 http 拦截器统一提示
  } finally {
    isSaving.value = false;
  }
};

const handleTestDing = async () => {
  testingDing.value = true;
  try {
    const res = await testDingTalk({ ...form.dingTalk });
    showToast(res.message, res.success ? 'success' : 'error');
    addLog('系统设置', `钉钉通知测试：${res.message}`, res.success ? 'normal' : 'warning');
  } catch {
    // 拦截器已提示
  } finally {
    testingDing.value = false;
  }
};

const handleTestEmail = async () => {
  testingEmail.value = true;
  try {
    const res = await testEmail({ ...form.email });
    showToast(res.message, res.success ? 'success' : 'error');
    addLog('系统设置', `邮件通知测试：${res.message}`, res.success ? 'normal' : 'warning');
  } catch {
    // 拦截器已提示
  } finally {
    testingEmail.value = false;
  }
};
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent overflow-y-auto">

    <!-- Top banner -->
    <div class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Bell class="w-5 h-5 text-slate-700 dark:text-slate-300" />
          消息通知
        </h2>
        <p class="text-xs text-slate-400">配置钉钉群机器人 / SMTP 邮件外部推送。</p>
      </div>
      <div class="flex items-center gap-2">
        <span v-if="saveSuccess" class="text-xs text-emerald-500 font-semibold flex items-center gap-1">
          <ShieldCheck class="w-3.5 h-3.5" /> 已保存，重启后生效
        </span>
        <button
          @click="handleSave"
          :disabled="isSaving || loading"
          class="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-lg bg-slate-900 dark:bg-slate-100 text-white dark:text-slate-900 text-xs font-bold hover:opacity-90 disabled:opacity-40 transition"
        >
          <Save class="w-3.5 h-3.5" /> {{ isSaving ? '保存中...' : '保存配置' }}
        </button>
      </div>
    </div>

    <div v-if="loading" class="flex-1 grid place-items-center text-sm text-slate-400">加载中...</div>

    <div v-else class="p-5 space-y-4 max-w-4xl">
      <div class="bg-amber-50/40 dark:bg-amber-950/30 border border-amber-100 dark:border-amber-900/50 p-3 rounded-lg leading-relaxed text-amber-700 dark:text-amber-300 text-xs">
        <b class="text-amber-800 dark:text-amber-200">说明：</b>
        配置写入叠加文件，需<code class="font-mono">重启服务</code>后生效；
        密钥/授权码留空或保持掩码 <code class="font-mono">******</code> 表示不修改；可先用“测试发送”验证参数。
      </div>

      <!-- 钉钉群机器人 -->
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs space-y-4 transition-colors">
        <h3 class="font-bold text-xs text-slate-900 dark:text-white border-b border-slate-100 dark:border-slate-800 pb-2.5 flex items-center gap-2">
          <MessageSquare class="w-4 h-4 text-[#0089FF]" /> 钉钉群机器人
        </h3>

        <div class="space-y-3.5 text-xs font-sans" :class="form.dingTalk.enabled ? '' : 'opacity-60'">
          <div class="flex items-center justify-between p-2.5 bg-slate-50 dark:bg-slate-950 rounded-lg">
            <div>
              <b class="text-slate-800 dark:text-slate-200 font-bold block">启用钉钉通知</b>
              <span class="text-[10px] text-slate-400 block font-normal mt-0.5">报警/设备/系统异常推送到群</span>
            </div>
            <input type="checkbox" v-model="form.dingTalk.enabled" class="accent-slate-900 w-5 h-5 cursor-pointer" />
          </div>

          <div>
            <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">Webhook 地址</label>
            <input
              v-model="form.dingTalk.webhook"
              type="text"
              placeholder="https://oapi.dingtalk.com/robot/send?access_token=xxx"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 text-slate-800 dark:text-white font-mono outline-none"
            />
          </div>

          <div class="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">加签密钥 Secret</label>
              <input
                v-model="form.dingTalk.secret"
                type="password"
                placeholder="留空/****** = 不改"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 text-slate-800 dark:text-white font-mono outline-none"
              />
            </div>
          </div>

          <div class="flex justify-end">
            <button
              @click="handleTestDing"
              :disabled="testingDing"
              class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 text-xs font-bold hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40 transition"
            >
              <Send class="w-3.5 h-3.5" /> {{ testingDing ? '发送中...' : '测试发送' }}
            </button>
          </div>
        </div>
      </div>

      <!-- SMTP 邮件 -->
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs space-y-4 transition-colors">
        <h3 class="font-bold text-xs text-slate-900 dark:text-white border-b border-slate-100 dark:border-slate-800 pb-2.5 flex items-center gap-2">
          <Mail class="w-4 h-4 text-emerald-500" /> SMTP 邮件
        </h3>

        <div class="space-y-3.5 text-xs font-sans" :class="form.email.enabled ? '' : 'opacity-60'">
          <div class="flex items-center justify-between p-2.5 bg-slate-50 dark:bg-slate-950 rounded-lg">
            <div>
              <b class="text-slate-800 dark:text-slate-200 font-bold block">启用邮件通知</b>
              <span class="text-[10px] text-slate-400 block font-normal mt-0.5">报警等统一发送到收件人邮箱</span>
            </div>
            <input type="checkbox" v-model="form.email.enabled" class="accent-slate-900 w-5 h-5 cursor-pointer" />
          </div>

          <div class="grid grid-cols-1 md:grid-cols-3 gap-3">
            <div class="md:col-span-2">
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">SMTP 主机</label>
              <input v-model="form.email.smtpHost" type="text" placeholder="smtp.example.com"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 text-slate-800 dark:text-white font-mono outline-none" />
            </div>
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">端口</label>
              <input v-model.number="form.email.smtpPort" type="number"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 text-slate-800 dark:text-white font-mono outline-none" />
            </div>
          </div>

          <div class="flex items-center gap-2 text-slate-500 dark:text-slate-400">
            <input type="checkbox" v-model="form.email.useSsl" class="accent-slate-900 w-4 h-4 cursor-pointer" />
            <span>使用 SSL/TLS（465 端口通常勾选；587 用 STARTTLS 时取消）</span>
          </div>

          <div class="grid grid-cols-1 md:grid-cols-3 gap-3">
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">账号</label>
              <input v-model="form.email.username" type="text" autocomplete="off"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 text-slate-800 dark:text-white font-mono outline-none" />
            </div>
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">授权码/密码</label>
              <input v-model="form.email.password" type="password" autocomplete="new-password" placeholder="留空/****** = 不改"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 text-slate-800 dark:text-white font-mono outline-none" />
            </div>
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">发件人</label>
              <input v-model="form.email.from" type="email"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 text-slate-800 dark:text-white font-mono outline-none" />
            </div>
          </div>

          <div>
            <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">发件人名称</label>
            <input v-model="form.email.fromName" type="text"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 text-slate-800 dark:text-white outline-none" />
          </div>

          <div>
            <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">收件人（可多个）</label>
            <div class="space-y-2">
              <div v-for="(_, i) in form.email.to" :key="i" class="flex items-center gap-2">
                <input v-model="form.email.to[i]" type="email" placeholder="receiver@example.com"
                  class="flex-1 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 text-slate-800 dark:text-white font-mono outline-none" />
                <button @click="removeRecipient(i)" :disabled="form.email.to.length <= 1"
                  class="p-2 rounded-lg text-slate-400 hover:text-red-500 disabled:opacity-30">
                  <X class="w-4 h-4" />
                </button>
              </div>
              <button @click="addRecipient"
                class="inline-flex items-center gap-1 text-slate-500 dark:text-slate-400 hover:text-slate-700 text-xs font-bold">
                <Plus class="w-3.5 h-3.5" /> 添加收件人
              </button>
            </div>
          </div>

          <div class="flex justify-end">
            <button
              @click="handleTestEmail"
              :disabled="testingEmail"
              class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 text-xs font-bold hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40 transition"
            >
              <Send class="w-3.5 h-3.5" /> {{ testingEmail ? '发送中...' : '测试发送' }}
            </button>
          </div>
        </div>
      </div>

      <!-- 推送策略 -->
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs space-y-4 transition-colors">
        <h3 class="font-bold text-xs text-slate-900 dark:text-white border-b border-slate-100 dark:border-slate-800 pb-2.5 flex items-center gap-2">
          <RefreshCw class="w-4 h-4 text-purple-500" /> 推送策略
        </h3>
        <div class="grid grid-cols-1 md:grid-cols-2 gap-3 text-xs">
          <label v-for="opt in [
            { key: 'pushAlarm', label: '报警触发/恢复' },
            { key: 'pushDeviceOffline', label: '设备离线' },
            { key: 'pushDeviceOnline', label: '设备上线' },
            { key: 'pushSystemAlarm', label: '系统报警' },
            { key: 'pushSystemError', label: '系统异常日志' },
            { key: 'pushScript', label: '脚本执行异常' }
          ]" :key="opt.key" class="flex items-center justify-between p-2.5 bg-slate-50 dark:bg-slate-950 rounded-lg cursor-pointer">
            <span class="text-slate-700 dark:text-slate-300">{{ opt.label }}</span>
            <input type="checkbox" v-model="(form.push as any)[opt.key]" class="accent-slate-900 w-4.5 h-4.5 cursor-pointer" />
          </label>
        </div>
      </div>

      <!-- 消息模板 -->
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs space-y-4 transition-colors">
        <h3 class="font-bold text-xs text-slate-900 dark:text-white border-b border-slate-100 dark:border-slate-800 pb-2.5 flex items-center gap-2">
          <FileText class="w-4 h-4 text-blue-500" /> 消息模板
        </h3>
        <p class="text-xs text-slate-500 dark:text-slate-400 leading-relaxed">
          自定义各类事件发送到钉钉/邮件的内容。<b class="text-slate-600 dark:text-slate-300">标题</b> 与
          <b class="text-slate-600 dark:text-slate-300">正文(Markdown)</b> 用于钉钉群；
          <b class="text-slate-600 dark:text-slate-300">邮件正文(HTML)</b> 用于邮箱。
          用 <code class="font-mono text-blue-600 dark:text-blue-400">{占位符}</code> 引用动态值，留空表示沿用系统默认。
        </p>

        <div class="space-y-4">
          <div v-for="tmpl in templateMeta" :key="tmpl.key"
            class="border border-slate-200 dark:border-slate-700 rounded-lg overflow-hidden">
            <div class="px-3 py-2 bg-slate-50 dark:bg-slate-950/60 flex items-center justify-between">
              <span class="font-bold text-sm text-slate-700 dark:text-slate-200">{{ tmpl.label }}</span>
              <span class="text-[10px] text-slate-400 font-mono">占位符: {{ fmtPlaceholders(tmpl.placeholders) }}</span>
            </div>
            <div class="p-3 space-y-3">
              <div>
                <label class="block text-xs font-bold text-slate-500 dark:text-slate-400 mb-1">标题</label>
                <input v-model="(form.templates as any)[tmpl.key].title" type="text"
                  class="w-full p-2 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg text-slate-800 dark:text-white text-xs font-mono outline-none" />
              </div>
              <div>
                <label class="block text-xs font-bold text-slate-500 dark:text-slate-400 mb-1">正文 (Markdown · 钉钉)</label>
                <textarea v-model="(form.templates as any)[tmpl.key].markdown" rows="4"
                  class="w-full p-2 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg text-slate-800 dark:text-white text-xs font-mono outline-none resize-y leading-relaxed"></textarea>
              </div>
              <div>
                <label class="block text-xs font-bold text-slate-500 dark:text-slate-400 mb-1">邮件正文 (HTML)</label>
                <textarea v-model="(form.templates as any)[tmpl.key].htmlBody" rows="4"
                  class="w-full p-2 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg text-slate-800 dark:text-white text-xs font-mono outline-none resize-y leading-relaxed"></textarea>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
