<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch, computed } from 'vue';
import { useRouter } from 'vue-router';
import {
  isAuthenticated,
  loginUser,
  performLogin,
  performLogout,
  changeMyPassword,
  systemConfig,
  currentTheme,
  toggleTheme,
  initTheme
} from './store';
import { initializeRealtimeSignals } from './services/signalRService';
import { ROLE_ADMIN } from './constants/roles';
import { startSystemResourceMonitoring } from './services/systemService';
import { syncAreas } from './services/areaService';
import { fetchDataModelsFromBackend } from './api/modelApi';
import { syncDevices } from './services/deviceService';
import ToastContainer from './components/ToastContainer.vue';

import {
  LayoutDashboard,
  Database,
  Cpu,
  Layers,
  Braces,
  MonitorPlay,
  Terminal,
  Clock,
  UserCheck,
  Bell,
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
  Users,
  Sun,
  Moon
} from 'lucide-vue-next';

const router = useRouter();

// Responsive sidebar state for mobile drawers
const isMobileSidebarOpen = ref(false);

// PC sidebar state: collapsible
const isSidebarCollapsed = ref(false);

// Login state bindings
const loginUsernameInput = ref('');
const loginPasswordInput = ref('');
const loginErrorMessage = ref('');

const triggerFormLogin = async () => {
  loginErrorMessage.value = '';
  const result = await performLogin(loginUsernameInput.value.trim(), loginPasswordInput.value.trim());
  if (!result.success) {
    loginErrorMessage.value = result.errorMessage || '登录失败，请检查网络连接';
  } else {
    // 阶段5：登录成功按角色落地——管理员进仪表盘，普通用户（Operator）进组态运行画面
    router.push(loginUser.value?.role === ROLE_ADMIN ? '/dashboard' : '/scada-view');
  }
};

// Human timestamp for the top control bar
const currentLocalTime = ref<string>('');
let clockInterval: any = null;

const startClock = () => {
  const pad = (n: number) => n.toString().padStart(2, '0');
  const update = () => {
    const d = new Date();
    currentLocalTime.value = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
  };
  update();
  clockInterval = setInterval(update, 1000);
};

onMounted(async () => {
  initTheme();
  startClock();
  await initializeRealtimeSignals();
});

// 登录（含 token 自动登录）成功后统一预载全局数据。
// 原实现将 areas/models 拉取放在 onMounted(登录前)，此时无 token 必 401 且登录后不重拉，
// 导致直接进入实时监控等页面时全局 store 为空。改为监听登录态后预载，供任意页面直接消费。
watch(
  isAuthenticated,
  (authed) => {
    if (!authed) return;

    Promise.all([
      syncAreas(),
      fetchDataModelsFromBackend(),
      syncDevices()
    ]).catch(err => {
      console.error('初始化数据失败:', err);
    });

  },
  { immediate: true }
);


onUnmounted(() => {
  if (clockInterval) clearInterval(clockInterval);
});

// Helper for navigation
const navigate = (path: string) => {
  router.push(path);
  isMobileSidebarOpen.value = false;
};

// Helper for active class
// 组态运行采用两级路由（/scada-view 列表 + /scada-view/:projectId 画布），
// 对 /scada-view 做前缀匹配，保证进入具体工程时侧边栏高亮不丢失。
const isActive = (path: string) =>
  path === '/scada-view'
    ? router.currentRoute.value.path === '/scada-view'
      || router.currentRoute.value.path.startsWith('/scada-view/')
    : router.currentRoute.value.path === path;

// 阶段5：角色隔离——仅管理员可见后台导航；普通用户（Operator）仅见「组态运行」入口
const isAdmin = computed(() => loginUser.value?.role === ROLE_ADMIN);

// ---- 自主修改密码（所有已登录用户可见，入口在侧边栏用户区）----
const showChangePwModal = ref(false);
const changePwOld = ref('');
const changePwNew = ref('');
const changePwConfirm = ref('');
const changePwError = ref('');
const changePwSuccess = ref('');

const openChangePwModal = () => {
  changePwOld.value = '';
  changePwNew.value = '';
  changePwConfirm.value = '';
  changePwError.value = '';
  changePwSuccess.value = '';
  showChangePwModal.value = true;
};

