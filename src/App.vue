<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue';
import { 
  activeTab, 
  isAuthenticated,
  loginUser,
  performLogin,
  performLogout,
  systemConfig,
  initializeAuth
  } from './store';
import { initializeRealtimeSignals } from './services/signalRService';
import { startBackendPolling } from './services/pollService';
import { startSystemResourceMonitoring } from './services/systemService';

// Core static views
import DashboardView from './components/DashboardView.vue';
import LiveDataView from './components/LiveDataView.vue';
import DeviceManagementView from './components/DeviceManagementView.vue';
import DataModelView from './components/DataModelView.vue';
import ScadaTopologyView from './components/ScadaTopologyView.vue';
import SystemLogsView from './components/SystemLogsView.vue';

// Extended Industrial plugin views
import TriggerManagementView from './components/TriggerManagementView.vue';
import TaskManagementView from './components/TaskManagementView.vue';
import SystemScriptsView from './components/SystemScriptsView.vue';
import DataInterfacesView from './components/DataInterfacesView.vue';
import HistoricalQueryView from './components/HistoricalQueryView.vue';
import DatabaseManagementView from './components/DatabaseManagementView.vue';
import SettingsCenterView from './components/SettingsCenterView.vue';
import MqttServersView from './components/MqttServersView.vue';
import DataConversionView from './components/DataConversionView.vue';
import UserManagementView from './components/UserManagementView.vue';

import { 
  LayoutDashboard, 
  Database, 
  Cpu, 
  Layers, 
  MonitorPlay, 
  Terminal, 
  Clock, 
  UserCheck, 
  ShieldAlert,
  Server,
  Calendar,
  FileCode,
  Network,
  History,
  HardDrive,
  Settings,
  LogOut,
  Menu,
  X,
  Lock,
  Eye,
  Activity,
  ChevronLeft,
  ChevronRight,
  Rss,
  Shuffle,
  Users
} from 'lucide-vue-next';

// Responsive sidebar state for mobile drawers
const isMobileSidebarOpen = ref(false);

// PC sidebar state: collapsible
const isSidebarCollapsed = ref(false);

// Login state bindings
const loginUsernameInput = ref('admin');
const loginPasswordInput = ref('admin888');
const loginErrorMessage = ref('');

const triggerFormLogin = async () => {
  loginErrorMessage.value = '';
  const result = await performLogin(loginUsernameInput.value.trim(), loginPasswordInput.value.trim());
  if (!result.success) {
    loginErrorMessage.value = result.errorMessage || '登录失败，请检查网络连接';
  }
};

const triggerBypassLogin = () => {
  loginUsernameInput.value = 'admin';
  loginPasswordInput.value = '123456';
  triggerFormLogin();
};

// Human timestamp for the top control bar
const currentLocalTime = ref<string>('');
let clockInterval: any = null;

