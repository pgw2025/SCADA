import { http } from './http';
import { systemConfig } from '../store/configStore';
import { HMIComponent, ScadaPage, ScadaScreenProject, ComponentType, PageBackground, PageAdaptMode } from '../types';

/**
 * 组态设计后端 API 封装（对应阶段1的 ScadaProject / ScadaPage / HmiComponent 三组端点）。
 *
 * 前后端字段映射单一真相源：
 *  - 后端 DTO 为 PascalCase，但 ASP.NET 默认 camelCase 线格式（与本项目 deviceApi 一致），
 *    故请求体/响应均按 camelCase 处理。
 *  - 前端组件 id 为字符串（uid），后端为 int 自增主键；通过 serverId 双轨对齐。
 *  - props 对象 <-> PropsJson 字符串互转。
 */
const API = () => `${systemConfig.value.backendApiUrl}/api`;

const PAGE_DEFAULT_W = 1100;
const PAGE_DEFAULT_H = 700;

// ---- 后端 DTO 线格式（camelCase） ----
export interface ProjectSummaryDto {
  id: number;
  name: string;
  description: string;
}
export interface ProjectFullDto {
  project: ProjectSummaryDto;
  pages: PageWithComponentsDto[];
}
export interface PageWithComponentsDto {
  id: number;
  projectId: number;
  name: string;
  isHome: boolean;
  platform: string;
  width: number;
  height: number;
  backgroundJson: string | null;
  adaptMode: string | null;
  components: ComponentDto[];
}
export interface PageDto {
  id: number;
  projectId: number;
  name: string;
  isHome: boolean;
  platform: string;
  width: number;
  height: number;
  backgroundJson?: string | null;
  adaptMode?: string | null;
}
export interface ComponentDto {
  id: number;
  pageId: number;
  type: string;
  name: string;
  x: number;
  y: number;
  width: number;
  height: number;
  zIndex: number;
  bindField: string;
  label: string | null;
  bindDeviceId: number | null;
  bindVariableKey: string | null;
  propsJson: string;
}

// ---- 读取 ----
export const loadProjectSummaries = async (): Promise<ProjectSummaryDto[]> => {
  const r = await http.get<ProjectSummaryDto[]>(`${API()}/ScadaProject`);
  return r.data || [];
};

export const loadProjectFull = async (id: number): Promise<ProjectFullDto> => {
  const r = await http.get<ProjectFullDto>(`${API()}/ScadaProject/${id}/full`);
  return r.data;
};

// ---- 工程 ----
export const createProject = async (dto: Partial<ProjectSummaryDto>): Promise<number> => {
  const r = await http.post<ProjectSummaryDto>(`${API()}/ScadaProject`, dto);
  return r.data.id;
};
export const updateProject = async (dto: Partial<ProjectSummaryDto>): Promise<void> => {
  await http.put(`${API()}/ScadaProject`, dto);
};
export const deleteProject = async (id: number): Promise<void> => {
  await http.delete(`${API()}/ScadaProject/${id}`);
};

// ---- 页面 ----
export const createPage = async (dto: Partial<PageDto>): Promise<number> => {
  const r = await http.post<PageDto>(`${API()}/ScadaPage`, dto);
  return r.data.id;
};
export const updatePage = async (dto: Partial<PageDto>): Promise<void> => {
  await http.put(`${API()}/ScadaPage`, dto);
};
export const deletePage = async (id: number): Promise<void> => {
  await http.delete(`${API()}/ScadaPage/${id}`);
};

// ---- 组件 ----
export const createComponent = async (dto: Partial<ComponentDto>): Promise<number> => {
  const r = await http.post<ComponentDto>(`${API()}/HmiComponent`, dto);
  return r.data.id;
};
export const updateComponent = async (dto: Partial<ComponentDto>, opts?: { silent?: boolean }): Promise<void> => {
  await http.put(`${API()}/HmiComponent`, dto, { silent: opts?.silent } as any);
};
export const deleteComponent = async (id: number): Promise<void> => {
  await http.delete(`${API()}/HmiComponent/${id}`);
};

