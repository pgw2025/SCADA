<script setup lang="ts">
/**
 * 组件模板管理（P3）：组件库动态化的管理后台。
 * - 列表：图标（4 形态）/ TemplateKey / 名称 / 分类 / 渲染轨徽标 / 系统内置徽标 / 尺寸 / SortOrder
 * - CRUD：新建 / 编辑（编辑态 TemplateKey 锁定）/ 删除（IsSystem 禁用）
 * - 导入导出：文件解析（D11 兼容单对象 / templates[] / 裸数组）→ 预览 → 冲突策略（覆盖/重命名/跳过）
 * - SVG 轨：源码文本域 + 占位符速查表（点击插入光标处）+ 实时预览（P4，同画布 sanitize+bind 链路）+ 256KB 双拦截
 * 操作成功后同步刷新运行时模板 store（组件库即时更新）。
 */
import { ref, computed, watch, onMounted } from 'vue';
import {
  Shapes,
  Plus,
  Edit3,
  Trash2,
  Search,
  RefreshCw,
  Upload,
  Download,
  X,
  AlertTriangle,
  Package,
  Eye,
} from 'lucide-vue-next';
import {
  loadWidgetTemplates,
  createWidgetTemplate,
  updateWidgetTemplate,
  deleteWidgetTemplate,
  importWidgetTemplate,
  importWidgetTemplates,
  exportWidgetTemplate,
  exportWidgetTemplates,
  WidgetTemplateDto,
  WidgetTemplateImportResult,
} from '../api/scadaApi';
import { extractApiError } from '../api/http';
import { showToast } from '../services/toastService';
import { widgetTemplates } from '../widgetTemplates';
import { getLucideIcon } from '../builtinRenderers';
import { sanitizeSvg, bindSvgTemplate, SvgBindingContext } from '../utils/svgTemplate';

// ================= 列表 =================
const list = ref<WidgetTemplateDto[]>([]);
const loading = ref(false);
const keyword = ref('');
const categoryFilter = ref<'all' | 'equipment' | 'sensors' | 'structures' | 'headers'>('all');

const filteredList = computed(() => {
  const term = keyword.value.trim().toLowerCase();
  return list.value.filter((t) => {
    if (categoryFilter.value !== 'all' && t.category !== categoryFilter.value) return false;
    if (!term) return true;
    return t.name.toLowerCase().includes(term)
      || t.templateKey.toLowerCase().includes(term)
      || t.renderType.toLowerCase().includes(term);
  });
});

const refreshList = async () => {
  loading.value = true;
  try {
    const data = await loadWidgetTemplates();
    list.value = data;
    widgetTemplates.value = data; // 组件库与模板 store 即时同步（审查 B6）
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  } finally {
    loading.value = false;
  }
};

// ================= 选择（批量导出） =================
const selectedIds = ref<number[]>([]);
const isSelected = (id: number) => selectedIds.value.includes(id);
const toggleSelect = (id: number) => {
  const i = selectedIds.value.indexOf(id);
  if (i >= 0) selectedIds.value.splice(i, 1);
  else selectedIds.value.push(id);
};
const allChecked = computed(() =>
  filteredList.value.length > 0 && filteredList.value.every((t) => isSelected(t.id)));
const toggleAll = () => {
  selectedIds.value = allChecked.value ? [] : filteredList.value.map((t) => t.id);
};

// ================= 分类 / 渲染轨 / 系统徽标文案 =================
const CATEGORY_LABEL: Record<string, string> = {
  equipment: '设备', sensors: '仪表', structures: '结构', headers: '标题背景',
};
const CATEGORY_COLOR: Record<string, string> = {
  equipment: 'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 border-sky-200 dark:border-sky-800',
  sensors: 'bg-purple-50 dark:bg-purple-950/60 text-purple-600 dark:text-purple-400 border-purple-200 dark:border-purple-800',
  structures: 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-800',
  headers: 'bg-amber-50 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400 border-amber-200 dark:border-amber-800',
};

// 表格内图标预览（4 种形态；渲染逻辑与 WidgetLibrary 对齐）
const iconComp = (t: WidgetTemplateDto) =>
  t.iconKind === 'lucide' ? (getLucideIcon(t.iconKey) ?? getLucideIcon('box')) : null;

// ================= 表单（新建 / 编辑） =================
type TemplateForm = {
  templateKey: string;
  renderType: string;
  name: string;
  category: 'equipment' | 'sensors' | 'structures' | 'headers';
  description: string;
  defaultWidth: number;
  defaultHeight: number;
  iconKind: 'lucide' | 'div' | 'svg' | 'emoji';
  iconKey: string;
  iconColor: string;
  renderKind: 'builtin' | 'svg';
  svgTemplate: string;
  defaultPropsJson: string;
  propSchemaJson: string;
  sortOrder: number;
};

const showModal = ref(false);
const editingId = ref<number | null>(null);
const editingTemplate = ref<WidgetTemplateDto | null>(null);
const formError = ref('');
const saving = ref(false);

const form = ref<TemplateForm>({
  templateKey: '', renderType: '', name: '', category: 'equipment', description: '',
  defaultWidth: 120, defaultHeight: 120,
  iconKind: 'lucide', iconKey: 'box', iconColor: 'text-sky-500',
  renderKind: 'builtin', svgTemplate: '',
  defaultPropsJson: '{}', propSchemaJson: '[]', sortOrder: 100,
});

