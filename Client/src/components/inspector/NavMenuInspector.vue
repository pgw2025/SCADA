<script setup lang="ts">
// nav-menu 导航菜单检查器：3~5 项，图标/文字/跳转目标 + 二级菜单（children），支持增删与上下排序
// 从 InspectorPanel.vue 抽出（Phase 2d）；通信：component 下行 / updateProp 上行
import { computed, ref } from 'vue';
import { HMIComponent, HmiMenuItem, HmiSubMenuItem } from '../../types';
import { currentPlatform } from '../../store/scadaStore';
import { MENU_ICON_OPTIONS, getMenuIcon } from '../../widgetRegistry';
import { ChevronUp, ChevronDown, Trash2, Plus } from 'lucide-vue-next';

const props = defineProps<{
  component: HMIComponent;
  /** 导航目标候选（父组件按 currentPlatform 过滤后传入，与 rounded-btn 共用同一计算） */
  navTargetOptions: { id: string; name: string }[];
}>();

const emit = defineEmits<{
  (e: 'updateProp', key: string, value: any): void;
}>();

const componentProps = computed(() => props.component.props ?? {});

const menuDevice = computed(() => componentProps.value.menuDevice ?? 'desktop');

const updateProp = (key: string, value: any) => emit('updateProp', key, value);

// 解析数值输入：合法（含 0）原样写入，非法（NaN/空）回退缺省值
const numInput = (raw: string, fallback: number): number => {
  const n = parseFloat(raw);
  return Number.isFinite(n) ? n : fallback;
};

// ===== nav-menu 菜单项编辑器：3~5 项，图标/文字/跳转目标，支持增删与上下排序 =====
const MENU_ITEM_MIN = 3;
const MENU_ITEM_MAX = 5;

const menuItems = computed<HmiMenuItem[]>(() => {
  const raw = componentProps.value.menuItems;
  return Array.isArray(raw) ? (raw as HmiMenuItem[]) : [];
});

// 整体替换式提交（menuItems 是数组 prop，须整体写入不可局部 mutate）
const commitMenuItems = (items: HmiMenuItem[]) => updateProp('menuItems', items);

const updateMenuItem = (index: number, patch: Partial<HmiMenuItem>) => {
  const next = menuItems.value.map((it, i) => (i === index ? { ...it, ...patch } : it));
  commitMenuItems(next);
};

const addMenuItem = () => {
  if (menuItems.value.length >= MENU_ITEM_MAX) return;
  commitMenuItems([...menuItems.value, { icon: 'settings', text: `菜单 ${menuItems.value.length + 1}`, targetPageId: null }]);
};

const removeMenuItem = (index: number) => {
  if (menuItems.value.length <= MENU_ITEM_MIN) return;
  commitMenuItems(menuItems.value.filter((_, i) => i !== index));
};

const moveMenuItem = (index: number, dir: -1 | 1) => {
  const to = index + dir;
  if (to < 0 || to >= menuItems.value.length) return;
  const next = [...menuItems.value];
  [next[index], next[to]] = [next[to], next[index]];
  commitMenuItems(next);
};

// ===== 二级菜单（children）：纯文件夹一级项的子项编辑 =====
const SUB_MENU_MAX = 8;
/** 展开二级菜单编辑区的一级项索引（-1 = 全部收起） */
const openSubMenuIndex = ref(-1);

const hasChildren = (item: HmiMenuItem) => Array.isArray(item.children) && item.children.length > 0;

// 整体替换式提交：children 为空数组时回退 undefined（PropsJson 不落空数组）
const updateSubMenu = (index: number, subItems: HmiSubMenuItem[]) =>
  updateMenuItem(index, { children: subItems.length ? subItems : undefined });

const addSubMenuItem = (index: number) => {
  const children = menuItems.value[index]?.children ?? [];
  if (children.length >= SUB_MENU_MAX) return;
  updateSubMenu(index, [...children, { text: `子项 ${children.length + 1}`, targetPageId: null }]);
};

const removeSubMenuItem = (index: number, subIndex: number) => {
  const children = menuItems.value[index]?.children ?? [];
  if (children.length <= 1) return;
  updateSubMenu(index, children.filter((_, i) => i !== subIndex));
};

