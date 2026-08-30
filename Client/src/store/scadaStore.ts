import { computed, ref, watch } from 'vue';
import { ScadaScreenProject, ScadaPage } from '../types';
import * as api from '../api/scadaApi';

/**
 * 组态编辑器数据源（D1=方案B「消除」：不再内置模板示例工程）。
 *
 *  - 启动时为空数组；仅由 initializeScada 从后端整树加载，后端为空则保持空态，
 *    由编辑器空态 UI 引导「新建工程」。模板 JSON（templates.ts）已随本方案删除。
 *  - selectedProjectId / selectedPageId 初始为空，加载到首个工程后再赋值。
 */
export const scadaProjects = ref<ScadaScreenProject[]>([]);

export const selectedProjectId = ref<string>('');
export const selectedPageId = ref<string>('');

/** 组态整树加载中（编辑器/卡片页据此显示加载态，避免闪空态） */
export const scadaLoading = ref(false);

export const currentProject = computed(() => {
  return scadaProjects.value.find(p => p.id === selectedProjectId.value) || scadaProjects.value[0];
});

export const currentPage = computed(() => {
  const proj = currentProject.value;
  if (!proj) return undefined;
  return proj.pages.find(pg => pg.id === selectedPageId.value) || proj.pages[0];
});

/**
 * 空页占位（冻结）：无工程/无页面时的安全兜底。
 * 仅供 currentPageSafe 使用：读取方可安全访问 components 等字段；
 * 冻结保证不会被误写（严格模式下写入抛错，暴露误用）。
 */
const _EMPTY_PAGE: ScadaPage = { id: '', name: '', components: [] };
export const EMPTY_PAGE: Readonly<ScadaPage> = Object.freeze(_EMPTY_PAGE);

/**
 * currentPage 的安全版本：无工程/无页面（整树加载前/后端为空）时返回空页占位，
 * components 恒为数组，供计算属性与渲染安全读取；语义判断（空态 UI）仍用 currentPage。
 */
export const currentPageSafe = computed<ScadaPage>(() => currentPage.value ?? (EMPTY_PAGE as ScadaPage));

// 双布局：当前编辑/查看的端（Desktop / Mobile）。缺省 Desktop。
export const currentPlatform = ref<'Desktop' | 'Mobile'>('Desktop');

/** 组态设计全屏模式状态（全屏下隐藏系统顶部菜单、系统侧边栏与编辑器左侧工程列表） */
export const isScadaFullscreen = ref<boolean>(false);
export const toggleScadaFullscreen = (val?: boolean) => {
  if (typeof val === 'boolean') {
    isScadaFullscreen.value = val;
  } else {
    isScadaFullscreen.value = !isScadaFullscreen.value;
  }
};

// 按归属端分组的页面列表（缺省按 Desktop 处理），编辑器页面树据此分两栏。
export const desktopPages = computed(() =>
  (currentProject.value?.pages ?? []).filter(p => (p.platform ?? 'Desktop') === 'Desktop'));
export const mobilePages = computed(() =>
  (currentProject.value?.pages ?? []).filter(p => (p.platform ?? 'Mobile') === 'Mobile'));

// 选中页面时自动同步当前端，保证视口与页面归属一致。
watch(selectedPageId, (id) => {
  const pg = currentProject.value?.pages.find(p => p.id === id);
  if (pg) currentPlatform.value = (pg.platform ?? 'Desktop') as 'Desktop' | 'Mobile';
});

/**
 * 挂载时从后端整树加载组态（D1=方案B「消除」）。
 *  - 后端有工程 -> 用后端数据（带 serverId）替换空列表；
 *  - 后端为空 / 未认证 / 请求失败 -> 保持空列表（不再回退本地模板），
 *    编辑器空态 UI 引导「新建工程」。scadaLoading 供 UI 显示加载态。
 */
