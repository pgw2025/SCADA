<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { useRouter } from 'vue-router';
import { projectSummaries, initializeProjectSummaries, upsertProjectSummary } from '../store/scadaStore';
import { loginUser } from '../store/userStore';
import { currentTheme, toggleTheme, performLogout } from '../store';
import { ROLE_ADMIN, ROLE_OPERATOR } from '../constants/roles';
import { exportProjectFile, parseTransferFile, importProject, loadProjectAuthorizations, saveProjectAuthorizations, ProjectSummaryDto } from '../api/scadaApi';
import { fetchSystemUsers } from '../api/authApi';
import { SystemUser } from '../types';
import {
  MonitorPlay,
  LogOut,
  ArrowRight,
  Pencil,
  LayoutGrid,
  List,
  Download,
  Upload,
  UserCheck,
  X,
  Search,
  Droplets,
  Zap,
  Wind,
  Cpu,
  Clock,
  Layers,
  CheckCircle2,
  RefreshCw,
  Sun,
  Moon
} from 'lucide-vue-next';

const router = useRouter();

const loading = ref(true);
const loadingError = ref(false);

// 实时工控大屏时钟
const currentTime = ref('');
let timerId: any = null;

const updateClock = () => {
  const now = new Date();
  const y = now.getFullYear();
  const m = String(now.getMonth() + 1).padStart(2, '0');
  const d = String(now.getDate()).padStart(2, '0');
  const hh = String(now.getHours()).padStart(2, '0');
  const mm = String(now.getMinutes()).padStart(2, '0');
  const ss = String(now.getSeconds()).padStart(2, '0');
  currentTime.value = `${y}-${m}-${d} ${hh}:${mm}:${ss}`;
};

// 交互过滤与视图模式
const searchQuery = ref('');
const selectedCategory = ref<string>('ALL');
const sortBy = ref<'default' | 'name' | 'variable'>('default');
const viewMode = ref<'grid' | 'list'>('grid');

// 工程卡片加载
onMounted(async () => {
  updateClock();
  timerId = setInterval(updateClock, 1000);
  try {
    await initializeProjectSummaries();
  } catch {
    loadingError.value = true;
  } finally {
    loading.value = false;
  }
});

onUnmounted(() => {
  if (timerId) clearInterval(timerId);
});

const isAdmin = computed(() => loginUser.value?.role === ROLE_ADMIN);
const isOperator = computed(() => loginUser.value?.role === ROLE_OPERATOR);

// 识别工程的工业工艺类型
const getCategory = (p: ProjectSummaryDto): string => {
  if (p.category) return p.category;
  if (p.name.includes('水') || p.name.includes('热') || p.name.includes('泵')) return '水处理与动力';
  if (p.name.includes('电') || p.name.includes('配') || p.name.includes('能')) return '变配电与能耗';
  if (p.name.includes('风') || p.name.includes('暖') || p.name.includes('空') || p.name.includes('车间')) return '暖通净化与环境';
  return '综合自动化';
};

// 工艺主题配色与图标（自适应深浅两套色彩空间）
const getTheme = (cat: string) => {
  switch (cat) {
    case '水处理与动力':
      return {
        key: 'water',
        gradient: 'from-sky-100/90 via-sky-50 to-cyan-100/70 dark:from-sky-950/80 dark:via-slate-900 dark:to-cyan-950/70 border-b border-sky-200/80 dark:border-sky-900/40',
        border: 'border-sky-200 hover:border-sky-400 dark:border-sky-500/30 dark:hover:border-sky-400',
        accentBg: 'bg-white/90 text-sky-600 border-sky-200 shadow-xs dark:bg-sky-500/15 dark:text-sky-400 dark:border-sky-500/30',
        glow: 'shadow-sky-500/10',
        badge: 'bg-white/80 text-sky-700 border border-sky-200 dark:bg-sky-500/20 dark:text-sky-300 dark:border-sky-500/30',
        icon: Droplets,
        iconColor: 'text-sky-600 dark:text-sky-400',
        gridStroke: 'text-sky-500/30 dark:text-sky-300'
      };
    case '变配电与能耗':
      return {
        key: 'power',
        gradient: 'from-amber-100/90 via-amber-50 to-orange-100/70 dark:from-amber-950/70 dark:via-slate-900 dark:to-indigo-950/80 border-b border-amber-200/80 dark:border-amber-900/40',
        border: 'border-amber-200 hover:border-amber-400 dark:border-amber-500/30 dark:hover:border-amber-400',
        accentBg: 'bg-white/90 text-amber-600 border-amber-200 shadow-xs dark:bg-amber-500/15 dark:text-amber-400 dark:border-amber-500/30',
        glow: 'shadow-amber-500/10',
        badge: 'bg-white/80 text-amber-800 border border-amber-200 dark:bg-amber-500/20 dark:text-amber-300 dark:border-amber-500/30',
        icon: Zap,
        iconColor: 'text-amber-600 dark:text-amber-400',
        gridStroke: 'text-amber-500/30 dark:text-amber-300'
      };
    case '暖通净化与环境':
      return {
        key: 'hvac',
        gradient: 'from-emerald-100/90 via-emerald-50 to-teal-100/70 dark:from-emerald-950/70 dark:via-slate-900 dark:to-teal-950/80 border-b border-emerald-200/80 dark:border-emerald-900/40',
        border: 'border-emerald-200 hover:border-emerald-400 dark:border-emerald-500/30 dark:hover:border-emerald-400',
        accentBg: 'bg-white/90 text-emerald-600 border-emerald-200 shadow-xs dark:bg-emerald-500/15 dark:text-emerald-400 dark:border-emerald-500/30',
        glow: 'shadow-emerald-500/10',
        badge: 'bg-white/80 text-emerald-800 border border-emerald-200 dark:bg-emerald-500/20 dark:text-emerald-300 dark:border-emerald-500/30',
        icon: Wind,
        iconColor: 'text-emerald-600 dark:text-emerald-400',
        gridStroke: 'text-emerald-500/30 dark:text-emerald-300'
      };
    default:
      return {
        key: 'automation',
        gradient: 'from-indigo-100/90 via-slate-50 to-purple-100/70 dark:from-indigo-950/70 dark:via-slate-900 dark:to-purple-950/70 border-b border-indigo-200/80 dark:border-indigo-900/40',
        border: 'border-indigo-200 hover:border-indigo-400 dark:border-indigo-500/30 dark:hover:border-indigo-400',
        accentBg: 'bg-white/90 text-indigo-600 border-indigo-200 shadow-xs dark:bg-indigo-500/15 dark:text-indigo-400 dark:border-indigo-500/30',
        glow: 'shadow-indigo-500/10',
        badge: 'bg-white/80 text-indigo-800 border border-indigo-200 dark:bg-indigo-500/20 dark:text-indigo-300 dark:border-indigo-500/30',
        icon: Cpu,
        iconColor: 'text-indigo-600 dark:text-indigo-400',
        gridStroke: 'text-indigo-500/30 dark:text-indigo-300'
      };
  }
};

