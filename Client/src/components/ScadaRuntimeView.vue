<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  currentProject,
  desktopPages,
  mobilePages,
  selectProject,
} from '../store/scadaStore';
import { devices } from '../store/deviceStore';
import { loginUser } from '../store/userStore';
import { ROLE_ADMIN, ROLE_OPERATOR } from '../constants/roles';
import { isAuthenticated, performLogout } from '../store';
import { addLog } from '../store';
import { getDeviceVariableValue, setDeviceVariableValue } from '../services/dataOrchestration';
import { showToast } from '../services/toastService';
import { MonitorPlay, LogOut, Smartphone, Monitor, Home, Pencil, ArrowLeft, RefreshCw } from 'lucide-vue-next';
import CanvasPanel from './CanvasPanel.vue';

const router = useRouter();
const route = useRoute();

// 阶段4：运行时按访问设备判定默认端；手机/小屏 → 移动端，桌面/大屏 → 桌面端。
// 同时提供手动切换，便于在桌面浏览器中预览移动端组态。
const detectMobile = (): boolean =>
  window.innerWidth < 768 || /Mobi|Android|iPhone|iPad|iPod/i.test(navigator.userAgent);

const runtimePlatform = ref<'Desktop' | 'Mobile'>(detectMobile() ? 'Mobile' : 'Desktop');
const switchRuntimePlatform = (p: 'Desktop' | 'Mobile') => {
  runtimePlatform.value = p;
  // 切换端后重新选默认页
  selectedRuntimePageId.value = '';
};

// 挂载时从路由参数懒加载具体工程（方案B：/scada-view/:projectId）
const loadingProject = ref(true);
const projectError = ref(false);

const loadFromParam = async () => {
  loadingProject.value = true;
  projectError.value = false;
  selectedRuntimePageId.value = '';
  const proj = await selectProject(route.params.projectId as string);
  loadingProject.value = false;
  if (!proj) projectError.value = true;
};
watch(() => route.params.projectId, loadFromParam, { immediate: true });

// 当前端可见画面列表（同端）
const runtimePages = computed(() =>
  runtimePlatform.value === 'Mobile' ? mobilePages.value : desktopPages.value
);

// 默认落地页：所在端首页（isHome）优先，否则取第一个
const selectedRuntimePageId = ref<string>('');
watch(
  runtimePages,
  (pages) => {
    if (selectedRuntimePageId.value && pages.some((p) => p.id === selectedRuntimePageId.value)) return;
    if (!pages.length) {
      selectedRuntimePageId.value = '';
      return;
    }
    const home = pages.find((p) => p.isHome) || pages[0];
    selectedRuntimePageId.value = home.id;
  },
  { immediate: true }
);

const currentPage = computed(
  () => runtimePages.value.find((p) => p.id === selectedRuntimePageId.value) || runtimePages.value[0] || null
);

const pageWidth = computed(() => currentPage.value?.width ?? (runtimePlatform.value === 'Mobile' ? 375 : 1100));
const pageHeight = computed(() => currentPage.value?.height ?? (runtimePlatform.value === 'Mobile' ? 812 : 700));

// 严格模式：运行时实时值解析（仅复合绑定 deviceId+variableKey；禁止裸 key 取值）
const warnedUnboundIds = new Set<string>();
const componentValues = computed(() => {
  const composite: Record<string, number | boolean> = {};
  devices.value.forEach((d) => {
    if (d.status === 'online' || d.status === 1) {
      Object.keys(d.variables).forEach((k) => {
        composite[`${d.id}:${k}`] = d.variables[k];
      });
    }
  });
  const result: Record<string, number | boolean> = {};
  (currentPage.value?.components ?? []).forEach((c) => {
    if (c.bindDeviceId != null && c.bindVariableKey) {
      const v = composite[`${c.bindDeviceId}:${c.bindVariableKey}`];
      if (v !== undefined) {
        result[c.id] = v;
        return;
      }
    }
    // 严格模式：未绑定设备/变量的组件禁止裸 key 取值，显示 0 并给出一次性警告
    if (!warnedUnboundIds.has(c.id)) {
      warnedUnboundIds.add(c.id);
      addLog('组态运行', `组件 [${c.id}] 未绑定设备/变量（bindDeviceId=${c.bindDeviceId}），禁止裸 key 取值，显示 0`, 'warning');
    }
    result[c.id] = 0;
  });
  return result;
});

// 阶段5：控制下发权限——仅 Operator/Admin 可下发写指令
const canControlWrite = computed(() => {
  const r = loginUser.value?.role;
  return r === ROLE_OPERATOR || r === ROLE_ADMIN;
});