// ---- 组态图片图库（图元/页面背景共用） ----

/** 与后端 HmiImageDto 对齐（camelCase 线格式） */
export interface HmiImageDto {
  /** 存储文件名（GUID_原名.扩展名），删除接口的标识 */
  fileName: string;
  /** 用户上传时的原始文件名（图库显示用） */
  originalName: string;
  sizeBytes: number;
  uploadedAtUtc: string;
  /** 图片访问相对 URL（/api/HmiImage/file/...） */
  url: string;
}

/** 上传图片（multipart，字段名 file）。Token 由 http 拦截器注入；失败统一 toast。 */
export const uploadHmiImage = async (file: File): Promise<HmiImageDto> => {
  const form = new FormData();
  form.append('file', file);
  const r = await http.post<HmiImageDto>(`${API()}/HmiImage/upload`, form);
  return r.data;
};

/** 图库列表（按上传时间倒序） */
export const listHmiImages = async (): Promise<HmiImageDto[]> => {
  const r = await http.get<HmiImageDto[]>(`${API()}/HmiImage/list`);
  return r.data || [];
};

/** 删除图片（后端校验 GUID 文件名格式，无引用检查——前端删除前确认提示） */
export const deleteHmiImage = async (fileName: string): Promise<void> => {
  await http.delete(`${API()}/HmiImage/${encodeURIComponent(fileName)}`);
};

// ---- 前端 -> 后端 DTO ----
export const toProjectDto = (p: ScadaScreenProject) => ({
  id: p.serverId ?? 0,
  name: p.name?.trim() || '未命名工程',
  description: p.description ?? '',
});

export const toPageDto = (pg: ScadaPage, projectId: number) => ({
  id: pg.serverId ?? 0,
  projectId,
  name: pg.name,
  isHome: pg.isHome ?? false,
  platform: pg.platform ?? 'Desktop',
  width: pg.width ?? PAGE_DEFAULT_W,
  height: pg.height ?? PAGE_DEFAULT_H,
  // 背景配置对象 <-> 后端 BackgroundJson 字符串；未配置传 null（后端归一化为 NULL）
  backgroundJson: pg.background ? JSON.stringify(pg.background) : null,
  adaptMode: pg.adaptMode ?? null,
});

export const toComponentDto = (c: HMIComponent, pageId: number) => ({
  id: c.serverId ?? 0,
  pageId,
  type: c.type,
  name: c.name,
  x: c.x,
  y: c.y,
  width: c.width,
  height: c.height,
  zIndex: c.zIndex,
  // bindVariableKey 非空时强制置空 bindField，逐步清理历史遗留的同值冗余字段，
  // 避免运行态误用 bindField 作为兜底写指令键（界面显示未绑定、实际写旧变量）。
  bindField: c.bindVariableKey ? '' : (c.bindField || ''),
  label: c.label || '',
  bindDeviceId: c.bindDeviceId ?? null,
  bindVariableKey: c.bindVariableKey ?? null,
  propsJson: JSON.stringify(c.props || {}),
});

// ---- 后端 DTO -> 前端 ----
export const fromComponentDto = (d: ComponentDto): HMIComponent => ({
  id: `srv-${d.id}`,
  serverId: d.id,
  type: d.type as ComponentType,
  name: d.name,
  x: d.x,
  y: d.y,
  width: d.width,
  height: d.height,
  zIndex: d.zIndex,
  label: d.label || '',
  bindField: d.bindField || '',
  bindDeviceId: d.bindDeviceId ?? null,
  bindVariableKey: d.bindVariableKey ?? null,
  props: d.propsJson ? safeParse(d.propsJson) : {},
});

export const fromPageDto = (d: PageWithComponentsDto): ScadaPage => ({
  id: `srv-${d.id}`,
  serverId: d.id,
  name: d.name,
  platform: d.platform === 'Mobile' ? 'Mobile' : 'Desktop',
  isHome: d.isHome,
  width: d.width,
  height: d.height,
  background: parseBackgroundJson(d.backgroundJson),
  adaptMode: d.adaptMode === 'FitScaleUp' || d.adaptMode === 'Stretch' ? (d.adaptMode as PageAdaptMode) : null,
  components: (d.components || []).map(fromComponentDto),
});