// 预定义工艺分类选项
const categories = [
  { id: 'ALL', name: '全部工程' },
  { id: '水处理与动力', name: '水处理与动力', icon: Droplets },
  { id: '变配电与能耗', name: '变配电与能耗', icon: Zap },
  { id: '暖通净化与环境', name: '暖通净化与环境', icon: Wind },
  { id: '综合自动化', name: '综合自动化', icon: Cpu }
];

const getCategoryCount = (catId: string) => {
  if (catId === 'ALL') return projectSummaries.value.length;
  return projectSummaries.value.filter(p => getCategory(p) === catId).length;
};

// 过滤与排序
const filteredProjects = computed(() => {
  let list = [...projectSummaries.value];

  // 按分类
  if (selectedCategory.value !== 'ALL') {
    list = list.filter(p => getCategory(p) === selectedCategory.value);
  }

  // 按关键字
  if (searchQuery.value.trim()) {
    const q = searchQuery.value.toLowerCase().trim();
    list = list.filter(p =>
      p.name.toLowerCase().includes(q) ||
      (p.description && p.description.toLowerCase().includes(q)) ||
      getCategory(p).toLowerCase().includes(q)
    );
  }

  // 排序
  if (sortBy.value === 'name') {
    list.sort((a, b) => a.name.localeCompare(b.name, 'zh-CN'));
  } else if (sortBy.value === 'variable') {
    list.sort((a, b) => (b.variableCount || 0) - (a.variableCount || 0));
  }

  return list;
});

// 独立运行页：新标签页打开
const enterProject = (id: number) => {
  window.open(router.resolve(`/scada-view/${id}`).href, '_blank');
};

const goEditor = (id?: number) => {
  if (id) {
    router.push(`/scada-editor?projectId=${id}`);
  } else {
    router.push('/scada-editor');
  }
};

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

// ===== 工程导入导出 =====
const importInput = ref<HTMLInputElement | null>(null);
const importMessage = ref<{ type: 'success' | 'error'; text: string } | null>(null);
const triggerImport = () => importInput.value?.click();

const handleImportFile = async (e: Event) => {
  const input = e.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = '';
  if (!file) return;
  importMessage.value = null;
  try {
    const pkg = await parseTransferFile(file);
    const result = await importProject(pkg);
    upsertProjectSummary({
      id: result.projectId,
      name: result.projectName,
      description: pkg.project?.description || '',
      pageCount: result.importedPages || 1,
      variableCount: 36,
      resolution: '1920×1080',
      status: 'Ready',
      version: 'v1.0'
    });
    const warn = result.warnings.length ? `；${result.warnings.length} 条绑定告警` : '';
    importMessage.value = {
      type: 'success',
      text: `已导入工程「${result.projectName}」（画面 ${result.importedPages}、组件 ${result.importedComponents}${warn}）`
    };
  } catch (err: any) {
    importMessage.value = { type: 'error', text: err?.response?.data?.message || err?.message || '导入失败' };
  }
};

const handleExport = async (p: { id: number; name: string }) => {
  importMessage.value = null;
  try {
    await exportProjectFile(p.id, p.name);
  } catch {
    importMessage.value = { type: 'error', text: '导出失败，请稍后重试' };
  }
};

// ===== 工程授权管理 =====
const authModal = ref<{
  visible: boolean;
  project: { id: number; name: string } | null;
  users: SystemUser[];
  checkedIds: Set<number>;
  loading: boolean;
  saving: boolean;
}>({
  visible: false,
  project: null,
  users: [],
  checkedIds: new Set(),
  loading: false,
  saving: false
});

const openAuthModal = async (p: { id: number; name: string }) => {
  authModal.value = { visible: true, project: p, users: [], checkedIds: new Set(), loading: true, saving: false };
  importMessage.value = null;
  try {
    const [users, grants] = await Promise.all([
      fetchSystemUsers(),
      loadProjectAuthorizations(p.id)
    ]);
    authModal.value.users = users.filter(u => u.role !== ROLE_ADMIN);
    authModal.value.checkedIds = new Set(grants.map(g => g.userId));
  } catch (err: any) {
    importMessage.value = { type: 'error', text: err?.response?.data?.message || err?.message || '加载授权信息失败' };
  } finally {
    authModal.value.loading = false;
  }
};

const closeAuthModal = () => {
  if (authModal.value.saving) return;
  authModal.value.visible = false;
};

const toggleAuthUser = (userId: number) => {
  const set = authModal.value.checkedIds;
  if (set.has(userId)) set.delete(userId);
  else set.add(userId);
};

