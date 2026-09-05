<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import { AreaTreeNode } from '../../types';
import { AreaFormData } from '../../services/areaService';
import { MapPin, X, Loader2 } from 'lucide-vue-next';

const props = defineProps<{
  show: boolean;
  isEditing: boolean;
  editingId: number | null;
  areaTree: AreaTreeNode[];
  initialData: AreaFormData;
  errors: Record<string, string>;
  errorMessage: string;
}>();

const emit = defineEmits<{
  (e: 'close'): void;
  (e: 'save', data: AreaFormData): void;
}>();

const formName = ref('');
const formDesc = ref('');
const formParentId = ref<number | null>(null);
const formCode = ref('');
const formAreaType = ref<number>(4);
const formSort = ref<number>(0);
const formEnabled = ref<boolean>(true);
const saving = ref(false);

watch(
  () => props.show,
  (val) => {
    if (val) {
      formName.value = props.initialData.name || '';
      formDesc.value = props.initialData.description || '';
      formParentId.value = props.initialData.parentId ?? null;
      formCode.value = props.initialData.code || '';
      formAreaType.value = props.initialData.areaType ?? 4;
      formSort.value = props.initialData.sort ?? 0;
      formEnabled.value = props.initialData.isEnabled ?? true;
      saving.value = false;
    }
  },
  { immediate: true }
);

// Flatten tree for parent selection
const flatten = (nodes: AreaTreeNode[], depth = 0, out: { node: AreaTreeNode; depth: number }[] = []): { node: AreaTreeNode; depth: number }[] => {
  nodes.forEach(n => {
    out.push({ node: n, depth });
    if (n.children?.length) flatten(n.children, depth + 1, out);
  });
  return out;
};

// Exclude self and descendants in edit mode
const parentOptions = computed(() => {
  const rows = flatten(props.areaTree);
  if (!props.isEditing || props.editingId == null) return rows;
  const banned = new Set<number>();
  const collect = (list: AreaTreeNode[], target: number) => {
    for (const n of list) {
      if (n.id === target) {
        const stack = [...(n.children || [])];
        while (stack.length) {
          const c = stack.pop()!;
          banned.add(c.id);
          stack.push(...(c.children || []));
        }
        return;
      }
      if (n.children?.length) collect(n.children, target);
    }
  };
  banned.add(props.editingId);
  collect(props.areaTree, props.editingId);
  return rows.filter(r => !banned.has(r.node.id));
});

const handleSave = () => {
  emit('save', {
    name: formName.value,
    description: formDesc.value,
    parentId: formParentId.value,
    code: formCode.value,
    areaType: formAreaType.value,
    sort: formSort.value,
    isEnabled: formEnabled.value
  });
};
</script>

<template>
  <div v-if="show" class="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-4 text-left select-none">
    <!-- Backdrop -->
    <div class="absolute inset-0 bg-slate-900/60 backdrop-blur-xs transition-opacity" @click="emit('close')" />

    <!-- Modal Dialog -->
    <div class="relative w-full max-w-md bg-white dark:bg-slate-900 rounded-2xl shadow-2xl border border-slate-200 dark:border-slate-800 overflow-hidden flex flex-col max-h-[90vh] animate-in fade-in zoom-in-95 duration-150">
      <!-- Header -->
      <div class="px-5 py-4 bg-slate-900 text-white flex items-center justify-between border-b border-slate-800 shrink-0">
        <div class="flex items-center gap-2">
          <MapPin class="w-4 h-4 text-sky-400" />
          <h3 class="font-bold text-sm tracking-tight">{{ isEditing ? '编辑工艺区域' : '添加工艺区域' }}</h3>
        </div>
        <button type="button" @click="emit('close')" class="text-slate-400 hover:text-white cursor-pointer">
          <X class="w-4 h-4" />
        </button>
      </div>

      <!-- Form Body -->
      <div class="p-5 overflow-y-auto space-y-4 text-xs">
        <div v-if="errorMessage" class="p-3 rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 text-rose-600 dark:text-rose-400 font-medium">
          {{ errorMessage }}
        </div>

        <div>
          <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">
            区域名称 <span class="text-rose-500">*</span>
          </label>
          <input
            v-model="formName"
            type="text"
            placeholder="例如: 智能三级沉降池"
            class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border text-slate-900 dark:text-white font-medium focus:outline-none focus:border-[#1890ff]"
            :class="errors.Name ? 'border-rose-500' : 'border-slate-200 dark:border-slate-700'"
          />
          <p v-if="errors.Name" class="text-rose-500 text-[10px] mt-1">{{ errors.Name }}</p>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <div>
            <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">父级区域</label>
            <select
              v-model="formParentId"
              class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-medium focus:outline-none focus:border-[#1890ff]"
            >
              <option :value="null">（作为根级区域）</option>
              <option v-for="r in parentOptions" :key="r.node.id" :value="r.node.id">
                {{ '　'.repeat(r.depth) }}{{ r.node.name }}
              </option>
            </select>
          </div>
          <div>
            <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">区域类型</label>
            <select
              v-model="formAreaType"
              class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-medium focus:outline-none focus:border-[#1890ff]"
            >
              <option :value="1">工厂</option>
              <option :value="2">车间</option>
              <option :value="3">产线</option>
              <option :value="4">区域</option>
              <option :value="5">仓库</option>
            </select>
          </div>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <div>
            <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">区域编码</label>
            <input
              v-model="formCode"
              type="text"
              placeholder="如: AREA-A"
              class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-mono uppercase font-bold focus:outline-none focus:border-[#1890ff]"
            />
          </div>
          <div>
            <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">排序权重</label>
            <input
              v-model.number="formSort"
              type="number"
              min="0"
              class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-mono focus:outline-none focus:border-[#1890ff]"
            />
          </div>
        </div>

        <div>
          <label class="flex items-center gap-2 font-bold text-slate-700 dark:text-slate-300 cursor-pointer">
            <input type="checkbox" v-model="formEnabled" class="rounded text-[#1890ff] focus:ring-0" />
            <span>启用该区域</span>
          </label>
        </div>

        <div>
          <label class="block font-bold text-slate-700 dark:text-slate-300 mb-1">详细描述</label>
          <textarea
            v-model="formDesc"
            rows="2"
            placeholder="描述此区域主要工艺功能与物理位置..."
            class="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 text-slate-900 dark:text-white font-sans focus:outline-none focus:border-[#1890ff]"
          />
        </div>
      </div>

      <!-- Footer -->
      <div class="px-5 py-3 bg-slate-50 dark:bg-slate-950 border-t border-slate-200 dark:border-slate-800 flex justify-end gap-2 shrink-0">
        <button
          type="button"
          @click="emit('close')"
          class="px-4 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 text-slate-600 dark:text-slate-300 font-bold text-xs cursor-pointer"
        >
          取消
        </button>
        <button
          type="button"
          @click="handleSave"
          class="px-5 py-2 rounded-lg bg-[#1890ff] hover:bg-sky-600 text-white font-bold text-xs cursor-pointer shadow-xs"
        >
          保存
        </button>
      </div>
    </div>
  </div>
</template>
