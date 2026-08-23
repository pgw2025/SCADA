<script setup lang="ts">
import { ref, computed } from 'vue';
import { ComponentType } from '../types';
import {
  Activity,
  Cpu,
  Layers,
  Thermometer,
  ToggleLeft,
  Tv,
  Type,
  BatteryCharging,
  Gauge,
  Workflow,
  Search,
  Package,
  SquareTerminal,
  Clock,
  SlidersHorizontal,
  RefreshCw,
} from 'lucide-vue-next';

interface WidgetDef {
  type: ComponentType;
  name: string;
  defaultWidth: number;
  defaultHeight: number;
  icon: any; // We can render using vue <component :is="icon" />
  iconColor: string;
  description: string;
}

const WIDGETS: WidgetDef[] = [
  {
    type: 'boiler',
    name: '加热锅炉反应釜',
    defaultWidth: 140,
    defaultHeight: 180,
    icon: BatteryCharging,
    iconColor: 'text-amber-500',
    description: '工业超温蒸汽燃煤锅炉，带火焰动态变频效果。',
  },
  {
    type: 'pump',
    name: '离心输送水泵',
    defaultWidth: 70,
    defaultHeight: 70,
    icon: Cpu,
    iconColor: 'text-emerald-500',
    description: '液体或气体加压叶轮主输水泵，运行自带叶片旋转效果。',
  },
  {
    type: 'valve',
    name: '智能两位电磁阀',
    defaultWidth: 60,
    defaultHeight: 60,
    icon: ToggleLeft,
    iconColor: 'text-indigo-500',
    description: '蝶阀/电磁球阀，状态切换时蝶阀手轮旋转90°。',
  },
  {
    type: 'tank',
    name: '圆角储液容器罐',
    defaultWidth: 120,
    defaultHeight: 160,
    icon: Layers,
    iconColor: 'text-sky-500',
    description: '带刻度及气泡波纹的液体深度容器。',
  },
  {
    type: 'conveyor',
    name: '变频滚轮传送带',
    defaultWidth: 260,
    defaultHeight: 40,
    icon: Workflow,
    iconColor: 'text-orange-500',
    description: '物料或箱体传动物件传送带，速度非零时展现位移动画。',
  },
  {
    type: 'gauge-dial',
    name: '高精度机械表盘',
    defaultWidth: 120,
    defaultHeight: 120,
    icon: Gauge,
    iconColor: 'text-purple-500',
    description: '圆形度盘表，支持设置极限阈值并同步变红警告。',
  },
  {
    type: 'gauge-level',
    name: '液位刻度警告柱',
    defaultWidth: 50,
    defaultHeight: 140,
    icon: Thermometer,
    iconColor: 'text-rose-500',
    description: '带有高、中、低限阈值的段式刻度检测条。',
  },
  {
    type: 'digital-val',
    name: '多功能数显仪表',
    defaultWidth: 130,
    defaultHeight: 60,
    icon: Tv,
    iconColor: 'text-cyan-500',
    description: '工业LED高亮七段数值显示面板，可绑定任意PLC点。',
  },
  {
    type: 'trend-chart',
    name: '实时波段趋势图',
    defaultWidth: 280,
    defaultHeight: 160,
    icon: Activity,
    iconColor: 'text-red-500',
    description: '动态微积分平滑滤波趋势图，记录历史PLC模拟参数。',
  },
  {
    type: 'pipe-h',
    name: '水平输水管路',
    defaultWidth: 160,
    defaultHeight: 16,
    icon: 'div-h',
    iconColor: '',
    description: '支持流向光带闪烁动效的水平流动金属管。',
  },
  {
    type: 'pipe-v',
    name: '垂直高压管道',
    defaultWidth: 16,
    defaultHeight: 160,
    icon: 'div-v',
    iconColor: '',
    description: '支持流速频闪的垂直重力回流水管。',
  },
  {
    type: 'led',
    name: '高发光LED指示灯',
    defaultWidth: 40,
    defaultHeight: 50,
    icon: 'div-led',
    iconColor: '',
    description: '红绿双色状态警告信源灯，支持光晕频闪效果。',
  },
  {
    type: 'text',
    name: '自定义文本组态',
    defaultWidth: 120,
    defaultHeight: 35,
    icon: Type,
    iconColor: 'text-slate-300',
    description: '静态或者动态映射文字说明，可调节字号和对齐方式。',
  },
  {
    type: 'button',
    name: '3D重载控制按钮',
    defaultWidth: 100,
    defaultHeight: 50,
    icon: SquareTerminal,
    iconColor: 'text-amber-500',
    description: '工业现场操作主令按钮，支持自锁(Toggle)、点动(Momentary)、设值(SetValue)三种回写执行逻辑。',
  },
  {
    type: 'switch',
    name: '两位旋动选择按钮',
    defaultWidth: 70,
    defaultHeight: 90,
    icon: ToggleLeft,
    iconColor: 'text-[#1890ff]',
    description: '自复位旋钮式状态控制开关，触手可及。',
  },
  {
    type: 'sys-time',
    name: '实时系统时钟',
    defaultWidth: 160,
    defaultHeight: 50,
    icon: Clock,
    iconColor: 'text-emerald-500',
    description: '数字式数码时钟控件，毫秒级响应显示。',
  },
  {
    type: 'state-text',
    name: 'PLC变量中文翻译器',
    defaultWidth: 155,
    defaultHeight: 55,
    icon: SlidersHorizontal,
    iconColor: 'text-blue-500',
    description: '多状态信号到汉字状态文本翻译映射转换板。',
  },
  {
    type: 'motor',
    name: '变频伺服AC电机',
    defaultWidth: 120,
    defaultHeight: 90,
    icon: RefreshCw,
    iconColor: 'text-sky-500',
    description: '变频配给驱动电机，工作时伴随冷却风扇叶极速旋转效果。',
  },
];

