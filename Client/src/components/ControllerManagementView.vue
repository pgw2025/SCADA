<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import {
  Cpu,
  Plus,
  Edit3,
  Trash2,
  Search,
  RefreshCw,
  ChevronLeft,
  ChevronRight,
  X,
  Power,
  Filter
} from 'lucide-vue-next';
import { systemConfig, addLog } from '../store/index';
import { fetchProtocols } from '../api/protocolApi';
import {
  fetchControllers,
  createController,
  updateController,
  deleteController
} from '../api/controllerApi';
import { extractApiError } from '../api/http';
import { showToast } from '../services/toastService';
import { Controller, ControllerRequest, Protocol } from '../types';

// ================= 列表 & 分页 =================
const list = ref<Controller[]>([]);
const total = ref(0);
const pageIndex = ref(1);
const pageSize = ref(20);
const loading = ref(false);

const filterProtocolId = ref<number | null>(null);
const keyword = ref('');
const protocols = ref<Protocol[]>([]);

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)));

const loadProtocols = async () => {
  protocols.value = await fetchProtocols();
};

const loadList = async () => {
  if (systemConfig.value.isSimulationActive) { list.value = []; total.value = 0; return; }
  loading.value = true;
  try {
    const result = await fetchControllers({
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      protocolId: filterProtocolId.value ?? undefined,
      keyword: keyword.value.trim() || undefined
    });
    list.value = result.items ?? [];
    total.value = result.total ?? 0;
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  } finally {
    loading.value = false;
  }
};

const applyFilter = () => {
  pageIndex.value = 1;
  loadList();
};
const resetFilter = () => {
  filterProtocolId.value = null;
  keyword.value = '';
  applyFilter();
};
const changePage = (delta: number) => {
  const next = pageIndex.value + delta;
  if (next < 1 || next > totalPages.value) return;
  pageIndex.value = next;
  loadList();
};

// ================= 表单（新增/编辑共用） =================
const showModal = ref(false);
const editingId = ref<number | null>(null);
const formError = ref('');
const saving = ref(false);

const form = ref<ControllerRequest>({
  Code: '',
  Name: '',
  ProtocolId: 0,
  Manufacturer: '',
  Model: '',
  Description: '',
  IsEnabled: true
});

const protocolOptions = computed(() => protocols.value);

const openCreate = () => {
  editingId.value = null;
  formError.value = '';
  form.value = {
    Code: '',
    Name: '',
    ProtocolId: protocols.value[0]?.id || 0,
    Manufacturer: '',
    Model: '',
    Description: '',
    IsEnabled: true
  };
  showModal.value = true;
};

const openEdit = (c: Controller) => {
  editingId.value = c.id;
  formError.value = '';
  form.value = {
    Code: c.code,
    Name: c.name,
    ProtocolId: c.protocolId,
    Manufacturer: c.manufacturer ?? '',
    Model: c.model ?? '',
    Description: c.description ?? '',
    IsEnabled: c.isEnabled
  };
  showModal.value = true;
};

const save = async () => {
  formError.value = '';
  if (!form.value.Code.trim()) { showToast('请输入控制器编码', 'warning'); return; }
  if (!form.value.Name.trim()) { showToast('请输入控制器名称', 'warning'); return; }
  if (!form.value.ProtocolId) { showToast('请选择协议类型', 'warning'); return; }

  saving.value = true;
  try {
    const dto: ControllerRequest = {
      Code: form.value.Code.trim(),
      Name: form.value.Name.trim(),
      ProtocolId: form.value.ProtocolId,
      Manufacturer: form.value.Manufacturer?.trim() || undefined,
      Model: form.value.Model?.trim() || undefined,
      Description: form.value.Description?.trim() || undefined,
      IsEnabled: form.value.IsEnabled
    };
    if (editingId.value != null) {
      await updateController(editingId.value, dto);
      addLog('控制器管理', `更新了控制器 [${dto.Name}]`, 'normal');
    } else {
      await createController(dto);
      addLog('控制器管理', `新增控制器 [${dto.Name}]`, 'normal');
    }
    showModal.value = false;
    showToast('保存成功', 'success');
    loadList();
  } catch (e: any) {
    formError.value = extractApiError(e);
  } finally {
    saving.value = false;
  }
};

const remove = async (c: Controller) => {
  if (!confirm(`确定删除控制器 [${c.name}]（编码 ${c.code}）吗？`)) return;
  try {
    await deleteController(c.id);
    addLog('控制器管理', `删除了控制器 [${c.name}]`, 'warning');
    showToast('已删除', 'success');
    // 当前页删空则回退一页
    if (list.value.length === 1 && pageIndex.value > 1) pageIndex.value -= 1;
    loadList();
  } catch (e: any) {
    showToast(extractApiError(e), 'error');
  }
};

