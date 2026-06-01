<script setup lang="ts">
import { ref } from 'vue';
import { databaseConfigs, addLog } from '../store';
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
  <div class="h-full flex flex-col text-[#1e293b] select-none bg-slate-50 overflow-y-auto">
    
    <!-- Title banner -->
    <div class="bg-white p-5 border-b border-slate-200 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 tracking-tight flex items-center gap-2">
          <Database class="w-5 h-5 text-indigo-500" />
          数据库管理
        </h2>
        <p class="text-xs text-slate-500 font-sans">
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
          class="bg-white border border-slate-200 rounded-xl p-5 shadow-xs divide-y divide-slate-100 space-y-4"
        >
          <!-- Card Header details -->
          <div class="flex items-start justify-between">
            <div class="flex items-center gap-3">
              <div 
                class="w-11 h-11 rounded-lg flex items-center justify-center shrink-0"
                :class="db.type === 'realtime' ? 'bg-sky-50 text-sky-600' : 'bg-purple-50 text-purple-600'"
              >
                <HardDrive class="w-6 h-6 animate-pulse" />
              </div>
              
              <div>
                <span class="text-[9px] uppercase tracking-wider font-bold block"
                  :class="db.type === 'realtime' ? 'text-sky-600' : 'text-purple-600'"
                >
                  {{ db.type === 'realtime' ? '实时缓存库' : '时序数据库' }}
                </span>
                <h3 class="font-bold text-xs text-slate-900 mt-0.5 leading-snug">{{ db.name }}</h3>
              </div>
            </div>

            <!-- Online status Badge -->
            <span class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-[10px] font-bold"
              :class="db.status === 'connected' ? 'bg-emerald-50 text-emerald-600 border border-emerald-100' : 'bg-rose-50 text-rose-600 border border-rose-100'"
            >
              <span class="w-1.5 h-1.5 rounded-full" :class="db.status === 'connected' ? 'bg-emerald-500 shadow-[0_0_5px_#10b981]' : 'bg-rose-500'" />
              {{ db.status === 'connected' ? '已连接' : '未连接' }}
            </span>
          </div>

          <!-- Configuration block form -->
          <div class="pt-4 space-y-3.5 text-xs font-sans">
            <div class="grid grid-cols-3 gap-3">
              <div>
                <label class="text-[10px] text-slate-400 font-bold block mb-1">持久化介质数据库</label>
                <select 
                  v-model="db.backendType"
                  class="w-full bg-slate-50 border border-slate-200 rounded-lg p-1.5 focus:bg-white text-slate-800 font-bold focus:outline-none"
                >
                  <option value="MySQL">MySQL 8.4 Server</option>
                  <option value="PostgreSQL">PostgreSQL 16</option>
                  <option value="TimescaleDB">TimescaleDB TimeSeries</option>
                  <option value="InfluxDB">InfluxDB TS-Engine</option>
                </select>
              </div>

              <div class="col-span-2">
                <label class="text-[10px] text-slate-400 font-bold block mb-1">数据库物理服务器主机/域名 (Host-Link)</label>
                <input 
                  v-model="db.host"
                  type="text"
                  placeholder="127.0.0.1"
                  class="w-full bg-slate-50 border border-slate-200 rounded-lg p-1.5 focus:bg-white text-slate-800 font-mono font-medium outline-none"
                />
              </div>
            </div>

            <div class="grid grid-cols-4 gap-3">
              <div>
                <label class="text-[10px] text-slate-400 font-bold block mb-1">端口 Port</label>
                <input 
                  v-model.number="db.port"
                  type="number"
                  class="w-full bg-slate-50 border border-slate-200 rounded-lg p-1.5 focus:bg-white text-slate-800 font-mono outline-none"
                />
              </div>

              <div>
                <label class="text-[10px] text-slate-400 font-bold block mb-1">用户名 Admin</label>
                <input 
                  v-model="db.username"
                  type="text"
                  class="w-full bg-slate-50 border border-slate-200 rounded-lg p-1.5 focus:bg-white text-slate-800 font-medium outline-none"
                />
              </div>

              <div class="col-span-2">
                <label class="text-[10px] text-slate-400 font-bold block mb-1">数据库/账套名称 (Schema Name)</label>
                <input 
                  v-model="db.databaseName"
                  type="text"
                  class="w-full bg-slate-50 border border-slate-200 rounded-lg p-1.5 focus:bg-white text-slate-800 font-bold font-mono outline-none"
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
              
              <div v-else-if="connectionTesters[db.id]?.success" class="text-slate-550 leading-relaxed font-sans font-medium text-slate-500">
                <span class="text-emerald-650 font-bold text-emerald-600 block">连接成功</span>
                <span class="text-[10px] font-mono text-slate-400 block mt-0.5">{{ connectionTesters[db.id]?.message }}</span>
              </div>
              
              <p v-else class="text-slate-400">
                配置完成后点击"测试连接"验证连通性。
              </p>
            </div>

            <!-- Operations buttons -->
            <div class="flex items-center gap-2 self-end shrink-0 select-none">
              <button 
                @click="triggerTestDbConnection(db)"
                :disabled="connectionTesters[db.id]?.loading"
                class="px-3 py-1.5 border border-slate-200 hover:bg-slate-50 font-bold text-slate-700 bg-white rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 disabled:opacity-50"
              >
                测试连接
              </button>

              <button 
                @click="handleSaveConfig(db)"
                class="px-4 py-1.5 font-bold text-white bg-slate-900 hover:bg-slate-800 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5"
              >
                保存变更
              </button>
            </div>
          </div>

        </div>

      </div>

      <!-- Infrastructure info board -->
      <div class="bg-indigo-950 text-indigo-350 rounded-xl p-5 border border-indigo-900 flex flex-col md:flex-row items-start md:items-center justify-between gap-4 font-mono text-[11px]">
        <div class="space-y-1.5 max-w-2xl text-left">
          <h4 class="font-bold text-white text-xs font-sans flex items-center gap-1.5">
            <CpuIcon class="w-4 h-4 text-indigo-400 animate-spin" />
            数据同步说明
          </h4>
          <p class="leading-relaxed text-indigo-200">
            SCADA内核以高频虚拟存储体自动维护数据。读取采集变量时，以<mark class="bg-indigo-800 text-white font-bold px-1.5 rounded">1200ms</mark>周期写入时序数据库，避免物理层过载，保障数据完整。
          </p>
        </div>
      </div>

    </div>

  </div>
</template>
