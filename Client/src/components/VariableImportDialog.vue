<script setup lang="ts">
import { ref, computed } from 'vue';
import { X, Upload, FileUp, Download, ChevronLeft, ChevronRight, AlertTriangle, CheckCircle2 } from 'lucide-vue-next';
import { previewVariableImport, submitVariableImport } from '../store/index';
import { ConflictStrategy, VariableImportPreview, VariableImportResult } from '../types';

const props = defineProps<{
  open: boolean;
  modelId: number;
}>();
const emit = defineEmits<{
  (e: 'close'): void;
  (e: 'done'): void;
}>();

type Step = 'pick' | 'preview' | 'result';
const step = ref<Step>('pick');
const loading = ref(false);
const errorMsg = ref('');

const selectedFile = ref<File | null>(null);
const preview = ref<VariableImportPreview | null>(null);
const result = ref<VariableImportResult | null>(null);
const strategy = ref<ConflictStrategy>('Skip');

// 仅当全部选择且预览完成时进入下一步
const canNextFromPick = computed(() => !!selectedFile.value);
const canSubmit = computed(() => !!preview.value && preview.value.validRows + preview.value.conflictRows > 0);

// 供输入框自动打开文件选择
const fileInput = ref<HTMLInputElement | null>(null);

// 重置为独立 step 的初始状态（每次打开或关闭后）
const reset = () => {
  step.value = 'pick';
  selectedFile.value = null;
  preview.value = null;
  result.value = null;
  errorMsg.value = '';
  strategy.value = 'Skip';
};

const onFileChange = (e: Event) => {
  const target = e.target as HTMLInputElement;
  selectedFile.value = target.files?.[0] || null;
  errorMsg.value = '';
};

const runPreview = async () => {
  if (!selectedFile.value) return;
  loading.value = true;
  errorMsg.value = '';
  try {
    preview.value = await previewVariableImport(props.modelId, selectedFile.value);
    step.value = 'preview';
  } catch (err: any) {
    errorMsg.value = err?.response?.data?.message || err?.message || '预览失败';
  } finally {
    loading.value = false;
  }
};

const runImport = async () => {
  if (!selectedFile.value || !preview.value) return;
  loading.value = true;
  errorMsg.value = '';
  try {
    result.value = await submitVariableImport(props.modelId, selectedFile.value, strategy.value);
    step.value = 'result';
    emit('done');
  } catch (err: any) {
    errorMsg.value = err?.response?.data?.message || err?.message || '导入失败';
  } finally {
    loading.value = false;
  }
};

const closeAndReset = () => {
  emit('close');
  reset();
};

// 行状态徽章样式
const rowBadgeClass = (hasError: boolean, isConflict: boolean) => {
  if (hasError) return 'bg-rose-50 text-rose-700 border-rose-200 dark:bg-rose-950/60 dark:text-rose-300 dark:border-rose-800';
  if (isConflict) return 'bg-amber-50 text-amber-700 border-amber-200 dark:bg-amber-950/60 dark:text-amber-300 dark:border-amber-800';
  return 'bg-emerald-50 text-emerald-700 border-emerald-200 dark:bg-emerald-950/60 dark:text-emerald-300 dark:border-emerald-800';
};
</script>

