<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { systemConfig, addLog, currentTheme, setTheme } from '../store/index';
import { initializeRealtimeSignals } from '../services/signalRService';
import { startBackendPolling } from '../services/pollService';
import {
  ensurePermission,
  subscribe,
  unsubscribe,
  getSubscriptionState,
  getMyDevices,
  sendTestPush
} from '../services/pushService';
import { 
  Settings, 
  Save, 
  HelpCircle, 
  Mail, 
  Radio, 
  Gauge, 
  Layers, 
  Eye, 
  ShieldAlert,
  Server,
  Code,
  Sun,
  Moon,
  Monitor,
  Bell
} from 'lucide-vue-next';

const isSaving = ref(false);
const saveSuccess = ref(false);

// ---- PWA 推送设置（阶段四 · 步骤 17） ----
const pushSupported = ref(true);
const pushPermission = ref<NotificationPermission | 'unsupported'>('default');
const pushSubscribed = ref(false);
const pushLoading = ref(false);
const pushMsg = ref('');
const pushMsgType = ref<'info' | 'error' | 'success'>('info');
const myDevices = ref<any[]>([]);

const setPushMsg = (type: 'info' | 'error' | 'success', msg: string) => {
  pushMsgType.value = type;
  pushMsg.value = msg;
};

const refreshPushState = async () => {
  try {
    const st = await getSubscriptionState();
    pushSupported.value = st.supported;
    pushPermission.value = st.permission;
    pushSubscribed.value = st.subscribed && st.serverBound;
    myDevices.value = pushSubscribed.value ? await getMyDevices() : [];
  } catch (e: any) {
    setPushMsg('error', '读取推送状态失败：' + (e?.message || '未知错误'));
  }
};

const togglePush = async (on: boolean) => {
  pushLoading.value = true;
  try {
    if (!on) {
      await unsubscribe();
      setPushMsg('success', '已关闭推送通知');
    } else {
      const perm = await ensurePermission();
      if (perm !== 'granted') {
        setPushMsg('error', '浏览器通知权限被拒绝，请在浏览器设置中开启通知权限后重试');
        pushLoading.value = false;
        await refreshPushState();
        return;
      }
      await subscribe();
      setPushMsg('success', '推送通知已开启，报警将推送到本设备');
    }
  } catch (e: any) {
    setPushMsg('error', '操作失败：' + (e?.message || '未知错误'));
  } finally {
    pushLoading.value = false;
    await refreshPushState();
  }
};

const testPush = async () => {
  pushLoading.value = true;
  try {
    await sendTestPush();
    setPushMsg('info', '测试通知已发送，请留意系统通知');
  } catch (e: any) {
    setPushMsg('error', '发送测试通知失败：' + (e?.message || '未知错误'));
  } finally {
    pushLoading.value = false;
  }
};

// 单设备模型下，移除即退订当前浏览器订阅
const removeDevice = async (_endpoint: string) => {
  pushLoading.value = true;
  try {
    await unsubscribe();
    setPushMsg('success', '已移除本设备订阅');
  } catch (e: any) {
    setPushMsg('error', '移除失败：' + (e?.message || '未知错误'));
  } finally {
    pushLoading.value = false;
    await refreshPushState();
  }
};

onMounted(refreshPushState);

