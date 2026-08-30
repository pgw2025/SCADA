import { HMIComponent, ScadaScreenProject, ScadaPage } from '../types';
import { scadaProjects, selectedProjectId, selectedPageId } from '../store/scadaStore';
import { addLog } from '../store/index';
import * as api from '../api/scadaApi';

/**
 * 组态编辑器持久化编排层（阶段2 + 阶段3 健壮性）。
 *
 * 设计：按实体持久化 + 父级先保存 + 防抖 PUT，避免引入 SaveLayout 往返匹配复杂度。
 *  - id 双轨：前端 uid（string，UI key）与后端 serverId（int 自增）并存；
 *    新实体本地先有 uid，POST 后回填 serverId，后续 PUT/DELETE 直接用 serverId。
 *  - 父级先保存：新增组件前确保所属页面已落库（ensurePageSaved），
 *    新增页面前确保所属工程已落库（ensureProjectSaved），消除层级新建的鸡生蛋问题。
 *  - 高频更新（拖拽/缩放/属性编辑）走 600ms 防抖的 per-component PUT，避免请求风暴。
 *  - 失败重试（阶段3）：瞬时网络故障自动重试；仅重试可重试错误（网络/5xx），
 *    4xx（含 404/校验失败）不重试，避免无效请求与重复建号。
 *  - 撤销对账（阶段3）：reconcileComponents 在撤销/重做后补齐与后端的差异——
 *    清理孤儿记录（撤销「新增/复制」）、重建丢失记录（撤销「删除」）、
 *    静默 PUT 校正属性，避免刷新后画面与编辑态不一致。
 */

/** 仅网络层/5xx 视为可重试（瞬时故障）；4xx 属确定错误不重试 */
const isRetryable = (e: any) =>
  !e?.response || e.response?.status === 408 || e.response?.status === 429 || e.response?.status >= 500;

/** 瞬时失败自动重试：默认 1 次重试（共 2 次尝试），间隔 500ms */
const withRetry = async <T>(fn: () => Promise<T>, retries = 1, delayMs = 500): Promise<T> => {
  let lastErr: unknown;
  for (let i = 0; i <= retries; i++) {
    try {
      return await fn();
    } catch (e) {
      lastErr = e;
      if (i < retries && isRetryable(e)) await new Promise((r) => setTimeout(r, delayMs));
    }
  }
  throw lastErr;
};

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
  const id = await withRetry(() => api.createProject(api.toProjectDto(proj)));
  proj.serverId = id;
  return id;
};

// 确保页面已落库（先确保工程），返回 serverId
export const ensurePageSaved = async (page: ScadaPage, proj: ScadaScreenProject): Promise<number> => {
  if (page.serverId && page.serverId > 0) return page.serverId;
  const projectId = await ensureProjectSaved(proj);
  const id = await withRetry(() => api.createPage(api.toPageDto(page, projectId)));
  page.serverId = id;
  return id;
};

// 新增组件：落库并回填 serverId
export const persistNewComponent = async (page: ScadaPage, proj: ScadaScreenProject, comp: HMIComponent) => {
  const pageId = await ensurePageSaved(page, proj);
  const id = await withRetry(() => api.createComponent(api.toComponentDto(comp, pageId)));
  comp.serverId = id;
};

// 新增页面（含其下组件）落库
export const persistDuplicatePage = async (page: ScadaPage, proj: ScadaScreenProject) => {
  const pageId = await ensurePageSaved(page, proj);
  await Promise.all((page.components || []).map(async (c) => {
    const id = await withRetry(() => api.createComponent(api.toComponentDto(c, pageId)));
    c.serverId = id;
  }));
};

// 防抖：组件属性/位置变更（已落库才 PUT）
// key 用 serverId 而非 uid：复制页面/撤销重建后可能出现同 uid，用 serverId 可避免两个组件互相顶掉落库。
const _updateTimers = new Map<string, ReturnType<typeof setTimeout>>();
export const persistComponentUpdate = (page: ScadaPage, comp: HMIComponent) => {
  if (!comp.serverId || !page.serverId) return; // 未落库的靠 persistNewComponent 处理
  const key = String(comp.serverId);
  if (_updateTimers.has(key)) clearTimeout(_updateTimers.get(key)!);
  const t = setTimeout(() => {
    _updateTimers.delete(key);
    withRetry(() => api.updateComponent(api.toComponentDto(comp, page.serverId!))).catch(() => { /* toast by interceptor */ });
  }, 600);
  _updateTimers.set(key, t);
};