/** 后端 BackgroundJson 字符串 -> 前端背景配置对象；非法/缺失返回 null（未配置） */
const parseBackgroundJson = (json: string | null | undefined): PageBackground | null => {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json);
    if (parsed && typeof parsed === 'object' && typeof parsed.type === 'string'
      && ['color', 'gradient', 'image'].includes(parsed.type)) {
      return parsed as PageBackground;
    }
    return null;
  } catch {
    return null;
  }
};

export const fromProjectFullDto = (d: ProjectFullDto): ScadaScreenProject => ({
  id: `srv-${d.project.id}`,
  serverId: d.project.id,
  name: d.project.name,
  description: d.project.description,
  pages: (d.pages || []).map(fromPageDto),
});

const safeParse = (json: string): Record<string, any> => {
  try {
    return JSON.parse(json) || {};
  } catch {
    return {};
  }
};

// ===== 组态导入导出（工程/画面迁移文件） =====

// ---- 迁移包线格式（与后端 ScadaTransferDto 对齐，camelCase） ----
export interface TransferComponentDto {
  type: string; name: string;
  x: number; y: number; width: number; height: number; zIndex: number;
  bindField: string; label: string | null;
  bindDeviceKey: string | null; bindVariableKey: string | null;
  propsJson: string;
}
export interface TransferPageDto {
  name: string; isHome: boolean; platform: string;
  width: number; height: number;
  backgroundJson: string | null; adaptMode: string | null;
  components: TransferComponentDto[];
}
export interface TransferPackageDto {
  format: string; version: number; exportedAt: string | null;
  project: { name: string; description: string } | null;
  pages: TransferPageDto[];
}
export interface ImportResultDto {
  projectId: number; projectName: string;
  pageId: number | null; pageName: string | null;
  importedPages: number; importedComponents: number;
  warnings: string[];
}

/** 文件名清洗：剔除 Windows/浏览器非法字符 */
const sanitizeFileName = (name: string): string =>
  (name || '').replace(/[\\/:*?"<>|]/g, '_').trim() || 'export';

/** 触发浏览器下载后端返回的 blob 附件（Token 由 http 拦截器自动注入） */
const downloadBlob = (data: Blob, filename: string): void => {
  const url = URL.createObjectURL(data);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
};

/** 导出工程（整树 JSON 文件下载） */
export const exportProjectFile = async (id: number, fallbackName: string): Promise<void> => {
  const r = await http.get(`${API()}/ScadaProject/${id}/export`, { responseType: 'blob' });
  downloadBlob(r.data, `${sanitizeFileName(fallbackName)}.scada-project.json`);
};

/** 导出画面（单画面 JSON 文件下载） */
export const exportPageFile = async (id: number, fallbackName: string): Promise<void> => {
  const r = await http.get(`${API()}/ScadaPage/${id}/export`, { responseType: 'blob' });
  downloadBlob(r.data, `${sanitizeFileName(fallbackName)}.scada-page.json`);
};

/** 读取并解析导入文件（前端先做 format 存在性校验，给友好提示） */
export const parseTransferFile = async (file: File): Promise<TransferPackageDto> => {
  const parsed = JSON.parse(await file.text());
  if (!parsed || typeof parsed.format !== 'string') {
    throw new Error('不是有效的组态导出文件（缺少 format 字段）');
  }
  return parsed as TransferPackageDto;
};

/** 导入工程（返回新工程 id/名称与告警列表） */
export const importProject = async (pkg: TransferPackageDto): Promise<ImportResultDto> => {
  const r = await http.post<ImportResultDto>(`${API()}/ScadaProject/import`, pkg);
  return r.data;
};

/** 导入画面到指定工程 */
export const importPage = async (projectId: number, pkg: TransferPackageDto): Promise<ImportResultDto> => {
  const r = await http.post<ImportResultDto>(`${API()}/ScadaPage/import?projectId=${projectId}`, pkg);
  return r.data;
};