// 阶段2-2：质量分级显示——按组件绑定（deviceId+variableKey）回读变量质量，
// 非 Good 质量（Bad/Uncertain/CommunicationError/…）在画布组件上叠加角标，提示数据不可信。
const componentQualities = computed(() => {
  const result: Record<string, string> = {};
  const devIndex = new Map<number | string, any>();
  devices.value.forEach((d) => devIndex.set(d.id, d));
  (currentPage.value?.components ?? []).forEach((c) => {
    if (c.bindDeviceId != null && c.bindVariableKey) {
      const q = devIndex.get(c.bindDeviceId)?.variableMeta?.[c.bindVariableKey]?.quality;
      if (q && q !== 'Good') result[c.id] = String(q);
    }
  });
  return result;
});

// 阶段3：导航按钮跳转（同端切换，跨端不允许）
const handleNavigate = (pageId: string) => {
  const target = runtimePages.value.find((p) => p.id === pageId);
  if (!target) return;
  selectedRuntimePageId.value = pageId;
  addLog('组态运行', `跳转到画面: [${target.name}]`, 'normal');
};

// 阶段4/6-2：控件写指令（含只读拦截，与编辑器一致）
const handleTriggerToggleValue = (
  deviceId: number | null,
  variableKey: string,
  legacyKey: string,
  actionType?: string,
  val?: any
) => {
  const key = variableKey || legacyKey;
  if (!key) return;

  // 严格模式：控件未绑定设备 → 禁止裸 key 写指令
  if (deviceId == null) {
    showToast('该控件未绑定设备，禁止写入（请到编辑器补全绑定）', 'warning');
    addLog('组态运行', `写指令被拒绝：组件未绑定设备 (key=${key})`, 'warning');
    return;
  }

  const dev = devices.value.find((d) => String(d.id) === String(deviceId));
  const meta = dev?.variableMeta?.[key];
  const isReadOnly = meta?.effectiveIsReadOnly ?? meta?.EffectiveIsReadOnly ?? false;
  if (isReadOnly) {
    showToast(`变量 [${key}] 为只读，禁止写入`, 'warning');
    return;
  }

  const current = getDeviceVariableValue(deviceId, key);
  let targetVal: any;
  if (actionType === 'setValue' && val !== undefined) {
    targetVal = val;
  } else if (actionType === 'momentary' && val !== undefined) {
    targetVal = typeof current === 'boolean' ? val : val ? 1 : 0;
  } else {
    if (typeof current === 'boolean') targetVal = !current;
    else if (typeof current === 'number') targetVal = current === 0 ? 100 : 0;
    else targetVal = val ?? 1;
  }
  setDeviceVariableValue(deviceId, key, targetVal);
};

const isAdmin = computed(() => loginUser.value?.role === ROLE_ADMIN);