const handleSaveSettings = () => {
  isSaving.value = true;
  saveSuccess.value = false;

  addLog('系统设置', '正在重构重载 SCADA 进程轮询及通道绑定链路...', 'info');

  setTimeout(() => {
    isSaving.value = false;
    saveSuccess.value = true;

    // Hot-reload physical signals pipelines on user demand
    initializeRealtimeSignals();
    startBackendPolling();

    addLog('系统设置', '全局配置应用成功：重整工业服务与物联遥测通道。', 'normal');
    setTimeout(() => {
      saveSuccess.value = false;
    }, 2500);
  }, 1000);
};
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent overflow-y-auto">
    
    <!-- Top banner -->
    <div class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Settings class="w-5 h-5 text-slate-700 dark:text-slate-300" />
          系统设置
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          配置系统核心参数，包括外观主题、数据源连接、轮询间隔、告警通知等。
        </p>
      </div>

      <!-- Save settings -->
      <button 
        @click="handleSaveSettings"
        :disabled="isSaving"
        class="font-bold text-xs bg-slate-900 dark:bg-sky-600 text-white hover:bg-slate-800 dark:hover:bg-sky-500 px-5 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-sm active:translate-y-0.5"
      >
        <Save class="w-4 h-4" />
        {{ isSaving ? '应用配置中...' : '保存配置' }}
      </button>
    </div>

    <!-- Setting layouts forms -->
    <div class="flex-1 p-6 space-y-6 text-left max-w-4xl">
      
      <!-- Alert banner of success -->
      <div v-if="saveSuccess" class="bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-200 dark:border-emerald-800 text-emerald-850 dark:text-emerald-300 p-4 rounded-xl flex items-center gap-3 animate-in fade-in slide-in-from-top-4 duration-200">
        <div class="w-8 h-8 rounded-full bg-emerald-500 text-white flex items-center justify-center font-bold">✓</div>
        <div>
          <b class="text-xs text-slate-900 dark:text-white block font-bold leading-none">系统控制参数写入成功！</b>
          <span class="text-[11px] block mt-0.5 text-slate-500 dark:text-slate-400 font-sans">M2M 采集器、触发器线程也已跟随在后台平滑秒级热加载重启。</span>
        </div>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        
        <!-- THEME SELECTION MODULE -->
        <div class="md:col-span-2 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-5 shadow-xs space-y-4 transition-colors">
          <div class="flex items-center justify-between border-b border-slate-100 dark:border-slate-800 pb-3">
            <h3 class="font-bold text-xs text-slate-900 dark:text-white flex items-center gap-2">
              <Sun class="w-4 h-4 text-amber-500" />
              界面主题模式
            </h3>
            <span class="text-[11px] font-mono font-bold text-slate-400">当前：{{ currentTheme === 'dark' ? '深色模式' : '浅色模式' }}</span>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <!-- Light theme option -->
            <button 
              type="button" 
              @click="setTheme('light')" 
              class="p-4 rounded-xl border-2 text-left flex items-start gap-3.5 transition-all cursor-pointer"
              :class="currentTheme === 'light' ? 'border-sky-500 bg-sky-50/50 dark:bg-sky-950/20 text-slate-900 dark:text-white' : 'border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700 bg-white dark:bg-slate-950/40 text-slate-600 dark:text-slate-400'"
            >
              <div class="w-9 h-9 rounded-lg bg-amber-100 dark:bg-amber-950/40 text-amber-600 flex items-center justify-center shrink-0">
                <Sun class="w-5 h-5" />
              </div>
              <div class="space-y-1">
                <div class="flex items-center gap-2">
                  <span class="font-bold text-xs">浅色模式 (Light Mode)</span>
                  <span v-if="currentTheme === 'light'" class="text-[9px] font-bold bg-sky-500 text-white px-1.5 py-0.5 rounded leading-none">使用中</span>
                </div>
                <p class="text-[11px] text-slate-500 dark:text-slate-400 font-normal">高清晰度明亮视觉，适合明亮控制室与办公环境。</p>
              </div>
            </button>

            <!-- Dark theme option -->
            <button 
              type="button" 
              @click="setTheme('dark')" 
              class="p-4 rounded-xl border-2 text-left flex items-start gap-3.5 transition-all cursor-pointer"
              :class="currentTheme === 'dark' ? 'border-sky-500 bg-sky-50/50 dark:bg-sky-950/20 text-slate-900 dark:text-white' : 'border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700 bg-white dark:bg-slate-950/40 text-slate-600 dark:text-slate-400'"
            >
              <div class="w-9 h-9 rounded-lg bg-indigo-100 dark:bg-indigo-950/40 text-indigo-500 flex items-center justify-center shrink-0">
                <Moon class="w-5 h-5" />
              </div>
              <div class="space-y-1">
                <div class="flex items-center gap-2">
                  <span class="font-bold text-xs">深色模式 (Dark Mode)</span>
                  <span v-if="currentTheme === 'dark'" class="text-[9px] font-bold bg-sky-500 text-white px-1.5 py-0.5 rounded leading-none">使用中</span>
                </div>
                <p class="text-[11px] text-slate-500 dark:text-slate-400 font-normal">工业控制中心沉浸式低光护眼暗色，降低视力疲劳。</p>
              </div>
            </button>
          </div>
        </div>

        <!-- INDUSTRIAL BACKEND BRIDGING & SIMULATOR CONSOLE -->
        <div class="md:col-span-2 bg-gradient-to-r from-blue-50/60 to-indigo-50/40 dark:from-slate-900/90 dark:to-slate-900/60 border border-indigo-200/90 dark:border-slate-800 rounded-2xl p-6 shadow-sm space-y-4">
          <div class="flex items-start justify-between gap-4 flex-col sm:flex-row">
            <div class="space-y-1 text-left">
            <h3 class="font-bold text-sm text-indigo-950 dark:text-indigo-300 flex items-center gap-2">
              <Server class="w-5 h-5 text-indigo-600 dark:text-indigo-400 animate-pulse" />
              数据源连接
            </h3>
            <p class="text-xs text-indigo-700/80 dark:text-slate-400 max-w-2xl font-sans leading-relaxed">
              配置后端 API 连接和数据仿真模式。
            </p>
          </div>
          
          <!-- Connection status badge -->
          <div class="flex items-center gap-2 shrink-0 bg-white dark:bg-slate-800 px-3 py-1.5 rounded-full border border-indigo-100 dark:border-slate-700 shadow-2xs">
            <span class="relative flex h-2 w-2">
              <span :class="systemConfig.isSimulationActive ? 'bg-amber-400' : 'bg-emerald-400 animate-ping absolute inline-flex h-full w-full rounded-full opacity-75'"></span>
              <span :class="systemConfig.isSimulationActive ? 'bg-amber-500' : 'bg-emerald-500'" class="relative inline-flex rounded-full h-2 w-2"></span>
            </span>
            <span class="text-[11px] font-bold" :class="systemConfig.isSimulationActive ? 'text-amber-700 dark:text-amber-400' : 'text-emerald-700 dark:text-emerald-400'">
              {{ systemConfig.isSimulationActive ? '仿真模式' : '已连接' }}
            </span>
          </div>
          </div>

          <div class="grid grid-cols-1 md:grid-cols-3 gap-5 pt-3 border-t border-indigo-100/80 dark:border-slate-800">
            <!-- Toggle Simulation -->
            <div class="bg-white/80 dark:bg-slate-950/60 border border-indigo-100/50 dark:border-slate-800 p-4 rounded-xl flex items-center justify-between shadow-3xs hover:bg-white dark:hover:bg-slate-950 transition-all">
              <div class="space-y-0.5 text-left">
              <b class="text-xs text-slate-800 dark:text-slate-200 block font-bold leading-normal">启用仿真模式</b>
              <span class="text-[10px] text-slate-400 font-sans block">关闭后将连接真实数据源</span>
            </div>
              <div class="flex items-center">
                <label class="relative inline-flex items-center cursor-pointer">
                  <input 
                    type="checkbox" 
                    v-model="systemConfig.isSimulationActive"
                    class="accent-indigo-600 w-5 h-5 cursor-pointer"
                  />
                </label>
              </div>
            </div>

            <!-- Server Base URL Input -->
            <div class="md:col-span-2 bg-white/80 dark:bg-slate-950/60 border border-indigo-100/50 dark:border-slate-800 p-4 rounded-xl space-y-2 shadow-3xs hover:bg-white dark:hover:bg-slate-950 transition-all">
              <div class="flex items-center justify-between text-left">
                <label class="font-bold text-xs text-slate-850 dark:text-slate-200">API 服务地址</label>
                <span class="text-[10px] font-mono font-bold text-indigo-600 dark:text-indigo-400">WebSocket & HTTP</span>
              </div>
              <div class="flex items-center gap-2">
                <div class="relative flex-1">
                  <input 
                    v-model="systemConfig.backendApiUrl"
                    type="text"
                    :disabled="systemConfig.isSimulationActive"
                    placeholder="留空走代理，或填 http://localhost:5555"
                    class="w-full bg-slate-50 dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 pl-8 text-slate-800 dark:text-white font-bold font-mono outline-none text-xs focus:bg-white focus:border-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed"
                  />
                  <Code class="absolute left-2.5 top-3.5 w-4 h-4 text-slate-400" />
                </div>
                <div class="bg-indigo-50 dark:bg-indigo-950/60 px-3 py-2.5 rounded-lg border border-indigo-150 dark:border-indigo-800 text-indigo-700 dark:text-indigo-300 text-xs font-mono font-bold select-none whitespace-nowrap">
                  PORT: 5555
                </div>
              </div>
            </div>
          </div>
        </div>
        
        <!-- MODULE 1: System Title & UI settings -->
        <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs space-y-4 transition-colors">
          <h3 class="font-bold text-xs text-slate-900 dark:text-white border-b border-slate-100 dark:border-slate-800 pb-2.5 flex items-center gap-2">
            <Eye class="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
            界面设置
          </h3>

          <div class="space-y-3.5 text-xs font-sans">
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">系统标题</label>
              <input 
                v-model="systemConfig.systemTitle"
                type="text"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-bold outline-none font-sans"
              />
            </div>

            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">数据刷新间隔</label>
              <div class="flex items-center gap-2">
                <input 
                  v-model.number="systemConfig.pollIntervalMs"
                  type="number"
                  step="100"
                  min="200"
                  class="w-32 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-mono font-bold outline-none"
                />
                <span class="text-[11px] text-slate-500 dark:text-slate-400">毫秒 (ms)</span>
              </div>
            </div>
          </div>
        </div>

        <!-- MODULE 2: Security & alarms routing -->
        <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs space-y-4 transition-colors">
          <h3 class="font-bold text-xs text-slate-900 dark:text-white border-b border-slate-100 dark:border-slate-800 pb-2.5 flex items-center gap-2">
            <ShieldAlert class="w-4 h-4 text-amber-500" />
            告警通知
          </h3>

          <div class="space-y-4 text-xs font-sans">
            <!-- Toggle alert email -->
            <div class="flex items-center justify-between p-2.5 bg-slate-50 dark:bg-slate-950 rounded-lg">
              <div>
                <b class="text-slate-800 dark:text-slate-200 font-bold block">启用邮件告警通知</b>
                <span class="text-[10px] text-slate-400 block font-normal mt-0.5">触发告警时发送邮件通知</span>
              </div>
              
              <input 
                type="checkbox" 
                v-model="systemConfig.alarmEmailNotify"
                class="accent-slate-900 w-5 h-5 cursor-pointer"
              />
            </div>

            <!-- Receiver address -->
            <div :class="!systemConfig.alarmEmailNotify ? 'opacity-40 pointer-events-none' : ''" class="transition-opacity">
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">告警邮箱地址</label>
              <div class="relative">
                <input 
                  v-model="systemConfig.alarmEmailAddress"
                  type="email"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 pl-9 text-slate-800 dark:text-white font-bold outline-none font-mono"
                  placeholder="alerts@factory.com"
                />
                <Mail class="absolute left-3 top-3.5 w-4 h-4 text-slate-400" />
              </div>
            </div>
          </div>
        </div>

        <!-- MODULE: 推送通知 (Web Push, 阶段四 · 步骤 17) -->
        <div class="md:col-span-2 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs space-y-4 transition-colors">
          <h3 class="font-bold text-xs text-slate-900 dark:text-white border-b border-slate-100 dark:border-slate-800 pb-2.5 flex items-center gap-2">
            <Bell class="w-4 h-4 text-[#1890ff]" />
            推送通知 (Web Push)
          </h3>

          <div v-if="!pushSupported" class="text-[11px] text-slate-500 dark:text-slate-400 bg-slate-50 dark:bg-slate-950 rounded-lg p-3 leading-relaxed">
            当前浏览器不支持 Web Push，或尚未安装为 PWA（iOS 需先「安装到主屏幕」）。推送能力暂不可用。
          </div>

          <template v-else>
            <div class="flex items-center justify-between p-2.5 bg-slate-50 dark:bg-slate-950 rounded-lg">
              <div>
                <b class="text-slate-800 dark:text-slate-200 font-bold block">启用推送通知</b>
                <span class="text-[10px] text-slate-400 block font-normal mt-0.5">
                  {{ pushPermission === 'denied' ? '浏览器已拒绝通知，请在浏览器设置中开启' : (pushSubscribed ? '已开启，报警将推送到本设备' : '开启后报警将推送到本设备') }}
                </span>
              </div>
              <input
                type="checkbox"
                :checked="pushSubscribed"
                :disabled="pushLoading || pushPermission === 'denied'"
                @change="togglePush(($event.target as HTMLInputElement).checked)"
                class="accent-slate-900 w-5 h-5 cursor-pointer disabled:opacity-50"
              />
            </div>

            <div v-if="pushSubscribed && myDevices.length" class="space-y-2">
              <label class="font-bold text-slate-500 dark:text-slate-400 text-[11px]">我的设备</label>
              <div v-for="d in myDevices" :key="d.endpoint"
                class="flex items-center justify-between bg-slate-50 dark:bg-slate-950 rounded-lg p-2.5 text-[11px]">
                <div class="min-w-0">
                  <div class="font-bold text-slate-700 dark:text-slate-200 truncate">{{ d.userAgent || '本设备' }}</div>
                  <div class="text-slate-400 font-mono truncate">
                    {{ d.lastPushAtUtc ? '最近推送：' + d.lastPushAtUtc : '尚未推送' }}
                  </div>
                </div>
                <button @click="removeDevice(d.endpoint)" :disabled="pushLoading"
                  class="text-rose-500 hover:text-rose-600 text-[11px] font-bold px-2 py-1 rounded cursor-pointer disabled:opacity-50">
                  移除
                </button>
              </div>
            </div>

            <div v-if="pushSubscribed" class="flex items-center gap-2">
              <button @click="testPush" :disabled="pushLoading"
                class="text-[11px] font-bold px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer disabled:opacity-50 transition-all">
                发送测试通知
              </button>
            </div>

            <div v-if="pushMsg"
              class="text-[11px] font-bold p-2.5 rounded-lg"
              :class="pushMsgType === 'error'
                ? 'bg-rose-50 dark:bg-rose-950/40 text-rose-600 dark:text-rose-300'
                : pushMsgType === 'success'
                  ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-300'
                  : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300'">
              {{ pushMsg }}
            </div>
          </template>
        </div>

        <!-- MODULE 3: 物联网数据中继与 OPC 通讯 -->
        <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs space-y-4 transition-colors">
          <h3 class="font-bold text-xs text-slate-900 dark:text-white border-b border-slate-100 dark:border-slate-800 pb-2.5 flex items-center gap-2">
            <Radio class="w-4 h-4 text-[#1890ff]" />
            协议网关
          </h3>

          <div class="space-y-3.5 text-xs font-sans">
            <div class="grid grid-cols-3 gap-3">
              <div class="col-span-2">
                <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">MQTT Broker 地址</label>
                <input 
                  v-model="systemConfig.mqttBrokerHost"
                  type="text"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-bold font-mono outline-none"
                />
              </div>

              <div>
                <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">端口</label>
                <input 
                  v-model.number="systemConfig.mqttBrokerPort"
                  type="number"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-mono outline-none"
                />
              </div>
            </div>

            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">OPC-UA 发现地址</label>
              <input 
                v-model="systemConfig.opcUaDiscoveryUrl"
                type="text"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-bold font-mono outline-none"
              />
            </div>
          </div>
        </div>

        <!-- MODULE 4: 数据库清理等设置 -->
        <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs space-y-4 transition-colors">
          <h3 class="font-bold text-xs text-slate-900 dark:text-white border-b border-slate-100 dark:border-slate-800 pb-2.5 flex items-center gap-2">
            <Layers class="w-4 h-4 text-purple-600 dark:text-purple-400" />
            数据保留
          </h3>

          <div class="space-y-3.5 text-xs font-sans">
            <div>
              <label class="font-bold text-slate-500 dark:text-slate-400 block mb-1">数据保留周期</label>
              <div class="flex items-center gap-2">
                <input 
                  v-model.number="systemConfig.retentionPeriodDays"
                  type="number"
                  class="w-24 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-white font-bold font-mono outline-none"
                />
                <span class="text-slate-400">天 · 超过期限的数据将被自动清理。</span>
              </div>
            </div>

            <div class="bg-amber-50/40 dark:bg-amber-950/30 border border-amber-100 dark:border-amber-900/50 p-3 rounded-lg leading-relaxed text-amber-700 dark:text-amber-300">
              <span class="font-bold block pb-0.5 text-amber-800 dark:text-amber-200">注意：</span>
              较短的保留期限可提升查询性能，但可能影响长期趋势分析。
            </div>
          </div>
        </div>

      </div>

    </div>

  </div>
</template>
