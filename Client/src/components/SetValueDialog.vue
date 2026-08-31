<script setup lang="ts">
// var-display 设值弹窗：工业数字键盘样式（大按钮适合触屏/戴手套操作）。
// 仅负责输入与校验（小数位/写入范围/二次确认），实际写入由父级走 handleTriggerToggleValue('setValue') 管道，
// 只读拦截与权限拦截复用该管道，本弹窗不直接下发。
import { ref, computed, onMounted, onUnmounted, watch } from 'vue';
import { X, Delete } from 'lucide-vue-next';
import { HMIComponent } from '../types';

const props = defineProps<{
  /** 目标组件（含绑定信息与 decimals/writeMin/writeMax/confirmRequired 配置） */
  component: HMIComponent;
  /** 变量当前值（boolean 时切换为 开/关 二选一写入） */
  current?: number | boolean;
}>();

const emit = defineEmits<{
  (e: 'close'): void;
  (e: 'confirm', value: number | boolean): void;
}>();

// 小数位：0~4 钳制，非法回退 2（与 HMIWidget 显示口径一致）
const decimals = computed(() => {
  const d = Number(props.component.props.decimals);
  if (!Number.isFinite(d)) return 2;
  return Math.min(4, Math.max(0, Math.round(d)));
});
const isBool = computed(() => typeof props.current === 'boolean');
const writeMin = computed(() => {
  const v = props.component.props.writeMin;
  return typeof v === 'number' && Number.isFinite(v) ? v : null;
});
const writeMax = computed(() => {
  const v = props.component.props.writeMax;
  return typeof v === 'number' && Number.isFinite(v) ? v : null;
});
const confirmRequired = computed(() => props.component.props.confirmRequired === true);
const varKey = computed(() => props.component.bindVariableKey || props.component.bindField || '');
const unit = computed(() => props.component.props.unit || '');

// 数字输入状态：以字符串维护避免「0.1 → 0.10」被 parseFloat 抹掉输入中的 0
const input = ref('');
// 二次确认状态：confirmRequired 时第一次点击进入待确认，第二次才真正下发
const pendingConfirm = ref(false);
const error = ref('');

watch(() => props.component.id, () => {
  input.value = typeof props.current === 'number' ? String(props.current) : '';
  pendingConfirm.value = false;
  error.value = '';
}, { immediate: true });

// 数字键盘输入；非法输入（双小数点/超位数）即时提示并拒绝
const pressKey = (key: string) => {
  if (key === 'back') {
    input.value = input.value.slice(0, -1);
    error.value = '';
    return;
  }
  if (key === '.') {
    if (input.value.includes('.')) return;
    input.value = (input.value === '' ? '0' : input.value) + '.';
    return;
  }
  if (input.value.replace(/[^0-9]/g, '').length >= 8) {
    error.value = '整数位数已达上限';
    return;
  }
  input.value = (input.value === '0' ? '' : input.value) + key;
  error.value = '';
};

// 提交前校验：范围 + 小数位；通过后按位数四舍五入（消除浮点噪声）
const validate = (): number | null => {
  const raw = input.value.trim();
  if (raw === '' || raw === '.') {
    error.value = '请输入有效数值';
    return null;
  }
  const n = Number(raw);
  if (!Number.isFinite(n)) {
    error.value = '请输入有效数值';
    return null;
  }
  if (writeMin.value != null && n < writeMin.value) {
    error.value = `低于写入下限 ${writeMin.value}`;
    return null;
  }
  if (writeMax.value != null && n > writeMax.value) {
    error.value = `超出写入上限 ${writeMax.value}`;
    return null;
  }
  const decLen = raw.includes('.') ? raw.split('.')[1].length : 0;
  if (decLen > decimals.value) {
    error.value = `小数最多 ${decimals.value} 位`;
    return null;
  }
  return Number(n.toFixed(decimals.value));
};

const roundTo = (n: number): number => Number(n.toFixed(decimals.value));

const onConfirm = () => {
  // 布尔变量：开/关二选一直接下发
  if (isBool.value) {
    emit('confirm', !props.current);
    return;
  }
  const n = validate();
  if (n == null) return;
  // 高危变量二次确认：第一次点击进入待确认，数值/范围变更后重置
  if (confirmRequired.value && !pendingConfirm.value) {
    pendingConfirm.value = true;
    return;
  }
  emit('confirm', roundTo(n));
};

const onBoolWrite = (v: boolean) => emit('confirm', v);