const handleChangeMyPassword = async () => {
  changePwError.value = '';
  changePwSuccess.value = '';
  // 前端预校验，减少无效请求（与后端策略一致：≥8 位且含字母与数字）
  if (!changePwOld.value.trim()) { changePwError.value = '请输入原密码'; return; }
  if (!changePwNew.value || changePwNew.value.length < 8) { changePwError.value = '新密码长度至少为 8 位'; return; }
  if (!/[A-Za-z]/.test(changePwNew.value) || !/\d/.test(changePwNew.value)) { changePwError.value = '新密码必须同时包含字母和数字'; return; }
  if (changePwNew.value !== changePwConfirm.value) { changePwError.value = '两次输入的新密码不一致'; return; }
  if (changePwNew.value === changePwOld.value) { changePwError.value = '新密码不能与原密码相同'; return; }

  try {
    await changeMyPassword(changePwOld.value.trim(), changePwNew.value);
    changePwSuccess.value = '密码修改成功，下次登录请使用新密码';
    changePwOld.value = '';
    changePwNew.value = '';
    changePwConfirm.value = '';
  } catch {
    // 失败提示由 http 拦截器统一 Toast 弹出（含后端具体 message，如"原密码不正确"）
  }
};
</script>

<template>
  <ToastContainer />
  <div v-if="!isAuthenticated" :class="currentTheme === 'dark' ? 'bg-slate-950 text-white' : 'bg-slate-50 text-slate-800'"
    class="h-screen w-screen flex items-center justify-center p-4 relative overflow-hidden font-sans select-none transition-colors duration-200">
    <!-- Top Right Theme Switcher on Login Page -->
    <div class="absolute top-5 right-5 z-30">
      <button type="button" @click="toggleTheme"
        class="flex items-center gap-2 px-3.5 py-1.5 rounded-full border shadow-sm text-xs font-semibold cursor-pointer transition-all duration-200"
        :class="currentTheme === 'dark' ? 'bg-slate-900 border-slate-700 text-amber-400 hover:bg-slate-800' : 'bg-white/90 border-slate-200 text-slate-700 hover:bg-white hover:border-slate-300 shadow-slate-200/50'"
        :title="currentTheme === 'dark' ? '切换到浅色模式' : '切换到深色模式'">
        <Sun v-if="currentTheme === 'dark'" class="w-3.5 h-3.5 text-amber-400" />
        <Moon v-else class="w-3.5 h-3.5 text-sky-600" />
        <span>{{ currentTheme === 'dark' ? '深色模式' : '浅色模式' }}</span>
      </button>
    </div>

    <!-- Bright, Clean Industrial Tech Aesthetic Background Elements -->
    <div v-if="currentTheme === 'dark'"
      class="absolute inset-0 bg-[radial-gradient(#334155_1px,transparent_1px)] [background-size:28px_28px] opacity-40 pointer-events-none" />
    <div v-else
      class="absolute inset-0 bg-[radial-gradient(#cbd5e1_1px,transparent_1px)] [background-size:24px_24px] opacity-60 pointer-events-none" />

    <!-- Ambient Glowing Tech Blurs (Bright & Clean) -->
    <template v-if="currentTheme === 'dark'">
      <div class="absolute -top-32 -left-32 w-96 h-96 rounded-full bg-sky-500/15 blur-[120px] pointer-events-none" />
      <div
        class="absolute -bottom-32 -right-32 w-96 h-96 rounded-full bg-indigo-500/15 blur-[120px] pointer-events-none" />
    </template>
    <template v-else>
      <div
        class="absolute -top-24 -left-24 w-[500px] h-[500px] rounded-full bg-sky-200/50 blur-[100px] pointer-events-none" />
      <div
        class="absolute -bottom-24 -right-24 w-[500px] h-[500px] rounded-full bg-indigo-200/40 blur-[100px] pointer-events-none" />
    </template>

    <!-- Login Card -->
    <div
      class="rounded-2xl w-full max-w-md overflow-hidden relative z-10 text-left animate-in fade-in zoom-in-95 duration-200 transition-colors"
      :class="currentTheme === 'dark' ? 'bg-slate-900/95 border border-slate-800 text-white shadow-2xl shadow-black/50' : 'bg-white/95 backdrop-blur-xl border border-slate-200/90 shadow-xl shadow-slate-200/70 text-slate-800'">
      <!-- Header Banner -->
      <div class="p-6 flex flex-col items-center justify-center border-b text-center gap-3 transition-colors"
        :class="currentTheme === 'dark' ? 'bg-slate-950/80 border-slate-800' : 'bg-gradient-to-b from-sky-50/70 via-slate-50/40 to-white border-slate-100'">
        <div
          class="w-12 h-12 rounded-xl bg-gradient-to-tr from-sky-500 to-indigo-600 flex items-center justify-center shadow-lg shadow-sky-500/25">
          <Server class="w-6 h-6 text-white" />
        </div>
        <div>
          <h1 class="text-base font-extrabold tracking-wider uppercase"
            :class="currentTheme === 'dark' ? 'text-white' : 'text-slate-900'">晋鑫设备管理系统</h1>
          <span class="text-xs font-medium tracking-wide mt-1 block"
            :class="currentTheme === 'dark' ? 'text-slate-400' : 'text-slate-500'">工业控制与数据采集平台</span>
        </div>
      </div>

      <!-- Login Form -->
      <form @submit.prevent="triggerFormLogin" class="p-6 space-y-4">
        <div v-if="loginErrorMessage"
          class="p-3 rounded-lg border text-xs font-medium leading-relaxed font-sans text-center"
          :class="currentTheme === 'dark' ? 'bg-rose-950/50 border-rose-800 text-rose-300' : 'bg-rose-50 border-rose-200 text-rose-600'">
          {{ loginErrorMessage }}
        </div>

        <div>
          <label class="block text-[11px] font-bold uppercase tracking-wider mb-1.5 font-mono"
            :class="currentTheme === 'dark' ? 'text-slate-400' : 'text-slate-600'">用户名</label>
          <input v-model="loginUsernameInput" type="text" required
            class="w-full rounded-lg p-2.5 text-xs font-bold outline-none transition-all"
            :class="currentTheme === 'dark' ? 'bg-slate-950 border border-slate-800 text-white focus:border-sky-500 placeholder:text-slate-600' : 'bg-slate-50 hover:bg-slate-100/60 focus:bg-white border border-slate-200 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 text-slate-800 placeholder:text-slate-400'"
            placeholder="请输入用户名" />
        </div>

        <div>
          <label class="block text-[11px] font-bold uppercase tracking-wider mb-1.5 font-mono"
            :class="currentTheme === 'dark' ? 'text-slate-400' : 'text-slate-600'">密码</label>
          <div class="relative">
            <input v-model="loginPasswordInput" type="password" required
              class="w-full rounded-lg p-2.5 pl-9 text-xs font-mono font-bold outline-none transition-all"
              :class="currentTheme === 'dark' ? 'bg-slate-950 border border-slate-800 text-white focus:border-sky-500 placeholder:text-slate-600' : 'bg-slate-50 hover:bg-slate-100/60 focus:bg-white border border-slate-200 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 text-slate-800 placeholder:text-slate-400'"
              placeholder="请输入密码" />
            <Lock class="absolute left-3 top-3 w-4 h-4"
              :class="currentTheme === 'dark' ? 'text-slate-500' : 'text-slate-400'" />
          </div>
        </div>

        <button type="submit"
          class="w-full py-2.5 bg-gradient-to-r from-sky-600 to-indigo-600 hover:from-sky-500 hover:to-indigo-500 text-white font-bold text-xs rounded-lg transition-all shadow-md shadow-sky-600/20 active:scale-[0.98] cursor-pointer mt-2">
          登录
        </button>

        <!-- Static Account Info Tip -->
        <div class="border-t pt-4 mt-3 text-center"
          :class="currentTheme === 'dark' ? 'border-slate-800' : 'border-slate-100'">
          <div class="text-xs font-sans leading-relaxed flex items-center justify-center gap-1.5 flex-wrap"
            :class="currentTheme === 'dark' ? 'text-slate-400' : 'text-slate-500'">
            <span>默认账户:</span>
            <span class="font-bold" :class="currentTheme === 'dark' ? 'text-slate-200' : 'text-slate-700'">admin</span>
            <span :class="currentTheme === 'dark' ? 'text-slate-600' : 'text-slate-300'">|</span>
            <span>密码:</span>
            <span class="font-mono font-bold"
              :class="currentTheme === 'dark' ? 'text-slate-200' : 'text-slate-700'">123456</span>
          </div>
        </div>
      </form>
    </div>
  </div>
  <div v-else
    class="h-screen w-screen flex flex-col font-sans text-slate-800 dark:text-slate-100 bg-slate-100 dark:bg-[#070b12] overflow-hidden select-none">
    <!-- Top Header Bar: Light in light mode, dark in dark mode -->
    <header
      class="h-14 bg-white dark:bg-[#070b12] text-slate-800 dark:text-white border-b border-slate-200 dark:border-slate-900 px-4 flex items-center justify-between shrink-0 shadow-xs relative z-30 transition-colors">
      <div class="flex items-center gap-3">
        <button @click="isMobileSidebarOpen = !isMobileSidebarOpen"
          class="lg:hidden p-1.5 rounded-lg border border-slate-200 dark:border-slate-800 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 active:scale-95 transition-all outline-none cursor-pointer">
          <Menu v-if="!isMobileSidebarOpen" class="w-4.5 h-4.5" />
          <X v-else class="w-4.5 h-4.5" />
        </button>
        <div
          class="w-9 h-9 rounded-lg bg-gradient-to-tr from-sky-600 to-indigo-600 flex items-center justify-center shadow-md shrink-0">
          <Server class="w-5 h-5 text-white animate-pulse" />
        </div>
        <div class="text-left">
          <h1
            class="text-xs sm:text-sm font-black tracking-wider uppercase flex items-center gap-2 leading-none text-slate-900 dark:text-slate-50">
            {{ systemConfig.systemTitle }}
            <span
              class="text-[9px] bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-[#38bdf8] font-bold px-1.5 py-0.5 rounded border border-sky-200 dark:border-slate-700 select-none font-mono hidden sm:inline-block">V6.0</span>
          </h1>
          <span
            class="text-[9px] sm:text-[10px] text-slate-500 dark:text-slate-400 leading-none mt-1 inline-block select-all">设备管理控制中心</span>
        </div>
      </div>
      <div class="flex items-center gap-2 sm:gap-3 text-[10px] sm:text-xs font-mono">
        <!-- Theme Toggle Button -->
        <button @click="toggleTheme"
          class="flex items-center gap-1.5 px-2.5 py-1 rounded-lg border transition-all cursor-pointer select-none active:scale-95 shadow-xs"
          :class="currentTheme === 'dark' ? 'bg-slate-800/90 border-slate-700 text-amber-400 hover:bg-slate-800 hover:text-amber-300' : 'bg-slate-100/90 border-slate-200 text-slate-700 hover:bg-slate-200/80 hover:text-slate-900'"
          :title="currentTheme === 'dark' ? '当前：深色模式 (点击切换为浅色)' : '当前：浅色模式 (点击切换为深色)'">
          <Sun v-if="currentTheme === 'dark'" class="w-3.5 h-3.5 text-amber-400" />
          <Moon v-else class="w-3.5 h-3.5 text-sky-600" />
          <span class="font-sans font-bold text-[11px] hidden sm:inline">{{ currentTheme === 'dark' ? '深色模式' : '浅色模式'
          }}</span>
        </button>

        <div
          class="hidden md:flex items-center gap-1.5 text-slate-600 dark:text-slate-300 bg-slate-50 dark:bg-slate-800/90 border border-slate-200 dark:border-slate-700 px-3 py-1 rounded-lg">
          <Clock class="w-3.5 h-3.5 text-slate-400 dark:text-slate-400" />
          <span>{{ currentLocalTime || '正在同步时钟...' }}</span>
        </div>
        <div
          class="flex items-center gap-1.5 bg-emerald-50 dark:bg-[#10b981]/15 text-emerald-600 dark:text-emerald-400 border border-emerald-200 dark:border-[#10b981]/30 px-2 py-0.5 sm:py-1 rounded-lg">
          <span class="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-ping"></span>
          <span class="font-bold uppercase tracking-wider text-[8px] sm:text-[9px]">系统运行中</span>
        </div>
      </div>
    </header>

    <div class="flex-1 flex overflow-hidden relative">
      <!-- Desktop Sidebar: Light in light mode, dark in dark mode -->
      <aside
        class="hidden lg:flex bg-white dark:bg-[#070b12] text-slate-600 dark:text-slate-300 border-r border-slate-200 dark:border-slate-900 flex-col justify-between shrink-0 select-none relative z-20 transition-all duration-300"
        :class="isSidebarCollapsed ? 'w-16' : 'w-64'">
        <div
          class="px-2 py-2 border-b border-slate-100 dark:border-slate-800/40 flex items-center justify-center shrink-0">
          <button @click="isSidebarCollapsed = !isSidebarCollapsed"
            class="w-full py-1.5 hover:bg-slate-100 dark:hover:bg-slate-800 bg-slate-50 dark:bg-slate-900/60 border border-slate-200 dark:border-slate-800/60 rounded-lg text-[10px] font-bold text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white flex items-center justify-center gap-1.5 cursor-pointer transition-all active:scale-95"
            :title="isSidebarCollapsed ? '展开导航' : '收起导航'">
            <ChevronRight v-if="isSidebarCollapsed" class="w-4 h-4 text-slate-400" />
            <template v-else>
              <ChevronLeft class="w-4 h-4 text-slate-400" />
              <span>收起导航</span>
            </template>
          </button>
        </div>

        <div class="flex-1 flex flex-col pt-3 overflow-y-auto space-y-2.5 pb-4">
          <!-- 组态运行：所有已登录用户（含普通用户）可见，是普通用户的唯一入口 -->
          <nav class="space-y-0.5 px-2">
            <button @click="navigate('/scada-view')" :class="[
              isActive('/scada-view')
                ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
              isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
            ]" class="flex items-center rounded-lg text-xs transition-all text-left cursor-pointer group w-full">
              <MonitorPlay class="w-4 h-4 shrink-0 transition-colors"
                :class="isActive('/scada-view') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
              <span v-if="!isSidebarCollapsed" class="truncate">组态运行</span>
            </button>
          </nav>

          <div v-if="isAdmin">
            <span v-if="!isSidebarCollapsed"
              class="text-[9px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-widest px-4 py-1 select-none text-left block">监控中心</span>
            <div v-else class="h-px bg-slate-200 dark:bg-slate-800/40 mx-2 my-1" />
            <nav class="space-y-0.5 px-2">
              <button @click="navigate('/dashboard')" :class="[
                isActive('/dashboard')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left cursor-pointer group w-full">
                <LayoutDashboard class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/dashboard') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">仪表盘</span>
              </button>

              <button @click="navigate('/live-data')" :class="[
                isActive('/live-data')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left cursor-pointer group w-full">
                <Database class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/live-data') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">实时监控</span>
              </button>

              <button @click="navigate('/device-management')" :class="[
                isActive('/device-management')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <Cpu class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/device-management') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">设备管理</span>
              </button>

              <button @click="navigate('/device-variables')" :class="[
                isActive('/device-variables')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <Braces class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/device-variables') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">设备变量</span>
              </button>

              <button @click="navigate('/data-models')" :class="[
                isActive('/data-models')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <Layers class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/data-models') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">数据模型</span>
              </button>

              <button @click="navigate('/scada-editor')" :class="[
                isActive('/scada-editor')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <MonitorPlay class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/scada-editor') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">组态设计</span>
              </button>

              <button @click="navigate('/system-logs')" :class="[
                isActive('/system-logs')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <Terminal class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/system-logs') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">系统日志</span>
              </button>
            </nav>
          </div>

          <div v-if="isAdmin">
            <span v-if="!isSidebarCollapsed"
              class="text-[9px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-widest px-4 py-1 select-none text-left block">自动化</span>
            <div v-else class="h-px bg-slate-200 dark:bg-slate-800/40 mx-2 my-1" />
            <nav class="space-y-0.5 px-2">
              <button @click="navigate('/alarm-management')" :class="[
                isActive('/alarm-management')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <Bell class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/alarm-management') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">报警管理</span>
              </button>

              <button @click="navigate('/task-management')" :class="[
                isActive('/task-management')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <Calendar class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/task-management') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">任务调度</span>
              </button>

              <button @click="navigate('/system-scripts')" :class="[
                isActive('/system-scripts')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <FileCode class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/system-scripts') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">脚本引擎</span>
              </button>

              <button @click="navigate('/data-interfaces')" :class="[
                isActive('/data-interfaces')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <Network class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/data-interfaces') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">接口管理</span>
              </button>

              <button @click="navigate('/historical-query')" :class="[
                isActive('/historical-query')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <History class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/historical-query') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">历史数据</span>
              </button>

              <button @click="navigate('/mqtt-servers')" :class="[
                isActive('/mqtt-servers')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <Rss class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/mqtt-servers') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">MQTT代理</span>
              </button>

              <button @click="navigate('/data-conversion')" :class="[
                isActive('/data-conversion')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <Shuffle class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/data-conversion') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">数据转换</span>
              </button>
            </nav>
          </div>

          <div v-if="isAdmin">
            <span v-if="!isSidebarCollapsed"
              class="text-[9px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-widest px-4 py-1 select-none text-left block">系统设置</span>
            <div v-else class="h-px bg-slate-200 dark:bg-slate-800/40 mx-2 my-1" />
            <nav class="space-y-0.5 px-2">
              <button @click="navigate('/database-management')" :class="[
                isActive('/database-management')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <HardDrive class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/database-management') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">数据库管理</span>
              </button>

              <button @click="navigate('/user-management')" :class="[
                isActive('/user-management')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <Users class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/user-management') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">用户管理</span>
              </button>

              <button @click="navigate('/settings-center')" :class="[
                isActive('/settings-center')
                  ? 'bg-sky-50 dark:bg-slate-800/90 text-sky-600 dark:text-white font-bold border-l-[#1890ff]'
                  : 'hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white border-l-transparent',
                isSidebarCollapsed ? 'justify-center w-10 h-10 mx-auto' : 'w-full gap-2.5 px-4 py-2.5 border-l-4'
              ]" class="flex items-center rounded-lg text-xs transition-all text-left group cursor-pointer w-full">
                <Settings class="w-4 h-4 shrink-0 transition-colors"
                  :class="isActive('/settings-center') ? 'text-sky-600 dark:text-sky-400' : 'text-slate-400 dark:text-slate-400 group-hover:text-slate-700 dark:group-hover:text-white'" />
                <span v-if="!isSidebarCollapsed" class="truncate">系统配置</span>
              </button>
            </nav>
          </div>
        </div>

        <!-- Sidebar footer: user profile -->
        <div
          class="p-3 bg-slate-50 dark:bg-slate-950 border-t border-slate-200 dark:border-slate-900 flex shrink-0 justify-between items-center select-none transition-colors">
          <div v-if="!isSidebarCollapsed" class="flex items-center gap-2 font-sans overflow-hidden">
            <div
              class="w-7.5 h-7.5 rounded-full bg-slate-200 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 flex items-center justify-center relative shrink-0">
              <UserCheck class="w-3.5 h-3.5 text-sky-600 dark:text-sky-400" />
              <span
                class="absolute bottom-0 right-0 w-2 h-2 bg-emerald-500 rounded-full border-2 border-white dark:border-slate-950"></span>
            </div>
            <div class="text-left overflow-hidden w-28 shrink-0">
              <h4 class="text-[11px] font-bold text-slate-800 dark:text-white truncate">{{ loginUser?.username || 'admin'
              }}</h4>
              <span class="text-[9px] text-slate-500 dark:text-slate-400 block truncate">{{ loginUser?.role || '管理员'
              }}</span>
            </div>
          </div>
          <button v-if="!isSidebarCollapsed" @click="openChangePwModal"
            class="p-1.5 hover:bg-slate-200 dark:hover:bg-slate-800 rounded-lg text-slate-400 hover:text-slate-700 dark:hover:text-slate-300 transition-colors cursor-pointer"
            title="修改密码">
            <Lock class="w-4 h-4" />
          </button>
          <button v-if="!isSidebarCollapsed" @click="performLogout"
            class="p-1.5 hover:bg-rose-100 dark:hover:bg-rose-900/30 rounded-lg text-slate-400 hover:text-rose-600 dark:hover:text-rose-400 transition-colors cursor-pointer"
            title="退出">
            <LogOut class="w-4 h-4" />
          </button>
          <div v-else class="flex flex-col items-center gap-3.5 py-1 w-full shrink-0">
            <div
              class="w-8 h-8 rounded-full bg-slate-200 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 flex items-center justify-center relative shrink-0"
              :title="(loginUser?.username || 'admin') + ' · ' + (loginUser?.role || '管理员')">
              <UserCheck class="w-4 h-4 text-sky-600 dark:text-sky-400" />
              <span
                class="absolute bottom-0 right-0 w-2 h-2 bg-emerald-500 rounded-full border-2 border-white dark:border-slate-950"></span>
            </div>
            <button @click="openChangePwModal"
              class="p-1.5 hover:bg-slate-200 dark:hover:bg-slate-800 rounded-lg text-slate-400 hover:text-slate-700 dark:hover:text-slate-300 transition-colors cursor-pointer"
              title="修改密码">
              <Lock class="w-4 h-4" />
            </button>
            <button @click="performLogout"
              class="p-1.5 hover:bg-rose-100 dark:hover:bg-rose-900/20 rounded-lg text-slate-400 hover:text-rose-600 dark:hover:text-[#ef4444] transition-colors cursor-pointer"
              title="退出">
              <LogOut class="w-4 h-4" />
            </button>
          </div>
        </div>
      </aside>

      <!-- Mobile drawer sidebar -->
      <div v-if="isMobileSidebarOpen" @click="isMobileSidebarOpen = false"
        class="fixed inset-0 bg-slate-950/70 z-40 lg:hidden" />
      <aside
        class="fixed inset-y-0 left-0 w-64 bg-white dark:bg-[#0f172a] text-slate-700 dark:text-slate-300 z-50 flex flex-col justify-between transition-transform duration-300 lg:hidden select-none border-r border-slate-200 dark:border-slate-800"
        :class="isMobileSidebarOpen ? 'translate-x-0' : '-translate-x-full'">
        <div class="flex-1 flex flex-col pt-4 overflow-y-auto space-y-2 pb-4">
          <div class="flex items-center justify-between px-4 mb-2 shrink-0">
            <span
              class="text-[9px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-widest text-left block">导航</span>
            <button @click="isMobileSidebarOpen = false"
              class="text-slate-400 hover:text-slate-700 dark:hover:text-white p-1 cursor-pointer">
              <X class="w-4.5 h-4.5" />
            </button>
          </div>
          <!-- 组态运行：所有已登录用户（含普通用户）可见 -->
          <nav class="space-y-0.5 px-2">
            <button @click="navigate('/scada-view'); isMobileSidebarOpen = false;"
              :class="[isActive('/scada-view') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <MonitorPlay class="w-4 h-4" />
              <span>组态运行</span>
            </button>
          </nav>

          <!-- 后台导航：仅管理员可见 -->
          <nav v-if="isAdmin" class="space-y-0.5 px-2">
            <button @click="navigate('/dashboard'); isMobileSidebarOpen = false;"
              :class="[isActive('/dashboard') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <LayoutDashboard class="w-4 h-4" />
              <span>仪表盘</span>
            </button>
            <button @click="navigate('/live-data'); isMobileSidebarOpen = false;"
              :class="[isActive('/live-data') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Database class="w-4 h-4" />
              <span>实时监控</span>
            </button>
            <button @click="navigate('/device-management'); isMobileSidebarOpen = false;"
              :class="[isActive('/device-management') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Cpu class="w-4 h-4" />
              <span>设备管理</span>
            </button>
            <button @click="navigate('/device-variables'); isMobileSidebarOpen = false;"
              :class="[isActive('/device-variables') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Braces class="w-4 h-4" />
              <span>设备变量</span>
            </button>
            <button @click="navigate('/data-models'); isMobileSidebarOpen = false;"
              :class="[isActive('/data-models') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Layers class="w-4 h-4" />
              <span>数据模型</span>
            </button>
            <button @click="navigate('/scada-editor'); isMobileSidebarOpen = false;"
              :class="[isActive('/scada-editor') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <MonitorPlay class="w-4 h-4" />
              <span>组态设计</span>
            </button>
            <button @click="navigate('/alarm-management'); isMobileSidebarOpen = false;"
              :class="[isActive('/alarm-management') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Bell class="w-4 h-4" />
              <span>报警管理</span>
            </button>
            <button @click="navigate('/system-logs'); isMobileSidebarOpen = false;"
              :class="[isActive('/system-logs') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Terminal class="w-4 h-4" />
              <span>系统日志</span>
            </button>
            <button @click="navigate('/task-management'); isMobileSidebarOpen = false;"
              :class="[isActive('/task-management') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Calendar class="w-4 h-4" />
              <span>任务调度</span>
            </button>
            <button @click="navigate('/system-scripts'); isMobileSidebarOpen = false;"
              :class="[isActive('/system-scripts') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <FileCode class="w-4 h-4" />
              <span>脚本引擎</span>
            </button>
            <button @click="navigate('/data-interfaces'); isMobileSidebarOpen = false;"
              :class="[isActive('/data-interfaces') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Network class="w-4 h-4" />
              <span>接口管理</span>
            </button>
            <button @click="navigate('/historical-query'); isMobileSidebarOpen = false;"
              :class="[isActive('/historical-query') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <History class="w-4 h-4" />
              <span>历史数据</span>
            </button>
            <button @click="navigate('/mqtt-servers'); isMobileSidebarOpen = false;"
              :class="[isActive('/mqtt-servers') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Rss class="w-4 h-4 text-slate-400" />
              <span>MQTT代理</span>
            </button>
            <button @click="navigate('/data-conversion'); isMobileSidebarOpen = false;"
              :class="[isActive('/data-conversion') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Shuffle class="w-4 h-4" />
              <span>数据转换</span>
            </button>
            <button @click="navigate('/database-management'); isMobileSidebarOpen = false;"
              :class="[isActive('/database-management') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <HardDrive class="w-4 h-4" />
              <span>数据库管理</span>
            </button>
            <button @click="navigate('/user-management'); isMobileSidebarOpen = false;"
              :class="[isActive('/user-management') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Users class="w-4 h-4" />
              <span>用户管理</span>
            </button>
            <button @click="navigate('/settings-center'); isMobileSidebarOpen = false;"
              :class="[isActive('/settings-center') ? 'bg-sky-50 dark:bg-slate-800 text-sky-600 dark:text-white font-bold' : 'hover:bg-slate-50 dark:hover:bg-slate-850 text-slate-600 dark:text-slate-400']"
              class="w-full flex items-center gap-2.5 px-4 py-2.5 rounded-lg text-xs font-bold transition-all text-left">
              <Settings class="w-4 h-4" />
              <span>系统配置</span>
            </button>
          </nav>
        </div>
        <div
          class="p-3 bg-slate-50 dark:bg-slate-950 border-t border-slate-200 dark:border-slate-900 flex shrink-0 justify-between items-center transition-colors">
          <div class="flex items-center gap-2 text-xs">
            <span class="font-bold text-slate-800 dark:text-white">{{ loginUser?.username || 'admin' }}</span>
          </div>
          <button @click="performLogout(); isMobileSidebarOpen = false;"
            class="text-rose-500 text-xs font-bold">退出</button>
        </div>
      </aside>
      <main class="flex-1 flex flex-col min-w-0 bg-slate-100 dark:bg-[#070b12] overflow-hidden relative">
        <router-view />
      </main>
    </div>
  </div>

  <!-- MODAL: 自主修改密码（所有已登录用户可用） -->
  <div v-if="showChangePwModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
    <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
      <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
        <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest text-emerald-400">
          <Lock class="w-4 h-4" />
          <span>修改密码</span>
        </div>
        <button @click="showChangePwModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
      </div>

      <div class="p-5 space-y-4 text-xs">
        <div>
          <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">原密码</label>
          <input v-model="changePwOld" type="password" placeholder="请输入原密码"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-slate-800 dark:focus:border-emerald-500" />
        </div>
        <div>
          <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">新密码</label>
          <input v-model="changePwNew" type="password" placeholder="至少 8 位，包含字母和数字"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-slate-800 dark:focus:border-emerald-500" />
        </div>
        <div>
          <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">确认新密码</label>
          <input v-model="changePwConfirm" type="password" placeholder="再次输入新密码"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-slate-800 dark:focus:border-emerald-500" />
        </div>

        <p v-if="changePwError" class="text-xs font-bold text-rose-500">{{ changePwError }}</p>
        <p v-if="changePwSuccess" class="text-xs font-bold text-emerald-500">{{ changePwSuccess }}</p>
      </div>

      <div class="bg-slate-50 dark:bg-slate-950 p-4 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800 shrink-0">
        <button @click="showChangePwModal = false"
          class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer">
          取消
        </button>
        <button @click="handleChangeMyPassword"
          class="px-4 py-1.5 bg-slate-900 dark:bg-emerald-600 border border-slate-900 dark:border-emerald-600 hover:bg-slate-800 dark:hover:bg-emerald-500 font-bold text-xs text-white rounded-lg cursor-pointer">
          确认修改
        </button>
      </div>
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

.dark ::-webkit-scrollbar-thumb {
  background: #334155;
}

::-webkit-scrollbar-thumb:hover {
  background: #94a3b8;
}

.dark ::-webkit-scrollbar-thumb:hover {
  background: #475569;
}

@keyframes ring {
  0% {
    transform: scale(1);
    opacity: 0.8;
  }

  50% {
    transform: scale(1.15);
    opacity: 0.4;
  }

  100% {
    transform: scale(1.3);
    opacity: 0;
  }
}
</style>