const emit = defineEmits<{
  (e: 'addWidget', type: ComponentType, w: number, h: number, name: string): void;
}>();

const searchTerm = ref('');
const activeTab = ref<'all' | 'equipment' | 'sensors' | 'structures'>('all');

const filteredWidgets = computed(() => {
  return WIDGETS.filter((w) => {
    const matchesSearch = w.name.toLowerCase().includes(searchTerm.value.toLowerCase());
    if (!matchesSearch) return false;

    if (activeTab.value === 'all') return true;
    if (activeTab.value === 'equipment') {
      return ['boiler', 'pump', 'valve', 'tank', 'conveyor', 'motor'].includes(w.type);
    }
    if (activeTab.value === 'sensors') {
      return ['gauge-dial', 'gauge-level', 'digital-val', 'trend-chart', 'led', 'sys-time', 'state-text'].includes(w.type);
    }
    if (activeTab.value === 'structures') {
      return ['pipe-h', 'pipe-v', 'text', 'button', 'switch'].includes(w.type);
    }
    return true;
  });
});
</script>

<template>
  <div class="h-full flex flex-col bg-white dark:bg-slate-900 border-r border-[#d9d9d9] dark:border-slate-800 text-[#262626] dark:text-slate-100 transition-colors">
    <!-- Search Header -->
    <div class="p-4 border-b border-[#f0f0f0] dark:border-slate-800 bg-[#fafafa] dark:bg-slate-950">
      <h3 class="text-xs font-bold text-[#141414] dark:text-slate-100 uppercase tracking-wider mb-2.5 flex items-center gap-2">
        <Package class="w-4 h-4 text-[#1890ff] dark:text-sky-400" />
        工业器件图库
      </h3>
      <div class="relative">
        <input
          type="text"
          placeholder="搜索工业器件..."
          v-model="searchTerm"
          class="w-full bg-white dark:bg-slate-900 border border-[#d9d9d9] dark:border-slate-700 rounded py-1.5 pl-8 pr-3 text-xs text-slate-800 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 focus:ring-1 focus:ring-[#1890ff]"
        />
        <Search class="w-3.5 h-3.5 text-gray-400 dark:text-slate-500 absolute left-2.5 top-2.5" />
      </div>
    </div>

    <!-- Tabs -->
    <div class="flex text-center border-b border-[#f0f0f0] dark:border-slate-800 px-1 text-[11px] bg-[#fafafa] dark:bg-slate-950">
      <button
        @click="activeTab = 'all'"
        :class="[
          'flex-1 py-2 font-medium transition-all cursor-pointer',
          activeTab === 'all'
            ? 'text-[#1890ff] dark:text-sky-400 border-b-2 border-[#1890ff] dark:border-sky-400 bg-white dark:bg-slate-900 font-bold'
            : 'text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/40 dark:hover:bg-slate-800/40'
        ]"
      >
        全部
      </button>
      <button
        @click="activeTab = 'equipment'"
        :class="[
          'flex-1 py-2 font-medium transition-all cursor-pointer',
          activeTab === 'equipment'
            ? 'text-[#1890ff] dark:text-sky-400 border-b-2 border-[#1890ff] dark:border-sky-400 bg-white dark:bg-slate-900 font-bold'
            : 'text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/40 dark:hover:bg-slate-800/40'
        ]"
      >
        设备
      </button>
      <button
        @click="activeTab = 'sensors'"
        :class="[
          'flex-1 py-2 font-medium transition-all cursor-pointer',
          activeTab === 'sensors'
            ? 'text-[#1890ff] dark:text-sky-400 border-b-2 border-[#1890ff] dark:border-sky-400 bg-white dark:bg-slate-900 font-bold'
            : 'text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/40 dark:hover:bg-slate-800/40'
        ]"
      >
        仪表
      </button>
      <button
        @click="activeTab = 'structures'"
        :class="[
          'flex-1 py-2 font-medium transition-all cursor-pointer',
          activeTab === 'structures'
            ? 'text-[#1890ff] dark:text-sky-400 border-b-2 border-[#1890ff] dark:border-sky-400 bg-white dark:bg-slate-900 font-bold'
            : 'text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/40 dark:hover:bg-slate-800/40'
        ]"
      >
        管道
      </button>
    </div>

    <!-- Grid List -->
    <div class="flex-1 overflow-y-auto p-3 space-y-2 bg-white dark:bg-slate-900">
      <div v-if="filteredWidgets.length === 0" class="text-center py-6 text-gray-400 dark:text-slate-500 text-xs">
        未找到相关组态器件
      </div>
      <div
        v-else
        v-for="widget in filteredWidgets"
        :key="widget.type"
        @click="emit('addWidget', widget.type, widget.defaultWidth, widget.defaultHeight, widget.name)"
        class="group flex gap-3 p-2.5 bg-[#fafafa] dark:bg-slate-950/60 hover:bg-white dark:hover:bg-slate-800 border border-[#f0f0f0] dark:border-slate-800 hover:border-[#1890ff] dark:hover:border-sky-500 hover:shadow-sm rounded cursor-pointer transition-all duration-200"
      >
        <div class="w-10 h-10 rounded bg-white dark:bg-slate-900 border border-[#f0f0f0] dark:border-slate-800 flex items-center justify-center group-hover:scale-105 transition-all shadow-sm">
          <!-- Render icons -->
          <component
            v-if="typeof widget.icon !== 'string'"
            :is="widget.icon"
            class="w-5 h-5"
            :class="widget.iconColor"
          />
          <div v-else-if="widget.icon === 'div-h'" class="w-7 h-2 bg-slate-600 dark:bg-slate-400 rounded-full" />
          <div v-else-if="widget.icon === 'div-v'" class="w-2 h-7 bg-slate-600 dark:bg-slate-400 rounded-full" />
          <div v-else-if="widget.icon === 'div-led'" class="w-4 h-4 rounded-full bg-emerald-500 ring-2 ring-emerald-300 dark:ring-emerald-600 animate-pulse" />
        </div>
        <div class="flex-1 min-w-0 text-left">
          <div class="flex justify-between items-start">
            <h4 class="text-xs font-semibold text-gray-800 dark:text-slate-200 group-hover:text-[#1890ff] dark:group-hover:text-sky-400 transition-colors">
              {{ widget.name }}
            </h4>
            <span class="text-[9px] text-gray-400 dark:text-slate-500 font-mono">
              {{ widget.defaultWidth }}x{{ widget.defaultHeight }}
            </span>
          </div>
          <p class="text-[10px] text-gray-400 dark:text-slate-400 mt-0.5 truncate leading-relaxed">
            {{ widget.description }}
          </p>
        </div>
      </div>
    </div>

    <!-- Instructions Footer -->
    <div class="p-3 bg-[#fafafa] dark:bg-slate-950 border-t border-[#f0f0f0] dark:border-slate-800 text-[10px] text-gray-400 dark:text-slate-500 text-center select-none leading-relaxed">
      💡 点击左侧器件即可放置在中央画布，可自由在画布上拖拽、双击配置。
    </div>
  </div>
</template>
