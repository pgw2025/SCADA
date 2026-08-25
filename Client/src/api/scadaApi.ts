import { http } from './http';
import { systemConfig } from '../store/configStore';
import { HMIComponent, ScadaPage, ScadaScreenProject, ComponentType } from '../types';

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
  id: number;
  name: string;
  description: string;
  pages: PageWithComponentsDto[];
}
export interface PageWithComponentsDto {
  id: number;
  projectId: number;
  name: string;
  isHome: boolean;
  width: number;
  height: number;
  components: ComponentDto[];
}
export interface PageDto {
  id: number;
  projectId: number;
  name: string;
  isHome: boolean;
  width: number;
  height: number;
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
export const updateComponent = async (dto: Partial<ComponentDto>): Promise<void> => {
  await http.put(`${API()}/HmiComponent`, dto);
};
export const deleteComponent = async (id: number): Promise<void> => {
  await http.delete(`${API()}/HmiComponent/${id}`);
};

// ---- 前端 -> 后端 DTO ----
export const toProjectDto = (p: ScadaScreenProject) => ({
  id: p.serverId ?? 0,
  name: p.name,
  description: p.description,
});

export const toPageDto = (pg: ScadaPage, projectId: number) => ({
  id: pg.serverId ?? 0,
  projectId,
  name: pg.name,
  isHome: false,
  width: pg.width ?? PAGE_DEFAULT_W,
  height: pg.height ?? PAGE_DEFAULT_H,
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
  bindField: c.bindField || '',
  label: c.label || '',
  bindDeviceId: null,
  bindVariableKey: null,
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
  props: d.propsJson ? safeParse(d.propsJson) : {},
});

export const fromPageDto = (d: PageWithComponentsDto): ScadaPage => ({
  id: `srv-${d.id}`,
  serverId: d.id,
  name: d.name,
  width: d.width,
  height: d.height,
  components: (d.components || []).map(fromComponentDto),
});

export const fromProjectFullDto = (d: ProjectFullDto): ScadaScreenProject => ({
  id: `srv-${d.id}`,
  serverId: d.id,
  name: d.name,
  description: d.description,
  pages: (d.pages || []).map(fromPageDto),
});

const safeParse = (json: string): Record<string, any> => {
  try {
    return JSON.parse(json) || {};
  } catch {
    return {};
  }
};
