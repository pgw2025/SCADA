<script setup lang="ts">
// 属性面板 Schema 通用渲染器（P5）：按 PropSchemaItem 渲染 text/number/color/select/switch 五类控件。
// 值回显三级 fallback：组件 props → 模板默认（defDefaults）→ schema default；
// 变更统一走 updateProp emit（与 InspectorPanel 既有链路一致）。
import type { PropSchemaItem } from '../../propSchemas';

const props = defineProps<{
  schema: PropSchemaItem[];
  /** 组件当前 props（HMIComponent.props） */
  props: Record<string, any>;
  /** 模板默认 props（defDefaults，回显缺省值） */
  defaults?: Record<string, any>;
}>();

const emit = defineEmits<{
  (e: 'updateProp', key: string, value: any): void;
}>();

/** 展示值：props 显式值优先（含空串/0/false），其次默认 props，最后 schema default */
const displayVal = (item: PropSchemaItem): any => {
  const v = props.props[item.key];
  if (v !== undefined && v !== null && v !== '') return v;
  const d = props.defaults?.[item.key];
  if (d !== undefined && d !== null && d !== '') return d;
  return item.default ?? (item.type === 'number' ? 0 : '');
};

/** number 回显：null（可空阈值）显示为空 */
const numDisplay = (item: PropSchemaItem): string => {
  const v = displayVal(item);
  return v === null || v === undefined ? '' : String(v);
};

/** number 输入：可空且清空 → null；非法回退兜底（与 InspectorPanel.numInput 语义一致） */
const onNumberInput = (item: PropSchemaItem, raw: string) => {
  if (item.nullable && raw.trim() === '') {
    emit('updateProp', item.key, null);
    return;
  }
  const n = parseFloat(raw);
  if (Number.isFinite(n)) {
    emit('updateProp', item.key, n);
  } else {
    emit('updateProp', item.key, item.default ?? 0);
  }
};

/** select 变更：按选项原始类型提交（数值选项如 borderWidth 保持 number） */
const onSelect = (item: PropSchemaItem, raw: string) => {
  const opt = item.options?.find((o) => String(o.value) === raw);
  emit('updateProp', item.key, opt ? opt.value : raw);
};
</script>

<template>
  <div class="grid grid-cols-2 gap-2 text-xs">
    <template v-for="item in schema" :key="item.key">
      <!-- switch：整行开关 -->
      <div v-if="item.type === 'switch'" class="flex items-center justify-between col-span-2 select-none">
        <label class="flex items-center gap-2 cursor-pointer">
          <input type="checkbox" :checked="!!displayVal(item)"
            @change="emit('updateProp', item.key, ($event.target as HTMLInputElement).checked)"
            class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
          <span class="text-xs font-semibold text-gray-700 dark:text-slate-300">{{ item.label }}</span>
        </label>
        <span v-if="item.help" class="text-[9px] text-gray-400 dark:text-slate-500 text-right leading-snug">{{ item.help }}</span>
      </div>

      <!-- color：取色器 + 十六进制文本 -->
      <div v-else-if="item.type === 'color'">
        <label class="text-[10px] text-gray-500 dark:text-slate-400 block">{{ item.label }}</label>
        <div class="flex items-center gap-1.5 mt-0.5">
          <input type="color" :value="displayVal(item) || '#3b82f6'"
            @input="emit('updateProp', item.key, ($event.target as HTMLInputElement).value)"
            class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden shrink-0" />
          <input type="text" :value="displayVal(item) || ''"
            @input="emit('updateProp', item.key, ($event.target as HTMLInputElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1.5 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500"
            spellcheck="false" />
        </div>
      </div>

      <!-- select -->
      <div v-else-if="item.type === 'select'">
        <label class="text-[10px] text-gray-500 dark:text-slate-400 block">{{ item.label }}</label>
        <select :value="displayVal(item)"
          @change="onSelect(item, ($event.target as HTMLSelectElement).value)"
          class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 mt-0.5 focus:outline-none text-xs text-[#262626] dark:text-white focus:border-[#1890ff] dark:focus:border-sky-500">
          <option v-for="o in item.options" :key="String(o.value)" :value="String(o.value)">{{ o.label }}</option>
        </select>
      </div>

      <!-- number -->
      <div v-else-if="item.type === 'number'">
        <label class="text-[10px] text-gray-500 dark:text-slate-400 block">
          {{ item.label }}
          <span v-if="item.nullable" class="text-slate-300 dark:text-slate-600">（空=不设）</span>
        </label>
        <input type="number" :value="numDisplay(item)" :min="item.min" :max="item.max" :step="item.step ?? 1"
          :placeholder="item.placeholder"
          @input="onNumberInput(item, ($event.target as HTMLInputElement).value)"
          class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 mt-0.5 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500" />
      </div>

      <!-- text -->
      <div v-else>
        <label class="text-[10px] text-gray-500 dark:text-slate-400 block">{{ item.label }}</label>
        <input type="text" :value="displayVal(item)" :placeholder="item.placeholder"
          @input="emit('updateProp', item.key, ($event.target as HTMLInputElement).value)"
          class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 mt-0.5 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500" />
      </div>
    </template>
  </div>
</template>
