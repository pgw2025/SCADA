import { HMIComponent, ScadaScreenProject, ScadaPage, ScadaPageFolder } from '../types';
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
  const id = await withRetry(() => api.createPage(api.toPageDto(page, projectId, buildFolderIdMap(proj))));
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
    withRetry(() => api.updatePage(api.toPageDto(page, proj?.serverId ?? 0, proj ? buildFolderIdMap(proj) : undefined)))
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

/**
 * 页面在夹间/段间移动：立即（非防抖）落库 FolderId，随后的 reorder 才满足后端
 * 「排序项须已在目标父级」的校验（ReorderAsync 只重排、不搬家）。
 */
export const persistPageMove = async (page: ScadaPage, proj: ScadaScreenProject): Promise<boolean> => {
  if (!page.serverId) return false;
  clearPageUpdateTimer(page.serverId);
  await withRetry(() => api.updatePage(api.toPageDto(page, proj.serverId ?? 0, buildFolderIdMap(proj))));
  return true;
};

export const persistProjectUpdate = async (proj: ScadaScreenProject) => {
  if (!proj.serverId) return;
  await withRetry(() => api.updateProject(api.toProjectDto(proj)));
};

export const persistProjectDelete = async (proj: ScadaScreenProject) => {
  if (proj.serverId) await withRetry(() => api.deleteProject(proj.serverId));
};

// ===== 画面文件夹持久化（P5：文件夹 uid/serverId 双轨 + 拖拽/排序编排）=====

/** 当前工程 文件夹uid -> serverId 映射（用于 toDto 的 folderId 解析、reorder 前后端一致） */
export const buildFolderIdMap = (proj: ScadaScreenProject): Map<string, number> => {
  const map = new Map<string, number>();
  proj.folders.forEach(f => { if (f.serverId && f.serverId > 0) map.set(f.id, f.serverId); });
  return map;
};

/** 后端 FolderDto 所需线格式：folderId(uid) -> parentFolderId(int)，父夹未落库时解析为 null（根级） */
const toFolderDto = (folder: ScadaPageFolder, proj: ScadaScreenProject) => {
  let parentServerId: number | null = null;
  if (folder.parentFolderId) {
    parentServerId = buildFolderIdMap(proj).get(folder.parentFolderId) ?? null;
    if (parentServerId == null) {
      const m = /^srv-(\d+)$/.exec(folder.parentFolderId);
      if (m) parentServerId = Number(m[1]);
    }
  }
  return {
    id: folder.serverId ?? 0,
    projectId: proj.serverId ?? 0,
    parentFolderId: parentServerId,
    platform: folder.platform,
    name: folder.name,
    sortOrder: folder.sortOrder ?? 0,
  };
};

// 确保文件夹已落库（先确保工程 + 父夹），返回 serverId
export const ensureFolderSaved = async (folder: ScadaPageFolder, proj: ScadaScreenProject): Promise<number> => {
  if (folder.serverId && folder.serverId > 0) return folder.serverId;
  const projectId = await ensureProjectSaved(proj);
  // 父文件夹（uid）先落库，保证其 serverId 可被解析为后端 int parentFolderId
  let parentServerId: number | null = null;
  if (folder.parentFolderId) {
    const parent = proj.folders.find(f => f.id === folder.parentFolderId);
    if (parent) parentServerId = await ensureFolderSaved(parent, proj);
    else {
      const m = /^srv-(\d+)$/.exec(folder.parentFolderId);
      if (m) parentServerId = Number(m[1]);
    }
  }
  const id = await withRetry(() => api.createPageFolder({
    projectId,
    parentFolderId: parentServerId,
    platform: folder.platform,
    name: folder.name,
    sortOrder: folder.sortOrder ?? 0,
  }));
  folder.serverId = id;
  return id;
};

// 防抖：文件夹重命名/移动（已落库才 PUT）
const _folderUpdateTimers = new Map<string, ReturnType<typeof setTimeout>>();
export const persistFolderUpdate = async (folder: ScadaPageFolder, proj: ScadaScreenProject): Promise<void> => {
  // 新建后立即改名：此时 serverId 可能尚未就绪。若存在在途创建（__creating）则等它完成，
  // 否则先行创建，随后以最新名字补一次更新，避免刷新后名字回退为默认名。
  if (!folder.serverId) {
    const creating = (folder as any).__creating as Promise<number> | undefined;
    if (creating) await creating.catch(() => { });
    else await ensureFolderSaved(folder, proj);
    if (!folder.serverId) return;
    scheduleFolderPut(folder, proj);
    return;
  }
  scheduleFolderPut(folder, proj);
};

const scheduleFolderPut = (folder: ScadaPageFolder, proj: ScadaScreenProject) => {
  const key = String(folder.serverId);
  if (_folderUpdateTimers.has(key)) clearTimeout(_folderUpdateTimers.get(key)!);
  _folderUpdateTimers.set(key, setTimeout(() => {
    _folderUpdateTimers.delete(key);
    withRetry(() => api.updatePageFolder(toFolderDto(folder, proj)))
      .catch(() => { /* toast by interceptor */ });
  }, 600));
};

/** 清除文件夹防抖定时器：删除前调用，避免残留 PUT 打到已删除文件夹。 */
export const clearFolderUpdateTimer = (folderServerId: number | undefined) => {
  if (folderServerId == null) return;
  const key = String(folderServerId);
  const t = _folderUpdateTimers.get(key);
  if (t) { clearTimeout(t); _folderUpdateTimers.delete(key); }
};

// 删除文件夹：默认 reparent（内容上提）；cascade 可选（连同子夹与画面删除）
export const persistFolderDelete = async (folder: ScadaPageFolder, mode: 'reparent' | 'cascade' = 'reparent') => {
  clearFolderUpdateTimer(folder.serverId);
  if (folder.serverId) await withRetry(() => api.deletePageFolder(folder.serverId, mode));
};

/** 文件夹夹间移动：立即（非防抖）落库 ParentFolderId，供 reorder 校验使用。 */
export const persistFolderMove = async (folder: ScadaPageFolder, proj: ScadaScreenProject): Promise<boolean> => {
  if (!folder.serverId) return false;
  clearFolderUpdateTimer(folder.serverId);
  await withRetry(() => api.updatePageFolder(toFolderDto(folder, proj)));
  return true;
};

/**
 * 同级统一排序落库：folderIds/pageIds 为前端 uid 数组（按目标顺序），
 * 排序赋 1..N 后全量提交 reorder。父级用 uid，未落库时解析为 null（根级）。
 */
export const persistFolderReorder = async (
  proj: ScadaScreenProject,
  platform: 'Desktop' | 'Mobile' | 'Popup',
  parentFolderId: string | undefined,
  folderIds: string[],
  pageIds: string[]
) => {
  if (!proj.serverId) return;
  const map = buildFolderIdMap(proj);
  let parentServerId: number | null = parentFolderId ? (map.get(parentFolderId) ?? null) : null;
  if (parentServerId == null && parentFolderId) {
    const m = /^srv-(\d+)$/.exec(parentFolderId);
    if (m) parentServerId = Number(m[1]);
  }
  await withRetry(() => api.reorderPageFolders({
    platform,
    parentFolderId: parentServerId,
    folders: folderIds.map((fid, i) => ({ id: map.get(fid) ?? 0, sortOrder: i + 1 })),
    pages: pageIds.map((pid, i) => {
      const page = proj.pages.find(p => p.id === pid);
      return { id: page?.serverId ?? Number((/^srv-(\d+)$/.exec(pid))?.[1] ?? 0), sortOrder: i + 1 };
    }),
  }));
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