const openCreate = () => {
  editingId.value = null;
  editingTemplate.value = null;
  formError.value = '';
  form.value = {
    templateKey: '', renderType: '', name: '', category: 'equipment', description: '',
    defaultWidth: 120, defaultHeight: 120,
    iconKind: 'lucide', iconKey: 'box', iconColor: 'text-sky-500',
    renderKind: 'builtin', svgTemplate: '',
    defaultPropsJson: '{}', propSchemaJson: '[]', sortOrder: 100,
  };
  showModal.value = true;
};

const openEdit = (t: WidgetTemplateDto) => {
  editingId.value = t.id;
  editingTemplate.value = t;
  formError.value = '';
  form.value = {
    templateKey: t.templateKey,
    renderType: t.renderType,
    name: t.name,
    category: t.category,
    description: t.description ?? '',
    defaultWidth: t.defaultWidth,
    defaultHeight: t.defaultHeight,
    iconKind: t.iconKind,
    iconKey: t.iconKey ?? '',
    iconColor: t.iconColor ?? '',
    renderKind: t.renderKind,
    svgTemplate: t.svgTemplate ?? '',
    defaultPropsJson: t.defaultPropsJson,
    propSchemaJson: t.propSchemaJson,
    sortOrder: t.sortOrder,
  };
  showModal.value = true;
};

// 表单内图标预览
const formIconComp = computed(() =>
  form.value.iconKind === 'lucide' ? (getLucideIcon(form.value.iconKey) ?? getLucideIcon('box')) : null);

// SVG 轨占位符速查表（P3-3.4 先展示，P4 实现实时预览）
const SVG_PLACEHOLDERS: Array<{ ph: string; desc: string }> = [
  { ph: '{value}', desc: '绑定值原始值' },
  { ph: '{numValue}', desc: '数值（布尔按 100/0）' },
  { ph: '{boolValue}', desc: '布尔值（数值按 >0）' },
  { ph: '{state}', desc: '开 / 关 文案' },
  { ph: '{unit}', desc: '单位' },
  { ph: '{label}', desc: '组件标签' },
  { ph: '{activeColor}', desc: '运行色' },
  { ph: '{inactiveColor}', desc: '停止色' },
  { ph: '{alertColor}', desc: '告警色（超阈值）' },
  { ph: '{thresholdMax}', desc: '上限阈值' },
  { ph: '{thresholdMin}', desc: '下限阈值' },
  { ph: '{fontSize}', desc: '字号' },
  { ph: '{quality}', desc: '质量状态' },
  { ph: '{normalizedPercent}', desc: '量程归一化 0~100' },
  { ph: '{qualityBad}', desc: '质量不良标记' },
];

// ===== SVG 实时预览（P4）：与画布同链路 sanitize + bind，注入示例值 =====
const SVG_MAX_LEN = 256 * 1024; // 与后端 SvgSanitizer / 前端 sanitizeSvg 限长一致
const SVG_PREVIEW_CTX: SvgBindingContext = {
  value: 42.5,
  numValue: 42.5,
  boolValue: true,
  normalizedPercent: 55,
  state: '开启',
  unit: '℃',
  label: '示例组件',
  activeColor: '#10b981',
  inactiveColor: '#94a3b8',
  alertColor: '#ef4444',
  thresholdMin: 10,
  thresholdMax: 90,
  fontSize: 12,
  quality: 'Good',
};
const svgPreviewHtml = computed(() => {
  const svg = form.value.svgTemplate;
  if (!svg || !svg.trim()) return '';
  return bindSvgTemplate(sanitizeSvg(svg), SVG_PREVIEW_CTX);
});
const svgTooLarge = computed(() => form.value.svgTemplate.length > SVG_MAX_LEN);

// 「插入占位符」快捷按钮：在光标处插入，未聚焦则追加到末尾
const svgEditorRef = ref<HTMLTextAreaElement | null>(null);
const insertPlaceholder = (ph: string) => {
  const ta = svgEditorRef.value;
  if (!ta) { form.value.svgTemplate += ph; return; }
  const start = ta.selectionStart ?? form.value.svgTemplate.length;
  const end = ta.selectionEnd ?? form.value.svgTemplate.length;
  form.value.svgTemplate = form.value.svgTemplate.slice(0, start) + ph + form.value.svgTemplate.slice(end);
  const pos = start + ph.length;
  requestAnimationFrame(() => { ta.focus(); ta.setSelectionRange(pos, pos); });
};

// 新建 SVG 模板时 DefaultProps 预填绑定上下文默认值（审查 B5）
const DEFAULT_SVG_PROPS = {
  activeColor: '#3b82f6',
  inactiveColor: '#94a3b8',
  minValue: 0,
  maxValue: 100,
  unit: '℃',
  fontSize: 12,
  thresholdMin: 10,
  thresholdMax: 90,
  onText: '开启',
  offText: '关闭',
};
watch(() => form.value.renderKind, (kind) => {
  if (kind !== 'svg') return;
  const cur = form.value.defaultPropsJson.trim();
  if (!cur || cur === '{}') {
    form.value.defaultPropsJson = JSON.stringify(DEFAULT_SVG_PROPS, null, 2);
  }
});

