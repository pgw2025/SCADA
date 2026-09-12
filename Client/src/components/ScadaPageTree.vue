<template>
  <div class="scada-page-tree">
    <template v-for="(node, index) in nodes" :key="node.id">
      <!-- 文件夹节点 -->
      <div v-if="node.kind === 'folder' && node.folder"
        class="page-tree-row group"
        :class="[rowCls(node, selectedPageId === node.id), guideCls(node.id)]"
        :style="indentStyle(node.depth)"
        draggable="true"
        @dragstart="onDragStart({ kind: 'folder', id: node.id, platform }, $event)"
        @dragend="onDragEnd"
        @dragover.prevent="onRowDragover(node, index, $event)"
        @dragleave="clearGuide"
        @drop.prevent="onRowDrop(node, index, $event)"
        @click="toggleFolder(node.id)">
        <div class="flex items-center justify-between w-full gap-1 min-w-0">
          <div class="flex items-center gap-1 min-w-0 flex-1">
            <ChevronRight class="w-3 h-3 shrink-0 text-slate-400 transition-transform"
              :class="expandedIds.has(node.id) ? 'rotate-90' : ''" />
            <FolderIcon class="w-3.5 h-3.5 shrink-0 text-amber-500" />
            <span v-if="isRenamingFolderId === node.id" class="flex items-center gap-1 w-full" @click.stop>
              <input :ref="setFolderRenameEl" v-model="renameFolderInputLocal" type="text" class="rename-input"
                @keyup.enter="saveRenameFolder(node.id)" @click.stop />
              <button class="text-emerald-600 dark:text-emerald-400 hover:text-emerald-700" @click="saveRenameFolder(node.id)">
                <Check class="w-4 h-4" />
              </button>
            </span>
            <span v-else class="font-bold text-xs flex-1 truncate leading-relaxed">{{ node.name }}</span>
            <span v-if="!isRenamingFolderIdNow(node.id) && node.children.length" class="shrink-0 text-[9px] text-slate-400">{{ node.children.length }}</span>
          </div>
          <div v-if="!isRenamingFolderIdNow(node.id)" class="flex items-center gap-1.5 shrink-0 opacity-0 group-hover:opacity-100 focus-within:opacity-100 transition-all">
            <button @click.stop="emitCreateSubfolder(node.id)" title="新建子文件夹" class="row-action text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400">
              <FolderPlus class="w-3 h-3" />
            </button>
            <button @click.stop="emitStartRenameFolder(node.id, node.name)" title="重命名文件夹" class="row-action text-slate-400 hover:text-slate-700 dark:hover:text-slate-200">
              <Edit class="w-3 h-3" />
            </button>
            <button @click.stop="emitDeleteFolder(node.folder)" title="删除文件夹" class="row-action text-rose-400 hover:text-rose-600 dark:hover:text-rose-300">
              <Trash2 class="w-3 h-3" />
            </button>
          </div>
        </div>
      </div>

      <!-- 文件夹子级容器：即便为空也保持为拖入落点（追加到该夹末尾）；收起时隐藏 -->
      <div v-if="node.kind === 'folder' && expandedIds.has(node.id)"
        class="page-tree-children"
        :class="{ 'folder-empty-drop': node.children.length === 0 && canDropHere(node.id) }"
        @dragover.prevent="onContainerDragover(node.id)"
        @dragleave="clearGuide"
        @drop.prevent="onContainerDrop(node.id)">
        <ScadaPageTree v-if="node.children.length"
          :nodes="node.children" :platform="platform" :parent-folder-id="node.id"
          :expanded-ids="expandedIds" :selected-page-id="selectedPageId"
          :is-renaming-page-id="isRenamingPageId" :rename-page-input="renamePageInput"
          :is-renaming-folder-id="isRenamingFolderId" :rename-folder-input="renameFolderInput"
          :drag-item="dragItem"
          @select-page="emitSelectPage" @start-rename-page="emitStartRenamePage"
          @save-rename-page="emitSaveRenamePage" @set-home="emitSetHome"
          @duplicate-page="emitDuplicatePage" @export-page="emitExportPage"
          @delete-page="emitDeletePage" @toggle-folder="emitToggleFolder"
          @create-subfolder="emitCreateSubfolder" @start-rename-folder="emitStartRenameFolder"
          @save-rename-folder="emitSaveRenameFolder" @delete-folder="emitDeleteFolder"
          @drag-start="emitDragStart" @drag-end="emitDragEnd"
          @drop-item="emitDropItem" />
      </div>

      <!-- 画面叶子节点 -->
      <div v-if="node.kind === 'page' && node.page"
        class="page-tree-row group"
        :class="[rowCls(node, selectedPageId === node.id), guideCls(node.id)]"
        :style="indentStyle(node.depth)"
        draggable="true"
        @dragstart="onDragStart({ kind: 'page', id: node.id, platform }, $event)"
        @dragend="onDragEnd"
        @dragover.prevent="onRowDragover(node, index, $event)"
        @dragleave="clearGuide"
        @drop.prevent="onRowDrop(node, index, $event)"
        @click="emitSelectPage(node.id)">
        <div class="flex items-center justify-between gap-2 w-full min-w-0">
          <div v-if="isRenamingPageId === node.id" class="flex items-center gap-1 w-full" @click.stop>
            <input :ref="setPageRenameEl" v-model="renamePageInputLocal" type="text" class="rename-input"
              @keyup.enter="emitSaveRenamePage(node.id)" @click.stop />
            <button class="text-emerald-600 dark:text-emerald-400 hover:text-emerald-700" @click="emitSaveRenamePage(node.id)">
              <Check class="w-4 h-4" />
            </button>
          </div>
          <span v-else class="font-bold text-xs flex-1 truncate leading-relaxed flex items-center gap-1 min-w-0">
            <span v-if="node.page.isHome" class="shrink-0 text-[8px] bg-amber-500 text-white px-1 py-0.5 rounded leading-none">首页</span>
            <span class="truncate">{{ node.name }}</span>
          </span>
          <div v-if="isRenamingPageId !== node.id" class="flex items-center gap-1.5 shrink-0 opacity-0 group-hover:opacity-100 focus-within:opacity-100 transition-all">
            <button @click.stop="emitSetHome(node.page)" class="row-action text-slate-400 hover:text-amber-500" :title="node.page.isHome ? '当前已是该端首页' : '设为该端首页'">
              <Home class="w-3 h-3" :class="node.page.isHome ? 'text-amber-500' : ''" />
            </button>
            <button @click.stop="emitStartRenamePage(node.id, node.name)" class="row-action text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="重命名">
              <Edit class="w-3 h-3" />
            </button>
            <button @click.stop="emitDuplicatePage(node.page)" class="row-action text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="复制页面">
              <Copy class="w-3 h-3" />
            </button>
            <button @click.stop="emitExportPage(node.page)" class="row-action text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" title="导出画面">
              <Download class="w-3 h-3" />
            </button>
            <button @click.stop="emitDeletePage(node.id, node.name)" class="row-action text-rose-400 hover:text-rose-600 dark:hover:text-rose-300" title="删除页面">
              <Trash2 class="w-3 h-3" />
            </button>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, shallowRef, watch, nextTick } from 'vue';
