<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { projectSummaries, initializeProjectSummaries } from '../store/scadaStore';
import { loginUser } from '../store/userStore';
import { isAuthenticated, performLogout } from '../store';
import { ROLE_ADMIN } from '../constants/roles';
import { MonitorPlay, LogOut, ArrowRight, Pencil, LayoutGrid } from 'lucide-vue-next';

const router = useRouter();

const loading = ref(true);
const loadingError = ref(false);

// 工程卡片加载（轻量摘要，不拉完整树）
onMounted(async () => {
  try {
    await initializeProjectSummaries();
  } catch {
    loadingError.value = true;
  } finally {
    loading.value = false;
  }
});

const isAdmin = computed(() => loginUser.value?.role === ROLE_ADMIN);

// 独立运行页：新标签页打开（路由 meta.standalone，App.vue 隐藏系统框架）。
// token 在 localStorage 共享，新页 boot 时自动回源认证；router.resolve 保证带上路由 base。
const enterProject = (id: number) => {
  window.open(router.resolve(`/scada-view/${id}`).href, '_blank');
};

const goEditor = () => router.push('/scada-editor');
const onLogout = () => {
  performLogout();
  router.push('/');
};

const retry = async () => {
  loading.value = true;
  loadingError.value = false;
  try {
    await initializeProjectSummaries();
  } catch {
    loadingError.value = true;
  } finally {
    loading.value = false;
  }
};
</script>

<template>
  <div class="h-screen w-screen flex flex-col bg-slate-100 dark:bg-[#070b12] text-slate-800 dark:text-slate-100 overflow-hidden select-none">
    <!-- Header -->
    <header class="h-14 bg-white dark:bg-[#070b12] border-b border-slate-200 dark:border-slate-900 px-4 flex items-center justify-between shrink-0 shadow-xs z-30">
      <div class="flex items-center gap-3 min-w-0">
        <div class="w-8 h-8 rounded-lg bg-gradient-to-tr from-sky-600 to-indigo-600 flex items-center justify-center shadow-md shrink-0">
          <MonitorPlay class="w-4 h-4 text-white" />
        </div>
        <div class="min-w-0">
          <h1 class="text-xs sm:text-sm font-black tracking-wider uppercase truncate">组态运行</h1>
          <span class="text-[9px] sm:text-[10px] text-slate-500 dark:text-slate-400 leading-none inline-block">
            选择工程进入组态监控画面
          </span>
        </div>
      </div>

      <div class="flex items-center gap-2 text-[11px]">
        <span class="hidden md:inline-flex items-center gap-1 text-slate-600 dark:text-slate-300 bg-slate-50 dark:bg-slate-800/90 border border-slate-200 dark:border-slate-700 px-2.5 py-1 rounded-lg">
          {{ loginUser?.username || 'user' }}
        </span>
        <button
          @click="onLogout"
          class="p-1.5 rounded-lg text-slate-400 hover:text-rose-500 hover:bg-rose-50 dark:hover:bg-rose-900/20 cursor-pointer"
          title="退出登录"
        ><LogOut class="w-4 h-4" /></button>
      </div>
    </header>

    <!-- 主体 -->
    <main class="flex-1 overflow-auto bg-slate-50 dark:bg-[#0b1220] p-4 sm:p-6">
      <!-- 加载中 -->
      <div v-if="loading" class="h-full flex items-center justify-center text-slate-400 dark:text-slate-500">
        <div class="text-center">
          <LayoutGrid class="w-8 h-8 mx-auto mb-2 animate-pulse opacity-40" />
          <p class="text-sm">正在加载工程列表…</p>
        </div>
      </div>

      <!-- 加载失败/重试 -->
      <div v-else-if="loadingError" class="h-full flex items-center justify-center text-center text-slate-500 dark:text-slate-400">
        <div>
          <p class="text-sm mb-3">工程列表加载失败，请检查网络后重试</p>
          <button
            @click="retry"
            class="px-4 py-1.5 rounded-lg text-xs font-bold border border-slate-300 dark:border-slate-600 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
          >重新加载</button>
        </div>
      </div>

      <!-- 空状态 -->
      <div v-else-if="projectSummaries.length === 0"
        class="h-full flex items-center justify-center text-center text-slate-500 dark:text-slate-400">
        <div>
          <MonitorPlay class="w-12 h-12 mx-auto mb-3 opacity-40" />
          <p class="text-sm">暂无工程</p>
          <p class="text-[11px] mt-1">请先在组态设计中创建并发布工程。</p>
          <button
            v-if="isAdmin"
            @click="goEditor"
            class="mt-4 inline-flex items-center gap-1.5 px-4 py-1.5 rounded-lg text-xs font-bold bg-[#1890ff] text-white hover:bg-[#40a9ff] cursor-pointer"
          ><Pencil class="w-3.5 h-3.5" /> 去组态设计</button>
        </div>
      </div>

      <!-- 工程卡片网格 -->
      <div v-else>
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          <button
            v-for="(p, idx) in projectSummaries"
            :key="p.id"
            @click="enterProject(p.id)"
            class="group text-left bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-700/80 hover:border-[#1890ff] dark:hover:border-sky-500 shadow-sm hover:shadow-lg transition-all cursor-pointer overflow-hidden"
            :title="`进入「${p.name}」组态运行`"
          >
            <!-- 卡片头：序号着色块 -->
            <div class="h-20 bg-gradient-to-tr from-sky-600 to-indigo-600 dark:from-sky-800 dark:to-indigo-900 flex items-center justify-center relative">
              <span class="text-3xl font-black text-white/30 select-none">{{ String(idx + 1).padStart(2, '0') }}</span>
              <div class="absolute bottom-2 right-2 w-7 h-7 rounded-full bg-white/15 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity">
                <ArrowRight class="w-4 h-4 text-white" />
              </div>
            </div>
            <!-- 卡片体 -->
            <div class="p-3.5">
              <h2 class="text-sm font-black text-slate-800 dark:text-slate-100 leading-tight truncate">{{ p.name }}</h2>
              <p class="mt-1.5 text-[11px] text-slate-500 dark:text-slate-400 leading-relaxed line-clamp-2">{{ p.description || '暂无描述' }}</p>
              <div class="mt-2.5 pt-2.5 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between">
                <span class="text-[10px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-wide">SCADA Project</span>
                <span class="inline-flex items-center gap-1 text-[10px] font-bold text-[#1890ff] dark:text-sky-400">
                  进入运行<ArrowRight class="w-3 h-3 group-hover:translate-x-0.5 transition-transform" />
                </span>
              </div>
            </div>
          </button>
        </div>
      </div>
    </main>
  </div>
</template>