const save = async () => {
  formError.value = '';
  if (!form.value.templateKey.trim()) { formError.value = '模板键不能为空'; return; }
  if (!form.value.renderType.trim()) { formError.value = '渲染类型不能为空'; return; }
  if (!form.value.name.trim()) { formError.value = '模板名称不能为空'; return; }
  if (form.value.renderKind === 'svg') {
    if (!form.value.svgTemplate.trim()) { formError.value = 'SVG 渲染轨必须提供 SVG 模板源码'; return; }
    if (form.value.svgTemplate.length > SVG_MAX_LEN) { formError.value = 'SVG 模板超过 256KB 上限，请精简源码'; return; }
    if (form.value.renderType.trim() !== form.value.templateKey.trim()) {
      formError.value = 'SVG 模板的渲染类型必须与模板键一致（D10 约束）'; return;
    }
  }
  try { JSON.parse(form.value.defaultPropsJson); }
  catch { formError.value = '默认属性不是合法 JSON（建议 {}）'; return; }
  try { JSON.parse(form.value.propSchemaJson); }
  catch { formError.value = '属性 Schema 不是合法 JSON（建议 []）'; return; }
  if (form.value.defaultWidth < 1 || form.value.defaultHeight < 1) { formError.value = '默认尺寸必须 ≥ 1'; return; }

  saving.value = true;
  try {
    const dto: Partial<WidgetTemplateDto> = {
      id: editingId.value ?? 0,
      templateKey: form.value.templateKey.trim(),
      renderType: form.value.renderType.trim(),
      name: form.value.name.trim(),
      category: form.value.category,
      description: form.value.description,
      defaultWidth: form.value.defaultWidth,
      defaultHeight: form.value.defaultHeight,
      iconKind: form.value.iconKind,
      iconKey: form.value.iconKey.trim(),
      iconColor: form.value.iconColor.trim(),
      renderKind: form.value.renderKind,
      svgTemplate: form.value.renderKind === 'svg' ? form.value.svgTemplate : null,
      defaultPropsJson: form.value.defaultPropsJson,
      propSchemaJson: form.value.propSchemaJson,
      sortOrder: form.value.sortOrder,
    };
    if (editingId.value != null) {
      // 后端 UpdateAsync 不覆盖 IsSystem；此处显式回传以保证 DTO 完整性
      await updateWidgetTemplate({ ...dto, isSystem: editingTemplate.value?.isSystem ?? false });
      showToast('模板已更新', 'success');
    } else {
      await createWidgetTemplate(dto);
      showToast('模板已创建', 'success');
    }
    showModal.value = false;
    await refreshList();
  } catch (e: any) {
    formError.value = extractApiError(e);
  } finally {
    saving.value = false;
  }
};

// ================= 删除 =================
const remove = async (t: WidgetTemplateDto) => {
  if (t.isSystem) return;
  if (!confirm(`确定删除模板 [${t.name}]（${t.templateKey}）吗？\n画布上已放置的该类型组件将显示为缺失，其数据保留。`)) return;
  try {
    await deleteWidgetTemplate(t.id);
    showToast('模板已删除', 'success');
    await refreshList();
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  }
};

// ================= 导入（D11：兼容单对象 / templates[] / 裸数组） =================
type ImportItem = {
  raw: Record<string, unknown>;
  key: string;
  name: string;
  category: string;
  renderKind: string;
  size: string;
  conflict: boolean;
};
const importPreview = ref<ImportItem[]>([]);
const showImportModal = ref(false);
const importing = ref(false);
const fileInput = ref<HTMLInputElement | null>(null);

const pick = (o: any, ...keys: string[]): any => {
  for (const k of keys) {
    if (o?.[k] != null) return o[k];
  }
  return undefined;
};

/** 导入项 → 后端 DTO（camelCase），兼容 PascalCase 源文件 */
const toCamel = (raw: Record<string, unknown>): Record<string, unknown> => ({
  id: Number(pick(raw, 'id', 'Id') ?? 0),
  templateKey: String(pick(raw, 'templateKey', 'TemplateKey') ?? ''),
  renderType: String(pick(raw, 'renderType', 'RenderType') ?? ''),
  name: String(pick(raw, 'name', 'Name') ?? ''),
  category: String(pick(raw, 'category', 'Category') ?? 'equipment'),
  description: String(pick(raw, 'description', 'Description') ?? ''),
  defaultWidth: Number(pick(raw, 'defaultWidth', 'DefaultWidth') ?? 120),
  defaultHeight: Number(pick(raw, 'defaultHeight', 'DefaultHeight') ?? 120),
  iconKind: String(pick(raw, 'iconKind', 'IconKind') ?? 'lucide'),
  iconKey: String(pick(raw, 'iconKey', 'IconKey') ?? ''),
  iconColor: String(pick(raw, 'iconColor', 'IconColor') ?? ''),
  renderKind: String(pick(raw, 'renderKind', 'RenderKind') ?? 'builtin'),
  svgTemplate: pick(raw, 'svgTemplate', 'SvgTemplate') ?? '',
  defaultPropsJson: String(pick(raw, 'defaultPropsJson', 'DefaultPropsJson') ?? '{}'),
  propSchemaJson: String(pick(raw, 'propSchemaJson', 'PropSchemaJson') ?? '[]'),
  isSystem: !!pick(raw, 'isSystem', 'IsSystem'),
  sortOrder: Number(pick(raw, 'sortOrder', 'SortOrder') ?? 100),
});