// 桌面端物理键盘直输：数字/小数点/退格/回车确认/Esc 关闭
const onKeydown = (e: KeyboardEvent) => {
  if (e.key >= '0' && e.key <= '9') { pressKey(e.key); e.preventDefault(); }
  else if (e.key === '.') { pressKey('.'); e.preventDefault(); }
  else if (e.key === 'Backspace') { pressKey('back'); e.preventDefault(); }
  else if (e.key === 'Enter') { onConfirm(); e.preventDefault(); }
  else if (e.key === 'Escape') { emit('close'); e.preventDefault(); }
};
onMounted(() => window.addEventListener('keydown', onKeydown));
onUnmounted(() => window.removeEventListener('keydown', onKeydown));

// 输入变化即重置待确认，避免改了数值还沿用上一次的确认态
watch(input, () => { pendingConfirm.value = false; });

const keys = ['7', '8', '9', '4', '5', '6', '1', '2', '3', '.', '0', 'back'];
</script>

<template>
  <div class="fixed inset-0 z-[100] flex items-center justify-center bg-black/50 backdrop-blur-sm p-4"
    @mousedown.self="emit('close')">
    <div
      class="w-full max-w-xs bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-xl shadow-2xl overflow-hidden select-none">
      <!-- 标题区 -->
      <div class="flex items-start justify-between px-4 pt-3 pb-2 border-b border-slate-100 dark:border-slate-800">
        <div class="min-w-0">
          <p class="text-sm font-bold text-slate-800 dark:text-white truncate">{{ component.label || '变量设定' }}</p>
          <p class="text-[10px] text-slate-400 dark:text-slate-500 mt-0.5 truncate">
            写入目标: {{ varKey || '未绑定' }}<template v-if="unit"> · 单位 {{ unit }}</template>
          </p>
        </div>
        <button @click="emit('close')"
          class="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 transition-colors -mr-1 p-1"
          aria-label="关闭">
          <X class="w-4 h-4" />
        </button>
      </div>

      <!-- 布尔变量：开/关二选一 -->
      <div v-if="isBool" class="p-4 space-y-3">
        <p class="text-[11px] text-slate-500 dark:text-slate-400">
          当前值: <span class="font-mono font-bold">{{ current ? '开 (1)' : '关 (0)' }}</span> · 布尔变量，选择写入状态
        </p>
        <div class="grid grid-cols-2 gap-3">
          <button @click="onBoolWrite(true)"
            class="h-14 rounded-lg bg-emerald-500 hover:bg-emerald-600 active:scale-95 transition-all text-white text-base font-bold shadow">
            写入 开
          </button>
          <button @click="onBoolWrite(false)"
            class="h-14 rounded-lg bg-slate-600 hover:bg-slate-700 active:scale-95 transition-all text-white text-base font-bold shadow">
            写入 关
          </button>
        </div>
      </div>

      <!-- 数值变量：当前值 + 数字键盘 -->
      <div v-else class="p-4">
        <div class="flex justify-between items-baseline text-[11px] text-slate-500 dark:text-slate-400 mb-1">
          <span>当前值: <span class="font-mono font-bold text-slate-700 dark:text-slate-200">{{
            typeof current === 'number' ? current.toFixed(decimals) : '--' }}</span></span>
          <span v-if="writeMin != null || writeMax != null">写入范围: {{ writeMin ?? '-∞' }} ~ {{ writeMax ?? '+∞' }}</span>
        </div>
        <div
          class="h-10 flex items-center justify-end px-3 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg font-mono text-2xl font-bold tabular-nums text-slate-800 dark:text-white">
          {{ input || '0' }}<span class="text-sm text-slate-400 font-normal ml-1">{{ unit }}</span>
        </div>
        <p class="h-4 mt-1 text-[10px] text-red-500 leading-4">{{ error || (decimals > 0 ? `保留 ${decimals} 位小数` : '仅整数') }}</p>

        <div class="grid grid-cols-3 gap-2">
          <button v-for="k in keys" :key="k" @click="pressKey(k)"
            class="h-11 rounded-lg bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 active:scale-95 transition-all border border-slate-200 dark:border-slate-700 text-slate-800 dark:text-white text-lg font-bold font-mono flex items-center justify-center">
            <Delete v-if="k === 'back'" class="w-5 h-5" />
            <template v-else>{{ k }}</template>
          </button>
        </div>

        <div class="grid grid-cols-2 gap-2 mt-3">
          <button @click="emit('close')"
            class="h-10 rounded-lg border border-slate-300 dark:border-slate-600 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-all text-sm font-bold">
            取消
          </button>
          <button @click="onConfirm()" :disabled="!!error && !isBool"
            class="h-10 rounded-lg transition-all text-sm font-bold text-white shadow active:scale-95 disabled:opacity-50 disabled:cursor-not-allowed"
            :class="pendingConfirm ? 'bg-red-500 hover:bg-red-600' : 'bg-sky-600 hover:bg-sky-700'">
            {{ pendingConfirm ? `确认写入 ${Number(input || 0).toFixed(decimals)} ?` : '确认写入' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