<template>
  <div
    v-if="open"
    class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-[60] p-4"
  >
    <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 w-full max-w-3xl overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150 flex flex-col" style="max-height: 88vh">
      <!-- Header -->
      <div class="bg-slate-900 dark:bg-slate-950 text-white px-4 py-3 flex items-center justify-between border-b border-slate-800 shrink-0">
        <div class="flex items-center gap-2 font-bold text-xs uppercase tracking-widest">
          <Upload class="w-4 h-4 text-violet-400" />
          <span>批量导入变量</span>
          <span class="text-[10px] font-normal text-slate-400 normal-case">模型 #{{ modelId }}</span>
        </div>
        <button @click="closeAndReset" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
      </div>

      <!-- Steps indicator -->
      <div class="flex items-center gap-1 px-4 py-2.5 border-b border-slate-100 dark:border-slate-800 text-[11px] shrink-0">
        <template v-for="(s, i) in ['选择文件', '预览确认', '导入结果']" :key="s">
          <span
            class="px-2 py-0.5 rounded-full font-bold"
            :class="step === ['pick','preview','result'][i] ? 'bg-violet-600 text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-400'"
          >{{ i + 1 }}. {{ s }}</span>
          <span v-if="i < 2" class="text-slate-300 dark:text-slate-600">›</span>
        </template>
      </div>

      <div class="overflow-y-auto p-4 flex-1 min-h-0">
        <!-- Step 1: 选择文件 -->
        <div v-if="step === 'pick'">
          <div class="space-y-3">
            <div
              class="border-2 border-dashed border-slate-200 dark:border-slate-700 rounded-xl p-8 text-center cursor-pointer hover:border-violet-400 dark:hover:border-violet-600 transition-colors"
              @click="fileInput?.click()"
            >
              <FileUp class="w-8 h-8 mx-auto text-slate-300 dark:text-slate-600 mb-2" />
              <p class="text-xs text-slate-500 dark:text-slate-400">
                {{ selectedFile ? selectedFile.name : '点击选择文件' }}
              </p>
              <p class="text-[10px] text-slate-400 dark:text-slate-500 mt-1">
                支持 TIA Portal 变量表导出的 xlsx，或本系统导出的标准 CSV 模板
              </p>
            </div>
            <input ref="fileInput" type="file" accept=".xlsx,.xls,.csv" class="hidden" @change="onFileChange" />

            <p v-if="errorMsg" class="text-[11px] text-rose-600 dark:text-rose-400 flex items-center gap-1">
              <AlertTriangle class="w-3.5 h-3.5" /> {{ errorMsg }}
            </p>
          </div>
        </div>

        <!-- Step 2: 预览确认 -->
        <div v-else-if="step === 'preview'" class="space-y-3">
          <div v-if="preview" class="flex flex-wrap gap-2 text-[11px]">
            <span class="px-2 py-1 rounded bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 font-bold">共 {{ preview.totalRows }} 行</span>
            <span class="px-2 py-1 rounded bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 font-bold">新增 {{ preview.validRows }}</span>
            <span class="px-2 py-1 rounded bg-amber-50 dark:bg-amber-950/60 text-amber-700 dark:text-amber-300 font-bold">冲突 {{ preview.conflictRows }}</span>
            <span class="px-2 py-1 rounded bg-rose-50 dark:bg-rose-950/60 text-rose-700 dark:text-rose-300 font-bold">错误 {{ preview.errorRows }}</span>
          </div>

          <!-- 冲突策略选择 -->
          <div class="flex items-center gap-2 text-xs">
            <span class="text-slate-500 dark:text-slate-400 font-bold shrink-0">冲突处理</span>
            <div class="flex gap-1.5">
              <button
                v-for="opt in ([
                  { value: 'Skip', label: '跳过' },
                  { value: 'Overwrite', label: '覆盖更新' },
                  { value: 'Abort', label: '存在冲突即中止' }
                ] as { value: ConflictStrategy; label: string }[])"
                :key="opt.value"
                @click="strategy = opt.value"
                class="px-2.5 py-1 rounded-lg border text-[11px] font-bold"
                :class="strategy === opt.value
                  ? 'bg-violet-600 border-violet-600 text-white'
                  : 'bg-white dark:bg-slate-900 border-slate-200 dark:border-slate-700 text-slate-500 dark:text-slate-400'"
              >{{ opt.label }}</button>
            </div>
          </div>

          <!-- 表格预览 -->
          <div class="overflow-hidden border border-slate-200 dark:border-slate-800 rounded-lg">
            <table class="w-full text-[11px] font-mono divide-y divide-slate-100 dark:divide-slate-800">
              <thead class="bg-slate-50 dark:bg-slate-950 text-slate-400 dark:text-slate-500 font-bold uppercase tracking-wider text-[10px]">
                <tr>
                  <th class="px-3 py-2 text-left">行</th>
                  <th class="px-3 py-2 text-left">标识</th>
                  <th class="px-3 py-2 text-left">类型</th>
                  <th class="px-3 py-2 text-left">地址</th>
                  <th class="px-3 py-2 text-left">状态</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-100 dark:divide-slate-800">
                <tr
                  v-for="r in preview.rows"
                  :key="r.rowNumber"
                  class="hover:bg-slate-50/50 dark:hover:bg-slate-800/40"
                  :class="r.hasError ? 'bg-rose-50/40 dark:bg-rose-950/20' : r.isConflict ? 'bg-amber-50/40 dark:bg-amber-950/20' : ''"
                >
                  <td class="px-3 py-1.5 text-slate-400">{{ r.rowNumber }}</td>
                  <td class="px-3 py-1.5 text-violet-700 dark:text-violet-400 font-bold">{{ r.key }}</td>
                  <td class="px-3 py-1.5">
                    <span v-if="r.dataTypeRaw" class="text-slate-500">{{ r.dataTypeRaw }}</span>
                    <span class="text-slate-900 dark:text-slate-200 font-bold">→ {{ r.dataType }}</span>
                    <span v-if="r.isApproxType" class="ml-1 text-[9px] text-orange-600 dark:text-orange-400">(近似)</span>
                  </td>
                  <td class="px-3 py-1.5 text-slate-500">{{ r.address || '-' }}</td>
                  <td class="px-3 py-1.5">
                    <span
                      v-if="r.hasError || r.isConflict"
                      class="px-1.5 py-0.5 rounded text-[9px] font-bold border"
                      :class="rowBadgeClass(r.hasError, r.isConflict)"
                    >{{ r.hasError ? r.errorReason : '冲突' }}</span>
                    <span v-else class="px-1.5 py-0.5 rounded text-[9px] font-bold border" :class="rowBadgeClass(false, false)">新增</span>
                  </td>
                </tr>
                <tr v-if="preview.rows.length === 0">
                  <td colspan="5" class="p-6 text-center text-slate-400">文件未解析出任何变量行</td>
                </tr>
              </tbody>
            </table>
          </div>

          <p v-if="errorMsg" class="text-[11px] text-rose-600 dark:text-rose-400 flex items-center gap-1">
            <AlertTriangle class="w-3.5 h-3.5" /> {{ errorMsg }}
          </p>
        </div>

        <!-- Step 3: 结果 -->
        <div v-else-if="step === 'result'" class="space-y-3">
          <div v-if="result" class="flex flex-wrap gap-2 text-[11px]">
            <span class="px-2 py-1 rounded bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 font-bold flex items-center gap-1"><CheckCircle2 class="w-3.5 h-3.5" />新增 {{ result.inserted }}</span>
            <span class="px-2 py-1 rounded bg-sky-50 dark:bg-sky-950/60 text-sky-700 dark:text-sky-300 font-bold">更新 {{ result.updated }}</span>
            <span class="px-2 py-1 rounded bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 font-bold">跳过 {{ result.skipped }}</span>
            <span v-if="result.failed > 0" class="px-2 py-1 rounded bg-rose-50 dark:bg-rose-950/60 text-rose-700 dark:text-rose-300 font-bold">失败 {{ result.failed }}</span>
          </div>

          <div v-if="result && result.failedRows.length > 0" class="overflow-hidden border border-rose-200 dark:border-rose-900/60 rounded-lg">
            <table class="w-full text-[11px] font-mono divide-y divide-slate-100 dark:divide-slate-800">
              <thead class="bg-rose-50/60 dark:bg-rose-950/30 text-rose-600 dark:text-rose-400 font-bold uppercase tracking-wider text-[10px]">
                <tr><th class="px-3 py-2 text-left">行</th><th class="px-3 py-2 text-left">标识</th><th class="px-3 py-2 text-left">原因</th></tr>
              </thead>
              <tbody class="divide-y divide-slate-100 dark:divide-slate-800">
                <tr v-for="fr in result.failedRows" :key="fr.rowNumber">
                  <td class="px-3 py-1.5 text-slate-400">{{ fr.rowNumber }}</td>
                  <td class="px-3 py-1.5 text-violet-700 dark:text-violet-400 font-bold">{{ fr.key }}</td>
                  <td class="px-3 py-1.5 text-rose-600 dark:text-rose-400">{{ fr.errorReason }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- Footer actions -->
      <div class="bg-slate-50 dark:bg-slate-950 px-4 py-3 border-t border-slate-100 dark:border-slate-800 flex items-center justify-end gap-2 shrink-0">
        <template v-if="step === 'pick'">
          <button @click="closeAndReset" class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer">关闭</button>
          <button
            @click="runPreview"
            :disabled="!canNextFromPick || loading"
            class="px-4 py-1.5 rounded-lg bg-[#1890ff] hover:bg-sky-500 font-bold text-xs text-white cursor-pointer inline-flex items-center gap-1 disabled:opacity-40 disabled:cursor-not-allowed"
          >
            <ChevronRight class="w-3.5 h-3.5" /> {{ loading ? '解析中...' : '解析预览' }}
          </button>
        </template>

        <template v-else-if="step === 'preview'">
          <button @click="step = 'pick'" class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer inline-flex items-center gap-1"><ChevronLeft class="w-3.5 h-3.5" />返回</button>
          <button
            :disabled="!canSubmit || loading"
            @click="runImport"
            class="px-4 py-1.5 rounded-lg bg-[#0f9d5c] hover:bg-emerald-600 font-bold text-xs text-white cursor-pointer inline-flex items-center gap-1 disabled:opacity-40 disabled:cursor-not-allowed"
          >
            <Download class="w-3.5 h-3.5" /> {{ loading ? '导入中...' : '确认导入' }}
          </button>
        </template>

        <template v-else>
          <button @click="closeAndReset" class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer">完成</button>
        </template>
      </div>
    </div>
  </div>
</template>