/**
 * 运行时模板源（组件库动态化核心）：
 * 组件库元数据一律来自后端模板表（P1），本地兜底种子仅用于首帧 / 后端不可用。
 * - getWidgetDef：两级匹配（store 按 templateKey 精确 → 按 renderType 泛化 → 兜底种子同序）
 * - widgetList：store（按 sortOrder 排序）∪ 兜底种子按 key 去重补齐（审查 B6：保存后即时刷新）
 */
import { ref, computed } from 'vue';
import * as api from './api/scadaApi';
import { getLucideIcon } from './lucideIcons';
import { BUILTIN_SEEDS } from './builtinSeeds';

export type WidgetCategory = 'equipment' | 'sensors' | 'structures' | 'headers';

export interface WidgetDef {
  key: string;            // templateKey（唯一）
  type: string;            // renderType（builtin=SFC 键；svg=与 key 同值，D10）
  name: string;
  defaultWidth: number;
  defaultHeight: number;
  icon: any;               // lucide 组件 | 'div-h'/'div-v'/'div-led' | svg 源码 | emoji 字符
  iconKind: 'lucide' | 'div' | 'svg' | 'emoji';
  iconColor: string;      // Tailwind 类（lucide/emoji 生效）
  description: string;
  category: WidgetCategory;
  defaultProps: () => Record<string, any>;
  renderKind: 'builtin' | 'svg';
  svgTemplate?: string | null;
  propSchema: any[];
  isSystem: boolean;
  sortOrder: number;
}

// ===== 运行时模板库 =====
export const widgetTemplates = ref<api.WidgetTemplateDto[]>([]);
export const widgetTemplatesLoaded = ref(false);
let _loading = false;

export const initializeWidgetTemplates = async (): Promise<void> => {
  if (_loading) return;
  _loading = true;
  try {
    const list = await api.loadWidgetTemplates();
    if (list.length > 0) {
      widgetTemplates.value = list;
      widgetTemplatesLoaded.value = true;
    }
  } catch {
    // 未登录 / 后端不可用：静默保留本地兜底种子，不阻断编辑器（审查 B8）
  } finally {
    _loading = false;
  }
};

const safeParse = <T,>(json: string | null | undefined, fallback: T): T => {
  try { return JSON.parse(json ?? '') as T; } catch { return fallback; }
};

const toDef = (t: api.WidgetTemplateDto): WidgetDef => ({
  key: t.templateKey,
  type: t.renderType,
  name: t.name,
  defaultWidth: t.defaultWidth,
  defaultHeight: t.defaultHeight,
  icon: t.iconKind === 'lucide' ? getLucideIcon(t.iconKey) : t.iconKey,
  iconKind: t.iconKind,
  iconColor: t.iconColor ?? '',
  description: t.description ?? '',
  category: t.category,
  defaultProps: () => safeParse(t.defaultPropsJson, {}),
  renderKind: t.renderKind,
  svgTemplate: t.svgTemplate,
  propSchema: safeParse(t.propSchemaJson, []),
  isSystem: t.isSystem,
  sortOrder: t.sortOrder,
});

/** 两级匹配（D2）：① store 按 key 精确 ② store 按 renderType 泛化（兼容存量 type）
 *  ③ 本地种子按 key ④ 本地种子按 type。语义与旧 widgetRegistry 等价。 */
export const getWidgetDef = (typeOrKey: string): WidgetDef | undefined => {
  const store = widgetTemplates.value;
  return store.find(t => t.templateKey === typeOrKey)
      ?? store.find(t => t.renderType === typeOrKey)
      ?? BUILTIN_SEEDS.find(d => d.key === typeOrKey)
      ?? BUILTIN_SEEDS.find(d => d.type === typeOrKey);
};

/** 组件库列表：store（按 sortOrder）∪ 兜底种子按 key 去重补齐 */
export const widgetList = computed<WidgetDef[]>(() => {
  const fromStore = [...widgetTemplates.value]
    .sort((a, b) => a.sortOrder - b.sortOrder || a.id - b.id)
    .map(toDef);
  const storeKeys = new Set(fromStore.map(d => d.key));
  return [...fromStore, ...BUILTIN_SEEDS.filter(d => !storeKeys.has(d.key))];
});
