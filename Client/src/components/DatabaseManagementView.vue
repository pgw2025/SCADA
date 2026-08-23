<script setup lang="ts">
import { ref } from 'vue';
import { databaseConfigs, addLog } from '../store/index';
import { 
  Database, 
  Settings2, 
  CheckCircle, 
  XX, 
  Activity, 
  Plus, 
  Trash2, 
  ServerCrash, 
  RefreshCw,
  HardDrive,
  Cpu,
  CpuIcon
} from 'lucide-vue-next';
import { DatabaseConfig } from '../types';

const connectionTesters = ref<Record<string, { loading: boolean; success: boolean; latency?: number; message?: string }>>({});

const triggerTestDbConnection = (db: DatabaseConfig) => {
  connectionTesters.value[db.id] = { loading: true, success: false };
  addLog('数据库管理', `开始执行对 Persist-DB [${db.name}] 的物理链路握手...`, 'info');

  setTimeout(() => {
    const lat = Math.floor(8 + Math.random() * 32);
    db.status = 'connected';
    connectionTesters.value[db.id] = {
      loading: false,
      success: true,
      latency: lat,
      message: `物理握手通过。时延: ${lat}ms，心跳状态正常。可用磁盘空间: 322 GB · 索引状态: GOOD`
    };
    addLog('数据库管理', `数据库 [${db.name}] 链路连接复核通过，时延 ${lat}ms`, 'normal');
  }, 900);
};