let _scadaInitialized = false;
export const initializeScada = async () => {
  if (_scadaInitialized) return;
  _scadaInitialized = true;
  scadaLoading.value = true;
  try {
    const summaries = await api.loadProjectSummaries();
    if (summaries && summaries.length > 0) {
      const trees = await Promise.all(summaries.map(s => api.loadProjectFull(s.id)));
      const projects = trees.map(api.fromProjectFullDto).filter(Boolean) as ScadaScreenProject[];
      if (projects.length > 0) {
        scadaProjects.value = projects;
        selectedProjectId.value = projects[0].id;
        selectedPageId.value = projects[0].pages[0]?.id || '';
        // 同步维护摘要列表（供工程卡片页使用）
        projectSummaries.value = summaries.map(s => ({ id: s.id, name: s.name, description: s.description }));
      }
    }
  } catch {
    // 后端不可用 / 未认证：静默保持空列表，允许下次重试
    _scadaInitialized = false;
  } finally {
    scadaLoading.value = false;
  }
};

// ===== 组态运行多工程（方案B：路由两级）=====
// 一级卡片列表页只拉摘要；进入具体工程才按 id 懒加载完整树。

/** 工程摘要列表（卡片页数据源，只含 id/name/description） */
export const projectSummaries = ref<{ id: number; name: string; description: string }[]>([]);

/**
 * 加载工程摘要（轻量，供工程卡片列表页使用）。
 *  - 向后端拉取摘要；后端不可用 / 未认证时回退用已带 serverId 的完整工程映射。
 */
let _summariesInitialized = false;
export const initializeProjectSummaries = async () => {
  if (_summariesInitialized) return;
  try {
    const list = await api.loadProjectSummaries();
    if (list && list.length > 0) {
      projectSummaries.value = list.map(s => ({ id: s.id, name: s.name, description: s.description }));
      _summariesInitialized = true;
      return;
    }
  } catch {
    // 后端不可用 / 未认证：落到下方回退
  }
  const fallback = scadaProjects.value
    .filter(p => p.serverId != null)
    .map(p => ({ id: p.serverId!, name: p.name, description: p.description }));
  if (fallback.length > 0) {
    projectSummaries.value = fallback;
    _summariesInitialized = true;
  }
};

/** 按后端数值 id 或前端字符串 id 定位工程；未加载则返回 undefined */
const findProject = (rawId: string | number) =>
  scadaProjects.value.find(p => p.serverId === Number(rawId))
  || scadaProjects.value.find(p => p.id === String(rawId));

/**
 * 选中具体工程（组态画布页）：
 *  - 已有完整树 → 直接使用；
 *  - 否则按后端 id 懒加载完整树并 upsert 到本地工程列表；
 *  - 非法 id 或请求失败 → 返回 null，由页面决定空态。
 */
export const selectProject = async (rawId: string | number): Promise<ScadaScreenProject | null> => {
  const existing = findProject(rawId);
  if (existing) {
    selectedProjectId.value = existing.id;
    return existing;
  }
  const num = Number(rawId);
  if (!Number.isFinite(num) || num <= 0) return null;
  try {
    const proj = api.fromProjectFullDto(await api.loadProjectFull(num));
    const idx = scadaProjects.value.findIndex(p => p.serverId === num);
    if (idx >= 0) scadaProjects.value.splice(idx, 1, proj);
    else scadaProjects.value.push(proj);
    selectedProjectId.value = proj.id;
    return proj;
  } catch {
    return null;
  }
};

/**
 * 导入导出后的刷新：强制从后端重拉指定工程完整树。
 * 先从本地移除再走 selectProject 懒加载，保证拿到最新数据。
 */
export const reloadProjectTree = async (serverId: number): Promise<ScadaScreenProject | null> => {
  const idx = scadaProjects.value.findIndex(p => p.serverId === serverId);
  if (idx >= 0) scadaProjects.value.splice(idx, 1);
  return selectProject(serverId);
};

/** 工程摘要列表 upsert（导入后卡片页立即可见，无需整页刷新） */
export const upsertProjectSummary = (s: { id: number; name: string; description: string }) => {
  const i = projectSummaries.value.findIndex(p => p.id === s.id);
  if (i >= 0) projectSummaries.value.splice(i, 1, s);
  else projectSummaries.value.push(s);
};
