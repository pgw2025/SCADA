<script setup lang="ts">
import { computed } from 'vue';
import { HMIComponent } from '../types';
import { Settings, Tag, Sliders, Layout, Hash } from 'lucide-vue-next';

const props = defineProps<{
  selectedComponent: HMIComponent | null;
  plcTags: Array<{ key: string; name: string }>;
}>();

const emit = defineEmits<{
  (e: 'updateComponent', id: string, updates: Partial<HMIComponent>): void;
}>();

const componentProps = computed(() => {
  return props.selectedComponent?.props ?? {};
});

// Prop mutator helper - emits change upwards
const updateProp = (key: string, value: any) => {
  if (!props.selectedComponent) return;
  emit('updateComponent', props.selectedComponent.id, {
    props: {
      ...componentProps.value,
      [key]: value,
    },
  });
};

const updateComponentField = (field: keyof HMIComponent, value: any) => {
  if (!props.selectedComponent) return;
  emit('updateComponent', props.selectedComponent.id, {
    [field]: value,
  });
};
</script>

<template>
  <div v-if="!selectedComponent" class="h-full bg-[#fafafa] border-l border-[#d9d9d9] p-6 text-gray-400 text-xs flex flex-col justify-center items-center text-center">
    <!-- Spinning Cog -->
    <Settings class="w-8 h-8 text-[#1890ff] mb-2 animate-spin-slow opacity-60" />
    <p class="font-semibold text-gray-700">属性配置面板</p>
    <p class="text-[10px] text-gray-400 mt-2.5 max-w-[200px] leading-relaxed">
      请在中央画布上点击选中任意组态元件，即可在此配置它的专属工业逻辑与 PLC 绑定参数。
    </p>
  </div>

  <div v-else class="h-full flex flex-col bg-white border-l border-[#d9d9d9] text-[#262626] overflow-y-auto">
    <!-- Title -->
    <div class="p-4 border-b border-[#f0f0f0] bg-[#fafafa] flex items-center gap-2">
      <Layout class="w-4 h-4 text-[#1890ff]" />
      <h3 class="text-xs font-bold text-[#141414] uppercase tracking-wider">
        属性物统绑定
      </h3>
    </div>

    <div class="p-4 space-y-4">
      <!-- Core Layout section -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700">
          <Sliders class="w-3.5 h-3.5 text-[#1890ff]" />
          基础布局属性
        </div>

        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 font-mono">元件标识 (ID)</label>
            <input
              type="text"
              disabled
              :value="selectedComponent.id"
              class="w-full bg-[#fafafa] border border-[#d9d9d9] rounded px-2.5 py-1.5 mt-0.5 text-gray-400 font-mono text-[10px] cursor-not-allowed"
            />
          </div>
          <div>
            <label class="text-[10px] text-gray-500">元件名称</label>
            <input
              type="text"
              :value="selectedComponent.name"
              @input="updateComponentField('name', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white border border-[#d9d9d9] hover:border-[#1890ff] focus:border-[#1890ff] rounded px-2.5 py-1.5 mt-0.5 text-[#262626] focus:outline-none"
            />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 font-mono">X 轴坐标 (px)</label>
            <input
              type="number"
              :value="selectedComponent.x"
              @input="updateComponentField('x', parseInt(($event.target as HTMLInputElement).value) || 0)"
              class="w-full bg-white border border-[#d9d9d9] hover:border-[#1890ff] focus:border-[#1890ff] rounded px-2.5 py-1.5 mt-0.5 text-[#262626] font-mono focus:outline-none"
            />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 font-mono">Y 轴坐标 (px)</label>
            <input
              type="number"
              :value="selectedComponent.y"
              @input="updateComponentField('y', parseInt(($event.target as HTMLInputElement).value) || 0)"
              class="w-full bg-white border border-[#d9d9d9] hover:border-[#1890ff] focus:border-[#1890ff] rounded px-2.5 py-1.5 mt-0.5 text-[#262626] font-mono focus:outline-none"
            />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 font-mono">宽度 (Width)</label>
            <input
              type="number"
              :value="selectedComponent.width"
              @input="updateComponentField('width', parseInt(($event.target as HTMLInputElement).value) || 20)"
              class="w-full bg-white border border-[#d9d9d9] hover:border-[#1890ff] focus:border-[#1890ff] rounded px-2.5 py-1.5 mt-0.5 text-[#262626] font-mono focus:outline-none"
            />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 font-mono">高度 (Height)</label>
            <input
              type="number"
              :value="selectedComponent.height"
              @input="updateComponentField('height', parseInt(($event.target as HTMLInputElement).value) || 20)"
              class="w-full bg-white border border-[#d9d9d9] hover:border-[#1890ff] focus:border-[#1890ff] rounded px-2.5 py-1.5 mt-0.5 text-[#262626] font-mono focus:outline-none"
            />
          </div>
        </div>

        <div>
          <label class="text-[10px] text-gray-500">图层顺序 (Z-Index)</label>
          <input
            type="number"
            :value="selectedComponent.zIndex ?? 1"
            @input="updateComponentField('zIndex', parseInt(($event.target as HTMLInputElement).value) || 1)"
            class="w-full bg-white border border-[#d9d9d9] hover:border-[#1890ff] focus:border-[#1890ff] rounded px-2.5 py-1.5 mt-0.5 text-[#262626] focus:outline-none"
          />
        </div>
      </section>

      <div class="border-t border-[#f0f0f0] my-4" />

      <!-- PLC Register binding selector -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700">
          <Tag class="w-3.5 h-3.5 text-[#1890ff]" />
          工业 PLC 寄存器绑定
        </div>

        <div>
          <label class="text-[10px] text-gray-500">选择绑定的 PLC 变量 (OPC-UA Tag)</label>
          <select
            :value="selectedComponent.bindField"
            @change="updateComponentField('bindField', ($event.target as HTMLSelectElement).value)"
            class="w-full bg-white border border-[#d9d9d9] hover:border-[#1890ff] focus:border-[#1890ff] rounded px-2.5 py-1.5 mt-0.5 text-[#262626] focus:outline-none text-xs"
          >
            <option value="">-- 静态常量单元 (无数据绑定) --</option>
            <option v-for="tag in plcTags" :key="tag.key" :value="tag.key">
              {{ tag.name }} ({{ tag.key }})
            </option>
          </select>
        </div>
      </section>

      <div class="border-t border-[#f0f0f0] my-4" />

      <!-- Widget specifics customization -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700">
          <Hash class="w-3.5 h-3.5 text-[#1890ff]" />
          特色工业属性
        </div>

        <div>
          <label class="text-[10px] text-gray-500">标定说明 (Label)</label>
          <textarea
            rows="2"
            :value="selectedComponent.label"
            @input="updateComponentField('label', ($event.target as HTMLTextAreaElement).value)"
            class="w-full bg-white border border-[#d9d9d9] hover:border-[#1890ff] focus:border-[#1890ff] rounded px-2.5 py-1.5 mt-0.5 text-[#262626] focus:outline-none text-xs"
          />
        </div>

        <!-- States color picks -->
        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500">运行激活光效</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input
                type="color"
                :value="componentProps.activeColor || '#1890ff'"
                @input="updateProp('activeColor', ($event.target as HTMLInputElement).value)"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden"
              />
              <input
                type="text"
                :value="componentProps.activeColor || '#1890ff'"
                @input="updateProp('activeColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white border border-[#d9d9d9] rounded text-[10px] px-1 py-1 font-mono text-gray-600 focus:outline-none"
              />
            </div>
          </div>

          <div>
            <label class="text-[10px] text-gray-500">空闲正常底色</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input
                type="color"
                :value="componentProps.inactiveColor || '#8c8c8c'"
                @input="updateProp('inactiveColor', ($event.target as HTMLInputElement).value)"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden"
              />
              <input
                type="text"
                :value="componentProps.inactiveColor || '#8c8c8c'"
                @input="updateProp('inactiveColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white border border-[#d9d9d9] rounded text-[10px] px-1 py-1 font-mono text-gray-600 focus:outline-none"
              />
            </div>
          </div>
        </div>

        <!-- Medium Fluid Filler style -->
        <div v-if="['tank', 'boiler', 'conveyor'].includes(selectedComponent.type)">
          <label class="text-[10px] text-gray-500">填充介质颜色 (Medium)</label>
          <div class="flex items-center gap-1.5 mt-1">
            <input
              type="color"
              :value="componentProps.fillColor || '#1890ff'"
              @input="updateProp('fillColor', ($event.target as HTMLInputElement).value)"
              class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden"
            />
            <input
              type="text"
              :value="componentProps.fillColor || '#1890ff'"
              @input="updateProp('fillColor', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white border border-[#d9d9d9] rounded text-[10px] px-1 focus:outline-none text-gray-600"
            />
          </div>
        </div>

        <!-- Calibration threshold and high warning limit meters -->
        <div v-if="['gauge-dial', 'gauge-level', 'digital-val'].includes(selectedComponent.type)" class="space-y-2">
          <div class="grid grid-cols-2 gap-2 text-xs">
            <div>
              <label class="text-[10px] text-gray-500">测量量程上限 (Max)</label>
              <input
                type="number"
                :value="componentProps.maxValue ?? 100"
                @input="updateProp('maxValue', parseFloat(($event.target as HTMLInputElement).value) || 100)"
                class="w-full bg-white border border-[#d9d9d9] rounded px-2.5 py-1 text-gray-800 focus:outline-none"
              />
            </div>
            <div>
              <label class="text-[10px] text-gray-500">测量单位 (Unit)</label>
              <input
                type="text"
                :value="componentProps.unit ?? ''"
                @input="updateProp('unit', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white border border-[#d9d9d9] rounded px-2.5 py-1 text-gray-800 focus:outline-none"
                placeholder="e.g. L/s, MPa, ℃"
              />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-2 text-xs">
            <div>
              <label class="text-[10px] text-red-500">红色高限报警值</label>
              <input
                type="number"
                :value="componentProps.thresholdMax ?? 90"
                @input="updateProp('thresholdMax', parseFloat(($event.target as HTMLInputElement).value) || 90)"
                class="w-full bg-white border border-red-300 rounded px-2.5 py-1 text-red-600 focus:outline-none focus:border-red-500"
              />
            </div>
            <div>
              <label class="text-[10px] text-amber-600">黄色低限预警值</label>
              <input
                type="number"
                :value="componentProps.thresholdMin ?? 10"
                @input="updateProp('thresholdMin', parseFloat(($event.target as HTMLInputElement).value) || 0)"
                class="w-full bg-white border border-rose-300 rounded px-2.5 py-1 text-amber-700 focus:outline-none focus:border-amber-500"
              />
            </div>
          </div>
        </div>

        <!-- INDUSTRIAL BUTTON SPECIFIC CONTROLS -->
        <div v-if="selectedComponent.type === 'button'" class="space-y-2 text-xs border border-gray-100 p-2 rounded bg-gray-50/50">
          <p class="font-bold text-[#1890ff] text-[10px] uppercase tracking-wider mb-1">按钮功能配置</p>
          <div>
            <label class="text-[10px] text-gray-500">操作类型 (Action Mode)</label>
            <select
              :value="componentProps.buttonMode || 'toggle'"
              @change="updateProp('buttonMode', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white border border-[#d9d9d9] rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] mt-0.5 text-xs text-[#262626]"
            >
              <option value="toggle">主锁/自锁 (Toggle - 单击取反)</option>
              <option value="momentary">点动操作 (Momentary - 按下1松开0)</option>
              <option value="set-value">恒定设值 (SetValue - 写入固定值)</option>
            </select>
          </div>
          
          <div v-if="componentProps.buttonMode === 'set-value'">
            <label class="text-[10px] text-gray-500">点击写入的数值</label>
            <input
              type="number"
              :value="componentProps.clickValue ?? 1"
              @input="updateProp('clickValue', parseFloat(($event.target as HTMLInputElement).value) || 0)"
              class="w-full bg-white border border-[#d9d9d9] rounded px-2.5 py-1 text-gray-800 focus:outline-none focus:border-[#1890ff] mt-0.5 text-xs"
            />
          </div>

          <div>
            <label class="text-[10px] text-gray-500">按钮文本说明 (Static Label)</label>
            <input
              type="text"
              :value="componentProps.buttonText ?? ''"
              @input="updateProp('buttonText', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white border border-[#d9d9d9] rounded px-2.5 py-1 text-gray-800 focus:outline-none focus:border-[#1890ff] mt-0.5 text-xs"
              placeholder="默认取本级Label"
            />
          </div>
        </div>

        <!-- TIME CLOCK WIDGET FORMATS -->
        <div v-if="selectedComponent.type === 'sys-time'" class="space-y-2 text-xs border border-gray-100 p-2 rounded bg-gray-50/50">
          <p class="font-bold text-emerald-600 text-[10px] uppercase tracking-wider mb-1">系统时间显示设置</p>
          <div>
            <label class="text-[10px] text-gray-500">排版格式 (DateTime Format)</label>
            <select
              :value="componentProps.timeFormat || 'HH:mm:ss'"
              @change="updateProp('timeFormat', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white border border-[#d9d9d9] rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] mt-0.5 text-xs text-[#262626]"
            >
              <option value="HH:mm:ss">时分秒 (HH:mm:ss)</option>
              <option value="YYYY-MM-DD HH:mm:ss">年月日 时分秒</option>
              <option value="YYYY-MM-DD">仅显示日期 (YYYY-MM-DD)</option>
            </select>
          </div>
        </div>

        <!-- STATE TEXT DICTIONARY MAPPING -->
        <div v-if="selectedComponent.type === 'state-text'" class="space-y-2 text-xs border border-gray-100 p-2 rounded bg-gray-50/50">
          <p class="font-bold text-sky-600 text-[10px] uppercase tracking-wider mb-1">汉字状态文本转换表</p>
          <div>
            <label class="text-[10px] text-gray-500 flex justify-between">
              <span>状态转换规则字典 (分号隔开键值)</span>
            </label>
            <textarea
              rows="3"
              :value="componentProps.stateMappings ?? ''"
              @input="updateProp('stateMappings', ($event.target as HTMLTextAreaElement).value)"
              class="w-full bg-white border border-[#d9d9d9] rounded px-2 py-1 mt-0.5 focus:outline-none focus:border-[#1890ff] text-xs font-mono text-gray-700 leading-relaxed"
              placeholder="e.g. 0:停机;1:低速;2:正运转"
            />
            <p class="text-[9px] text-gray-400 mt-1 leading-snug">
              格式：值:汉字,用分号或全角分号隔离。例如 <code class="bg-gray-100 px-1 py-0.5 rounded text-gray-600 font-mono">0:停止;1:开启</code> 或 <code class="bg-gray-100 px-1 py-0.5 rounded text-gray-600 font-mono">false:关闭;true:开启</code>。
            </p>
          </div>
        </div>

        <!-- Custom fonts controls for Text boxes -->
        <div v-if="['text', 'button', 'state-text'].includes(selectedComponent.type)" class="space-y-2 text-xs">
          <div class="grid grid-cols-2 gap-2">
            <div>
              <label class="text-[10px] text-gray-500">对齐方式</label>
              <select
                :value="componentProps.align || 'center'"
                @change="updateProp('align', ($event.target as HTMLSelectElement).value)"
                class="w-full bg-white border border-[#d9d9d9] hover:border-[#1890ff] focus:border-[#1890ff] rounded px-2.5 py-1 text-gray-800 mt-0.5 focus:outline-none"
              >
                <option value="left">靠左对齐</option>
                <option value="center">居中对齐</option>
                <option value="right">靠右对齐</option>
              </select>
            </div>
            <div>
              <label class="text-[10px] text-gray-500">字体大小 (px)</label>
              <input
                type="number"
                :value="componentProps.fontSize || 12"
                @input="updateProp('fontSize', parseInt(($event.target as HTMLInputElement).value) || 12)"
                class="w-full bg-white border border-[#d9d9d9] hover:border-[#1890ff] focus:border-[#1890ff] rounded px-2.5 py-1 text-[#262626] mt-0.5 focus:outline-none"
              />
            </div>
          </div>
 
          <div class="flex items-center gap-2 mt-2">
            <input
              type="checkbox"
              id="fontBoldDef"
              :checked="componentProps.bold || false"
              @change="updateProp('bold', ($event.target as HTMLInputElement).checked)"
              class="rounded border-[#d9d9d9] text-[#1890ff] focus:ring-0"
            />
            <label htmlFor="fontBoldDef" class="text-xs text-gray-700 select-none cursor-pointer">
              加粗字体 (Font Bold)
            </label>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>
