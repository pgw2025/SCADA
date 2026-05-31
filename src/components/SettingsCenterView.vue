<script setup lang="ts">
import { ref } from 'vue';
import { systemConfig, addLog } from '../store';
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
          SCADA 全新物联控制及系统参数配置中心 (Settings Console)
        </h2>
        <p class="text-xs text-slate-500 font-sans">
          高度集中的内核参数控制底盘。支持编辑全局轮询心跳时长、MQTT 消息中间网桥服务器地址和全车间异常邮件告警推送地址。
        </p>
      </div>

      <!-- Save settings -->
      <button 
        @click="handleSaveSettings"
        :disabled="isSaving"
        class="font-bold text-xs bg-slate-900 text-white hover:bg-slate-800 px-5  py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-sm active:translate-y-0.5"
      >
        <Save class="w-4 h-4" />
        {{ isSaving ? '应用并重整内核中...' : '保存应用全局配置' }}
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
        
        <!-- MODULE 1: System Title & UI settings -->
        <div class="bg-white border border-slate-200 rounded-xl p-5 shadow-xs space-y-4">
          <h3 class="font-bold text-xs text-slate-900 border-b border-slate-100 pb-2.5 flex items-center gap-2">
            <Eye class="w-4 h-4 text-emerald-600" />
            1. 品牌定义与界面视效配置 (Visual Settings)
          </h3>

          <div class="space-y-3.5 text-xs font-sans">
            <div>
              <label class="font-bold text-slate-500 block mb-1">SCADA 后台物联系统主标题</label>
              <input 
                v-model="systemConfig.systemTitle"
                type="text"
                class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 focus:bg-white text-slate-800 font-bold outline-none font-sans"
              />
            </div>

            <div>
              <label class="font-bold text-slate-500 block mb-1">物理轮询采集心跳刷新间隔 (Polling Delay)</label>
              <div class="flex items-center gap-2">
                <input 
                  v-model.number="systemConfig.pollIntervalMs"
                  type="number"
                  step="100"
                  min="200"
                  class="w-32 bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 font-mono font-bold outline-none"
                />
                <span class="text-[11px] text-slate-500">毫秒 (ms) - 较低延迟将使组态看板图表拟合极尽丝滑</span>
              </div>
            </div>
          </div>
        </div>

        <!-- MODULE 2: Security & alarms routing -->
        <div class="bg-white border border-slate-200 rounded-xl p-5 shadow-xs space-y-4">
          <h3 class="font-bold text-xs text-slate-900 border-b border-slate-100 pb-2.5 flex items-center gap-2">
            <ShieldAlert class="w-4 h-4 text-amber-500" />
            2. 系统异常越界 SMTP 邮件推送服务 (Alarm Mailer)
          </h3>

          <div class="space-y-4 text-xs font-sans">
            <!-- Toggle alert email -->
            <div class="flex items-center justify-between p-2.5 bg-slate-50 rounded-lg">
              <div>
                <b class="text-slate-800 font-bold block">启用工艺寄存器溢限邮件通知</b>
                <span class="text-[10px] text-slate-400 block font-normal mt-0.5">当任何触发器告警燃沸，联动发送通知</span>
              </div>
              
              <input 
                type="checkbox" 
                v-model="systemConfig.alarmEmailNotify"
                class="accent-slate-900 w-5 h-5 cursor-pointer"
              />
            </div>

            <!-- Receiver address -->
            <div :class="!systemConfig.alarmEmailNotify ? 'opacity-40 pointer-events-none' : ''" class="transition-opacity">
              <label class="font-bold text-slate-500 block mb-1">告警中继收件群组邮箱 (SMTP Mail Register)</label>
              <div class="relative">
                <input 
                  v-model="systemConfig.alarmEmailAddress"
                  type="email"
                  class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 pl-9 text-slate-800 font-bold outline-none font-mono"
                  placeholder="alerts@iota-factory.com"
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
            3. 现场物联总线及协议网关 (Industrial Protocols)
          </h3>

          <div class="space-y-3.5 text-xs font-sans">
            <div class="grid grid-cols-3 gap-3">
              <div class="col-span-2">
                <label class="font-bold text-slate-500 block mb-1">MQTT 消息代理服务代理主干 Host</label>
                <input 
                  v-model="systemConfig.mqttBrokerHost"
                  type="text"
                  class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 font-bold font-mono outline-none"
                />
              </div>

              <div>
                <label class="font-bold text-slate-500 block mb-1">物理端口 Port</label>
                <input 
                  v-model.number="systemConfig.mqttBrokerPort"
                  type="number"
                  class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 font-mono outline-none"
                />
              </div>
            </div>

            <div>
              <label class="font-bold text-slate-500 block mb-1">OPC-UA 主动寻踪注册链路 URL (Endpoint URI)</label>
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
            4. 自助物理介质与存储持久化寿命 (Data Pruner)
          </h3>

          <div class="space-y-3.5 text-xs font-sans">
            <div>
              <label class="font-bold text-slate-500 block mb-1">时序大数据流物理表最长保留周期 (Days)</label>
              <div class="flex items-center gap-2">
                <input 
                  v-model.number="systemConfig.retentionPeriodDays"
                  type="number"
                  class="w-24 bg-slate-50 border border-slate-200 rounded-lg p-2 focus:bg-white text-slate-800 font-bold font-mono outline-none"
                />
                <span class="text-slate-400">天数 · 超过该时域的数据将被时序清理脚本定期自动剪裁。</span>
              </div>
            </div>

            <div class="bg-rose-50/40 border border-rose-100 p-3 rounded-lg leading-relaxed text-rose-700">
              <span class="font-bold block pb-0.5 text-rose-800">⚠️ 时序自削危险提示：</span>
              缩短保留天数可最大程度提升时序计算表的聚合查询时延；但过短的天数可能会破坏工艺长期报表的审查和大数据趋势。
            </div>
          </div>
        </div>

      </div>

    </div>

  </div>
</template>