const startClock = () => {
  const pad = (n: number) => n.toString().padStart(2, '0');
  const update = () => {
    const d = new Date();
    currentLocalTime.value = `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
  };
  update();
  clockInterval = setInterval(update, 1000);
};

import { syncAreas } from './services/areaService';
import { fetchDataModelsFromBackend } from './api/modelApi';

onMounted(async () => {
  // 初始化认证状态（检查 localStorage 中的 Token）
  initializeAuth();
  
  startClock();
  // Fire off the background SCADA physics loops & hardware load simulations
  startSystemResourceMonitoring();
  initializeRealtimeSignals();
  startBackendPolling();
  
  // 初始化全局基础数据
  await Promise.all([
    syncAreas(),
    fetchDataModelsFromBackend()
  ]);
});

onUnmounted(() => {
  if (clockInterval) clearInterval(clockInterval);
});
</script>

<template>
  <!-- 1. AUTHENTICATION LOCK SCREEN (Logged out state) -->
  <div 
    v-if="!isAuthenticated" 
    class="h-screen w-screen bg-slate-950 flex items-center justify-center p-4 relative overflow-hidden font-sans select-none"
  >
    <!-- Background grid & floating technical lights -->
    <div class="absolute inset-0 bg-[radial-gradient(#1e293b_1px,transparent_1px)] [background-size:24px_24px] opacity-35" />
    <div class="absolute w-96 h-96 rounded-full bg-indigo-650/10 blur-[120px] top-10 left-10 animate-pulse pointer-events-none" />
    <div class="absolute w-96 h-96 rounded-full bg-sky-500/5 blur-[125px] bottom-10 right-10 pointer-events-none" />

    <!-- Centered login card -->
    <div class="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl overflow-hidden relative z-10 text-left animate-in fade-in zoom-in-95 duration-200">
      
      <!-- Brand banner inside login -->
      <div class="bg-slate-950 p-6 flex flex-col items-center justify-center border-b border-slate-800 text-center gap-3">
          <div class="w-12 h-12 rounded-xl bg-gradient-to-tr from-sky-600 to-indigo-600 flex items-center justify-center shadow-lg">
            <Server class="w-6 h-6 text-white animate-pulse" />
          </div>
          <div>
            <h1 class="text-sm font-black tracking-widest text-white uppercase">IOTA-SCADA 系统</h1>
            <span class="text-[10px] text-slate-400 font-medium tracking-wide mt-1 block">工业控制与数据采集平台</span>
          </div>
        </div>

      <!-- Login inputs form -->
      <form @submit.prevent="triggerFormLogin" class="p-6 space-y-4">
        
        <!-- Error print -->
        <div v-if="loginErrorMessage" class="p-3 rounded-lg bg-rose-950/40 border border-rose-800 text-rose-300 text-xs font-medium leading-relaxed font-sans text-center">
          {{ loginErrorMessage }}
        </div>

        <div>
          <label class="block text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-1 font-mono">用户名</label>
          <input 
            v-model="loginUsernameInput"
            type="text"
            required
            class="w-full bg-slate-950 border border-slate-800 rounded-lg p-2.5 text-xs font-bold text-white outline-none focus:border-sky-500 transition-colors"
            placeholder="用户名"
          />
        </div>

        <div>
          <label class="block text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-1 font-mono">密码</label>
          <div class="relative">
            <input 
              v-model="loginPasswordInput"
              type="password"
              required
              class="w-full bg-slate-950 border border-slate-800 rounded-lg p-2.5 pl-9 text-xs font-mono font-bold text-white outline-none focus:border-sky-500 transition-colors"
              placeholder="••••••••"
            />
            <Lock class="absolute left-3 top-3 w-4.5 h-4.5 text-slate-500" />
          </div>
        </div>

        <!-- Submit btn -->
        <button type="submit" 
          class="w-full py-2.5 bg-sky-600 hover:bg-sky-500 text-white font-bold text-xs rounded-lg transition-transform active:scale-95 cursor-pointer mt-2"
        >
          登录
        </button>

        <div class="border-t border-slate-800 pt-4 mt-2 grid grid-cols-1 gap-2.5 text-center">
          <div class="text-[10px] text-slate-500 font-sans leading-relaxed">
            默认账户: <button type="button" @click="loginUsernameInput='admin'; loginPasswordInput='123456'" class="text-sky-400 hover:underline">admin</button> / <button type="button" @click="loginUsernameInput='admin'; loginPasswordInput='admin888'" class="text-sky-400 hover:underline">admin888</button>
          </div>

          <!-- Quick bypass button for high fidelity UX -->
          <button 
            type="button"
            @click="triggerBypassLogin"
            class="py-1.5 border border-slate-800 hover:bg-slate-805/30 bg-slate-850/10 text-slate-350 hover:text-white rounded-lg text-[10px] font-bold uppercase tracking-wider inline-flex items-center justify-center gap-1.5 transition-colors cursor-pointer"
          >
            <UserCheck class="w-3.5 h-3.5" />
            快速登录
          </button>
        </div>

      </form>
    </div>
  </div>

  <!-- 2. MAIN LOGGED-IN SCADA DASHBOARD WORKSPACE -->
  <div 
    v-else 
    class="h-screen w-screen flex flex-col font-sans text-slate-800 bg-slate-150 overflow-hidden select-none"
  >
    
    <!-- SYSTEM TOP DECK HEADER -->
    <header class="h-14 bg-[#0b0f19] text-white border-b border-slate-950 px-4 flex items-center justify-between shrink-0 shadow-lg relative z-30">
      
      <!-- Brand Launcher with dynamic config title -->
      <div class="flex items-center gap-3">
        <!-- Hamburg Menu (on small displays) -->
        <button 
          @click="isMobileSidebarOpen = !isMobileSidebarOpen"
          class="lg:hidden p-1.5 rounded-lg border border-slate-800 text-slate-300 hover:bg-slate-900 active:scale-95 transition-all outline-none cursor-pointer"
        >
          <Menu v-if="!isMobileSidebarOpen" class="w-4.5 h-4.5" />
          <X v-else class="w-4.5 h-4.5" />
        </button>

        <div class="w-9 h-9 rounded-lg bg-gradient-to-tr from-sky-600 to-indigo-600 flex items-center justify-center shadow-md shrink-0">
          <Server class="w-5 h-5 text-white animate-pulse" />
        </div>
        
        <div class="text-left">
          <h1 class="text-xs sm:text-sm font-black tracking-wider uppercase flex items-center gap-2 leading-none text-slate-50">
            {{ systemConfig.systemTitle }}
            <span class="text-[9px] bg-slate-900 text-[#1890ff] font-bold px-1.5 py-0.5 rounded border border-slate-800 select-none font-mono hidden sm:inline-block">
              V6.0
            </span>
          </h1>
          <span class="text-[9px] sm:text-[10px] text-slate-400 leading-none mt-1 inline-block select-all">
            SCADA 控制中心
          </span>
        </div>
      </div>

      <!-- Real-time Status Badge & clock -->
      <div class="flex items-center gap-3 text-[10px] sm:text-xs font-mono">
        <!-- Human clock -->
        <div class="hidden md:flex items-center gap-1.5 text-slate-300 bg-[#111827] border border-slate-850 px-3 py-1 rounded-lg">
          <Clock class="w-3.5 h-3.5 text-slate-400" />
          <span>{{ currentLocalTime || '正在同步时钟...' }}</span>
        </div>

        <!-- Master active state -->
          <div class="flex items-center gap-1.5 bg-[#10b981]/10 text-emerald-400 border border-[#10b981]/25 px-2 py-0.5 sm:py-1 rounded-lg">
            <span class="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-ping"></span>
            <span class="font-bold uppercase tracking-wider text-[8px] sm:text-[9px]">系统运行中</span>
          </div>
      </div>
    </header>

    <!-- CENTRAL SPLIT SCREEN VIEW -->
    <div class="flex-1 flex overflow-hidden relative">
      
      <!-- 2.1 DESKTOP NAVIGATION SIDEBAR (Visible on lg:flex) -->
      <aside 
        class="hidden lg:flex bg-[#0f172a] text-slate-350 border-r border-[#090d16] flex-col justify-between shrink-0 select-none relative z-20 transition-all duration-300"
        :class="isSidebarCollapsed ? 'w-16' : 'w-64'"
      >
        <!-- Collapse/Expand trigger button -->
        <div class="px-2 py-2 border-b border-slate-800/40 flex items-center justify-center shrink-0">
          <button 
            @click="isSidebarCollapsed = !isSidebarCollapsed" 
            class="w-full py-1.5 hover:bg-slate-800 bg-slate-900 border border-slate-800/60 rounded-lg text-[10px] font-bold text-slate-400 hover:text-white flex items-center justify-center gap-1.5 cursor-pointer transition-all active:scale-95"
            :title="isSidebarCollapsed ? '展开导航' : '收起导航'"
          >
            <ChevronRight v-if="isSidebarCollapsed" class="w-4 h-4 text-slate-400" />
            <template v-else>
              <ChevronLeft class="w-4 h-4 text-slate-400" />
              <span>收起导航</span>
            </template>
          </button>
        </div>

        <div class="flex-1 flex flex-col pt-3 overflow-y-auto space-y-2.5 pb-4">
          
          <!-- Category A: Core Control Panel -->
          <div>
            <span v-if="!isSidebarCollapsed" class="text-[9px] font-bold text-slate-500 uppercase tracking-widest px-4 py-1 select-none text-left block">
              监控中心
            </span>
            <div v-else class="h-px bg-slate-800/40 mx-2 my-1" />

            <nav class="space-y-0.5 px-2">
              <button 
                @click="activeTab = 'dashboard'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left cursor-pointer group w-full"
                :title="isSidebarCollapsed ? '仪表盘' : ''"
                :class="[
                  activeTab === 'dashboard' ? 'bg-slate-800 text-white font-bold' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'dashboard' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <LayoutDashboard class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">仪表盘</span>
              </button>

              <button 
                @click="activeTab = 'live-data'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left cursor-pointer group w-full"
                :title="isSidebarCollapsed ? '实时监控' : ''"
                :class="[
                  activeTab === 'live-data' ? 'bg-slate-800 text-white font-bold' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'live-data' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <Database class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">实时监控</span>
              </button>

              <button 
                @click="activeTab = 'device-management'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '设备管理' : ''"
                :class="[
                  activeTab === 'device-management' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'device-management' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <Cpu class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">设备管理</span>
              </button>

              <button 
                @click="activeTab = 'data-models'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '数据模型' : ''"
                :class="[
                  activeTab === 'data-models' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'data-models' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <Layers class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">数据模型</span>
              </button>

              <button 
                @click="activeTab = 'scada-editor'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '组态设计' : ''"
                :class="[
                  activeTab === 'scada-editor' ? 'bg-slate-800 text-white font-bold' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'scada-editor' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <MonitorPlay class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">组态设计</span>
              </button>

              <button 
                @click="activeTab = 'system-logs'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '系统日志' : ''"
                :class="[
                  activeTab === 'system-logs' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'system-logs' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <Terminal class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">系统日志</span>
              </button>
            </nav>
          </div>

          <!-- Category B: Automation & SCADA plugins -->
          <div>
            <span v-if="!isSidebarCollapsed" class="text-[9px] font-bold text-slate-500 uppercase tracking-widest px-4 py-1 select-none text-left block">
              自动化
            </span>
            <div v-else class="h-px bg-slate-800/40 mx-2 my-1" />

            <nav class="space-y-0.5 px-2">
              <button 
                @click="activeTab = 'trigger-management'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '告警管理' : ''"
                :class="[
                  activeTab === 'trigger-management' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'trigger-management' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <ShieldAlert class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">告警管理</span>
              </button>

              <button 
                @click="activeTab = 'task-management'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '任务调度' : ''"
                :class="[
                  activeTab === 'task-management' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'task-management' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <Calendar class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">任务调度</span>
              </button>

              <button 
                @click="activeTab = 'system-scripts'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '脚本引擎' : ''"
                :class="[
                  activeTab === 'system-scripts' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'system-scripts' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <FileCode class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">脚本引擎</span>
              </button>

              <button 
                @click="activeTab = 'data-interfaces'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '接口管理' : ''"
                :class="[
                  activeTab === 'data-interfaces' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'data-interfaces' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <Network class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">接口管理</span>
              </button>

              <button 
                @click="activeTab = 'historical-query'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '历史数据' : ''"
                :class="[
                  activeTab === 'historical-query' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'historical-query' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <History class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">历史数据</span>
              </button>

              <button 
                @click="activeTab = 'mqtt-servers'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? 'MQTT代理' : ''"
                :class="[
                  activeTab === 'mqtt-servers' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'mqtt-servers' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <Rss class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">MQTT代理</span>
              </button>

              <button 
                @click="activeTab = 'data-conversion'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '数据转换' : ''"
                :class="[
                  activeTab === 'data-conversion' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'data-conversion' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <Shuffle class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">数据转换</span>
              </button>
            </nav>
          </div>

          <!-- Category C: Core System configuration -->
          <div>
            <span v-if="!isSidebarCollapsed" class="text-[9px] font-bold text-slate-500 uppercase tracking-widest px-4 py-1 select-none text-left block">
              系统设置
            </span>
            <div v-else class="h-px bg-slate-800/40 mx-2 my-1" />

            <nav class="space-y-0.5 px-2">
              <button 
                @click="activeTab = 'database-management'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '数据库管理' : ''"
                :class="[
                  activeTab === 'database-management' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'database-management' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <HardDrive class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">数据库管理</span>
              </button>

              <button 
                @click="activeTab = 'user-management'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '用户管理' : ''"
                :class="[
                  activeTab === 'user-management' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'user-management' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <Users class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">用户管理</span>
              </button>

              <button 
                @click="activeTab = 'settings-center'"
                class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full"
                :title="isSidebarCollapsed ? '系统配置' : ''"
                :class="[
                  activeTab === 'settings-center' ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white',
                  isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent',
                  !isSidebarCollapsed && activeTab === 'settings-center' ? 'border-l-[#1890ff]' : ''
                ]"
              >
                <Settings class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">系统配置</span>
              </button>
            </nav>
          </div>

        </div>

        <!-- OPERATOR PROFILE & LOGOUT -->
        <div class="p-3 bg-slate-950 border-t border-slate-900 flex shrink-0 justify-between items-center select-none">
          <div v-if="!isSidebarCollapsed" class="flex items-center gap-2 font-sans overflow-hidden">
            <div class="w-7.5 h-7.5 rounded-full bg-slate-800 border border-slate-700 flex items-center justify-center relative shrink-0">
              <UserCheck class="w-3.5 h-3.5 text-sky-450 text-sky-400" />
              <span class="absolute bottom-0 right-0 w-2 h-2 bg-emerald-500 rounded-full border-2 border-slate-950"></span>
            </div>
            
            <div class="text-left overflow-hidden w-28 shrink-0">
              <h4 class="text-[11px] font-bold text-white truncate">{{ loginUser?.username || 'admin' }}</h4>
              <span class="text-[9px] text-slate-400 block truncate">{{ loginUser?.role || '管理员' }}</span>
            </div>
          </div>

          <button 
            v-if="!isSidebarCollapsed"
            @click="performLogout"
            class="p-1.5 hover:bg-rose-900/30 rounded-lg text-slate-500 hover:text-rose-400 transition-colors cursor-pointer"
            title="退出"
          >
            <LogOut class="w-4 h-4" />
          </button>

          <!-- Collapsed Profile Avatar & Vertical Stack -->
          <div v-else class="flex flex-col items-center gap-3.5 py-1 w-full shrink-0">
            <div class="w-8 h-8 rounded-full bg-slate-800 border border-slate-700 flex items-center justify-center relative shrink-0" :title="(loginUser?.username || 'admin') + ' · ' + (loginUser?.role || '管理员')">
              <UserCheck class="w-4 h-4 text-sky-450 text-sky-400" />
              <span class="absolute bottom-0 right-0 w-2 h-2 bg-emerald-500 rounded-full border-2 border-slate-950"></span>
            </div>
            <button 
              @click="performLogout"
              class="p-1.5 hover:bg-rose-900/20 rounded-lg text-slate-550 hover:text-[#ef4444] transition-colors cursor-pointer"
              title="退出"
            >
              <LogOut class="w-4 h-4" />
            </button>
          </div>
        </div>

      </aside>

      <!-- 2.2 POPUP DRAWER MOBILE SIDEBAR (Toggled via menu button) -->
      <div 
        v-if="isMobileSidebarOpen" 
        @click="isMobileSidebarOpen = false" 
        class="fixed inset-0 bg-slate-950/60 backdrop-blur-xs z-40 lg:hidden"
      />
      
      <aside 
        class="fixed inset-y-0 left-0 w-64 bg-[#0f172a] text-slate-300 z-50 flex flex-col justify-between transition-transform duration-300 lg:hidden select-none"
        :class="isMobileSidebarOpen ? 'translate-x-0' : '-translate-x-full'"
      >
        <div class="flex-1 flex flex-col pt-4 overflow-y-auto space-y-2 pb-4">
          <div class="flex items-center justify-between px-4 mb-2 shrink-0">
            <span class="text-[9px] font-bold text-slate-500 uppercase tracking-widest text-left block">
              导航
            </span>
            <button @click="isMobileSidebarOpen = false" class="text-slate-400 hover:text-white p-1 cursor-pointer">
              <X class="w-4.5 h-4.5" />
            </button>
          </div>

          <!-- Combined Mobile Nav items -->
          <nav class="space-y-0.5 px-2">
            <button 
              @click="activeTab = 'dashboard'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'dashboard' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <LayoutDashboard class="w-4 h-4" />
              <span>仪表盘</span>
            </button>

            <button 
              @click="activeTab = 'live-data'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'live-data' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <Database class="w-4 h-4" />
              <span>实时监控</span>
            </button>

            <button 
              @click="activeTab = 'device-management'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'device-management' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <Cpu class="w-4 h-4" />
              <span>设备管理</span>
            </button>

            <button 
              @click="activeTab = 'data-models'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'data-models' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <Layers class="w-4 h-4" />
              <span>数据模型</span>
            </button>

            <button 
              @click="activeTab = 'scada-editor'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'scada-editor' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <MonitorPlay class="w-4 h-4" />
              <span>组态设计</span>
            </button>

            <button 
              @click="activeTab = 'system-logs'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'system-logs' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <Terminal class="w-4 h-4" />
              <span>系统日志</span>
            </button>

            <button 
              @click="activeTab = 'trigger-management'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'trigger-management' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <ShieldAlert class="w-4 h-4" />
              <span>告警管理</span>
            </button>

            <button 
              @click="activeTab = 'task-management'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'task-management' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <Calendar class="w-4 h-4" />
              <span>任务调度</span>
            </button>

            <button 
              @click="activeTab = 'system-scripts'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'system-scripts' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <FileCode class="w-4 h-4" />
              <span>脚本引擎</span>
            </button>

            <button 
              @click="activeTab = 'data-interfaces'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'data-interfaces' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <Network class="w-4 h-4" />
              <span>接口管理</span>
            </button>

            <button 
              @click="activeTab = 'historical-query'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'historical-query' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <History class="w-4 h-4" />
              <span>历史数据</span>
            </button>

            <button 
              @click="activeTab = 'mqtt-servers'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'mqtt-servers' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <Rss class="w-4 h-4 text-slate-400" />
              <span>MQTT代理</span>
            </button>

            <button 
              @click="activeTab = 'data-conversion'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'data-conversion' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <Shuffle class="w-4 h-4" />
              <span>数据转换</span>
            </button>

            <button 
              @click="activeTab = 'database-management'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'database-management' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <HardDrive class="w-4 h-4" />
              <span>数据库管理</span>
            </button>

            <button 
              @click="activeTab = 'user-management'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'user-management' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <Users class="w-4 h-4" />
              <span>用户管理</span>
            </button>

            <button 
              @click="activeTab = 'settings-center'; isMobileSidebarOpen = false;"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left"
              :class="activeTab === 'settings-center' ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400'"
            >
              <Settings class="w-4 h-4" />
              <span>系统配置</span>
            </button>
          </nav>
        </div>

        <div class="p-3 bg-slate-950 border-t border-slate-900 flex shrink-0 justify-between items-center">
          <div class="flex items-center gap-2 text-xs">
            <span class="font-bold text-white">{{ loginUser?.username || 'admin' }}</span>
          </div>
          <button @click="performLogout(); isMobileSidebarOpen = false;" class="text-rose-500 text-xs">退出</button>
        </div>
      </aside>

      <!-- 2.3 MAIN ROUTER PORT WITH COMPONENT CHANGER -->
      <main class="flex-1 flex flex-col min-w-0 bg-slate-100 overflow-hidden relative">
        <DashboardView v-if="activeTab === 'dashboard'" />
        <LiveDataView v-else-if="activeTab === 'live-data'" />
        <DeviceManagementView v-else-if="activeTab === 'device-management'" />
        <DataModelView v-else-if="activeTab === 'data-models'" />
        <ScadaTopologyView v-else-if="activeTab === 'scada-editor'" />
        <SystemLogsView v-else-if="activeTab === 'system-logs'" />

        <!-- Extended industrial plugin views router -->
        <TriggerManagementView v-else-if="activeTab === 'trigger-management'" />
        <TaskManagementView v-else-if="activeTab === 'task-management'" />
        <SystemScriptsView v-else-if="activeTab === 'system-scripts'" />
        <DataInterfacesView v-else-if="activeTab === 'data-interfaces'" />
        <HistoricalQueryView v-else-if="activeTab === 'historical-query'" />
        <MqttServersView v-else-if="activeTab === 'mqtt-servers'" />
        <DataConversionView v-else-if="activeTab === 'data-conversion'" />
        <UserManagementView v-else-if="activeTab === 'user-management'" />
        <DatabaseManagementView v-else-if="activeTab === 'database-management'" />
        <SettingsCenterView v-else-if="activeTab === 'settings-center'" />
      </main>

    </div>

  </div>
</template>

<style>
/* Clean layout animations & smooth scrollbar bindings */
::-webkit-scrollbar {
  width: 6px;
  height: 6px;
}
::-webkit-scrollbar-track {
  background: transparent;
}
::-webkit-scrollbar-thumb {
  background: #cbd5e1;
  border-radius: 99px;
}
::-webkit-scrollbar-thumb:hover {
  background: #94a3b8;
}
@keyframes ring {
  0% { transform: scale(1); opacity: 0.8; }
  50% { transform: scale(1.15); opacity: 0.4; }
  100% { transform: scale(1.3); opacity: 0; }
}
</style>