const saveAuthModal = async () => {
  const proj = authModal.value.project;
  if (!proj) return;
  authModal.value.saving = true;
  importMessage.value = null;
  try {
    const count = authModal.value.checkedIds.size;
    await saveProjectAuthorizations(proj.id, [...authModal.value.checkedIds]);
    importMessage.value = { type: 'success', text: `已保存工程「${proj.name}」的授权（${count} 个用户）` };
    authModal.value.visible = false;
  } catch (err: any) {
    importMessage.value = { type: 'error', text: err?.response?.data?.message || err?.message || '保存授权失败' };
  } finally {
    authModal.value.saving = false;
  }
};
</script>

<template>
  <div
    class="h-full w-full flex flex-col bg-slate-100 dark:bg-[#070b14] text-slate-800 dark:text-slate-100 overflow-hidden select-none font-sans transition-colors duration-200">
    <!-- 顶部工业控制台 Header -->
    <header
      class="h-14 bg-white dark:bg-[#0b1120] border-b border-slate-200 dark:border-slate-800/80 px-4 sm:px-6 flex items-center justify-between shrink-0 shadow-xs z-30 transition-colors">
      <!-- 左侧：SCADA平台品牌标识 -->
      <div class="flex items-center gap-3 min-w-0">
        <div
          class="w-8 h-8 sm:w-9 sm:h-9 rounded-xl bg-gradient-to-tr from-sky-500 to-indigo-600 flex items-center justify-center shadow-md shadow-sky-500/20 shrink-0">
          <MonitorPlay class="w-4 h-4 sm:w-5 sm:h-5 text-white" />
        </div>
        <div class="min-w-0">
          <div class="flex items-center gap-2">
            <h1 class="text-xs sm:text-sm font-bold tracking-wide text-slate-900 dark:text-white uppercase truncate">
              组态运行控制台
            </h1>
            <span
              class="inline-flex items-center gap-1 text-[9px] sm:text-[10px] font-mono px-2 py-0.5 rounded-full bg-emerald-50 text-emerald-700 border border-emerald-200 dark:bg-emerald-500/15 dark:border-emerald-500/30 dark:text-emerald-400">
              <span
                class="w-1.5 h-1.5 rounded-full bg-emerald-500 dark:bg-emerald-400 animate-ping inline-block"></span>
              SCADA RUNTIME
            </span>
          </div>
          <span
            class="text-[10px] sm:text-[11px] text-slate-500 dark:text-slate-400 leading-none hidden sm:inline-block">
            实时就绪 {{ projectSummaries.length }} 个工业现场工程 · 集中监控与调度
          </span>
        </div>
      </div>

      <!-- 右侧：深浅色切换、工业时钟、用户信息与系统控制按钮 -->
      <div class="flex items-center gap-2 sm:gap-3 text-xs">
        <!-- 主题切换按钮 (即时深浅色切换) -->
        <button id="btn-scada-theme-toggle" type="button" @click="toggleTheme"
          class="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg border transition-all cursor-pointer select-none active:scale-95 shadow-xs text-xs font-semibold"
          :class="currentTheme === 'dark'
            ? 'bg-slate-900 border-slate-700 text-amber-400 hover:bg-slate-800'
            : 'bg-slate-100 border-slate-200 text-slate-700 hover:bg-slate-200/80 hover:text-slate-900'"
          :title="currentTheme === 'dark' ? '当前：深色模式 (点击切换为浅色)' : '当前：浅色模式 (点击切换为深色)'">
          <Sun v-if="currentTheme === 'dark'" class="w-3.5 h-3.5 text-amber-400" />
          <Moon v-else class="w-3.5 h-3.5 text-sky-600" />
          <span class="hidden md:inline">{{ currentTheme === 'dark' ? '深色模式' : '浅色模式' }}</span>
        </button>

        <!-- 实时系统时钟 -->
        <div
          class="hidden lg:flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-slate-100 dark:bg-slate-900/90 border border-slate-200 dark:border-slate-800 text-slate-600 dark:text-slate-400 font-mono text-[11px]">
          <Clock class="w-3.5 h-3.5 text-sky-600 dark:text-sky-400" />
          <span>{{ currentTime || '2026-09-05 12:00:00' }}</span>
        </div>

        <!-- 当前用户身份徽章 -->
        <div
          class="inline-flex items-center gap-1.5 bg-slate-100 dark:bg-slate-900/90 border border-slate-200 dark:border-slate-800 px-2.5 py-1.5 rounded-lg text-slate-700 dark:text-slate-200">
          <span class="w-2 h-2 rounded-full bg-emerald-500"></span>
          <span class="font-medium truncate max-w-[80px] sm:max-w-none">{{ loginUser?.username || 'user' }}</span>
          <span
            class="text-[10px] font-mono px-1.5 py-0.5 rounded bg-white dark:bg-slate-800 text-sky-600 dark:text-sky-400 border border-slate-200 dark:border-slate-700/60">
            {{ isAdmin ? '管理员' : isOperator ? '操作员' : '观察员' }}
          </span>
        </div>

        <!-- 导入工程按钮（管理员） -->
        <button v-if="isAdmin" id="btn-import-project" @click="triggerImport"
          class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-sky-600 hover:bg-sky-500 text-white shadow-xs transition-colors cursor-pointer active:scale-95"
          title="从 .scada-project.json 文件导入工程">
          <Upload class="w-3.5 h-3.5" />
          <span class="hidden sm:inline">导入工程</span>
        </button>

        <!-- 组态设计跳转（管理员） -->
        <button v-if="isAdmin" id="btn-go-editor" @click="() => goEditor()"
          class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-100 hover:bg-slate-200 border border-slate-200 text-slate-700 dark:bg-slate-800 dark:hover:bg-slate-700 dark:border-slate-700 dark:text-slate-200 transition-colors cursor-pointer active:scale-95"
          title="跳转到组态编辑器">
          <Pencil class="w-3.5 h-3.5 text-sky-600 dark:text-sky-400" />
          <span class="hidden md:inline">组态设计</span>
        </button>

        <!-- 退出登录按钮 -->
        <button id="btn-logout" @click="onLogout"
          class="p-2 rounded-lg text-slate-500 hover:text-rose-600 hover:bg-rose-50 dark:text-slate-400 dark:hover:text-rose-400 dark:hover:bg-rose-500/10 border border-transparent hover:border-rose-200 dark:hover:border-rose-500/20 transition-all cursor-pointer"
          title="退出登录">
          <LogOut class="w-4 h-4" />
        </button>
      </div>
    </header>

    <!-- 检索与分类控制栏 (Workbench Toolbar) -->
    <div
      class="bg-slate-50 dark:bg-[#0c1322] border-b border-slate-200 dark:border-slate-800 px-4 sm:px-6 py-2.5 flex flex-wrap items-center justify-between gap-3 shrink-0 transition-colors">
      <!-- 分类标签过滤器 -->
      <div class="flex items-center gap-1.5 overflow-x-auto no-scrollbar py-0.5 max-w-full">
        <button v-for="cat in categories" :key="cat.id" :id="`tab-cat-${cat.id}`" @click="selectedCategory = cat.id"
          class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all whitespace-nowrap cursor-pointer active:scale-95"
          :class="selectedCategory === cat.id
            ? 'bg-sky-600 text-white shadow-sm shadow-sky-600/30'
            : 'bg-white dark:bg-slate-900/80 text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800/80 border border-slate-200 dark:border-slate-800'">
          <component :is="cat.icon || Layers" class="w-3.5 h-3.5" />
          <span>{{ cat.name }}</span>
          <span class="text-[10px] font-mono px-1.5 py-0.2 rounded-full"
            :class="selectedCategory === cat.id ? 'bg-white/20 text-white' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-500'">
            {{ getCategoryCount(cat.id) }}
          </span>
        </button>
      </div>

      <!-- 右侧控制组：即时检索、排序、视图切换 -->
      <div class="flex items-center gap-2.5 ml-auto">
        <!-- 搜索输入框 -->
        <div class="relative w-48 sm:w-64">
          <Search class="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500" />
          <input id="input-project-search" v-model="searchQuery" type="text" placeholder="搜索工程名称、工艺、描述..."
            class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-lg pl-8 pr-7 py-1.5 text-xs text-slate-800 dark:text-slate-200 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:border-sky-500 transition-colors" />
          <button v-if="searchQuery" @click="searchQuery = ''"
            class="absolute right-2 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-700 dark:text-slate-500 dark:hover:text-slate-300">
            <X class="w-3.5 h-3.5" />
          </button>
        </div>

        <!-- 排序方式 -->
        <select id="select-project-sort" v-model="sortBy"
          class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-lg px-2.5 py-1.5 text-xs text-slate-700 dark:text-slate-300 focus:outline-none focus:border-sky-500 transition-colors cursor-pointer">
          <option value="default">默认排序</option>
          <option value="name">工程名称 (A-Z)</option>
          <option value="variable">测点规模 (从大到小)</option>
        </select>

        <!-- 视图切换：网格卡片 / 紧凑列表 -->
        <div
          class="flex items-center bg-slate-200/80 dark:bg-slate-900 p-0.5 rounded-lg border border-slate-200 dark:border-slate-800">
          <button id="btn-view-grid" @click="viewMode = 'grid'" class="p-1.5 rounded-md transition-all cursor-pointer"
            :class="viewMode === 'grid' ? 'bg-sky-600 text-white shadow-xs' : 'text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'"
            title="网格卡片视图">
            <LayoutGrid class="w-3.5 h-3.5" />
          </button>
          <button id="btn-view-list" @click="viewMode = 'list'" class="p-1.5 rounded-md transition-all cursor-pointer"
            :class="viewMode === 'list' ? 'bg-sky-600 text-white shadow-xs' : 'text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'"
            title="紧凑列表视图">
            <List class="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </div>

    <!-- 主展示区 -->
    <main class="flex-1 overflow-auto bg-slate-100 dark:bg-[#070b14] p-4 sm:p-6 transition-colors">
      <!-- 导入导出结果消息条 -->
      <div v-if="importMessage" id="alert-import-message"
        class="mb-4 px-4 py-3 rounded-xl border text-xs flex items-center justify-between shadow-sm" :class="importMessage.type === 'success'
          ? 'bg-emerald-50 border-emerald-200 text-emerald-800 dark:bg-emerald-950/40 dark:border-emerald-700/60 dark:text-emerald-300'
          : 'bg-rose-50 border-rose-200 text-rose-800 dark:bg-rose-950/40 dark:border-rose-700/60 dark:text-rose-300'">
        <div class="flex items-center gap-2">
          <CheckCircle2 v-if="importMessage.type === 'success'"
            class="w-4 h-4 text-emerald-600 dark:text-emerald-400 shrink-0" />
          <X v-else class="w-4 h-4 text-rose-600 dark:text-rose-400 shrink-0" />
          <span class="whitespace-pre-wrap">{{ importMessage.text }}</span>
        </div>
        <button @click="importMessage = null"
          class="ml-3 font-bold text-slate-400 hover:text-slate-700 dark:hover:text-white cursor-pointer">✕</button>
      </div>

      <input ref="importInput" type="file" accept=".json,application/json" class="hidden" @change="handleImportFile" />

      <!-- 加载中状态 -->
      <div v-if="loading" class="h-full flex items-center justify-center text-slate-500">
        <div class="text-center">
          <RefreshCw class="w-8 h-8 mx-auto mb-3 animate-spin text-sky-600 dark:text-sky-500 opacity-80" />
          <p class="text-sm font-medium text-slate-700 dark:text-slate-300">正在同步组态工程清单…</p>
          <p class="text-xs text-slate-500 mt-1">连接工业通信网关中</p>
        </div>
      </div>

      <!-- 加载失败/重试 -->
      <div v-else-if="loadingError" class="h-full flex items-center justify-center text-center text-slate-400">
        <div
          class="max-w-sm p-6 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm">
          <div
            class="w-12 h-12 rounded-full bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/20 text-rose-600 dark:text-rose-400 flex items-center justify-center mx-auto mb-3">
            <X class="w-6 h-6" />
          </div>
          <p class="text-sm font-bold text-slate-800 dark:text-slate-200 mb-1">工程列表加载失败</p>
          <p class="text-xs text-slate-500 mb-4 leading-relaxed">后端通信或认证服务无响应，请检查通信链路后重试</p>
          <button id="btn-retry-load" @click="retry"
            class="px-4 py-2 rounded-xl text-xs font-bold bg-slate-100 hover:bg-slate-200 text-slate-800 border border-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 dark:text-slate-200 dark:border-slate-700 cursor-pointer active:scale-95">
            重新连接加载
          </button>
        </div>
      </div>

      <!-- 工程总库完全为空 -->
      <div v-else-if="projectSummaries.length === 0"
        class="h-full flex items-center justify-center text-center text-slate-400">
        <div
          class="max-w-md p-8 rounded-2xl bg-white dark:bg-slate-900/60 border border-slate-200 dark:border-slate-800 shadow-sm">
          <div
            class="w-14 h-14 rounded-2xl bg-sky-50 dark:bg-sky-500/10 border border-sky-200 dark:border-sky-500/20 text-sky-600 dark:text-sky-400 flex items-center justify-center mx-auto mb-4">
            <MonitorPlay class="w-7 h-7" />
          </div>
          <h3 class="text-base font-bold text-slate-800 dark:text-slate-200 mb-1">暂无组态运行工程</h3>
          <p class="text-xs text-slate-500 dark:text-slate-400 mb-5 leading-relaxed">
            {{ isAdmin ? '您可以直接进入组态设计器新建并发布工程，或者通过文件导入已有工程包。' : '暂无可访问的组态工程，请联系管理员为您配置工程访问授权。' }}
          </p>
          <div v-if="isAdmin" class="flex items-center justify-center gap-3">
            <button id="btn-empty-go-editor" @click="() => goEditor()"
              class="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl text-xs font-bold bg-sky-600 hover:bg-sky-500 text-white cursor-pointer shadow-md shadow-sky-600/20 active:scale-95">
              <Pencil class="w-3.5 h-3.5" /> 去组态设计
            </button>
            <button id="btn-empty-import" @click="triggerImport"
              class="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl text-xs font-bold bg-slate-100 hover:bg-slate-200 text-slate-700 border border-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 dark:text-slate-200 dark:border-slate-700 cursor-pointer active:scale-95">
              <Upload class="w-3.5 h-3.5" /> 导入工程文件
            </button>
          </div>
        </div>
      </div>

      <!-- 搜索过滤无结果状态 -->
      <div v-else-if="filteredProjects.length === 0"
        class="h-64 flex flex-col items-center justify-center text-center text-slate-400">
        <Search class="w-10 h-10 text-slate-400 dark:text-slate-600 mb-3" />
        <p class="text-sm font-medium text-slate-700 dark:text-slate-300">没有匹配的组态工程</p>
        <p class="text-xs text-slate-500 mt-1 mb-4">
          未找到与关键词「{{ searchQuery }}」相关的工程，请调整搜索条件
        </p>
        <button id="btn-reset-filters" @click="searchQuery = ''; selectedCategory = 'ALL'"
          class="px-3.5 py-1.5 rounded-lg text-xs font-semibold bg-slate-100 hover:bg-slate-200 text-sky-600 border border-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 dark:text-sky-400 dark:border-slate-700 cursor-pointer active:scale-95">
          重置检索条件
        </button>
      </div>

      <!-- ================= 方案 A：工业数字孪生 · 工艺特征卡片网格 ================= -->
      <div v-else-if="viewMode === 'grid'">
        <div class="grid gap-5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-3 2xl:grid-cols-4">
          <div v-for="p in filteredProjects" :key="p.id" :id="`project-card-${p.id}`"
            class="group text-left bg-white dark:bg-gradient-to-b dark:from-[#0e1626] dark:to-[#0a0f1d] rounded-2xl border border-slate-200 dark:border-slate-800 hover:border-sky-400 dark:hover:border-sky-500/80 shadow-xs hover:shadow-xl hover:shadow-sky-500/5 dark:hover:shadow-sky-500/10 transition-all duration-300 flex flex-col justify-between overflow-hidden relative">
            <!-- 卡片头部工艺看板视窗 -->
            <div class="h-28 relative p-4 flex flex-col justify-between overflow-hidden bg-gradient-to-br"
              :class="getTheme(getCategory(p)).gradient">
              <!-- 工业精密网格纹理背景 (SVG) -->
              <svg class="absolute inset-0 w-full h-full opacity-15 pointer-events-none"
                xmlns="http://www.w3.org/2000/svg">
                <defs>
                  <pattern :id="`grid-${p.id}`" width="16" height="16" patternUnits="userSpaceOnUse">
                    <path d="M 16 0 L 0 0 0 16" fill="none" stroke="currentColor" stroke-width="0.8"
                      :class="getTheme(getCategory(p)).gridStroke" />
                  </pattern>
                </defs>
                <rect width="100%" height="100%" :fill="`url(#grid-${p.id})`" />
              </svg>

              <!-- 顶部信息栏：工程编号 + 状态呼吸灯 -->
              <div class="flex items-center justify-between relative z-10">
                <!-- 工程编号与版本 -->
                <div class="flex items-center gap-1.5">
                  <span
                    class="text-[11px] font-mono font-bold px-2 py-0.5 rounded-md bg-white/90 text-slate-800 border border-slate-200/80 shadow-xs dark:bg-black/40 dark:text-slate-200 dark:border-white/10">
                    PRJ-{{ String(p.id).padStart(2, '0') }}
                  </span>
                  <span
                    class="text-[10px] font-mono px-1.5 py-0.5 rounded bg-white/70 text-slate-600 border border-slate-200/60 dark:bg-white/10 dark:text-slate-300 dark:border-transparent">
                    {{ p.version || 'v2.0' }}
                  </span>
                </div>

                <!-- 工业运行状态呼吸灯指示 -->
                <div
                  class="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-[10px] font-semibold bg-white/90 border border-slate-200/80 shadow-xs dark:bg-black/40 dark:border-white/10">
                  <span class="w-1.5 h-1.5 rounded-full"
                    :class="(p.status || 'Running') === 'Running' ? 'bg-emerald-500 dark:bg-emerald-400 animate-pulse ring-2 ring-emerald-400/30' : 'bg-sky-500 dark:bg-sky-400'"></span>
                  <span
                    :class="(p.status || 'Running') === 'Running' ? 'text-emerald-700 dark:text-emerald-400' : 'text-sky-700 dark:text-sky-400'">
                    {{ (p.status || 'Running') === 'Running' ? '实时运行' : '系统就绪' }}
                  </span>
                </div>
              </div>

              <!-- 中部工艺特征概念展示：专属工艺图标与名称 -->
              <div class="flex items-center gap-3 relative z-10 mt-1">
                <div class="w-10 h-10 rounded-xl flex items-center justify-center shadow-xs backdrop-blur-md border"
                  :class="getTheme(getCategory(p)).accentBg">
                  <component :is="getTheme(getCategory(p)).icon" class="w-5 h-5"
                    :class="getTheme(getCategory(p)).iconColor" />
                </div>
                <div class="min-w-0 flex-1">
                  <span
                    class="text-[10px] font-semibold tracking-wider uppercase text-slate-500 dark:text-slate-400 block">
                    {{ getCategory(p) }}
                  </span>
                  <span class="text-[11px] text-slate-700 dark:text-slate-300 font-mono truncate block">
                    工艺回路就绪 · 实时遥测
                  </span>
                </div>
              </div>
            </div>

            <!-- 卡片主体：工程名称、描述与工业核心指标 -->
            <div class="p-4 flex-1 flex flex-col justify-between">
              <div>
                <!-- 工程标题 -->
                <h2 @click="enterProject(p.id)"
                  class="text-sm font-bold text-slate-900 dark:text-white group-hover:text-sky-600 dark:group-hover:text-sky-400 transition-colors cursor-pointer truncate"
                  :title="p.name">
                  {{ p.name }}
                </h2>

                <!-- 工程描述 -->
                <p class="mt-1.5 text-xs text-slate-500 dark:text-slate-400 line-clamp-2 leading-relaxed h-8">
                  {{ p.description || '暂无工艺描述信息，该工程已完成工业网关与组态图元绑定。' }}
                </p>
              </div>

              <!-- 工业关键指标矩阵 (Key Metrics Bar) -->
              <div class="mt-4 pt-3 border-t border-slate-100 dark:border-slate-800/80">
                <div class="grid grid-cols-3 gap-2">
                  <div
                    class="bg-slate-50 dark:bg-slate-900/90 rounded-lg p-2 border border-slate-200/80 dark:border-slate-800/80 text-center">
                    <span class="text-[10px] text-slate-400 dark:text-slate-500 block">画面数量</span>
                    <span class="text-xs font-mono font-bold text-slate-800 dark:text-slate-200">
                      {{ p.pageCount || 1 }} <span
                        class="text-[10px] text-slate-400 dark:text-slate-500 font-normal">P</span>
                    </span>
                  </div>
                  <div
                    class="bg-slate-50 dark:bg-slate-900/90 rounded-lg p-2 border border-slate-200/80 dark:border-slate-800/80 text-center">
                    <span class="text-[10px] text-slate-400 dark:text-slate-500 block">测点规模</span>
                    <span class="text-xs font-mono font-bold text-emerald-600 dark:text-emerald-400">
                      {{ p.variableCount || (p.id === 1 ? 48 : p.id === 2 ? 64 : 32) }} <span
                        class="text-[10px] text-slate-400 dark:text-slate-500 font-normal">点</span>
                    </span>
                  </div>
                  <div
                    class="bg-slate-50 dark:bg-slate-900/90 rounded-lg p-2 border border-slate-200/80 dark:border-slate-800/80 text-center">
                    <span class="text-[10px] text-slate-400 dark:text-slate-500 block">画布比例</span>
                    <span class="text-xs font-mono font-bold text-sky-600 dark:text-sky-400">
                      {{ p.resolution ? p.resolution.split('×')[0] : '1920' }}
                    </span>
                  </div>
                </div>
              </div>

              <!-- 底部操作按钮栏 (工控触控友好) -->
              <div
                class="mt-4 pt-3 border-t border-slate-100 dark:border-slate-800/80 flex items-center justify-between gap-2">
                <!-- 主操作：进入运行 -->
                <button :id="`btn-enter-${p.id}`" @click="enterProject(p.id)"
                  class="flex-1 h-9 rounded-xl font-bold text-xs bg-sky-600 hover:bg-sky-500 text-white flex items-center justify-center gap-1.5 shadow-md shadow-sky-600/20 transition-all cursor-pointer active:scale-95">
                  <MonitorPlay class="w-3.5 h-3.5" />
                  <span>进入运行大屏</span>
                  <ArrowRight class="w-3.5 h-3.5 group-hover:translate-x-0.5 transition-transform" />
                </button>

                <!-- 管理员专属工具组 (始终直观可见，触控友好) -->
                <div v-if="isAdmin" class="flex items-center gap-1">
                  <!-- 导出工程 -->
                  <button :id="`btn-export-${p.id}`" @click="handleExport(p)"
                    class="w-9 h-9 rounded-xl bg-slate-100 hover:bg-slate-200 border border-slate-200 text-slate-600 hover:text-sky-600 dark:bg-slate-900 dark:hover:bg-slate-800 dark:border-slate-800 dark:text-slate-400 dark:hover:text-sky-400 flex items-center justify-center transition-colors cursor-pointer"
                    title="导出工程文件 (.scada-project.json)">
                    <Download class="w-4 h-4" />
                  </button>

                  <!-- 权限授权 -->
                  <button :id="`btn-auth-${p.id}`" @click="openAuthModal(p)"
                    class="w-9 h-9 rounded-xl bg-slate-100 hover:bg-slate-200 border border-slate-200 text-slate-600 hover:text-emerald-600 dark:bg-slate-900 dark:hover:bg-slate-800 dark:border-slate-800 dark:text-slate-400 dark:hover:text-emerald-400 flex items-center justify-center transition-colors cursor-pointer"
                    title="管理用户访问权限">
                    <UserCheck class="w-4 h-4" />
                  </button>

                  <!-- 快捷编辑 -->
                  <button :id="`btn-edit-${p.id}`" @click="() => goEditor(p.id)"
                    class="w-9 h-9 rounded-xl bg-slate-100 hover:bg-slate-200 border border-slate-200 text-slate-600 hover:text-indigo-600 dark:bg-slate-900 dark:hover:bg-slate-800 dark:border-slate-800 dark:text-slate-400 dark:hover:text-indigo-400 flex items-center justify-center transition-colors cursor-pointer"
                    title="在组态设计器中编辑该工程">
                    <Pencil class="w-4 h-4" />
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- ================= 紧凑列表视图 (List / Table Mode) ================= -->
      <div v-else
        class="bg-white dark:bg-[#0b1120] border border-slate-200 dark:border-slate-800 rounded-2xl overflow-hidden shadow-sm transition-colors">
        <table class="w-full text-left text-xs border-collapse">
          <thead>
            <tr
              class="bg-slate-50 dark:bg-slate-900/80 border-b border-slate-200 dark:border-slate-800 text-slate-500 dark:text-slate-400 uppercase tracking-wider font-mono text-[11px]">
              <th class="py-3 px-4 w-20">编号</th>
              <th class="py-3 px-4">工程名称 & 工艺分类</th>
              <th class="py-3 px-4 hidden md:table-cell">工艺描述</th>
              <th class="py-3 px-4 text-center">画面数</th>
              <th class="py-3 px-4 text-center">测点规模</th>
              <th class="py-3 px-4 hidden lg:table-cell text-center">分辨率</th>
              <th class="py-3 px-4 text-center">运行状态</th>
              <th class="py-3 px-4 text-right">操作</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100 dark:divide-slate-800/60 text-slate-700 dark:text-slate-300">
            <tr v-for="p in filteredProjects" :key="p.id" :id="`project-row-${p.id}`"
              class="hover:bg-slate-50/80 dark:hover:bg-slate-800/40 transition-colors group">
              <!-- 编号 -->
              <td class="py-3 px-4 font-mono font-bold text-sky-600 dark:text-sky-400">
                PRJ-{{ String(p.id).padStart(2, '0') }}
              </td>

              <!-- 工程名称 & 工艺分类 -->
              <td class="py-3 px-4">
                <div class="flex items-center gap-2.5">
                  <div class="w-7 h-7 rounded-lg flex items-center justify-center shrink-0"
                    :class="getTheme(getCategory(p)).accentBg">
                    <component :is="getTheme(getCategory(p)).icon" class="w-4 h-4" />
                  </div>
                  <div class="min-w-0">
                    <span @click="enterProject(p.id)"
                      class="font-bold text-slate-900 dark:text-white hover:text-sky-600 dark:hover:text-sky-400 transition-colors cursor-pointer block truncate">
                      {{ p.name }}
                    </span>
                    <span class="text-[10px] text-slate-400 dark:text-slate-500 font-medium">
                      {{ getCategory(p) }} · {{ p.version || 'v2.0' }}
                    </span>
                  </div>
                </div>
              </td>

              <!-- 描述 -->
              <td class="py-3 px-4 hidden md:table-cell text-slate-500 dark:text-slate-400 max-w-xs truncate">
                {{ p.description || '工业组态监控工程' }}
              </td>

              <!-- 画面数 -->
              <td class="py-3 px-4 text-center font-mono font-bold text-slate-800 dark:text-slate-200">
                {{ p.pageCount || 1 }} <span class="text-slate-400 dark:text-slate-500 text-[10px]">P</span>
              </td>

              <!-- 测点规模 -->
              <td class="py-3 px-4 text-center font-mono font-bold text-emerald-600 dark:text-emerald-400">
                {{ p.variableCount || (p.id === 1 ? 48 : p.id === 2 ? 64 : 32) }}
              </td>

              <!-- 分辨率 -->
              <td class="py-3 px-4 hidden lg:table-cell text-center font-mono text-slate-500 dark:text-slate-400">
                {{ p.resolution || '1920×1080' }}
              </td>

              <!-- 状态 -->
              <td class="py-3 px-4 text-center">
                <span
                  class="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-50 text-emerald-700 border border-emerald-200 dark:bg-emerald-500/10 dark:text-emerald-400 dark:border-emerald-500/20">
                  <span class="w-1.5 h-1.5 rounded-full bg-emerald-500 dark:bg-emerald-400 animate-pulse"></span>
                  实时运行
                </span>
              </td>

              <!-- 操作列 -->
              <td class="py-3 px-4 text-right">
                <div class="flex items-center justify-end gap-1.5">
                  <button :id="`btn-list-enter-${p.id}`" @click="enterProject(p.id)"
                    class="px-2.5 py-1.5 rounded-lg bg-sky-600 hover:bg-sky-500 text-white font-semibold text-xs inline-flex items-center gap-1 cursor-pointer transition-colors active:scale-95"
                    title="进入运行">
                    <MonitorPlay class="w-3.5 h-3.5" />
                    <span>运行</span>
                  </button>

                  <button v-if="isAdmin" :id="`btn-list-export-${p.id}`" @click="handleExport(p)"
                    class="p-1.5 rounded-lg bg-slate-100 hover:bg-slate-200 text-slate-600 hover:text-sky-600 border border-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 dark:text-slate-300 dark:hover:text-sky-400 dark:border-slate-700 cursor-pointer"
                    title="导出工程">
                    <Download class="w-3.5 h-3.5" />
                  </button>

                  <button v-if="isAdmin" :id="`btn-list-auth-${p.id}`" @click="openAuthModal(p)"
                    class="p-1.5 rounded-lg bg-slate-100 hover:bg-slate-200 text-slate-600 hover:text-emerald-600 border border-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 dark:text-slate-300 dark:hover:text-emerald-400 dark:border-slate-700 cursor-pointer"
                    title="管理授权">
                    <UserCheck class="w-3.5 h-3.5" />
                  </button>

                  <button v-if="isAdmin" :id="`btn-list-edit-${p.id}`" @click="() => goEditor(p.id)"
                    class="p-1.5 rounded-lg bg-slate-100 hover:bg-slate-200 text-slate-600 hover:text-indigo-600 border border-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 dark:text-slate-300 dark:hover:text-indigo-400 dark:border-slate-700 cursor-pointer"
                    title="组态编辑">
                    <Pencil class="w-3.5 h-3.5" />
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </main>

    <!-- 工程授权管理弹窗 (管理员专属) -->
    <div v-if="authModal.visible" id="modal-project-auth"
      class="fixed inset-0 bg-slate-900/60 dark:bg-black/75 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div
        class="bg-white dark:bg-slate-900 rounded-2xl shadow-2xl border border-slate-200 dark:border-slate-800 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div
          class="bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-white p-4 flex items-center justify-between border-b border-slate-200 dark:border-slate-800">
          <div
            class="flex items-center gap-2 font-bold text-xs uppercase tracking-widest text-sky-600 dark:text-sky-400">
            <UserCheck class="w-4 h-4" />
            <span>管理工程访问授权</span>
          </div>
          <button @click="closeAuthModal"
            class="text-slate-400 hover:text-slate-700 dark:hover:text-white cursor-pointer p-1 rounded-md hover:bg-slate-100 dark:hover:bg-slate-800">
            <X class="w-4 h-4" />
          </button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div
            class="bg-slate-50 dark:bg-slate-800/60 border border-slate-200 dark:border-slate-700/80 rounded-xl p-3.5">
            <p class="font-bold text-slate-800 dark:text-slate-200 truncate">工程：{{ authModal.project?.name }}</p>
            <p class="mt-1 text-[11px] text-slate-500 dark:text-slate-400 leading-relaxed">
              仅勾选的用户可在「组态运行」列表中查看并打开该工程；管理员拥有全系统全局视窗权限，无需单独授权。
            </p>
          </div>

          <div>
            <label class="text-slate-700 dark:text-slate-400 font-bold block mb-2">选择已授权的用户</label>
            <div v-if="authModal.loading" class="py-8 text-center text-slate-500">
              <RefreshCw class="w-6 h-6 mx-auto mb-2 animate-spin text-sky-500 opacity-60" />
              <p>正在读取用户权限列表…</p>
            </div>
            <div v-else-if="authModal.users.length === 0" class="py-8 text-center text-slate-500">
              暂无非管理员用户（管理员默认具有全部工程访问权限）
            </div>
            <div v-else
              class="max-h-60 overflow-auto border border-slate-200 dark:border-slate-800 rounded-xl divide-y divide-slate-100 dark:divide-slate-800 bg-slate-50/50 dark:bg-slate-950/50">
              <label v-for="u in authModal.users" :key="u.id"
                class="flex items-center gap-3 px-3.5 py-2.5 hover:bg-white dark:hover:bg-slate-800/60 transition-colors cursor-pointer">
                <input type="checkbox" :checked="authModal.checkedIds.has(u.id)" @change="toggleAuthUser(u.id)"
                  class="w-4 h-4 rounded text-sky-500 focus:ring-0 accent-sky-500 cursor-pointer" />
                <span class="flex-1 min-w-0">
                  <span class="block font-bold text-slate-800 dark:text-slate-200 truncate">{{ u.username }}</span>
                  <span class="block text-[10px] text-slate-500 dark:text-slate-400">
                    {{ u.role === 'Operator' ? '现场操作员' : '综合观察员' }}
                  </span>
                </span>
                <span class="shrink-0 text-[10px] font-bold px-2 py-0.5 rounded-full"
                  :class="u.status === 'Active'
                    ? 'bg-emerald-50 text-emerald-700 border border-emerald-200 dark:bg-emerald-500/15 dark:text-emerald-400 dark:border-emerald-500/30'
                    : 'bg-slate-100 text-slate-500 border border-slate-200 dark:bg-slate-800 dark:text-slate-500 dark:border-slate-700'">
                  {{ u.status === 'Active' ? '账号正常' : '已停用' }}
                </span>
              </label>
            </div>
          </div>
        </div>

        <div
          class="bg-slate-50 dark:bg-slate-950 p-4 flex justify-end gap-2.5 border-t border-slate-200 dark:border-slate-800 shrink-0">
          <button id="btn-auth-cancel" @click="closeAuthModal" :disabled="authModal.saving"
            class="px-4 py-1.5 rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-100 dark:hover:bg-slate-800 font-bold text-xs text-slate-700 dark:text-slate-300 cursor-pointer disabled:opacity-50 active:scale-95">
            取消
          </button>
          <button id="btn-auth-save" @click="saveAuthModal" :disabled="authModal.loading || authModal.saving"
            class="px-4 py-1.5 rounded-xl bg-sky-600 hover:bg-sky-500 font-bold text-xs text-white cursor-pointer disabled:opacity-50 shadow-md shadow-sky-600/20 active:scale-95">
            {{ authModal.saving ? '保存中…' : '保存授权' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.no-scrollbar::-webkit-scrollbar {
  display: none;
}

.no-scrollbar {
  -ms-overflow-style: none;
  scrollbar-width: none;
}
</style>
