<script setup lang="ts">
import { ref, watch } from 'vue';
import { Upload, Trash2, X, ImageIcon } from 'lucide-vue-next';
import { listHmiImages, uploadHmiImage, deleteHmiImage, HmiImageDto } from '../api/scadaApi';
import { addLog } from '../services/logService';
import { showToast } from '../services/toastService';

/**
 * 组态图片图库对话框：上传 / 缩略图网格 / 选择复用 / 删除。
 * 三处复用：图元添加（ScadaTopologyView）、图元换图与背景选图（InspectorPanel）。
 * 上传/删除失败由 http 拦截器统一 toast，此处仅做流程闭环（刷新列表/写日志）。
 */
const props = defineProps<{ modelValue: boolean }>();

const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void;
  (e: 'select', img: { url: string; fileName: string; originalName: string; width: number; height: number }): void;
}>();

const images = ref<HmiImageDto[]>([]);
const loading = ref(false);
const uploading = ref(false);
const fileInputRef = ref<HTMLInputElement | null>(null);
const isDragOver = ref(false);

const close = () => emit('update:modelValue', false);

// 打开时拉取列表
watch(() => props.modelValue, async (open) => {
  if (open) await refresh();
});

const refresh = async () => {
  loading.value = true;
  try {
    images.value = await listHmiImages();
  } finally {
    loading.value = false;
  }
};

// ---- 上传（点击 + 拖拽共用） ----
const doUpload = async (file: File | undefined | null) => {
  if (!file || uploading.value) return;
  uploading.value = true;
  try {
    const dto = await uploadHmiImage(file);
    addLog('组态编辑', `上传图片成功: ${dto.originalName}`, 'info');
    await refresh();
  } catch {
    // 失败提示由 http 拦截器统一弹出
  } finally {
    uploading.value = false;
  }
};

const onFileChange = (e: Event) => {
  const input = e.target as HTMLInputElement;
  doUpload(input.files?.[0]);
  input.value = ''; // 允许重复选择同一文件
};

const onDrop = (e: DragEvent) => {
  isDragOver.value = false;
  doUpload(e.dataTransfer?.files?.[0]);
};

// ---- 选择：预载获取原始尺寸后回传（调用方按宽高比落布） ----
const selectImage = (img: HmiImageDto) => {
  const image = new Image();
  image.onload = () => {
    emit('select', {
      url: img.url,
      fileName: img.fileName,
      originalName: img.originalName,
      width: image.naturalWidth || 200,
      height: image.naturalHeight || 150,
    });
  };
  image.onerror = () => {
    // 加载失败仍允许选择（按默认尺寸落布），不阻塞流程
    emit('select', { url: img.url, fileName: img.fileName, originalName: img.originalName, width: 200, height: 150 });
  };
  image.src = img.url;
};

// ---- 删除：不做引用检查（后端无组件反查），提示用户裂图风险 ----
const removeImage = (img: HmiImageDto) => {
  if (!confirm(`确定删除图片「${img.originalName}」吗？\n正在引用该图的组件/背景将显示裂图。`)) return;
  deleteHmiImage(img.fileName)
    .then(() => {
      addLog('组态编辑', `删除图片: ${img.originalName}`, 'info');
      return refresh();
    })
    .catch(() => { /* 失败提示由拦截器统一弹出 */ });
};

const formatSize = (bytes: number): string => {
  if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
};
</script>