const onLogout = () => {
  performLogout();
  router.push('/');
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
          <h1 class="text-xs sm:text-sm font-black tracking-wider uppercase truncate">
            {{ currentProject?.name || '组态运行' }}
          </h1>
          <span class="text-[9px] sm:text-[10px] text-slate-500 dark:text-slate-400 leading-none inline-block">
            组态运行监控 · {{ runtimePlatform === 'Mobile' ? '移动端' : '桌面端' }}
          </span>
        </div>
      </div>

      <div class="flex items-center gap-2 text-[11px] font-mono">
        <!-- 端切换（预览用） -->
        <div class="hidden sm:flex items-center rounded-full border border-slate-200 dark:border-slate-700 overflow-hidden">
          <button
            @click="switchRuntimePlatform('Desktop')"
            :class="runtimePlatform === 'Desktop' ? 'bg-[#1890ff] text-white' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800'"
            class="px-2.5 py-1 cursor-pointer transition-colors flex items-center gap-1"
            title="桌面端视图"
          ><Monitor class="w-3.5 h-3.5" /> 桌面</button>
          <button
            @click="switchRuntimePlatform('Mobile')"
            :class="runtimePlatform === 'Mobile' ? 'bg-[#1890ff] text-white' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800'"
            class="px-2.5 py-1 cursor-pointer transition-colors flex items-center gap-1"
            title="移动端视图"
          ><Smartphone class="w-3.5 h-3.5" /> 移动</button>
        </div>

        <span class="hidden md:inline-flex items-center gap-1 text-slate-600 dark:text-slate-300 bg-slate-50 dark:bg-slate-800/90 border border-slate-200 dark:border-slate-700 px-2.5 py-1 rounded-lg">
          <Home class="w-3.5 h-3.5" /> {{ loginUser?.username || 'user' }}
        </span>

        <button
          @click="router.push('/scada-view')"
          class="px-2.5 py-1 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 flex items-center gap-1 cursor-pointer"
          title="返回工程列表"
        ><ArrowLeft class="w-3.5 h-3.5" /> 返回</button>

        <button
          v-if="isAdmin"
          @click="router.push('/scada-editor')"
          class="px-2.5 py-1 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 flex items-center gap-1 cursor-pointer"
          title="返回组态设计"
        ><Pencil class="w-3.5 h-3.5" /> 编辑</button>

        <button
          @click="onLogout"
          class="p-1.5 rounded-lg text-slate-400 hover:text-rose-500 hover:bg-rose-50 dark:hover:bg-rose-900/20 cursor-pointer"
          title="退出登录"
        ><LogOut class="w-4 h-4" /></button>
      </div>
    </header>

    <!-- Canvas area -->
    <main class="flex-1 flex items-center justify-center overflow-auto bg-slate-200 dark:bg-[#0b1220] p-4">
      <!-- 加载中 -->
      <div v-if="loadingProject" class="text-center text-slate-500 dark:text-slate-400">
        <RefreshCw class="w-8 h-8 mx-auto mb-2 animate-spin opacity-40" />
        <p class="text-sm">正在加载工程组态…</p>
      </div>

      <!-- 空态 / 工程加载失败 / 当前端无画面 -->
      <div v-else-if="projectError || !currentPage" class="text-center text-slate-500 dark:text-slate-400">
        <MonitorPlay class="w-10 h-10 mx-auto mb-2 opacity-40" />
        <p v-if="projectError" class="text-sm">工程加载失败或不存在</p>
        <p v-else class="text-sm">当前工程暂无「{{ runtimePlatform === 'Mobile' ? '移动端' : '桌面端' }}」组态画面</p>
        <p class="text-[11px] mt-1">请在组态设计中为该端新增画面并发布。</p>
        <button
          @click="router.push('/scada-view')"
          class="mt-4 inline-flex items-center gap-1 px-4 py-1.5 rounded-lg text-xs font-bold border border-slate-300 dark:border-slate-600 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
        ><ArrowLeft class="w-3.5 h-3.5" /> 返回工程列表</button>
      </div>

      <!-- 组态画布 -->
      <div v-else
        class="bg-white dark:bg-slate-900 rounded-xl shadow-2xl overflow-hidden ring-1 ring-slate-300 dark:ring-slate-800 flex flex-col max-h-full min-w-0"
        :class="runtimePlatform === 'Mobile' ? 'rounded-[2rem] p-2 bg-neutral-900' : ''"
      >
        <div :class="runtimePlatform === 'Mobile' ? 'rounded-[1.6rem] overflow-hidden flex flex-col flex-1 min-h-0' : 'flex flex-col flex-1 min-h-0'">
          <CanvasPanel
            class="min-h-0"
            :components="currentPage.components"
            :selectedId="null"
            :selectedIds="[]"
            :isActiveMode="true"
            :component-values="componentValues"
            :component-qualities="componentQualities"
            :canvas-width="pageWidth"
            :canvas-height="pageHeight"
            :can-control-write="canControlWrite"
            :readonly="true"
            @triggerToggleValue="handleTriggerToggleValue"
            @navigateToPage="handleNavigate"
          />
        </div>
      </div>
    </main>

    <!-- Bottom page tabs（同端画面切换） -->
    <nav
      v-if="runtimePages.length > 1"
      class="shrink-0 bg-white dark:bg-[#070b12] border-t border-slate-200 dark:border-slate-900 px-3 py-2 flex items-center gap-2 overflow-x-auto"
    >
      <button
        v-for="pg in runtimePages"
        :key="pg.id"
        @click="selectedRuntimePageId = pg.id"
        :class="[
          selectedRuntimePageId === pg.id
            ? 'bg-[#1890ff] text-white border-[#1890ff]'
            : 'bg-slate-50 dark:bg-slate-800/60 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-slate-800',
          'shrink-0 px-3.5 py-1.5 rounded-full text-xs font-bold border transition-all cursor-pointer whitespace-nowrap flex items-center gap-1'
        ]"
      >
        <span v-if="pg.isHome" class="w-1.5 h-1.5 rounded-full bg-amber-400" />
        {{ pg.name }}
      </button>
    </nav>
  </div>
</template>