const onPickFile = async (e: Event) => {
  const input = e.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = '';
  if (!file) return;
  try {
    const parsed = JSON.parse(await file.text());
    let items: any[] = [];
    if (Array.isArray(parsed)) items = parsed;
    else if (Array.isArray(parsed.templates)) items = parsed.templates;
    else if (parsed && typeof parsed === 'object' && parsed.template) items = [parsed.template];
    else items = [parsed];
    if (items.length === 0) { showToast('文件中没有可导入的模板', 'error'); return; }

    const keys = new Set(list.value.map((t) => t.templateKey));
    importPreview.value = items.map((raw) => ({
      raw,
      key: String(pick(raw, 'templateKey', 'TemplateKey') ?? ''),
      name: String(pick(raw, 'name', 'Name') ?? ''),
      category: String(pick(raw, 'category', 'Category') ?? 'equipment'),
      renderKind: String(pick(raw, 'renderKind', 'RenderKind') ?? 'builtin'),
      size: `${pick(raw, 'defaultWidth', 'DefaultWidth') ?? '?'}x${pick(raw, 'defaultHeight', 'DefaultHeight') ?? '?'}`,
      conflict: keys.has(String(pick(raw, 'templateKey', 'TemplateKey') ?? '')),
    }));
    showImportModal.value = true;
  } catch (err) {
    showToast(`导入文件解析失败：${(err as Error)?.message ?? '不是合法 JSON'}`, 'error');
  }
};

const conflictCount = computed(() => importPreview.value.filter((p) => p.conflict).length);

const doImport = async (mode: 'overwrite' | 'rename' | 'skip') => {
  importing.value = true;
  try {
    const items = importPreview.value.map((p) => toCamel(p.raw));
    const conflicting = importPreview.value.map((p) => p.conflict);
    let results: WidgetTemplateImportResult[] = [];
    if (conflictCount.value > 0 && mode === 'skip') {
      const keep = items.filter((_, i) => !conflicting[i]);
      results = keep.length ? await importWidgetTemplates(keep) : [];
    } else if (conflictCount.value > 0 && mode === 'overwrite') {
      // 冲突项走单条 overwrite；无冲突项批量创建
      const overwrites: WidgetTemplateImportResult[] = [];
      for (let i = 0; i < items.length; i++) {
        if (conflicting[i]) overwrites.push(await importWidgetTemplate(items[i], 'overwrite'));
      }
      const news = items.filter((_, i) => !conflicting[i]);
      const newResults = news.length ? await importWidgetTemplates(news) : [];
      results = [...overwrites, ...newResults];
    } else {
      // 无冲突批量创建；冲突时走 rename（后端 import-bundle 固定 rename）
      results = await importWidgetTemplates(items);
    }
    const ok = results.filter((r) => r.ok).length;
    showToast(`导入完成：成功 ${ok} / ${items.length}`, ok > 0 ? 'success' : 'error');
    showImportModal.value = false;
    importPreview.value = [];
    await refreshList();
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  } finally {
    importing.value = false;
  }
};

// ================= 导出 =================
const exportOne = async (t: WidgetTemplateDto) => {
  try {
    await exportWidgetTemplate(t.id, `${t.templateKey}.widget.json`);
    showToast('已导出：' + t.templateKey, 'success');
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  }
};

const exportSelected = async () => {
  if (selectedIds.value.length === 0) { showToast('请先勾选要导出的模板', 'warning'); return; }
  try {
    const stamp = new Date().toISOString().replace(/[-:T]/g, '').slice(0, 14);
    await exportWidgetTemplates(selectedIds.value, `widget-templates-${stamp}.widget.json`);
    showToast(`已导出 ${selectedIds.value.length} 条模板`, 'success');
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  }
};

onMounted(refreshList);
</script>