// 清除组件的防抖定时器：删除组件/清空画布时调用，避免残留 PUT 打到已删除的 id 上。
export const clearComponentUpdateTimer = (compId: string | number | undefined) => {
  if (compId == null) return;
  const key = String(compId);
  const t = _updateTimers.get(key);
  if (t) {
    clearTimeout(t);
    _updateTimers.delete(key);
  }
};

export const persistComponentDelete = async (comp: HMIComponent) => {
  clearComponentUpdateTimer(comp.serverId ?? comp.id);
  if (comp.serverId) await withRetry(() => api.deleteComponent(comp.serverId));
};

// 防抖：页面属性/图层变更（已落库才 PUT）。key 用 serverId，
// 透明度滑条 @input、画布尺寸拖拽等高频更新借此收敛为少量请求。
// 返回 Promise 仅为兼容既有调用处的 `.catch(() => {})`，实际 PUT 由内部定时器触发。
const _pageUpdateTimers = new Map<string, ReturnType<typeof setTimeout>>();

export const persistPageUpdate = (page: ScadaPage): Promise<void> => {
  if (!page.serverId) return Promise.resolve();
  const key = String(page.serverId);
  if (_pageUpdateTimers.has(key)) clearTimeout(_pageUpdateTimers.get(key)!);
  _pageUpdateTimers.set(key, setTimeout(() => {
    _pageUpdateTimers.delete(key);
    const proj = findProjectOf(page);
    withRetry(() => api.updatePage(api.toPageDto(page, proj?.serverId ?? 0)))
      .catch(() => { /* toast by interceptor */ });
  }, 600));
  return Promise.resolve();
};

/** 清除页面防抖定时器：删除页面前调用，避免残留 PUT 打到已删除的页面。 */
export const clearPageUpdateTimer = (pageServerId: number | undefined) => {
  if (pageServerId == null) return;
  const key = String(pageServerId);
  const t = _pageUpdateTimers.get(key);
  if (t) {
    clearTimeout(t);
    _pageUpdateTimers.delete(key);
  }
};

export const persistPageDelete = async (page: ScadaPage) => {
  clearPageUpdateTimer(page.serverId);
  if (page.serverId) await withRetry(() => api.deletePage(page.serverId));
};

export const persistProjectUpdate = async (proj: ScadaScreenProject) => {
  if (!proj.serverId) return;
  await withRetry(() => api.updateProject(api.toProjectDto(proj)));
};

export const persistProjectDelete = async (proj: ScadaScreenProject) => {
  if (proj.serverId) await withRetry(() => api.deleteProject(proj.serverId));
};

/**
 * 撤销/重做后的后端对账（阶段3）。
 *
 * 撤销/重做仅改前端内存数组，后端可能存在分歧，这里按 diff 补齐：
 *  - after 中消失且已落库的组件 → DELETE（撤销「新增/复制/清空」后清理孤儿记录）；
 *  - after 中无 serverId 的组件 → POST 新建（撤销「删除」后重建记录）；
 *  - after 中带 serverId 的组件 → 静默 PUT 校正属性；若 404（记录已被删）降级为 POST 重建。
 *  - 真实网络/5xx 错误仍照常 toast，交由用户重试；本函数不阻塞 UI。
 */
export const reconcileComponents = async (
  page: ScadaPage,
  proj: ScadaScreenProject,
  before: HMIComponent[],
  after: HMIComponent[]
) => {
  const pageId = await ensurePageSaved(page, proj);
  const afterIds = new Set(after.map((c) => c.id));

  // 1) 清理孤儿：before 中已落库、after 中已消失的组件
  await Promise.all(
    before
      .filter((c) => c.serverId && !afterIds.has(c.id))
      .map((c) => persistComponentDelete(c).catch(() => {}))
  );

  // 2) 补齐 after 中的每个组件
  await Promise.all(after.map(async (c) => {
    if (!c.serverId) {
      const id = await withRetry(() => api.createComponent(api.toComponentDto(c, pageId)));
      c.serverId = id;
      return;
    }
    try {
      await api.updateComponent(api.toComponentDto(c, pageId), { silent: true });
    } catch (e: any) {
      // 404 = 记录已被撤销「删除」时清理，降级为重建
      if (e?.response?.status === 404) {
        const id = await withRetry(() =>
          api.createComponent(api.toComponentDto({ ...c, serverId: undefined }, pageId)));
        c.serverId = id;
      }
      // 其它错误由拦截器 toast（silent 仅静默 404 路径）
    }
  }));
};