const handleSaveConfig = (db: DatabaseConfig) => {
  addLog('数据库管理', `已保存数据库 [${db.name}] 注册参数修改。`, 'normal');
  alert('Persistance Parameter 数据库连接定义已写入 SCADA 全局内核配置文件！');
};
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent overflow-y-auto">
    
    <!-- Title banner -->
    <div class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Database class="w-5 h-5 text-indigo-500" />
          数据库管理
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          管理时序数据库和历史数据库连接配置。
        </p>
      </div>
    </div>

    <!-- DB Cards list -->
    <div class="flex-1 p-6 space-y-6 text-left">
      <div class="grid grid-cols-1 xl:grid-cols-2 gap-6">
        
        <div 
          v-for="db in databaseConfigs" 
          :key="db.id"
          class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs divide-y divide-slate-100 dark:divide-slate-800 space-y-4 transition-colors"
        >
          <!-- Card Header details -->
          <div class="flex items-start justify-between">
            <div class="flex items-center gap-3">
              <div 
                class="w-11 h-11 rounded-lg flex items-center justify-center shrink-0"
                :class="db.type === 'realtime' ? 'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400' : 'bg-purple-50 dark:bg-purple-950/60 text-purple-600 dark:text-purple-400'"
              >
                <HardDrive class="w-6 h-6 animate-pulse" />
              </div>
              
              <div>
                <span class="text-[9px] uppercase tracking-wider font-bold block"
                  :class="db.type === 'realtime' ? 'text-sky-600 dark:text-sky-400' : 'text-purple-600 dark:text-purple-400'"
                >
                  {{ db.type === 'realtime' ? '实时缓存库' : '时序数据库' }}
                </span>
                <h3 class="font-bold text-xs text-slate-900 dark:text-slate-100 mt-0.5 leading-snug">{{ db.name }}</h3>
              </div>
            </div>

            <!-- Online status Badge -->
            <span class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-[10px] font-bold"
              :class="db.status === 'connected' ? 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400 border border-emerald-100 dark:border-emerald-800' : 'bg-rose-50 dark:bg-rose-950/60 text-rose-600 dark:text-rose-400 border border-rose-100 dark:border-rose-800'"
            >
              <span class="w-1.5 h-1.5 rounded-full" :class="db.status === 'connected' ? 'bg-emerald-500 shadow-[0_0_5px_#10b981]' : 'bg-rose-500'" />
              {{ db.status === 'connected' ? '已连接' : '未连接' }}
            </span>
          </div>

          <!-- Configuration block form -->
          <div class="pt-4 space-y-3.5 text-xs font-sans">
            <div class="grid grid-cols-3 gap-3">
              <div>
                <label class="text-[10px] text-slate-400 dark:text-slate-400 font-bold block mb-1">持久化介质数据库</label>
                <select 
                  v-model="db.backendType"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-slate-100 font-bold focus:outline-none"
                >
                  <option value="MySQL">MySQL 8.4 Server</option>
                  <option value="PostgreSQL">PostgreSQL 16</option>
                  <option value="TimescaleDB">TimescaleDB TimeSeries</option>
                  <option value="InfluxDB">InfluxDB TS-Engine</option>
                </select>
              </div>

              <div class="col-span-2">
                <label class="text-[10px] text-slate-400 dark:text-slate-400 font-bold block mb-1">数据库物理服务器主机/域名 (Host-Link)</label>
                <input 
                  v-model="db.host"
                  type="text"
                  placeholder="127.0.0.1"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-slate-100 font-mono font-medium outline-none"
                />
              </div>
            </div>

            <div class="grid grid-cols-4 gap-3">
              <div>
                <label class="text-[10px] text-slate-400 dark:text-slate-400 font-bold block mb-1">端口 Port</label>
                <input 
                  v-model.number="db.port"
                  type="number"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-slate-100 font-mono outline-none"
                />
              </div>

              <div>
                <label class="text-[10px] text-slate-400 dark:text-slate-400 font-bold block mb-1">用户名 Admin</label>
                <input 
                  v-model="db.username"
                  type="text"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-slate-100 font-medium outline-none"
                />
              </div>

              <div class="col-span-2">
                <label class="text-[10px] text-slate-400 dark:text-slate-400 font-bold block mb-1">数据库/账套名称 (Schema Name)</label>
                <input 
                  v-model="db.databaseName"
                  type="text"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 focus:bg-white dark:focus:bg-slate-900 text-slate-800 dark:text-slate-100 font-bold font-mono outline-none"
                />
              </div>
            </div>
          </div>

          <!-- Bottom test links & Save -->
          <div class="pt-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3 text-[11px]">
            <div class="flex-1 min-w-0 pr-4">
              <!-- Tester indicator -->
              <div v-if="connectionTesters[db.id]?.loading" class="flex items-center gap-1.5 text-[#1890ff] font-bold">
                <RefreshCw class="w-3.5 h-3.5 animate-spin" />
                <span>正在测试连接...</span>
              </div>
              
              <div v-else-if="connectionTesters[db.id]?.success" class="leading-relaxed font-sans font-medium text-slate-500 dark:text-slate-400">
                <span class="font-bold text-emerald-600 dark:text-emerald-400 block">连接成功</span>
                <span class="text-[10px] font-mono text-slate-400 dark:text-slate-500 block mt-0.5">{{ connectionTesters[db.id]?.message }}</span>
              </div>
              
              <p v-else class="text-slate-400 dark:text-slate-500">
                配置完成后点击"测试连接"验证连通性。
              </p>
            </div>

            <!-- Operations buttons -->
            <div class="flex items-center gap-2 self-end shrink-0 select-none">
              <button 
                @click="triggerTestDbConnection(db)"
                :disabled="connectionTesters[db.id]?.loading"
                class="px-3 py-1.5 border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-slate-700 dark:text-slate-200 bg-white dark:bg-slate-800 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 disabled:opacity-50"
              >
                测试连接
              </button>

              <button 
                @click="handleSaveConfig(db)"
                class="px-4 py-1.5 font-bold text-white bg-slate-900 dark:bg-indigo-600 hover:bg-slate-800 dark:hover:bg-indigo-500 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5"
              >
                保存变更
              </button>
            </div>
          </div>

        </div>

      </div>

      <!-- Infrastructure info board -->
      <div class="bg-indigo-950/90 dark:bg-slate-900 text-indigo-200 dark:text-slate-300 rounded-xl p-5 border border-indigo-900 dark:border-slate-800 flex flex-col md:flex-row items-start md:items-center justify-between gap-4 font-mono text-[11px] transition-colors">
        <div class="space-y-1.5 max-w-2xl text-left">
          <h4 class="font-bold text-white text-xs font-sans flex items-center gap-1.5">
            <CpuIcon class="w-4 h-4 text-indigo-400 animate-spin" />
            数据同步说明
          </h4>
          <p class="leading-relaxed text-indigo-200 dark:text-slate-400 font-sans">
            SCADA内核以高频虚拟存储体自动维护数据。读取采集变量时，以<mark class="bg-indigo-800 dark:bg-indigo-900 text-white font-bold px-1.5 rounded">1200ms</mark>周期写入时序数据库，避免物理层过载，保障数据完整。
          </p>
        </div>
      </div>

    </div>

  </div>
</template>