<template>
  <div v-if="modelValue" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-[70] p-4"
    @click.self="close">
    <div
      class="bg-white dark:bg-slate-900 rounded-lg shadow-2xl border border-slate-200 dark:border-slate-700 w-full max-w-3xl max-h-[85vh] flex flex-col text-[#262626] dark:text-slate-100">
      <!-- 头部 -->
      <div class="flex items-center justify-between px-4 py-3 border-b border-slate-200 dark:border-slate-800">
        <div class="flex items-center gap-2">
          <ImageIcon class="w-4 h-4 text-[#1890ff] dark:text-sky-400" />
          <h3 class="text-sm font-bold">组态图片图库</h3>
          <span class="text-[10px] text-slate-400 dark:text-slate-500">{{ images.length }} 张</span>
        </div>
        <button @click="close"
          class="p-1 rounded text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors cursor-pointer"
          title="关闭">
          <X class="w-4 h-4" />
        </button>
      </div>

      <!-- 上传区（点击 + 拖拽） -->
      <div class="px-4 pt-4">
        <div @click="fileInputRef?.click()" @dragover.prevent="isDragOver = true" @dragleave="isDragOver = false"
          @drop.prevent="onDrop"
          class="border-2 border-dashed rounded-lg py-5 flex flex-col items-center gap-1.5 cursor-pointer transition-colors select-none"
          :class="isDragOver
            ? 'border-[#1890ff] bg-[#e6f7ff] dark:bg-sky-950/30'
            : 'border-slate-300 dark:border-slate-700 hover:border-[#1890ff] dark:hover:border-sky-500'">
          <Upload class="w-5 h-5 text-[#1890ff] dark:text-sky-400" :class="uploading ? 'animate-pulse' : ''" />
          <p class="text-xs text-slate-600 dark:text-slate-300">
            {{ uploading ? '正在上传…' : '点击选择或拖拽图片到此处上传' }}
          </p>
          <p class="text-[10px] text-slate-400 dark:text-slate-500">支持 png / jpg / gif / webp / svg，单张 ≤ 10MB</p>
          <input ref="fileInputRef" type="file" accept=".png,.jpg,.jpeg,.gif,.webp,.svg" class="hidden"
            @change="onFileChange" />
        </div>
      </div>

      <!-- 缩略图网格 -->
      <div class="flex-1 overflow-y-auto p-4">
        <div v-if="loading" class="text-xs text-slate-400 dark:text-slate-500 text-center py-8">加载中…</div>
        <div v-else-if="images.length === 0"
          class="flex flex-col items-center gap-2 py-8 text-slate-400 dark:text-slate-500">
          <ImageIcon class="w-8 h-8 opacity-40" />
          <p class="text-xs">暂无图片，请先上传</p>
        </div>
        <div v-else class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-3">
          <div v-for="img in images" :key="img.fileName"
            class="group relative border border-slate-200 dark:border-slate-700 rounded-lg overflow-hidden cursor-pointer hover:ring-2 hover:ring-[#1890ff] transition-all bg-slate-50 dark:bg-slate-950"
            :title="img.originalName" @click="selectImage(img)">
            <div class="h-28 flex items-center justify-center overflow-hidden">
              <img :src="img.url" :alt="img.originalName" loading="lazy"
                class="max-h-full max-w-full object-contain" draggable="false" />
            </div>
            <div class="px-2 py-1.5 border-t border-slate-200 dark:border-slate-800">
              <p class="text-[11px] truncate font-medium">{{ img.originalName }}</p>
              <p class="text-[9px] text-slate-400 dark:text-slate-500">{{ formatSize(img.sizeBytes) }}</p>
            </div>
            <!-- 删除按钮：悬浮显示，阻止冒泡避免触发选择 -->
            <button @click.stop="removeImage(img)"
              class="absolute top-1.5 right-1.5 p-1 rounded bg-slate-900/70 text-white opacity-0 group-hover:opacity-100 hover:bg-red-600 transition-all cursor-pointer"
              title="删除">
              <Trash2 class="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </div>

      <!-- 底部提示 -->
      <div class="px-4 py-2.5 border-t border-slate-200 dark:border-slate-800 text-[10px] text-slate-400 dark:text-slate-500">
        点击图片即可选用；删除不做引用检查，被引用的图片删除后将显示裂图。
      </div>
    </div>
  </div>
</template>