const fmtTime = (ts?: string | null) => {
  if (!ts) return '—';
  const d = new Date(ts);
  if (isNaN(d.getTime())) return ts;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

onMounted(async () => {
  await loadProtocols();
  loadList();
});
</script>

<template>
  <div class="h-full overflow-y-auto p-4 sm:p-6 bg-slate-50/50 dark:bg-transparent text-[#1e293b] dark:text-slate-100 select-none">
    <!-- Header -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between border-b border-slate-200 dark:border-slate-800 pb-5 gap-4 text-left">
      <div>
        <h1 class="text-xl font-bold font-sans text-slate-900 dark:text-white tracking-tight">控制器管理</h1>
        <p class="text-xs text-slate-500 dark:text-slate-400 mt-1">
          登记控制器 / PLC 硬件资产台账（阶段 2，仅资产登记，不影响既有采集）
        </p>
      </div>
      <button
        @click="openCreate"
        class="bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 shadow-sm"
      >
        <Plus class="w-4 h-4" />
        添加控制器
      </button>
    </div>

    <!-- Filter bar -->
    <div class="mt-5 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl px-3 py-2.5 flex flex-wrap items-center gap-x-4 gap-y-2 text-xs">
      <div class="flex items-center gap-1.5 font-bold text-slate-500 dark:text-slate-400">
        <Filter class="w-4 h-4" />
        筛选
      </div>
      <select
        v-model="filterProtocolId"
        @change="applyFilter"
        class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-xs font-bold text-slate-700 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]"
      >
        <option :value="null">全部协议</option>
        <option v-for="p in protocolOptions" :key="p.id" :value="p.id">{{ p.name }}</option>
      </select>
      <div class="flex items-center gap-1.5">
        <div class="relative">
          <Search class="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400" />
          <input
            v-model="keyword"
            type="text"
            placeholder="编码 / 名称 / 厂商 / 型号"
            @keyup.enter="applyFilter"
            class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg pl-8 pr-2.5 py-1.5 w-56 text-xs text-slate-700 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]"
          />
        </div>
        <button
          @click="applyFilter"
          class="px-2.5 py-1.5 rounded-lg bg-slate-900 dark:bg-sky-600 text-white font-bold text-xs cursor-pointer hover:bg-slate-800 dark:hover:bg-sky-500"
        >
          查询
        </button>
        <button
          v-if="filterProtocolId != null || keyword"
          @click="resetFilter"
          class="text-rose-500 hover:text-rose-700 font-bold cursor-pointer text-xs"
        >
          清除
        </button>
      </div>
    </div>

    <!-- Table -->
    <div class="mt-5 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm text-left transition-colors">
      <div class="flex items-center justify-between px-4 py-3 border-b border-slate-100 dark:border-slate-800">
        <h3 class="text-xs font-bold tracking-widest uppercase text-slate-500 dark:text-slate-400">
          控制器列表（共 {{ total }} 条）
        </h3>
        <button
          @click="loadList"
          class="text-[10px] text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 font-bold px-2 py-0.5 rounded border border-slate-200 dark:border-slate-700 hover:border-slate-300 transition-all cursor-pointer inline-flex items-center gap-1"
        >
          <RefreshCw class="w-3 h-3" :class="loading ? 'animate-spin' : ''" />
          刷新
        </button>
      </div>

      <table class="w-full text-xs">
        <thead>
          <tr class="bg-slate-50 dark:bg-slate-950/60 ring-1 ring-slate-100 dark:ring-slate-800 uppercase text-[10px] text-slate-400 dark:text-slate-500 font-bold tracking-wider">
            <th class="px-4 py-3 text-left">ID</th>
            <th class="px-4 py-3 text-left">编码</th>
            <th class="px-4 py-3 text-left">名称</th>
            <th class="px-4 py-3 text-left">协议类型</th>
            <th class="px-4 py-3 text-left">厂商 / 型号</th>
            <th class="px-4 py-3 text-left">状态</th>
            <th class="px-4 py-3 text-left">更新时间</th>
            <th class="px-4 py-3 text-right">操作</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100 dark:divide-slate-800">
          <tr v-for="c in list" :key="c.id" class="hover:bg-slate-50/50 dark:hover:bg-slate-800/40 transition-all">
            <td class="px-4 py-3 font-bold text-slate-500 dark:text-slate-400">{{ c.id }}</td>
            <td class="px-4 py-3 font-mono font-bold text-[#1890ff] dark:text-sky-400">{{ c.code }}</td>
            <td class="px-4 py-3 font-sans font-bold text-slate-800 dark:text-white">
              <span class="inline-flex items-center gap-1.5">
                <Cpu class="w-3.5 h-3.5 text-slate-400" />
                {{ c.name }}
              </span>
            </td>
            <td class="px-4 py-3">
              <span class="bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 font-bold px-2 py-0.5 rounded-full text-[10px]">
                {{ c.protocolName || `#${c.protocolId}` }}
              </span>
            </td>
            <td class="px-4 py-3 text-slate-500 dark:text-slate-400">
              <template v-if="c.manufacturer || c.model">
                <span class="font-sans font-medium">{{ c.manufacturer || '—' }}</span>
                <span class="text-slate-300 dark:text-slate-600"> / </span>
                <span class="font-sans font-medium">{{ c.model || '—' }}</span>
              </template>
              <span v-else class="text-slate-300 dark:text-slate-600">—</span>
            </td>
            <td class="px-4 py-3">
              <span
                class="inline-flex items-center gap-1 text-[10px] font-bold px-2 py-0.5 rounded-full border"
                :class="c.isEnabled
                  ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-800'
                  : 'bg-slate-100 dark:bg-slate-800 text-slate-400 border-slate-200 dark:border-slate-700'"
              >
                <Power class="w-3 h-3" />
                {{ c.isEnabled ? '启用' : '停用' }}
              </span>
            </td>
            <td class="px-4 py-3 font-mono text-slate-400 dark:text-slate-500">{{ fmtTime(c.updatedAt) }}</td>
            <td class="px-4 py-3 text-right whitespace-nowrap">
              <button
                @click="openEdit(c)"
                class="text-[#1890ff] dark:text-sky-400 hover:text-sky-600 cursor-pointer font-sans font-bold inline-flex items-center gap-0.5 mr-3"
              >
                <Edit3 class="w-3.5 h-3.5" />
                编辑
              </button>
              <button
                @click="remove(c)"
                class="text-rose-500 hover:text-rose-700 cursor-pointer font-sans font-bold inline-flex items-center gap-0.5"
              >
                <Trash2 class="w-3.5 h-3.5" />
                删除
              </button>
            </td>
          </tr>
        </tbody>
      </table>

      <div v-if="list.length === 0 && !loading" class="py-12 text-center text-slate-400 dark:text-slate-500 text-xs">
        <Cpu class="w-8 h-8 mx-auto mb-2 opacity-30" />
        <span>暂无控制器，点击右上角"添加控制器"登记第一台 PLC / 控制器</span>
      </div>

      <!-- Pagination -->
      <div class="flex items-center justify-between px-4 py-3 border-t border-slate-100 dark:border-slate-800 text-xs">
        <div class="flex items-center gap-2 text-slate-500 dark:text-slate-400">
          <span>每页</span>
          <select v-model.number="pageSize" @change="applyFilter" class="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-xs focus:outline-none">
            <option :value="10">10</option>
            <option :value="20">20</option>
            <option :value="50">50</option>
          </select>
          <span>条</span>
          <span class="ml-2">第 {{ pageIndex }} / {{ totalPages }} 页</span>
        </div>
        <div class="flex items-center gap-1.5">
          <button
            @click="changePage(-1)"
            :disabled="pageIndex <= 1"
            class="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-500 dark:text-slate-400 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
          >
            <ChevronLeft class="w-3.5 h-3.5" />
          </button>
          <button
            @click="changePage(1)"
            :disabled="pageIndex >= totalPages"
            class="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-500 dark:text-slate-400 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
          >
            <ChevronRight class="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: ADD / EDIT -->
    <div v-if="showModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Cpu class="w-4 h-4 text-[#1890ff]" />
            <span>{{ editingId != null ? '编辑控制器' : '添加控制器' }}</span>
          </div>
          <button @click="showModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs overflow-y-auto max-h-[420px]">
          <div v-if="formError" class="bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 rounded-lg p-3 text-rose-600 dark:text-rose-400 whitespace-pre-line">
            {{ formError }}
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">控制器编码 <span class="text-rose-500">*</span></label>
            <input
              v-model="form.Code"
              type="text"
              placeholder="例如: CTRL-PLC-001"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-mono font-bold uppercase focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
            />
            <p class="text-slate-400 dark:text-slate-500 text-[10px] mt-1">编码全局唯一，建议使用简短业务标识（≤50 字符）</p>
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">控制器名称 <span class="text-rose-500">*</span></label>
            <input
              v-model="form.Name"
              type="text"
              placeholder="例如: 1# 车间 S7-1500 主站"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
            />
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">协议类型 <span class="text-rose-500">*</span></label>
            <select
              v-model="form.ProtocolId"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
            >
              <option v-for="p in protocolOptions" :key="p.id" :value="p.id">{{ p.name }}（{{ p.key }}）</option>
            </select>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">厂商</label>
              <input
                v-model="form.Manufacturer"
                type="text"
                placeholder="例如: Siemens"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
              />
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">型号</label>
              <input
                v-model="form.Model"
                type="text"
                placeholder="例如: S7-1500"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
              />
            </div>
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">描述</label>
            <textarea
              v-model="form.Description"
              rows="2"
              placeholder="控制器用途、安装位置、备注等"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] leading-relaxed"
            />
          </div>

          <label class="flex items-center gap-2 font-bold text-slate-600 dark:text-slate-300 cursor-pointer select-none">
            <input type="checkbox" v-model="form.IsEnabled" class="text-[#1890ff] focus:ring-0" />
            启用该控制器
          </label>
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
  </div>
</template>