<template>
  <div class="h-full overflow-y-auto p-4 sm:p-6 bg-slate-50/50 dark:bg-transparent text-[#1e293b] dark:text-slate-100 select-none">
    <!-- Header -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between border-b border-slate-200 dark:border-slate-800 pb-5 gap-4 text-left">
      <div>
        <h1 class="text-xl font-bold font-sans text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Shapes class="w-5 h-5 text-[#1890ff]" />
          组件模板管理
        </h1>
        <p class="text-xs text-slate-500 dark:text-slate-400 mt-1">
          组件库动态化：模板元数据入库管理，支持导入导出与 SVG 图形组件（零代码）
        </p>
      </div>
      <div class="flex items-center gap-2">
        <input ref="fileInput" type="file" accept=".json,.widget.json" class="hidden" @change="onPickFile" />
        <button
          @click="fileInput?.click()"
          class="border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all"
        >
          <Upload class="w-4 h-4" />
          导入
        </button>
        <button
          @click="exportSelected"
          :disabled="selectedIds.length === 0"
          class="border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all disabled:opacity-40 disabled:cursor-not-allowed"
        >
          <Download class="w-4 h-4" />
          导出选中{{ selectedIds.length ? ` (${selectedIds.length})` : '' }}
        </button>
        <button
          @click="openCreate"
          class="bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 shadow-sm"
        >
          <Plus class="w-4 h-4" />
          新建模板
        </button>
      </div>
    </div>

    <!-- Toolbar -->
    <div class="mt-4 flex flex-wrap items-center gap-2 text-left">
      <div class="relative">
        <Search class="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400" />
        <input
          v-model="keyword"
          type="text"
          placeholder="名称 / 模板键 / 渲染类型"
          class="w-56 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg pl-8 pr-2.5 py-1.5 text-xs text-slate-700 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]"
        />
      </div>
      <select
        v-model="categoryFilter"
        class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-xs font-bold text-slate-700 dark:text-slate-200 focus:outline-none"
      >
        <option value="all">全部分类</option>
        <option value="equipment">设备</option>
        <option value="sensors">仪表</option>
        <option value="structures">结构</option>
        <option value="headers">标题背景</option>
      </select>
      <button
        @click="refreshList"
        class="ml-auto text-[10px] text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 font-bold px-2 py-0.5 rounded border border-slate-200 dark:border-slate-700 hover:border-slate-300 transition-all cursor-pointer inline-flex items-center gap-1"
      >
        <RefreshCw class="w-3 h-3" :class="loading ? 'animate-spin' : ''" />
        刷新
      </button>
    </div>

    <!-- Table -->
    <div class="mt-4 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden text-left">
      <div class="overflow-x-auto">
        <table class="w-full text-xs">
          <thead>
            <tr class="bg-slate-50 dark:bg-slate-950 text-slate-500 dark:text-slate-400 text-[10px] uppercase tracking-wider">
              <th class="py-2.5 pl-4 pr-2 w-8">
                <input type="checkbox" :checked="allChecked" @change="toggleAll" class="text-[#1890ff] focus:ring-0 cursor-pointer" />
              </th>
              <th class="py-2.5 px-2 w-14 text-center">图标</th>
              <th class="py-2.5 px-2">模板键</th>
              <th class="py-2.5 px-2">名称</th>
              <th class="py-2.5 px-2">分类</th>
              <th class="py-2.5 px-2 text-center">渲染轨</th>
              <th class="py-2.5 px-2 text-center">系统内置</th>
              <th class="py-2.5 px-2 text-center">尺寸</th>
              <th class="py-2.5 px-2 text-center">排序</th>
              <th class="py-2.5 pl-2 pr-4 text-right">操作</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100 dark:divide-slate-800">
            <tr v-for="t in filteredList" :key="t.id" class="hover:bg-slate-50/60 dark:hover:bg-slate-800/40 transition-colors">
              <td class="py-2.5 pl-4 pr-2">
                <input type="checkbox" :checked="isSelected(t.id)" @change="toggleSelect(t.id)" class="text-[#1890ff] focus:ring-0 cursor-pointer" />
              </td>
              <td class="py-2.5 px-2 text-center">
                <div class="w-8 h-8 mx-auto rounded bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 flex items-center justify-center">
                  <component v-if="t.iconKind === 'lucide'" :is="iconComp(t)" class="w-4 h-4" :class="t.iconColor || 'text-slate-500'" />
                  <div v-else-if="t.iconKind === 'div' && t.iconKey === 'div-h'" class="w-6 h-1.5 bg-slate-600 dark:bg-slate-400 rounded-full" />
                  <div v-else-if="t.iconKind === 'div' && t.iconKey === 'div-v'" class="w-1.5 h-6 bg-slate-600 dark:bg-slate-400 rounded-full" />
                  <div v-else-if="t.iconKind === 'div' && t.iconKey === 'div-led'" class="w-3 h-3 rounded-full bg-emerald-500 ring-2 ring-emerald-300 dark:ring-emerald-600 animate-pulse" />
                  <span v-else-if="t.iconKind === 'emoji'" class="text-base leading-none" :class="t.iconColor || ''">{{ t.iconKey }}</span>
                  <span v-else-if="t.iconKind === 'svg'" class="w-4 h-4 flex items-center justify-center" v-html="sanitizeSvg(t.iconKey)" />
                  <Package v-else class="w-4 h-4 text-slate-400" />
                </div>
              </td>
              <td class="py-2.5 px-2 font-mono text-[#1890ff] dark:text-sky-400">{{ t.templateKey }}</td>
              <td class="py-2.5 px-2 font-sans font-bold text-slate-800 dark:text-white">{{ t.name }}</td>
              <td class="py-2.5 px-2">
                <span class="text-[10px] font-bold px-1.5 py-0.5 rounded-full border" :class="CATEGORY_COLOR[t.category] || CATEGORY_COLOR.equipment">
                  {{ CATEGORY_LABEL[t.category] || t.category }}
                </span>
              </td>
              <td class="py-2.5 px-2 text-center">
                <span class="text-[10px] font-bold px-1.5 py-0.5 rounded border"
                  :class="t.renderKind === 'svg'
                    ? 'bg-violet-50 dark:bg-violet-950/60 text-violet-600 dark:text-violet-400 border-violet-200 dark:border-violet-800'
                    : 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700'">
                  {{ t.renderKind === 'svg' ? 'SVG' : '内置' }}
                </span>
              </td>
              <td class="py-2.5 px-2 text-center">
                <span v-if="t.isSystem" class="text-[10px] font-bold px-1.5 py-0.5 rounded-full bg-amber-50 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400 border border-amber-200 dark:border-amber-800">内置</span>
                <span v-else class="text-[10px] text-slate-300 dark:text-slate-600">—</span>
              </td>
              <td class="py-2.5 px-2 text-center font-mono text-slate-500 dark:text-slate-400">{{ t.defaultWidth }}x{{ t.defaultHeight }}</td>
              <td class="py-2.5 px-2 text-center font-mono text-slate-500 dark:text-slate-400">{{ t.sortOrder }}</td>
              <td class="py-2.5 pl-2 pr-4">
                <div class="flex items-center justify-end gap-2">
                  <button
                    @click="exportOne(t)"
                    class="text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 cursor-pointer"
                    title="导出该模板"
                  >
                    <Download class="w-3.5 h-3.5" />
                  </button>
                  <button
                    @click="openEdit(t)"
                    class="text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 cursor-pointer"
                    title="编辑（内置模板可编辑名称/尺寸/排序等，仅删除受限）"
                  >
                    <Edit3 class="w-3.5 h-3.5" />
                  </button>
                  <button
                    @click="remove(t)"
                    :disabled="t.isSystem"
                    class="text-slate-400 hover:text-rose-500 cursor-pointer disabled:opacity-30 disabled:cursor-not-allowed"
                    :title="t.isSystem ? '系统内置模板不可删除' : '删除该模板'"
                  >
                    <Trash2 class="w-3.5 h-3.5" />
                  </button>
                </div>
              </td>
            </tr>
            <tr v-if="filteredList.length === 0 && !loading">
              <td colspan="10" class="py-10 text-center text-slate-400 dark:text-slate-500 text-xs">暂无模板</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="px-4 py-2 border-t border-slate-100 dark:border-slate-800 text-[10px] text-slate-400 dark:text-slate-500 flex items-center justify-between">
        <span>共 {{ list.length }} 条模板，当前显示 {{ filteredList.length }} 条</span>
        <span>修改模板仅影响新放置的组件，画布存量组件保持放置时快照</span>
      </div>
    </div>

    <!-- MODAL: 新建 / 编辑 -->
    <div v-if="showModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-2xl w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Shapes class="w-4 h-4 text-[#1890ff]" />
            <span>{{ editingId != null ? '编辑模板' : '新建模板' }}</span>
          </div>
          <button @click="showModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs overflow-y-auto max-h-[60vh]">
          <div v-if="formError" class="bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 rounded-lg p-3 text-rose-600 dark:text-rose-400 whitespace-pre-line">
            {{ formError }}
          </div>
          <div v-if="editingId != null" class="bg-sky-50 dark:bg-sky-950/40 border border-sky-200 dark:border-sky-800 rounded-lg p-3 text-sky-600 dark:text-sky-400">
            模板修改仅影响新放置的组件；画布上已放置的该类型组件保持放置时的属性快照。
            <template v-if="editingTemplate?.isSystem">系统内置模板禁止删除。</template>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">模板键 <span class="text-rose-500">*</span></label>
              <input
                v-model="form.templateKey"
                type="text"
                :disabled="editingId != null"
                placeholder="如 pump-backup"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] disabled:opacity-60 disabled:cursor-not-allowed"
              />
              <p class="text-slate-400 dark:text-slate-500 text-[10px] mt-1">{{ editingId != null ? '编辑态锁定，不可变更' : '全局唯一（≤64 字符）' }}</p>
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">渲染类型 <span class="text-rose-500">*</span></label>
              <input
                v-model="form.renderType"
                type="text"
                :disabled="form.renderKind === 'svg'"
                placeholder="如 pump"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] disabled:opacity-60 disabled:cursor-not-allowed"
              />
              <p class="text-slate-400 dark:text-slate-500 text-[10px] mt-1">
                {{ form.renderKind === 'svg' ? 'SVG 轨锁定与模板键一致（D10）' : 'builtin 轨对应前端内置渲染器' }}
              </p>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">模板名称 <span class="text-rose-500">*</span></label>
              <input
                v-model="form.name"
                type="text"
                placeholder="如 备用输送泵"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
              />
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">分类</label>
              <select
                v-model="form.category"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
              >
                <option value="equipment">设备</option>
                <option value="sensors">仪表</option>
                <option value="structures">结构</option>
                <option value="headers">标题背景</option>
              </select>
            </div>
          </div>

          <div class="grid grid-cols-3 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">默认宽度</label>
              <input v-model.number="form.defaultWidth" type="number" min="1" max="4096"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none" />
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">默认高度</label>
              <input v-model.number="form.defaultHeight" type="number" min="1" max="4096"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none" />
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">排序</label>
              <input v-model.number="form.sortOrder" type="number"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none" />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">渲染轨</label>
              <select
                v-model="form.renderKind"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
              >
                <option value="builtin">内置 SFC 渲染器</option>
                <option value="svg">SVG 模板渲染器</option>
              </select>
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">图标形态</label>
              <select
                v-model="form.iconKind"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
              >
                <option value="lucide">lucide 图标</option>
                <option value="div">div 图形</option>
                <option value="svg">SVG 源码</option>
                <option value="emoji">emoji 字符</option>
              </select>
            </div>
          </div>

          <div class="grid grid-cols-[1fr_auto_1fr] items-end gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">
                {{ form.iconKind === 'lucide' ? '图标名' : form.iconKind === 'emoji' ? 'emoji 字符' : form.iconKind === 'svg' ? 'SVG 图标源码' : 'div 变体' }}
              </label>
              <input
                v-model="form.iconKey"
                type="text"
                :placeholder="form.iconKind === 'lucide' ? '如 box / cpu / gauge' : form.iconKind === 'emoji' ? '如 🚀' : form.iconKind === 'svg' ? '<svg ...>…</svg>' : 'div-h / div-v / div-led'"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
              />
            </div>
            <div v-if="form.iconKind === 'lucide' || form.iconKind === 'emoji'">
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">颜色</label>
              <input
                v-model="form.iconColor"
                type="text"
                placeholder="text-sky-500"
                class="w-28 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
              />
            </div>
            <div class="flex items-center gap-2 pb-2 pl-2">
              <span class="text-slate-400 dark:text-slate-500 text-[10px]">预览</span>
              <div class="w-9 h-9 rounded bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 flex items-center justify-center">
                <component v-if="form.iconKind === 'lucide'" :is="formIconComp" class="w-4 h-4" :class="form.iconColor || 'text-slate-500'" />
                <div v-else-if="form.iconKind === 'div' && form.iconKey === 'div-h'" class="w-6 h-1.5 bg-slate-600 dark:bg-slate-400 rounded-full" />
                <div v-else-if="form.iconKind === 'div' && form.iconKey === 'div-v'" class="w-1.5 h-6 bg-slate-600 dark:bg-slate-400 rounded-full" />
                <div v-else-if="form.iconKind === 'div' && form.iconKey === 'div-led'" class="w-3 h-3 rounded-full bg-emerald-500 ring-2 ring-emerald-300 dark:ring-emerald-600 animate-pulse" />
                <span v-else-if="form.iconKind === 'emoji'" class="text-lg leading-none" :class="form.iconColor || ''">{{ form.iconKey }}</span>
                <span v-else-if="form.iconKind === 'svg'" class="w-5 h-5 flex items-center justify-center" v-html="sanitizeSvg(form.iconKey)" />
              </div>
            </div>
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">描述</label>
            <textarea
              v-model="form.description"
              rows="2"
              placeholder="组件用途说明（组件库列表展示）"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] leading-relaxed"
            />
          </div>

          <!-- SVG 轨：源码编辑 + 实时预览 + 占位符速查（P4 全量启用） -->
          <div v-if="form.renderKind === 'svg'">
            <div class="flex items-center justify-between mb-1">
              <label class="text-slate-500 dark:text-slate-400 font-bold">SVG 模板源码 <span class="text-rose-500">*</span></label>
              <span class="text-[10px] inline-flex items-center gap-1.5"
                :class="svgTooLarge ? 'text-rose-500 dark:text-rose-400 font-bold' : 'text-slate-400 dark:text-slate-500'">
                <Eye class="w-3 h-3" />
                {{ svgTooLarge ? `超过 256KB 上限（当前 ${form.svgTemplate.length} 字符）` : `${form.svgTemplate.length} 字符` }}
              </span>
            </div>
            <textarea
              ref="svgEditorRef"
              v-model="form.svgTemplate"
              rows="8"
              spellcheck="false"
              placeholder="<svg width=&quot;100%&quot; height=&quot;100%&quot; viewBox=&quot;0 0 100 100&quot;><rect width=&quot;{normalizedPercent}&quot; height=&quot;20&quot; fill=&quot;{activeColor}&quot;/></svg>"
              class="w-full bg-slate-950 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono text-[10px] leading-relaxed text-emerald-300 focus:outline-none focus:border-violet-500 placeholder-slate-600"
            />
            <p v-if="svgTooLarge" class="mt-1 text-[10px] text-rose-500 dark:text-rose-400">
              超过 256KB 上限，保存将被拒绝（与后端清洗器限长一致）。
            </p>

            <!-- 实时预览：与画布同链路 sanitize + bind（注入示例值） -->
            <div class="mt-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-950 p-2.5">
              <div class="flex items-center justify-between mb-1.5">
                <p class="text-[10px] font-bold text-emerald-600 dark:text-emerald-400 inline-flex items-center gap-1">
                  <Eye class="w-3 h-3" /> 实时预览（示例值：42.5 ℃ · 开启 · 55% · 运行色）
                </p>
                <span class="text-[9px] text-slate-400 dark:text-slate-500 font-mono">sanitize + bind</span>
              </div>
              <div
                class="h-36 rounded bg-slate-50 dark:bg-slate-900 border border-dashed border-slate-200 dark:border-slate-700 flex items-center justify-center overflow-hidden p-2">
                <div v-if="svgPreviewHtml" class="w-full h-full svg-icon-preview" v-html="svgPreviewHtml" />
                <span v-else class="text-[10px] text-slate-400 dark:text-slate-500">输入 SVG 源码后此处实时预览</span>
              </div>
            </div>

            <div class="mt-2 rounded-lg border border-violet-200 dark:border-violet-900 bg-violet-50/50 dark:bg-violet-950/20 p-2.5">
              <p class="text-[10px] font-bold text-violet-600 dark:text-violet-400 mb-1.5">
                可用占位符（运行态按绑定值替换，未知占位符原样保留；点击插入到光标处）
              </p>
              <div class="flex flex-wrap gap-1.5">
                <button v-for="p in SVG_PLACEHOLDERS" :key="p.ph" type="button" @click="insertPlaceholder(p.ph)"
                  class="text-[10px] text-slate-500 dark:text-slate-400 bg-white/60 dark:bg-slate-900/60 border border-violet-200 dark:border-violet-800 rounded px-1.5 py-0.5 hover:border-violet-400 dark:hover:border-violet-600 hover:text-violet-600 dark:hover:text-violet-300 cursor-pointer transition-colors">
                  <code class="font-mono text-violet-500 dark:text-violet-400">{{ p.ph }}</code> {{ p.desc }}
                </button>
              </div>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">默认属性 JSON</label>
              <textarea
                v-model="form.defaultPropsJson"
                rows="4"
                spellcheck="false"
                placeholder="{ &quot;activeColor&quot;: &quot;#3b82f6&quot; }"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono text-[10px] leading-relaxed focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
              />
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">属性 Schema JSON <span class="text-slate-300 dark:text-slate-600">（P5 启用）</span></label>
              <textarea
                v-model="form.propSchemaJson"
                rows="4"
                spellcheck="false"
                placeholder="[]"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono text-[10px] leading-relaxed focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
              />
            </div>
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-4 border-t border-slate-100 dark:border-slate-800 flex justify-end gap-2">
          <button
            @click="showModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button
            @click="save"
            :disabled="saving"
            class="px-4 py-1.5 rounded-lg bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {{ saving ? '保存中...' : '保存' }}
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: 导入预览 + 冲突策略 -->
    <div v-if="showImportModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-2xl w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Upload class="w-4 h-4 text-[#1890ff]" />
            <span>导入预览（{{ importPreview.length }} 条）</span>
          </div>
          <button @click="showImportModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-3 text-xs overflow-y-auto max-h-[55vh]">
          <div v-if="conflictCount > 0"
            class="bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-800 rounded-lg p-3 text-amber-600 dark:text-amber-400 flex items-start gap-2">
            <AlertTriangle class="w-4 h-4 shrink-0 mt-0.5" />
            <span>{{ conflictCount }} 条模板与现有模板键冲突，请选择冲突处理策略；无冲突项将直接新增。</span>
          </div>

          <div class="space-y-2">
            <div v-for="(p, i) in importPreview" :key="i"
              class="flex items-center gap-3 p-2.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-950/40">
              <span v-if="p.conflict" class="text-[10px] font-bold px-1.5 py-0.5 rounded-full bg-amber-100 dark:bg-amber-900/50 text-amber-600 dark:text-amber-400 border border-amber-200 dark:border-amber-800 shrink-0">冲突</span>
              <span v-else class="text-[10px] font-bold px-1.5 py-0.5 rounded-full bg-emerald-100 dark:bg-emerald-900/50 text-emerald-600 dark:text-emerald-400 border border-emerald-200 dark:border-emerald-800 shrink-0">新增</span>
              <div class="min-w-0 flex-1">
                <div class="font-mono text-[#1890ff] dark:text-sky-400 text-[10px]">{{ p.key }}</div>
                <div class="font-bold text-slate-800 dark:text-white text-xs truncate">{{ p.name }}</div>
              </div>
              <span class="text-[10px] text-slate-400 shrink-0">{{ CATEGORY_LABEL[p.category] || p.category }}</span>
              <span class="text-[10px] font-mono text-slate-400 shrink-0">{{ p.size }}</span>
              <span class="text-[10px] font-bold px-1.5 py-0.5 rounded border shrink-0"
                :class="p.renderKind === 'svg'
                  ? 'bg-violet-50 dark:bg-violet-950/60 text-violet-600 dark:text-violet-400 border-violet-200 dark:border-violet-800'
                  : 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700'">
                {{ p.renderKind === 'svg' ? 'SVG' : '内置' }}
              </span>
            </div>
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-4 border-t border-slate-100 dark:border-slate-800 flex justify-end gap-2">
          <button
            @click="showImportModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <template v-if="conflictCount > 0">
            <button
              @click="doImport('skip')"
              :disabled="importing"
              class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer disabled:opacity-50"
            >
              跳过冲突
            </button>
            <button
              @click="doImport('rename')"
              :disabled="importing"
              class="px-3.5 py-1.5 rounded-lg border border-amber-300 dark:border-amber-700 bg-amber-50 dark:bg-amber-950/50 hover:bg-amber-100 dark:hover:bg-amber-900/50 font-bold text-xs text-amber-600 dark:text-amber-400 cursor-pointer disabled:opacity-50"
            >
              重命名导入
            </button>
            <button
              @click="doImport('overwrite')"
              :disabled="importing"
              class="px-3.5 py-1.5 rounded-lg bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white cursor-pointer disabled:opacity-50"
            >
              {{ importing ? '导入中...' : '覆盖导入' }}
            </button>
          </template>
          <button
            v-else
            @click="doImport('rename')"
            :disabled="importing"
            class="px-4 py-1.5 rounded-lg bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white cursor-pointer disabled:opacity-50"
          >
            {{ importing ? '导入中...' : '确认导入' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
