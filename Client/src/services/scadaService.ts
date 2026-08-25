import { HMIComponent, ScadaScreenProject, ScadaPage } from '../types';
import { scadaProjects, selectedProjectId, selectedPageId } from '../store/scadaStore';
import { addLog } from '../store/index';
import * as api from '../api/scadaApi';

/**
 * 组态编辑器持久化编排层（阶段2）。
 *
 * 设计：按实体持久化 + 父级先保存 + 防抖 PUT，避免引入 SaveLayout 往返匹配复杂度。
 *  - id 双轨：前端 uid（string，UI key）与后端 serverId（int 自增）并存；
 *    新实体本地先有 uid，POST 后回填 serverId，后续 PUT/DELETE 直接用 serverId。
 *  - 父级先保存：新增组件前确保所属页面已落库（ensurePageSaved），
 *    新增页面前确保所属工程已落库（ensureProjectSaved），消除层级新建的鸡生蛋问题。
 *  - 高频更新（拖拽/缩放/属性编辑）走 600ms 防抖的 per-component PUT，避免请求风暴。
 *  - 失败由 http 拦截器统一弹 Toast；本地乐观更新已先行，此处仅负责落库。
 */

// 局部更新当前页组件数组（内存，保持原有行为）
export const updateCurrentPageComponents = (newComponents: HMIComponent[]) => {
  const projIdx = scadaProjects.value.findIndex(p => p.id === selectedProjectId.value);
  if (projIdx === -1) return;
  const pageIdx = scadaProjects.value[projIdx].pages.findIndex(pg => pg.id === selectedPageId.value);
  if (pageIdx === -1) return;

  // 直接替换数组（保证引用更新触发响应式）
  scadaProjects.value[projIdx].pages[pageIdx].components = [...newComponents];
};

// 定位包含某页面的工程
const findProjectOf = (page: ScadaPage): ScadaScreenProject | undefined => {
  return scadaProjects.value.find(p => p.pages.some(pg => pg === page || pg.id === page.id));
};

// 确保工程已落库，返回 serverId
export const ensureProjectSaved = async (proj: ScadaScreenProject): Promise<number> => {
  if (proj.serverId && proj.serverId > 0) return proj.serverId;
  const id = await api.createProject(api.toProjectDto(proj));
  proj.serverId = id;
  return id;
};

// 确保页面已落库（先确保工程），返回 serverId
export const ensurePageSaved = async (page: ScadaPage, proj: ScadaScreenProject): Promise<number> => {
  if (page.serverId && page.serverId > 0) return page.serverId;
  const projectId = await ensureProjectSaved(proj);
  const id = await api.createPage(api.toPageDto(page, projectId));
  page.serverId = id;
  return id;
};

// 新增组件：落库并回填 serverId
export const persistNewComponent = async (page: ScadaPage, proj: ScadaScreenProject, comp: HMIComponent) => {
  const pageId = await ensurePageSaved(page, proj);
  const id = await api.createComponent(api.toComponentDto(comp, pageId));
  comp.serverId = id;
};

// 新增页面（含其下组件）落库
export const persistDuplicatePage = async (page: ScadaPage, proj: ScadaScreenProject) => {
  const pageId = await ensurePageSaved(page, proj);
  await Promise.all((page.components || []).map(async (c) => {
    const id = await api.createComponent(api.toComponentDto(c, pageId));
    c.serverId = id;
  }));
};

// 防抖：组件属性/位置变更（已落库才 PUT）
const _updateTimers = new Map<string, ReturnType<typeof setTimeout>>();
export const persistComponentUpdate = (page: ScadaPage, comp: HMIComponent) => {
  if (!comp.serverId || !page.serverId) return; // 未落库的靠 persistNewComponent 处理
  const key = comp.id;
  if (_updateTimers.has(key)) clearTimeout(_updateTimers.get(key)!);
  const t = setTimeout(() => {
    api.updateComponent(api.toComponentDto(comp, page.serverId!)).catch(() => { /* toast by interceptor */ });
  }, 600);
  _updateTimers.set(key, t);
};

export const persistComponentDelete = async (comp: HMIComponent) => {
  if (comp.serverId) await api.deleteComponent(comp.serverId);
};

export const persistPageUpdate = async (page: ScadaPage) => {
  if (!page.serverId) return;
  const proj = findProjectOf(page);
  await api.updatePage(api.toPageDto(page, proj?.serverId ?? 0));
};

export const persistPageDelete = async (page: ScadaPage) => {
  if (page.serverId) await api.deletePage(page.serverId);
};

export const persistProjectUpdate = async (proj: ScadaScreenProject) => {
  if (!proj.serverId) return;
  await api.updateProject(api.toProjectDto(proj));
};

export const persistProjectDelete = async (proj: ScadaScreenProject) => {
  if (proj.serverId) await api.deleteProject(proj.serverId);
};