import { ChevronRight, FolderIcon, FolderPlus, Edit, Trash2, Check, Copy, Download, Home } from 'lucide-vue-next';
import type { PageTreeNode } from '../store/scadaStore';

defineOptions({ name: 'ScadaPageTree' });

export type DragItem = { kind: 'page' | 'folder'; id: string; platform: string };

const props = defineProps<{
  nodes: PageTreeNode[];
  platform: 'Desktop' | 'Mobile';
  parentFolderId?: string;
  expandedIds: Set<string>;
  selectedPageId: string;
  isRenamingPageId: string | null;
  renamePageInput: string;
  isRenamingFolderId: string | null;
  renameFolderInput: string;
  dragItem: DragItem | null;
}>();

const emit = defineEmits<{
  (e: 'select-page', id: string): void;
  (e: 'start-rename-page', id: string, name: string): void;
  (e: 'save-rename-page', id: string): void;
  (e: 'set-home', page: any): void;
  (e: 'duplicate-page', page: any): void;
  (e: 'export-page', page: any): void;
  (e: 'delete-page', id: string, name: string): void;
  (e: 'toggle-folder', id: string): void;
  (e: 'create-subfolder', parentFolderId: string): void;
  (e: 'start-rename-folder', id: string, name: string): void;
  (e: 'save-rename-folder', id: string): void;
  (e: 'delete-folder', folder: any): void;
  (e: 'drag-start', item: DragItem): void;
  (e: 'drag-end'): void;
  (e: 'drop-item', targetParentId?: string, beforeId?: string): void;
}>();

// 事件透传封装（避免模板二元组数组问题）
const emitSelectPage = (id: string) => emit('select-page', id);
const emitStartRenamePage = (id: string, name: string) => emit('start-rename-page', id, name);
const emitSaveRenamePage = (id: string) => emit('save-rename-page', id);
const emitSetHome = (page: any) => emit('set-home', page);
const emitDuplicatePage = (page: any) => emit('duplicate-page', page);
const emitExportPage = (page: any) => emit('export-page', page);
const emitDeletePage = (id: string, name: string) => emit('delete-page', id, name);
const emitToggleFolder = (id: string) => emit('toggle-folder', id);
const emitCreateSubfolder = (parentFolderId: string) => emit('create-subfolder', parentFolderId);
const emitStartRenameFolder = (id: string, name: string) => emit('start-rename-folder', id, name);
const emitSaveRenameFolder = (id: string) => emit('save-rename-folder', id);
const emitDeleteFolder = (folder: any) => emit('delete-folder', folder);
const emitDragStart = (item: DragItem) => emit('drag-start', item);
const emitDragEnd = () => emit('drag-end');
const emitDropItem = (targetParentId?: string, beforeId?: string) => emit('drop-item', targetParentId, beforeId);