const moveSubMenuItem = (index: number, subIndex: number, dir: -1 | 1) => {
  const children = [...(menuItems.value[index]?.children ?? [])];
  const to = subIndex + dir;
  if (to < 0 || to >= children.length) return;
  [children[subIndex], children[to]] = [children[to], children[subIndex]];
  updateSubMenu(index, children);
};

const updateSubMenuItem = (index: number, subIndex: number, patch: Partial<HmiSubMenuItem>) => {
  const children = (menuItems.value[index]?.children ?? [])
    .map((it, i) => (i === subIndex ? { ...it, ...patch } : it));
  updateSubMenu(index, children);
};

// 图标选择网格的展开项（-1 = 全部收起；同一时间只展开一项）
const openIconPickerIndex = ref(-1);
// 二级菜单图标选择网格的展开项（键 `${idx}-${subIdx}`，null = 全部收起）
const openSubIconPicker = ref<string | null>(null);
</script>

<template>
  <!-- NAV-MENU SPECIFIC CONTROLS（导航菜单专属配置：3~5 项，图标/文字/跳转目标） -->
  <div
    class="space-y-2.5 text-xs border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/40 dark:bg-sky-950/20">
    <p class="font-bold text-sky-600 dark:text-sky-400 text-[10px] uppercase tracking-wider">导航菜单配置</p>
    <p class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug">
      菜单项 {{ menuItems.length }}/{{ MENU_ITEM_MAX }}，跳转目标仅限「{{ currentPlatform === 'Mobile' ? '移动端' : '桌面端'
      }}」画面（不含当前页）。配置了二级菜单的一级项为纯文件夹，自身不跳转。
    </p>

    <!-- 端型切换：底部标签栏 / 顶部导航条（跨端错位时无需重拖图元） -->
    <div class="flex items-center justify-between">
      <label class="text-[10px] text-gray-500 dark:text-slate-400">端型</label>
      <div class="flex rounded overflow-hidden border border-[#d9d9d9] dark:border-slate-700">
        <button type="button" @click="updateProp('menuDevice', 'desktop')" :class="menuDevice === 'desktop'
          ? 'bg-[#1890ff] text-white'
          : 'bg-white dark:bg-slate-950 text-gray-500 dark:text-slate-400 hover:bg-gray-50 dark:hover:bg-slate-800'"
          class="px-2.5 py-1 text-[10px] font-medium transition-colors cursor-pointer" title="桌面端·顶部导航条">
          顶部导航条
        </button>
        <button type="button" @click="updateProp('menuDevice', 'mobile')" :class="menuDevice === 'mobile'
          ? 'bg-[#1890ff] text-white'
          : 'bg-white dark:bg-slate-950 text-gray-500 dark:text-slate-400 hover:bg-gray-50 dark:hover:bg-slate-800'"
          class="px-2.5 py-1 text-[10px] font-medium transition-colors cursor-pointer" title="移动端·底部标签栏">
          底部标签栏
        </button>
      </div>
    </div>

    <div>
      <label class="text-[10px] text-gray-500 dark:text-slate-400">风格主题 (Style Preset)</label>
      <select :value="componentProps.menuStyle || 'navy-midnight'"
        @change="updateProp('menuStyle', ($event.target as HTMLSelectElement).value)"
        class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
        <optgroup label="☀️ 浅色大方系列">
          <option value="pure-white">极简亮白 (Pure Crisp White · 浅色)</option>
          <option value="titanium-light">工业钛灰 (Titanium Light · 浅色)</option>
        </optgroup>
        <optgroup label="🌙 深色稳健系列">
          <option value="slate-dark">经典石板深灰 (Classic Slate · 深色)</option>
          <option value="navy-midnight">深海商务暗蓝 (Navy Midnight · 深色)</option>
        </optgroup>
        <optgroup label="🌿 轻量通透系列">
          <option value="translucent-frost">悬浮通透胶囊 (Adaptive Frost · 通透)</option>
        </optgroup>
        <optgroup label="⚙️ 经典特色预设">
          <option value="eco-green">生态翡翠绿 (Eco Green)</option>
          <option value="carbon-orange">机能碳纤橙 (Carbon Orange)</option>
          <option value="tech-blue">科技蓝 (Tech Blue)</option>
        </optgroup>
      </select>
    </div>

    <!-- 菜单项列表 -->
    <div v-for="(item, idx) in menuItems" :key="idx"
      class="border border-gray-200 dark:border-slate-700 rounded p-2 space-y-1.5 bg-white/70 dark:bg-slate-950/50">
      <div class="flex items-center justify-between">
        <span class="text-[10px] font-bold text-gray-500 dark:text-slate-400">菜单项 {{ idx + 1 }}</span>
        <div class="flex items-center gap-0.5">
          <button type="button" @click="moveMenuItem(idx, -1)" :disabled="idx === 0"
            class="p-1 rounded hover:bg-gray-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed text-gray-500 dark:text-slate-400"
            title="上移">
            <ChevronUp class="w-3.5 h-3.5" />
          </button>
          <button type="button" @click="moveMenuItem(idx, 1)" :disabled="idx === menuItems.length - 1"
            class="p-1 rounded hover:bg-gray-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed text-gray-500 dark:text-slate-400"
            title="下移">
            <ChevronDown class="w-3.5 h-3.5" />
          </button>
          <button type="button" @click="removeMenuItem(idx)" :disabled="menuItems.length <= MENU_ITEM_MIN"
            class="p-1 rounded hover:bg-red-50 dark:hover:bg-red-950/50 disabled:opacity-30 disabled:cursor-not-allowed text-red-500"
            :title="menuItems.length <= MENU_ITEM_MIN ? `最少保留 ${MENU_ITEM_MIN} 项` : '删除该项'">
            <Trash2 class="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      <!-- 图标选择：按钮展开内置图标网格 -->
      <div class="flex items-center gap-2">
        <button type="button" @click="openIconPickerIndex = openIconPickerIndex === idx ? -1 : idx"
          class="shrink-0 w-8 h-8 rounded border border-gray-200 dark:border-slate-700 flex items-center justify-center hover:border-[#1890ff] dark:hover:border-sky-500 transition-colors"
          :class="openIconPickerIndex === idx ? 'border-[#1890ff]! dark:border-sky-500!' : ''"
          :title="`选择图标（当前: ${item.icon}）`">
          <component :is="getMenuIcon(item.icon)" class="w-4 h-4 text-[#1890ff] dark:text-sky-400" />
        </button>
        <div class="flex-1 min-w-0">
          <label class="text-[10px] text-gray-500 dark:text-slate-400">显示文字</label>
          <input type="text" :value="item.text" maxlength="10"
            @input="updateMenuItem(idx, { text: ($event.target as HTMLInputElement).value })"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500" />
        </div>
      </div>

      <!-- 内置图标网格（仅当前展开项显示） -->
      <div v-if="openIconPickerIndex === idx"
        class="grid grid-cols-9 gap-1 p-1.5 rounded border border-gray-100 dark:border-slate-800 bg-gray-50/60 dark:bg-slate-900/60">
        <button v-for="opt in MENU_ICON_OPTIONS" :key="opt.name" type="button"
          @click="updateMenuItem(idx, { icon: opt.name }); openIconPickerIndex = -1"
          class="aspect-square rounded flex items-center justify-center transition-all" :class="item.icon === opt.name
            ? 'bg-[#1890ff]/15 ring-1 ring-[#1890ff] dark:ring-sky-500'
            : 'hover:bg-gray-200/70 dark:hover:bg-slate-800'" :title="opt.label">
          <component :is="opt.icon" class="w-3.5 h-3.5"
            :class="item.icon === opt.name ? 'text-[#1890ff] dark:text-sky-400' : 'text-gray-500 dark:text-slate-400'" />
        </button>
      </div>

      <div>
        <label class="text-[10px] text-gray-500 dark:text-slate-400">跳转目标画面（仅同端）</label>
        <select :value="item.targetPageId ?? ''"
          :disabled="hasChildren(item)"
          @change="updateMenuItem(idx, { targetPageId: (($event.target as HTMLSelectElement).value || null) })"
          class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 text-xs text-[#262626] dark:text-white disabled:opacity-50 disabled:cursor-not-allowed">
          <option value="">-- 请选择目标画面 --</option>
          <option v-for="opt in navTargetOptions" :key="opt.id" :value="opt.id">{{ opt.name }}</option>
        </select>
        <p v-if="hasChildren(item)" class="mt-0.5 text-[9px] text-amber-600 dark:text-amber-400">
          有二级菜单的一级项为纯文件夹，点击仅展开二级菜单，不配置跳转。
        </p>
      </div>

      <!-- 二级菜单（children）编辑区：纯文件夹子项配置 -->
      <div class="mt-1.5 rounded border border-gray-100 dark:border-slate-800 bg-gray-50/60 dark:bg-slate-900/60 p-1.5 space-y-1.5">
        <div class="flex items-center justify-between">
          <span class="text-[10px] font-medium text-gray-500 dark:text-slate-400">
            二级菜单 <template v-if="hasChildren(item)">（{{ item.children!.length }}）</template>
          </span>
          <div class="flex items-center gap-0.5">
            <button type="button" v-if="hasChildren(item)" @click="updateMenuItem(idx, { children: undefined })"
              class="p-1 rounded hover:bg-red-50 dark:hover:bg-red-950/50 text-red-500" title="移除全部二级菜单">
              <Trash2 class="w-3 h-3" />
            </button>
            <button type="button" @click="openSubMenuIndex = openSubMenuIndex === idx ? -1 : idx"
              class="px-1.5 py-0.5 rounded text-[9px] border border-gray-200 dark:border-slate-700 text-gray-500 dark:text-slate-400 hover:bg-gray-100 dark:hover:bg-slate-800"
              title="编辑二级菜单">
              {{ openSubMenuIndex === idx ? '收起' : (hasChildren(item) ? '编辑' : '添加') }}
            </button>
          </div>
        </div>

        <div v-if="openSubMenuIndex === idx" class="space-y-1.5">
          <div v-for="(sub, si) in (item.children ?? [])" :key="si"
            class="border border-gray-200/70 dark:border-slate-700/70 rounded p-1.5 space-y-1">
            <div class="flex items-center justify-between">
              <span class="text-[9px] text-gray-400 dark:text-slate-500">子项 {{ si + 1 }}</span>
              <div class="flex items-center gap-0.5">
                <button type="button" @click="moveSubMenuItem(idx, si, -1)" :disabled="si === 0"
                  class="p-1 rounded hover:bg-gray-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed text-gray-500 dark:text-slate-400"
                  title="上移">
                  <ChevronUp class="w-3 h-3" />
                </button>
                <button type="button" @click="moveSubMenuItem(idx, si, 1)"
                  :disabled="si === (item.children?.length ?? 0) - 1"
                  class="p-1 rounded hover:bg-gray-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed text-gray-500 dark:text-slate-400"
                  title="下移">
                  <ChevronDown class="w-3 h-3" />
                </button>
                <button type="button" @click="removeSubMenuItem(idx, si)"
                  :disabled="(item.children?.length ?? 0) <= 1"
                  class="p-1 rounded hover:bg-red-50 dark:hover:bg-red-950/50 disabled:opacity-30 disabled:cursor-not-allowed text-red-500"
                  :title="(item.children?.length ?? 0) <= 1 ? '至少保留 1 个子项' : '删除该子项'">
                  <Trash2 class="w-3 h-3" />
                </button>
              </div>
            </div>

            <div class="flex items-center gap-1.5">
              <button type="button"
                @click="openSubIconPicker = openSubIconPicker === `${idx}-${si}` ? null : `${idx}-${si}`"
                class="shrink-0 w-6 h-6 rounded border border-gray-200 dark:border-slate-700 flex items-center justify-center hover:border-[#1890ff] dark:hover:border-sky-500 transition-colors"
                :class="openSubIconPicker === `${idx}-${si}` ? 'border-[#1890ff]! dark:border-sky-500!' : ''"
                :title="`子项图标（当前: ${sub.icon ?? '无'}）`">
                <component :is="getMenuIcon(sub.icon ?? 'file-text')" class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
              </button>
              <div class="flex-1 min-w-0">
                <label class="text-[10px] text-gray-500 dark:text-slate-400">显示文字</label>
                <input type="text" :value="sub.text" maxlength="12"
                  @input="updateSubMenuItem(idx, si, { text: ($event.target as HTMLInputElement).value })"
                  class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500" />
              </div>
            </div>

            <!-- 子项图标网格（仅当前展开项显示） -->
            <div v-if="openSubIconPicker === `${idx}-${si}`"
              class="grid grid-cols-9 gap-1 p-1.5 rounded border border-gray-100 dark:border-slate-800 bg-white/70 dark:bg-slate-900/70">
              <button v-for="opt in MENU_ICON_OPTIONS" :key="opt.name" type="button"
                @click="updateSubMenuItem(idx, si, { icon: opt.name }); openSubIconPicker = null"
                class="aspect-square rounded flex items-center justify-center transition-all" :class="sub.icon === opt.name
                  ? 'bg-[#1890ff]/15 ring-1 ring-[#1890ff] dark:ring-sky-500'
                  : 'hover:bg-gray-200/70 dark:hover:bg-slate-800'" :title="opt.label">
                <component :is="opt.icon" class="w-3 h-3"
                  :class="sub.icon === opt.name ? 'text-[#1890ff] dark:text-sky-400' : 'text-gray-500 dark:text-slate-400'" />
              </button>
            </div>

            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">子项跳转目标画面（仅同端）</label>
              <select :value="sub.targetPageId ?? ''"
                @change="updateSubMenuItem(idx, si, { targetPageId: (($event.target as HTMLSelectElement).value || null) })"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 text-xs text-[#262626] dark:text-white">
                <option value="">-- 请选择目标画面 --</option>
                <option v-for="opt in navTargetOptions" :key="opt.id" :value="opt.id">{{ opt.name }}</option>
              </select>
            </div>
          </div>

          <button type="button" @click="addSubMenuItem(idx)" :disabled="(item.children?.length ?? 0) >= SUB_MENU_MAX"
            class="w-full py-1 rounded border border-dashed border-[#1890ff]/60 dark:border-sky-500/60 text-[#1890ff] dark:text-sky-400 hover:bg-[#1890ff]/5 dark:hover:bg-sky-500/10 disabled:opacity-40 disabled:cursor-not-allowed flex items-center justify-center gap-1 transition-colors">
            <Plus class="w-3 h-3" />
            添加子项（{{ item.children?.length ?? 0 }}/{{ SUB_MENU_MAX }}）
          </button>
        </div>
      </div>
    </div>

    <!-- 新增菜单项（上限 5） -->
    <button type="button" @click="addMenuItem" :disabled="menuItems.length >= MENU_ITEM_MAX"
      class="w-full py-1.5 rounded border border-dashed border-[#1890ff]/60 dark:border-sky-500/60 text-[#1890ff] dark:text-sky-400 hover:bg-[#1890ff]/5 dark:hover:bg-sky-500/10 disabled:opacity-40 disabled:cursor-not-allowed flex items-center justify-center gap-1 transition-colors">
      <Plus class="w-3.5 h-3.5" />
      添加菜单项（3~5 项）
    </button>

    <!-- 主题微调：强调色 / 字号 -->
    <div class="grid grid-cols-2 gap-2">
      <div>
        <label class="text-[10px] text-gray-500 dark:text-slate-400">强调色</label>
        <input type="color" :value="componentProps.menuAccentColor ?? '#38bdf8'"
          @input="updateProp('menuAccentColor', ($event.target as HTMLInputElement).value)"
          class="w-full h-7 bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded cursor-pointer" />
      </div>
      <div>
        <label class="text-[10px] text-gray-500 dark:text-slate-400">文字字号 (px)</label>
        <input type="number" min="10" max="22" :value="componentProps.menuFontSize ?? 14"
          @input="updateProp('menuFontSize', numInput(($event.target as HTMLInputElement).value, 14))"
          class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500" />
      </div>
    </div>
  </div>
</template>
