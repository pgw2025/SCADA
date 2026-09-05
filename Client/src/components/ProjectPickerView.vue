<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { projectSummaries, initializeProjectSummaries, upsertProjectSummary } from '../store/scadaStore';
import { loginUser } from '../store/userStore';
import { isAuthenticated, performLogout } from '../store';
import { ROLE_ADMIN } from '../constants/roles';
import { exportProjectFile, parseTransferFile, importProject, loadProjectAuthorizations, saveProjectAuthorizations } from '../api/scadaApi';
import { fetchSystemUsers } from '../api/authApi';
import { SystemUser } from '../types';
import { MonitorPlay, LogOut, ArrowRight, Pencil, LayoutGrid, Download, Upload, UserCheck, X } from 'lucide-vue-next';

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

// ===== 工程导入导出（独立运行页：无全局 toast 容器，结果用内联消息条展示） =====
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
    upsertProjectSummary({ id: result.projectId, name: result.projectName, description: pkg.project?.description || '' });
    const warn = result.warnings.length ? `；${result.warnings.length} 条绑定告警` : '';
    importMessage.value = { type: 'success', text: `已导入工程「${result.projectName}」（画面 ${result.importedPages}、组件 ${result.importedComponents}${warn}）` };
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

// ===== 工程授权管理（管理员专属：工程维度勾选用户，全量覆盖保存） =====
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
    // 剔除 Admin：其默认可见全部工程，无需授权记录（与后端保存时剔除双保险）
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
        <button v-if="isAdmin" @click="triggerImport"
          class="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-[11px] font-bold bg-[#1890ff] text-white hover:bg-[#40a9ff] cursor-pointer"
          title="从 .scada-project.json 文件导入工程"><Upload class="w-3.5 h-3.5" /> 导入工程</button>
        <button
          @click="onLogout"
          class="p-1.5 rounded-lg text-slate-400 hover:text-rose-500 hover:bg-rose-50 dark:hover:bg-rose-900/20 cursor-pointer"
          title="退出登录"
        ><LogOut class="w-4 h-4" /></button>
      </div>
    </header>

    <!-- 主体 -->
    <main class="flex-1 overflow-auto bg-slate-50 dark:bg-[#0b1220] p-4 sm:p-6">
      <!-- 导入导出结果消息条（独立运行页无全局 toast，内联展示） -->
      <div v-if="importMessage"
        class="mb-4 px-4 py-2.5 rounded-lg border text-xs flex items-center justify-between"
        :class="importMessage.type === 'success'
          ? 'bg-emerald-50 dark:bg-emerald-900/20 border-emerald-200 dark:border-emerald-800 text-emerald-700 dark:text-emerald-300'
          : 'bg-rose-50 dark:bg-rose-900/20 border-rose-200 dark:border-rose-800 text-rose-700 dark:text-rose-300'">
        <span class="whitespace-pre-wrap">{{ importMessage.text }}</span>
        <button @click="importMessage = null" class="ml-3 font-bold shrink-0 cursor-pointer">✕</button>
      </div>
      <input ref="importInput" type="file" accept=".json,application/json" class="hidden" @change="handleImportFile" />
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
          <p class="text-[11px] mt-1">{{ isAdmin ? '请先在组态设计中创建并发布工程。' : '暂无可访问的工程，请联系管理员授权。' }}</p>
          <button
            v-if="isAdmin"
            @click="goEditor"
            class="mt-4 inline-flex items-center gap-1.5 px-4 py-1.5 rounded-lg text-xs font-bold bg-[#1890ff] text-white hover:bg-[#40a9ff] cursor-pointer"
          ><Pencil class="w-3.5 h-3.5" /> 去组态设计</button>
        </div>
      </div>

      <!-- 工程卡片网格（外层 div 承载点击：卡片内含导出按钮，HTML 不允许 button 嵌套 button） -->
      <div v-else>
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          <div v-for="(p, idx) in projectSummaries" :key="p.id"
            @click="enterProject(p.id)"
            class="group text-left bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-700/80 hover:border-[#1890ff] dark:hover:border-sky-500 shadow-sm hover:shadow-lg transition-all cursor-pointer overflow-hidden"
            :title="`进入「${p.name}」组态运行`">
            <!-- 卡片头：序号着色块 -->
            <div class="h-20 bg-gradient-to-tr from-sky-600 to-indigo-600 dark:from-sky-800 dark:to-indigo-900 flex items-center justify-center relative">
              <span class="text-3xl font-black text-white/30 select-none">{{ String(idx + 1).padStart(2, '0') }}</span>
              <button v-if="isAdmin" @click.stop="handleExport(p)"
                class="absolute top-2 left-2 w-7 h-7 rounded-full bg-white/15 backdrop-blur flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity text-white"
                title="导出工程（.scada-project.json）"><Download class="w-4 h-4" /></button>
              <button v-if="isAdmin" @click.stop="openAuthModal(p)"
                class="absolute top-2 right-2 w-7 h-7 rounded-full bg-white/15 backdrop-blur flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity text-white"
                title="管理授权（仅授权用户可见此工程）"><UserCheck class="w-4 h-4" /></button>
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
          </div>
        </div>
      </div>
    </main>

    <!-- 工程授权管理弹窗（管理员专属） -->
    <div v-if="authModal.visible" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest text-sky-400">
            <UserCheck class="w-4 h-4" />
            <span>管理工程授权</span>
          </div>
          <button @click="closeAuthModal" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div class="bg-slate-50 dark:bg-slate-800/60 border border-slate-200 dark:border-slate-700 rounded-lg p-3">
            <p class="font-bold text-slate-700 dark:text-slate-200 truncate">工程：{{ authModal.project?.name }}</p>
            <p class="mt-1 text-[11px] text-slate-500 dark:text-slate-400 leading-relaxed">
              仅被勾选的用户能在「组态运行」中看到并打开该工程；管理员默认可见全部工程，无需授权。
            </p>
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1.5">选择可访问的用户</label>
            <div v-if="authModal.loading" class="py-6 text-center text-slate-400 dark:text-slate-500">
              <LayoutGrid class="w-6 h-6 mx-auto mb-2 animate-pulse opacity-40" />
              <p>正在加载用户列表…</p>
            </div>
            <div v-else-if="authModal.users.length === 0"
              class="py-6 text-center text-slate-400 dark:text-slate-500">暂无其他用户（管理员无需授权）</div>
            <div v-else class="max-h-60 overflow-auto border border-slate-200 dark:border-slate-700 rounded-lg divide-y divide-slate-100 dark:divide-slate-800">
              <label v-for="u in authModal.users" :key="u.id"
                class="flex items-center gap-2.5 px-3 py-2 hover:bg-slate-50 dark:hover:bg-slate-800/60 cursor-pointer">
                <input type="checkbox"
                  :checked="authModal.checkedIds.has(u.id)"
                  @change="toggleAuthUser(u.id)"
                  class="accent-sky-500 focus:ring-0 cursor-pointer" />
                <span class="flex-1 min-w-0">
                  <span class="block font-bold text-slate-700 dark:text-slate-200 truncate">{{ u.username }}</span>
                  <span class="block text-[10px] text-slate-400 dark:text-slate-500">
                    {{ u.role === 'Operator' ? '操作员' : '观察员' }}
                  </span>
                </span>
                <span class="shrink-0 text-[10px] font-bold px-1.5 py-0.5 rounded-full"
                  :class="u.status === 'Active'
                    ? 'bg-emerald-50 dark:bg-emerald-900/20 text-emerald-600 dark:text-emerald-400'
                    : 'bg-slate-100 dark:bg-slate-800 text-slate-400 dark:text-slate-500'">
                  {{ u.status === 'Active' ? '启用' : '停用' }}
                </span>
              </label>
            </div>
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-4 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800 shrink-0">
          <button
            @click="closeAuthModal"
            :disabled="authModal.saving"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer disabled:opacity-50"
          >取消</button>
          <button
            @click="saveAuthModal"
            :disabled="authModal.loading || authModal.saving"
            class="px-3.5 py-1.5 rounded-lg bg-sky-500 hover:bg-sky-600 font-bold text-xs text-white cursor-pointer disabled:opacity-50"
          >{{ authModal.saving ? '保存中…' : '保存授权' }}</button>
        </div>
      </div>
    </div>
  </div>
</template>