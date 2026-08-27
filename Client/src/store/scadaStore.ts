import { computed, ref, watch } from 'vue';
import { HMIComponent, ScadaScreenProject } from '../types';
import { TEMPLATES } from '../templates';
import * as api from '../api/scadaApi';

export const scadaProjects = ref<ScadaScreenProject[]>([
    {
        id: 'project-purify',
        name: '循环污水高倍净化系统工程',
        description: '工业曝气池双水箱重力落差级联调节、离心排量流量管线监控',
        pages: [
            {
                id: 'page-ww-primary',
                name: '曝气净化段主画面 (Primary Monitor)',
                components: JSON.parse(JSON.stringify(TEMPLATES[0].components)) // Wastewater
            },
            {
                id: 'page-ww-sub',
                name: '气动闸阀调试辅助图 (Valve Tuning Mimic)',
                components: [
                    // Subpage preloaded layout elements
                    {
                        id: 'intro-valve-sub',
                        type: 'text',
                        name: '子页面说明',
                        x: 100,
                        y: 40,
                        width: 500,
                        height: 40,
                        label: '区域B电磁排量闸阀点对点操作面板',
                        bindField: '',
                        zIndex: 1,
                        props: { fontSize: 16, bold: true, align: 'left' }
                    },
                    {
                        id: 'sub-valve-1',
                        type: 'valve',
                        name: '1号子阀 KV101',
                        x: 150,
                        y: 120,
                        width: 100,
                        height: 100,
                        label: '1号初滤进水电动阀 4001',
                        bindField: 'valve_state',
                        zIndex: 2,
                        props: { activeColor: '#10b981', inactiveColor: '#ef4444' }
                    },
                    {
                        id: 'sub-val-led1',
                        type: 'led',
                        name: '阀合闸状态',
                        x: 350,
                        y: 155,
                        width: 32,
                        height: 32,
                        label: '阀门双位行程常开指示',
                        bindField: 'valve_state',
                        zIndex: 3,
                        props: { activeColor: '#10b981', inactiveColor: '#ef4444' }
                    },
                    {
                        id: 'sub-valve-btn-ctrl',
                        type: 'button',
                        name: '按钮',
                        x: 150,
                        y: 260,
                        width: 140,
                        height: 60,
                        label: '手动阀门紧急切断',
                        bindField: 'valve_state',
                        zIndex: 3,
                        props: { buttonMode: 'toggle', buttonText: '阀门合闸/开路切换' }
                    }
                ]
            }
        ]
    },
    {
        id: 'project-boiler',
        name: '热力站2号超真空高压反应大底盘',
        description: '核心锅炉受阻高温熔池蒸汽缓冲压力、排风冷却机风扇联动监控系统',
        pages: [
            {
                id: 'page-blr-main',
                name: '过热熔融反应主视图 (Boiler Hearth)',
                components: JSON.parse(JSON.stringify(TEMPLATES[1].components)) // Thermal boiler
            }
        ]
    },
    {
        id: 'project-sorting',
        name: '3号变频传动轮物料流水分拣线',
        description: '变频电动机转速反馈与重力吨位落料池动态曲线仓储',
        pages: [
            {
                id: 'page-sort-main',
                name: '配给打包输送传送带主视图 (Packaging line)',
                components: JSON.parse(JSON.stringify(TEMPLATES[2].components)) // Conveyor
            }
        ]
    }
]);

export const selectedProjectId = ref<string>('project-purify');
export const selectedPageId = ref<string>('page-ww-primary');

export const currentProject = computed(() => {
    return scadaProjects.value.find(p => p.id === selectedProjectId.value) || scadaProjects.value[0];
});

export const currentPage = computed(() => {
  const proj = currentProject.value;
  return proj.pages.find(pg => pg.id === selectedPageId.value) || proj.pages[0];
});

// 双布局：当前编辑/查看的端（Desktop / Mobile）。缺省 Desktop。
export const currentPlatform = ref<'Desktop' | 'Mobile'>('Desktop');

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
 * 阶段2：挂载时从后端整树加载组态。
 *  - 后端有工程 -> 用后端数据（带 serverId）替换本地模板；
 *  - 后端为空 / 未登录 / 请求失败 -> 保留硬编码模板（离线可用，编辑时再落库）。
 */
let _scadaInitialized = false;
export const initializeScada = async () => {
  if (_scadaInitialized) return;
  _scadaInitialized = true;
  try {
    const summaries = await api.loadProjectSummaries();
    if (!summaries || summaries.length === 0) return; // 保留模板

    const trees = await Promise.all(summaries.map(s => api.loadProjectFull(s.id)));
    const projects = trees.map(api.fromProjectFullDto).filter(Boolean) as ScadaScreenProject[];
    if (projects.length === 0) return;

    scadaProjects.value = projects;
    selectedProjectId.value = projects[0].id;
    selectedPageId.value = projects[0].pages[0]?.id || '';
    // 同步维护摘要列表（供工程卡片页使用）
    projectSummaries.value = summaries.map(s => ({ id: s.id, name: s.name, description: s.description }));
  } catch {
    // 后端不可用 / 未认证：静默回退到本地模板
    _scadaInitialized = false; // 允许下次重试
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
