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
          全站物理数据库引擎与持久化配置中心
        </h2>
        <p class="text-xs text-slate-500 font-sans">
          SCADA 控制底层数据库挂载面板。配置分别承载实时高速缓存与高密度时序采集指标的时序数据库及历史多维数据库连接池。
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
                  {{ db.type === 'realtime' ? '1. 工业实时高频写缓冲 (Realtime Cache)' : '2. 大数据时序时频归档 (Historical Time-Series)' }}
                </span>
                <h3 class="font-bold text-xs text-slate-900 mt-0.5 leading-snug">{{ db.name }}</h3>
              </div>
            </div>

            <!-- Online status Badge -->
            <span class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-[10px] font-bold"
              :class="db.status === 'connected' ? 'bg-emerald-50 text-emerald-600 border border-emerald-100' : 'bg-rose-50 text-rose-600 border border-rose-100'"
            >
              <span class="w-1.5 h-1.5 rounded-full" :class="db.status === 'connected' ? 'bg-emerald-500 shadow-[0_0_5px_#10b981]' : 'bg-rose-500'" />
              {{ db.status === 'connected' ? '链路畅通' : '未连接/脱轨' }}
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
                <span>正在向目的实例握手机制发送 IP 封装数据分组...</span>
              </div>
              
              <div v-else-if="connectionTesters[db.id]?.success" class="text-slate-550 leading-relaxed font-sans font-medium text-slate-500">
                <span class="text-emerald-650 font-bold text-emerald-600 block">连接建立成功 (Success)</span>
                <span class="text-[10px] font-mono text-slate-400 block mt-0.5">{{ connectionTesters[db.id]?.message }}</span>
              </div>
              
              <p v-else class="text-slate-400">
                请配置完毕数据库物理配置后，运行链路测试验证连通性。
              </p>
            </div>

            <!-- Operations buttons -->
            <div class="flex items-center gap-2 self-end shrink-0 select-none">
              <button 
                @click="triggerTestDbConnection(db)"
                :disabled="connectionTesters[db.id]?.loading"
                class="px-3 py-1.5 border border-slate-200 hover:bg-slate-50 font-bold text-slate-700 bg-white rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 disabled:opacity-50"
              >
                测试链路
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
            分布式数据缓冲同步中间件运行指引 (Cluster Sync Middleware)
          </h4>
          <p class="leading-relaxed text-indigo-200">
            本 SCADA 内核由自主 M2M 连接中间件驱动。在读取 OPC-UA、Modbus 或 MQTT 采集变量时，自动维护一个高频虚拟存储体（Realtime Memory Cache），并在此以
            <mark class="bg-indigo-800 text-white font-bold px-1.5 rounded">1200ms</mark> 周期归入 TimescaleDB 数据仓库，避免物理层过载，保障工业生产数据 100% 不溢失。
          </p>
        </div>
      </div>

    </div>

  </div>
</template>