// ---- 样式辅助 ----
const indentStyle = (depth: number) => ({ paddingLeft: `${6 + depth * 14}px` });
const rowCls = (node: PageTreeNode, sel: boolean) =>
  sel ? 'bg-sky-50/50 dark:bg-sky-950/40 text-[#1890ff] dark:text-sky-400 border-r-4 border-r-[#1890ff] dark:border-r-sky-500'
      : 'text-slate-700 dark:text-slate-300';
const isRenamingFolderIdNow = (id: string) => props.isRenamingFolderId === id;

// ---- 重命名输入（本地代理即时输入；父级状态变化时回写）----
const renameFolderInputLocal = ref(props.renameFolderInput);
const renamePageInputLocal = ref(props.renamePageInput);
watch(() => props.renameFolderInput, (v) => { renameFolderInputLocal.value = v ?? ''; });
watch(() => props.renamePageInput, (v) => { renamePageInputLocal.value = v ?? ''; });
const pageRenameEl = shallowRef<HTMLInputElement | null>(null);
const setPageRenameEl = (el: any) => { pageRenameEl.value = el ?? null; };
watch(() => props.isRenamingPageId, async (v, old) => {
  if (v && v !== old) { await nextTick(); pageRenameEl.value?.focus(); }
});
const folderRenameEl = shallowRef<HTMLInputElement | null>(null);
const setFolderRenameEl = (el: any) => { folderRenameEl.value = el ?? null; };
watch(() => props.isRenamingFolderId, async (v, old) => {
  if (v && v !== old) { await nextTick(); folderRenameEl.value?.focus(); }
});

const saveRenameFolder = (id: string) => emitSaveRenameFolder(id);

// ---- 拖拽 ----
const guide = ref<{ before: boolean; after: boolean }>({ before: false, after: false });
const guideId = ref<string | null>(null);

const canDropHere = (targetId: string | undefined): boolean => {
  const item = props.dragItem;
  if (!item || item.platform !== props.platform) return false;
  if (targetId == null) return true;
  return item.id !== targetId;
};

const onDragStart = (item: DragItem, e: DragEvent) => {
  emit('drag-start', item);
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move';
    try { e.dataTransfer.setData('text/plain', item.id); } catch { /* 忽略 */ }
  }
};
const onDragEnd = () => {
  emit('drag-end');
  guideId.value = null;
  guide.value = { before: false, after: false };
};
const clearGuide = () => {
  guideId.value = null;
  guide.value = { before: false, after: false };
};

const guideCls = (nodeId: string) =>
  guideId.value !== nodeId ? '' : (guide.value.before ? 'before-guide' : 'after-guide');

const onRowDragover = (node: PageTreeNode, index: number, e: DragEvent) => {
  if (!canDropHere(node.id)) return;
  const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
  const before = e.clientY < rect.top + rect.height / 2;
  guideId.value = node.id;
  guide.value = { before, after: !before };
};
const onRowDrop = (node: PageTreeNode, index: number, e: DragEvent) => {
  if (!canDropHere(node.id)) return;
  const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
  const before = e.clientY < rect.top + rect.height / 2;
  const beforeId = before ? node.id : props.nodes[index + 1]?.id;
  emitDropItem(props.parentFolderId, beforeId);
  clearGuide();
};

const onContainerDragover = (parentId: string) => {
  if (!canDropHere(parentId)) return;
  guideId.value = null;
  guide.value = { before: false, after: false };
};
const onContainerDrop = (parentId: string) => {
  if (!canDropHere(parentId)) return;
  emitDropItem(parentId, undefined);
  clearGuide();
};
</script>

<style scoped>
.page-tree-row {
  display: flex;
  align-items: center;
  position: relative;
  padding: 0.5rem;
  cursor: pointer;
  transition: all 0.15s ease;
}
.page-tree-row:hover { background-color: rgba(241, 245, 249, 0.5); }
:global(.dark) .page-tree-row:hover { background-color: rgba(30, 41, 59, 0.5); }
.page-tree-row.after-guide::after,
.page-tree-row.before-guide::before {
  content: '';
  position: absolute;
  left: 4px;
  right: 4px;
  height: 2px;
  background: #1890ff;
  border-radius: 2px;
  z-index: 2;
}
.page-tree-row.before-guide::before { top: 0; }
.page-tree-row.after-guide::after { bottom: 0; }
.page-tree-children { position: relative; }
.page-tree-children.folder-empty-drop { min-height: 18px; }
.rename-input {
  width: 100%;
  background: #fff;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  padding: 0.125rem 0.25rem;
  font-size: 0.75rem;
  line-height: 1rem;
  color: #1e293b;
  outline: none;
}
:global(.dark) .rename-input { background: #0f172a; border-color: #334155; color: #f1f5f9; }
.row-action { font-size: 0.75rem; line-height: 1rem; }
</style>