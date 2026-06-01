<script setup lang="ts">
import { ref } from 'vue';
import { systemConfig, addLog, initializeRealtimeSignals, startBackendPolling } from '../store/index';
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
  Code
} from 'lucide-vue-next';

const isSaving = ref(false);
const saveSuccess = ref(false);

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
  <div class="h-full flex flex-col text-[#1e293b] select-none bg-slate-50 overflow-y-auto">
    
    <!-- Top banner -->
    <div class="bg-white p-5 border-b border-slate-200 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 tracking-tight flex items-center gap-2">
          <Settings class="w-5 h-5 text-slate-700" />
          系统设置
        </h2>
        <p class="text-xs text-slate-500 font-sans">
          配置系统核心参数，包括数据源连接、轮询间隔、告警通知等。
        </p>
      </div>

      <!-- Save settings -->
      <button 
        @click="handleSaveSettings"
        :disabled="isSaving"
        class="font-bold text-xs bg-slate-900 text-white hover:bg-slate-800 px-5  py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-sm active:translate-y-0.5"
      >
        <Save class="w-4 h-4" />
        {{ isSaving ? '应用配置中...' : '保存配置' }}
      </button>
    </div>

    <!-- Setting layouts forms -->
    <div class="flex-1 p-6 space-y-6 text-left max-w-4xl">
      
      <!-- Alert banner of success -->
      <div v-if="saveSuccess" class="bg-emerald-50 border border-emerald-200 text-emerald-850 p-4 rounded-xl flex items-center gap-3 animate-in fade-in slide-in-from-top-4 duration-200">
        <div class="w-8 h-8 rounded-full bg-emerald-500 text-white flex items-center justify-center font-bold">✓</div>
        <div>
          <b class="text-xs text-slate-900 block font-bold leading-none">系统控制参数写入成功！</b>
          <span class="text-[11px] block mt-0.5 text-slate-500 font-sans">M2M 采集器、触发器线程也已跟随在后台平滑秒级热加载重启。</span>
        </div>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        
        <!-- INDUSTRIAL BACKEND BRIDGING & SIMULATOR CONSOLE -->
        <div class="md:col-span-2 bg-gradient-to-r from-blue-50/60 to-indigo-50/40 border border-indigo-200/90 rounded-2xl p-6 shadow-sm space-y-4">
          <div class="flex items-start justify-between gap-4 flex-col sm:flex-row">
            <div class="space-y-1 text-left">
            <h3 class="font-bold text-sm text-indigo-950 flex items-center gap-2">
              <Server class="w-5 h-5 text-indigo-600 animate-pulse" />
              数据源连接
            </h3>
            <p class="text-xs text-indigo-700/80 max-w-2xl font-sans leading-relaxed">
              配置后端 API 连接和数据仿真模式。
            </p>
          </div>
          
          <!-- Connection status badge -->
          <div class="flex items-center gap-2 shrink-0 bg-white px-3 py-1.5 rounded-full border border-indigo-100 shadow-2xs">
            <span class="relative flex h-2 w-2">
              <span :class="systemConfig.isSimulationActive ? 'bg-amber-400' : 'bg-emerald-400 animate-ping absolute inline-flex h-full w-full rounded-full opacity-75'"></span>
              <span :class="systemConfig.isSimulationActive ? 'bg-amber-500' : 'bg-emerald-500'" class="relative inline-flex rounded-full h-2 w-2"></span>
            </span>
            <span class="text-[11px] font-bold" :class="systemConfig.isSimulationActive ? 'text-amber-700' : 'text-emerald-700'">
              {{ systemConfig.isSimulationActive ? '仿真模式' : '已连接' }}
            </span>
          </div>
          </div>

          <div class="grid grid-cols-1 md:grid-cols-3 gap-5 pt-3 border-t border-indigo-100/80">
            <!-- Toggle Simulation -->
            <div class="bg-white/80 border border-indigo-100/50 p-4 rounded-xl flex items-center justify-between shadow-3xs hover:bg-white transition-all">
              <div class="space-y-0.5 text-left">
              <b class="text-xs text-slate-800 block font-bold leading-normal">启用仿真模式</b>
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
            <div class="md:col-span-2 bg-white/80 border border-indigo-100/50 p-4 rounded-xl space-y-2 shadow-3xs hover:bg-white transition-all">
              <div class="flex items-center justify-between text-left">
                <label class="font-bold text-xs text-slate-850">API 服务地址</label>
                <span class="text-[10px] font-mono font-bold text-indigo-600">WebSocket & HTTP</span>
              </div>
              <div class="flex items-center gap-2">
                <div class="relative flex-1">
                  <input 
                    v-model="systemConfig.backendApiUrl"
                    type="text"
                    :disabled="systemConfig.isSimulationActive"
                    placeholder="e.g. http://localhost:5000"
                    class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 pl-8 text-slate-800 font-bold font-mono outline-none text-xs focus:bg-white focus:border-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed"
                  />
                  <Code class="absolute left-2.5 top-3.5 w-4 h-4 text-slate-400" />
                </div>
                <div class="bg-indigo-50 px-3 py-2.5 rounded-lg border border-indigo-150 text-indigo-700 text-xs font-mono font-bold select-none whitespace-nowrap">
                  PORT: 5000
                </div>
              </div>
            </div>
          </div>
        </div>
        
        <!-- MODULE 1: System Title & UI settings -->
        <div class="bg-white border border-slate-200 rounded-xl p-5 shadow-xs space-y-4">
          <h3 class="font-bold text-xs text-slate-900 border-b border-slate-100 pb-2.5 flex items-center gap-2">
            <Eye class="w-4 h-4 text-emerald-600" />
            界面设置
          </h3>

          <div class="space-y-3.5 text-xs font-sans">
            <div>
              <label class="font-bold text-slate-500 block mb-1">系统标题</label>
              <input 
                v-model="systemConfig.systemTitle"
                type="text"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 focus:bg-white text-slate-800 font-bold outline-none font-sans"
              />
            </div>

            <div>
              <label class="font-bold text-slate-500 block mb-1">数据刷新间隔</label>
              <div class="flex items-center gap-2">
                <input 
                  v-model.number="systemConfig.pollIntervalMs"
                  type="number"
                  step="100"
                  min="200"
                  class="w-32 bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 font-mono font-bold outline-none"
                />
                <span class="text-[11px] text-slate-500">毫秒 (ms)</span>
              </div>
            </div>
          </div>
        </div>

        <!-- MODULE 2: Security & alarms routing -->
        <div class="bg-white border border-slate-200 rounded-xl p-5 shadow-xs space-y-4">
          <h3 class="font-bold text-xs text-slate-900 border-b border-slate-100 pb-2.5 flex items-center gap-2">
            <ShieldAlert class="w-4 h-4 text-amber-500" />
            告警通知
          </h3>

          <div class="space-y-4 text-xs font-sans">
            <!-- Toggle alert email -->
            <div class="flex items-center justify-between p-2.5 bg-slate-50 rounded-lg">
              <div>
                <b class="text-slate-800 font-bold block">启用邮件告警通知</b>
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
              <label class="font-bold text-slate-500 block mb-1">告警邮箱地址</label>
              <div class="relative">
                <input 
                  v-model="systemConfig.alarmEmailAddress"
                  type="email"
                  class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 pl-9 text-slate-800 font-bold outline-none font-mono"
                  placeholder="alerts@factory.com"
                />
                <Mail class="absolute left-3 top-3.5 w-4 h-4 text-slate-400" />
              </div>
            </div>
          </div>
        </div>

        <!-- MODULE 3: 物联网数据中继与 OPC 通讯 -->
        <div class="bg-white border border-slate-200 rounded-xl p-5 shadow-xs space-y-4">
          <h3 class="font-bold text-xs text-slate-900 border-b border-slate-100 pb-2.5 flex items-center gap-2">
            <Radio class="w-4 h-4 text-[#1890ff]" />
            协议网关
          </h3>

          <div class="space-y-3.5 text-xs font-sans">
            <div class="grid grid-cols-3 gap-3">
              <div class="col-span-2">
                <label class="font-bold text-slate-500 block mb-1">MQTT Broker 地址</label>
                <input 
                  v-model="systemConfig.mqttBrokerHost"
                  type="text"
                  class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 font-bold font-mono outline-none"
                />
              </div>

              <div>
                <label class="font-bold text-slate-500 block mb-1">端口</label>
                <input 
                  v-model.number="systemConfig.mqttBrokerPort"
                  type="number"
                  class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 font-mono outline-none"
                />
              </div>
            </div>

            <div>
              <label class="font-bold text-slate-500 block mb-1">OPC-UA 发现地址</label>
              <input 
                v-model="systemConfig.opcUaDiscoveryUrl"
                type="text"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 focus:bg-white text-slate-800 font-bold font-mono outline-none"
              />
            </div>
          </div>
        </div>

        <!-- MODULE 4: 数据库清理等设置 -->
        <div class="bg-white border border-slate-200 rounded-xl p-5 shadow-xs space-y-4">
          <h3 class="font-bold text-xs text-slate-900 border-b border-slate-100 pb-2.5 flex items-center gap-2">
            <Layers class="w-4 h-4 text-purple-600" />
            数据保留
          </h3>

          <div class="space-y-3.5 text-xs font-sans">
            <div>
              <label class="font-bold text-slate-500 block mb-1">数据保留周期</label>
              <div class="flex items-center gap-2">
                <input 
                  v-model.number="systemConfig.retentionPeriodDays"
                  type="number"
                  class="w-24 bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 font-bold font-mono outline-none"
                />
                <span class="text-slate-400">天 · 超过期限的数据将被自动清理。</span>
              </div>
            </div>

            <div class="bg-amber-50/40 border border-amber-100 p-3 rounded-lg leading-relaxed text-amber-700">
              <span class="font-bold block pb-0.5 text-amber-800">注意：</span>
              较短的保留期限可提升查询性能，但可能影响长期趋势分析。
            </div>
          </div>
        </div>

      </div>

    </div>

  </div>
</template>
