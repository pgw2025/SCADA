<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { 
  isAuthenticated,
  loginUser,
  performLogin,
  performLogout,
  systemConfig,
  initializeAuth
  } from './store';
import { initializeRealtimeSignals } from './services/signalRService';
import { startSystemResourceMonitoring } from './services/systemService';
import { syncAreas } from './services/areaService';
import { fetchDataModelsFromBackend } from './api/modelApi';
import { fetchDevicesFromBackend } from './api/deviceApi';

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
  UserCheck as UserCheckIcon,
  ChevronLeft,
  ChevronRight,
  Rss,
  Shuffle,
  Users
} from 'lucide-vue-next';

const router = useRouter();

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

onMounted(async () => {
  initializeAuth();
  startClock();
  initializeRealtimeSignals();
  
  await Promise.all([
    syncAreas(),
    fetchDataModelsFromBackend()
    // Removed fetchDevicesFromBackend() as it will be managed by DeviceManagementView
  ]);
});

onUnmounted(() => {
  if (clockInterval) clearInterval(clockInterval);
});

// Helper for navigation
const navigate = (path: string) => {
  router.push(path);
  isMobileSidebarOpen.value = false;
};

// Helper for active class
const isActive = (path: string) => router.currentRoute.value.path === path;
</script>

<template>
  <div v-if="!isAuthenticated" class="h-screen w-screen bg-slate-950 flex items-center justify-center p-4 relative overflow-hidden font-sans select-none">
    <div class="absolute inset-0 bg-[radial-gradient(#1e293b_1px,transparent_1px)] [background-size:24px_24px] opacity-35" />
    <div class="absolute w-96 h-96 rounded-full bg-indigo-650/10 blur-[120px] top-10 left-10 animate-pulse pointer-events-none" />
    <div class="absolute w-96 h-96 rounded-full bg-sky-500/5 blur-[125px] bottom-10 right-10 pointer-events-none" />
    <div class="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl overflow-hidden relative z-10 text-left animate-in fade-in zoom-in-95 duration-200">
      <div class="bg-slate-950 p-6 flex flex-col items-center justify-center border-b border-slate-800 text-center gap-3">
          <div class="w-12 h-12 rounded-xl bg-gradient-to-tr from-sky-600 to-indigo-600 flex items-center justify-center shadow-lg">
            <Server class="w-6 h-6 text-white animate-pulse" />
          </div>
          <div>
            <h1 class="text-sm font-black tracking-widest text-white uppercase">IOTA-SCADA 系统</h1>
            <span class="text-[10px] text-slate-400 font-medium tracking-wide mt-1 block">工业控制与数据采集平台</span>
          </div>
        </div>
      <form @submit.prevent="triggerFormLogin" class="p-6 space-y-4">
        <div v-if="loginErrorMessage" class="p-3 rounded-lg bg-rose-950/40 border border-rose-800 text-rose-300 text-xs font-medium leading-relaxed font-sans text-center">
          {{ loginErrorMessage }}
        </div>
        <div>
          <label class="block text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-1 font-mono">用户名</label>
          <input v-model="loginUsernameInput" type="text" required class="w-full bg-slate-950 border border-slate-800 rounded-lg p-2.5 text-xs font-bold text-white outline-none focus:border-sky-500 transition-colors" placeholder="用户名" />
        </div>
        <div>
          <label class="block text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-1 font-mono">密码</label>
          <div class="relative">
            <input v-model="loginPasswordInput" type="password" required class="w-full bg-slate-950 border border-slate-800 rounded-lg p-2.5 pl-9 text-xs font-mono font-bold text-white outline-none focus:border-sky-500 transition-colors" placeholder="••••••••" />
            <Lock class="absolute left-3 top-3 w-4.5 h-4.5 text-slate-500" />
          </div>
        </div>
        <button type="submit" class="w-full py-2.5 bg-sky-600 hover:bg-sky-500 text-white font-bold text-xs rounded-lg transition-transform active:scale-95 cursor-pointer mt-2">
          登录
        </button>
        <div class="border-t border-slate-800 pt-4 mt-2 grid grid-cols-1 gap-2.5 text-center">
          <div class="text-[10px] text-slate-500 font-sans leading-relaxed">
            默认账户: <button type="button" @click="loginUsernameInput='admin'; loginPasswordInput='123456'" class="text-sky-400 hover:underline">admin</button> / <button type="button" @click="loginUsernameInput='admin'; loginPasswordInput='admin888'" class="text-sky-400 hover:underline">admin888</button>
          </div>
          <button type="button" @click="triggerBypassLogin" class="py-1.5 border border-slate-800 hover:bg-slate-805/30 bg-slate-850/10 text-slate-350 hover:text-white rounded-lg text-[10px] font-bold uppercase tracking-wider inline-flex items-center justify-center gap-1.5 transition-colors cursor-pointer">
            <UserCheck class="w-3.5 h-3.5" />
            快速登录
          </button>
        </div>
      </form>
    </div>
  </div>
  <div v-else class="h-screen w-screen flex flex-col font-sans text-slate-800 bg-slate-150 overflow-hidden select-none">
    <header class="h-14 bg-[#0b0f19] text-white border-b border-slate-950 px-4 flex items-center justify-between shrink-0 shadow-lg relative z-30">
      <div class="flex items-center gap-3">
        <button @click="isMobileSidebarOpen = !isMobileSidebarOpen" class="lg:hidden p-1.5 rounded-lg border border-slate-800 text-slate-300 hover:bg-slate-900 active:scale-95 transition-all outline-none cursor-pointer">
          <Menu v-if="!isMobileSidebarOpen" class="w-4.5 h-4.5" />
          <X v-else class="w-4.5 h-4.5" />
        </button>
        <div class="w-9 h-9 rounded-lg bg-gradient-to-tr from-sky-600 to-indigo-600 flex items-center justify-center shadow-md shrink-0">
          <Server class="w-5 h-5 text-white animate-pulse" />
        </div>
        <div class="text-left">
          <h1 class="text-xs sm:text-sm font-black tracking-wider uppercase flex items-center gap-2 leading-none text-slate-50">
            {{ systemConfig.systemTitle }}
            <span class="text-[9px] bg-slate-900 text-[#1890ff] font-bold px-1.5 py-0.5 rounded border border-slate-800 select-none font-mono hidden sm:inline-block">V6.0</span>
          </h1>
          <span class="text-[9px] sm:text-[10px] text-slate-400 leading-none mt-1 inline-block select-all">SCADA 控制中心</span>
        </div>
      </div>
      <div class="flex items-center gap-3 text-[10px] sm:text-xs font-mono">
        <div class="hidden md:flex items-center gap-1.5 text-slate-300 bg-[#111827] border border-slate-850 px-3 py-1 rounded-lg">
          <Clock class="w-3.5 h-3.5 text-slate-400" />
          <span>{{ currentLocalTime || '正在同步时钟...' }}</span>
        </div>
        <div class="flex items-center gap-1.5 bg-[#10b981]/10 text-emerald-400 border border-[#10b981]/25 px-2 py-0.5 sm:py-1 rounded-lg">
          <span class="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-ping"></span>
          <span class="font-bold uppercase tracking-wider text-[8px] sm:text-[9px]">系统运行中</span>
        </div>
      </div>
    </header>
    <div class="flex-1 flex overflow-hidden relative">
      <aside class="hidden lg:flex bg-[#0f172a] text-slate-350 border-r border-[#090d16] flex-col justify-between shrink-0 select-none relative z-20 transition-all duration-300" :class="isSidebarCollapsed ? 'w-16' : 'w-64'">
        <div class="px-2 py-2 border-b border-slate-800/40 flex items-center justify-center shrink-0">
          <button @click="isSidebarCollapsed = !isSidebarCollapsed" class="w-full py-1.5 hover:bg-slate-800 bg-slate-900 border border-slate-800/60 rounded-lg text-[10px] font-bold text-slate-400 hover:text-white flex items-center justify-center gap-1.5 cursor-pointer transition-all active:scale-95" :title="isSidebarCollapsed ? '展开导航' : '收起导航'">
            <ChevronRight v-if="isSidebarCollapsed" class="w-4 h-4 text-slate-400" />
            <template v-else>
              <ChevronLeft class="w-4 h-4 text-slate-400" />
              <span>收起导航</span>
            </template>
          </button>
        </div>
        <div class="flex-1 flex flex-col pt-3 overflow-y-auto space-y-2.5 pb-4">
          <div>
            <span v-if="!isSidebarCollapsed" class="text-[9px] font-bold text-slate-500 uppercase tracking-widest px-4 py-1 select-none text-left block">监控中心</span>
            <div v-else class="h-px bg-slate-800/40 mx-2 my-1" />
            <nav class="space-y-0.5 px-2">
              <button @click="navigate('/dashboard')" :class="[isActive('/dashboard') ? 'bg-slate-800 text-white font-bold' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/dashboard') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left cursor-pointer group w-full">
                <LayoutDashboard class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">仪表盘</span>
              </button>
              <button @click="navigate('/live-data')" :class="[isActive('/live-data') ? 'bg-slate-800 text-white font-bold' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/live-data') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left cursor-pointer group w-full">
                <Database class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">实时监控</span>
              </button>
              <button @click="navigate('/device-management')" :class="[isActive('/device-management') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/device-management') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <Cpu class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">设备管理</span>
              </button>
              <button @click="navigate('/data-models')" :class="[isActive('/data-models') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/data-models') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <Layers class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">数据模型</span>
              </button>
              <button @click="navigate('/scada-editor')" :class="[isActive('/scada-editor') ? 'bg-slate-800 text-white font-bold' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/scada-editor') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <MonitorPlay class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">组态设计</span>
              </button>
              <button @click="navigate('/system-logs')" :class="[isActive('/system-logs') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/system-logs') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <Terminal class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">系统日志</span>
              </button>
            </nav>
          </div>
          <div>
            <span v-if="!isSidebarCollapsed" class="text-[9px] font-bold text-slate-500 uppercase tracking-widest px-4 py-1 select-none text-left block">自动化</span>
            <div v-else class="h-px bg-slate-800/40 mx-2 my-1" />
            <nav class="space-y-0.5 px-2">
              <button @click="navigate('/trigger-management')" :class="[isActive('/trigger-management') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/trigger-management') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <ShieldAlert class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">告警管理</span>
              </button>
              <button @click="navigate('/task-management')" :class="[isActive('/task-management') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/task-management') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <Calendar class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">任务调度</span>
              </button>
              <button @click="navigate('/system-scripts')" :class="[isActive('/system-scripts') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/system-scripts') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <FileCode class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">脚本引擎</span>
              </button>
              <button @click="navigate('/data-interfaces')" :class="[isActive('/data-interfaces') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/data-interfaces') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <Network class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">接口管理</span>
              </button>
              <button @click="navigate('/historical-query')" :class="[isActive('/historical-query') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/historical-query') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <History class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">历史数据</span>
              </button>
              <button @click="navigate('/mqtt-servers')" :class="[isActive('/mqtt-servers') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/mqtt-servers') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <Rss class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">MQTT代理</span>
              </button>
              <button @click="navigate('/data-conversion')" :class="[isActive('/data-conversion') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/data-conversion') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <Shuffle class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">数据转换</span>
              </button>
            </nav>
          </div>
          <div>
            <span v-if="!isSidebarCollapsed" class="text-[9px] font-bold text-slate-500 uppercase tracking-widest px-4 py-1 select-none text-left block">系统设置</span>
            <div v-else class="h-px bg-slate-800/40 mx-2 my-1" />
            <nav class="space-y-0.5 px-2">
              <button @click="navigate('/database-management')" :class="[isActive('/database-management') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/database-management') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <HardDrive class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">数据库管理</span>
              </button>
              <button @click="navigate('/user-management')" :class="[isActive('/user-management') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/user-management') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <Users class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">用户管理</span>
              </button>
              <button @click="navigate('/settings-center')" :class="[isActive('/settings-center') ? 'bg-slate-800 text-white' : 'hover:bg-slate-800 text-slate-400 hover:text-white', isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4 border-transparent', !isSidebarCollapsed && isActive('/settings-center') ? 'border-l-[#1890ff]' : '']" class="flex items-center rounded-lg text-xs font-bold transition-all text-left group cursor-pointer w-full">
                <Settings class="w-4 h-4 text-slate-400 group-hover:text-white shrink-0" />
                <span v-if="!isSidebarCollapsed" class="truncate">系统配置</span>
              </button>
            </nav>
          </div>
        </div>
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
          <button v-if="!isSidebarCollapsed" @click="performLogout" class="p-1.5 hover:bg-rose-900/30 rounded-lg text-slate-500 hover:text-rose-400 transition-colors cursor-pointer" title="退出">
            <LogOut class="w-4 h-4" />
          </button>
          <div v-else class="flex flex-col items-center gap-3.5 py-1 w-full shrink-0">
            <div class="w-8 h-8 rounded-full bg-slate-800 border border-slate-700 flex items-center justify-center relative shrink-0" :title="(loginUser?.username || 'admin') + ' · ' + (loginUser?.role || '管理员')">
              <UserCheck class="w-4 h-4 text-sky-450 text-sky-400" />
              <span class="absolute bottom-0 right-0 w-2 h-2 bg-emerald-500 rounded-full border-2 border-slate-950"></span>
            </div>
            <button @click="performLogout" class="p-1.5 hover:bg-rose-900/20 rounded-lg text-slate-550 hover:text-[#ef4444] transition-colors cursor-pointer" title="退出">
              <LogOut class="w-4 h-4" />
            </button>
          </div>
        </div>
      </aside>
      <div v-if="isMobileSidebarOpen" @click="isMobileSidebarOpen = false" class="fixed inset-0 bg-slate-950/60 backdrop-blur-xs z-40 lg:hidden" />
      <aside class="fixed inset-y-0 left-0 w-64 bg-[#0f172a] text-slate-300 z-50 flex flex-col justify-between transition-transform duration-300 lg:hidden select-none" :class="isMobileSidebarOpen ? 'translate-x-0' : '-translate-x-full'">
        <div class="flex-1 flex flex-col pt-4 overflow-y-auto space-y-2 pb-4">
          <div class="flex items-center justify-between px-4 mb-2 shrink-0">
            <span class="text-[9px] font-bold text-slate-500 uppercase tracking-widest text-left block">导航</span>
            <button @click="isMobileSidebarOpen = false" class="text-slate-400 hover:text-white p-1 cursor-pointer">
              <X class="w-4.5 h-4.5" />
            </button>
          </div>
          <nav class="space-y-0.5 px-2">
            <button @click="navigate('/dashboard'); isMobileSidebarOpen = false;" :class="[isActive('/dashboard') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <LayoutDashboard class="w-4 h-4" />
              <span>仪表盘</span>
            </button>
            <button @click="navigate('/live-data'); isMobileSidebarOpen = false;" :class="[isActive('/live-data') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Database class="w-4 h-4" />
              <span>实时监控</span>
            </button>
            <button @click="navigate('/device-management'); isMobileSidebarOpen = false;" :class="[isActive('/device-management') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Cpu class="w-4 h-4" />
              <span>设备管理</span>
            </button>
            <button @click="navigate('/data-models'); isMobileSidebarOpen = false;" :class="[isActive('/data-models') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Layers class="w-4 h-4" />
              <span>数据模型</span>
            </button>
            <button @click="navigate('/scada-editor'); isMobileSidebarOpen = false;" :class="[isActive('/scada-editor') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <MonitorPlay class="w-4 h-4" />
              <span>组态设计</span>
            </button>
            <button @click="navigate('/system-logs'); isMobileSidebarOpen = false;" :class="[isActive('/system-logs') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Terminal class="w-4 h-4" />
              <span>系统日志</span>
            </button>
            <button @click="navigate('/trigger-management'); isMobileSidebarOpen = false;" :class="[isActive('/trigger-management') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <ShieldAlert class="w-4 h-4" />
              <span>告警管理</span>
            </button>
            <button @click="navigate('/task-management'); isMobileSidebarOpen = false;" :class="[isActive('/task-management') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Calendar class="w-4 h-4" />
              <span>任务调度</span>
            </button>
            <button @click="navigate('/system-scripts'); isMobileSidebarOpen = false;" :class="[isActive('/system-scripts') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <FileCode class="w-4 h-4" />
              <span>脚本引擎</span>
            </button>
            <button @click="navigate('/data-interfaces'); isMobileSidebarOpen = false;" :class="[isActive('/data-interfaces') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Network class="w-4 h-4" />
              <span>接口管理</span>
            </button>
            <button @click="navigate('/historical-query'); isMobileSidebarOpen = false;" :class="[isActive('/historical-query') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <History class="w-4 h-4" />
              <span>历史数据</span>
            </button>
            <button @click="navigate('/mqtt-servers'); isMobileSidebarOpen = false;" :class="[isActive('/mqtt-servers') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Rss class="w-4 h-4 text-slate-400" />
              <span>MQTT代理</span>
            </button>
            <button @click="navigate('/data-conversion'); isMobileSidebarOpen = false;" :class="[isActive('/data-conversion') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Shuffle class="w-4 h-4" />
              <span>数据转换</span>
            </button>
            <button @click="navigate('/database-management'); isMobileSidebarOpen = false;" :class="[isActive('/database-management') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <HardDrive class="w-4 h-4" />
              <span>数据库管理</span>
            </button>
            <button @click="navigate('/user-management'); isMobileSidebarOpen = false;" :class="[isActive('/user-management') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Users class="w-4 h-4" />
              <span>用户管理</span>
            </button>
            <button @click="navigate('/settings-center'); isMobileSidebarOpen = false;" :class="[isActive('/settings-center') ? 'bg-slate-800 text-white' : 'hover:bg-slate-850 text-slate-400']" class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
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
      <main class="flex-1 flex flex-col min-w-0 bg-slate-100 overflow-hidden relative">
        <router-view />
      </main>
    </div>
  </div>
</template>

<style>
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