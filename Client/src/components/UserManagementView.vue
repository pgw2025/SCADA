<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import {
  systemUsers,
  addLog,
  loadSystemUsers,
  createSystemUser,
  updateSystemUser,
  deleteSystemUser,
  resetSystemUserPassword,
  loginUser
} from '../store/index';
import { SystemUser } from '../types';
import { ROLE_ADMIN, ROLE_OPERATOR } from '../constants/roles';
import {
  Plus,
  Trash2,
  Edit3,
  Users,
  X,
  Search,
  Check,
  ShieldCheck,
  UserPlus,
  UserMinus,
  Lock,
  UserCheck,
  LayoutList,
  AlignJustify,
  Eye,
  Copy,
  ChevronRight,
  Shield
} from 'lucide-vue-next';

const showModal = ref(false);
const isEditing = ref(false);
const editingUserId = ref<number | null>(null);
// 编辑时记录原始用户名，用于判定内置 admin 锁定保护（不能依赖可编辑的 uName 实时值）
const editingOriginalName = ref('');

// Form Fields
const uName = ref('');
const uRole = ref<string>(ROLE_OPERATOR);
const uStatus = ref<string>('Active');
const uPassword = ref('');

const filterQuery = ref('');

// 角色值 -> 中文显示名（与后端统一值域：Admin/Operator/Viewer）
const ROLE_LABELS: Record<string, string> = {
  Admin: '管理员',
  Operator: '操作员',
  Viewer: '观察员'
};

onMounted(async () => {
  try {
    await loadSystemUsers();
  } catch (error) {
    console.error('加载用户列表失败:', error);
  }
});

// Filtered listed users
const filteredUsers = computed(() => {
  const query = filterQuery.value.trim().toLowerCase();
  if (!query) return systemUsers.value;
  return systemUsers.value.filter(u =>
    u.username.toLowerCase().includes(query) ||
    u.role.toLowerCase().includes(query) ||
    (ROLE_LABELS[u.role] || '').toLowerCase().includes(query)
  );
});

const openNewUserModal = () => {
  isEditing.value = false;
  editingUserId.value = null;
  editingOriginalName.value = '';
  uName.value = '';
  uRole.value = ROLE_OPERATOR;
  uStatus.value = 'Active';
  uPassword.value = '';
  showModal.value = true;
};

const openEditUserModal = (user: SystemUser) => {
  isEditing.value = true;
  editingUserId.value = user.id;
  editingOriginalName.value = user.username;
  uName.value = user.username;
  uRole.value = user.role;
  uStatus.value = user.status;
  uPassword.value = '';
  showModal.value = true;
};

const handleSaveUser = async () => {
  if (!uName.value.trim()) return;

  try {
    if (isEditing.value && editingUserId.value !== null) {
      // 后台管理员自降级确认：编辑的是当前登录账号且把角色从 Admin 降为非 Admin 时二次确认
      // loginUser 不携带 id，按用户名唯一性比对（编辑前的原始用户名）
      if (
        editingOriginalName.value === loginUser.value?.username &&
        editingOriginalName.value !== 'admin' &&
        uRole.value !== ROLE_ADMIN
      ) {
        const originalUser = systemUsers.value.find(u => u.id === editingUserId.value);
        if (originalUser?.role === ROLE_ADMIN) {
          if (!confirm(`您正在降级自己的账号 [${editingOriginalName.value}]。降级后您将失去后台管理功能入口，确定继续吗？`)) {
            return;
          }
        }
      }

      await updateSystemUser({
        id: editingUserId.value,
        username: uName.value.trim(),
        role: uRole.value,
        status: uStatus.value
      });
      addLog('用户管理', `更新了账户 [${uName.value}] 级别为: ${uRole.value}`, 'normal');
    } else {
      if (!uPassword.value.trim()) {
        alert('新建用户必须设置初始密码');
        return;
      }
      await createSystemUser({
        username: uName.value.trim(),
        password: uPassword.value,
        role: uRole.value,
        status: uStatus.value
      });
      addLog('用户管理', `新开设了职工操作柜账户 [${uName.value}] 授权为: ${uRole.value}`, 'normal');
    }
    await loadSystemUsers();
    showModal.value = false;
  } catch {
    // 失败提示由 http 拦截器统一 Toast 弹出（含后端具体 message）
  }
};

const handleDeleteUser = async (id: number, name: string) => {
  if (name === 'admin') {
    alert('安全机制警告：主中控超级管理员 [admin] 无法删除！这是全系统底层唯一默认硬核安全点。');
    return;
  }

  if (confirm(`确定永久注销系统用户 [${name}] 的中控台控制权限吗？`)) {
    try {
      await deleteSystemUser(id);
      await loadSystemUsers();
      addLog('用户管理', `注销了系统登录帐户 [${name}]`, 'warning');
    } catch {
      // 失败提示由 http 拦截器统一 Toast 弹出（含后端具体 message）
    }
  }
};

// ---- 管理员重置他人密码 ----
const showResetPwModal = ref(false);
const resetPwUserId = ref<number | null>(null);
const resetPwName = ref('');
const resetPwPass = ref('');

const openResetPwModal = (user: SystemUser) => {
  resetPwUserId.value = user.id;
  resetPwName.value = user.username;
  resetPwPass.value = '';
  showResetPwModal.value = true;
};

const handleResetPassword = async () => {
  if (resetPwUserId.value === null) return;
  if (!resetPwPass.value.trim() || resetPwPass.value.length < 8) {
    alert('新密码长度至少为 8 位');
    return;
  }
  try {
    await resetSystemUserPassword(resetPwUserId.value, resetPwPass.value);
    addLog('用户管理', `管理员重置了用户 [${resetPwName.value}] 的密码`, 'warning');
    showResetPwModal.value = false;
    alert(`已重置用户 [${resetPwName.value}] 的密码`);
  } catch {
    // 失败提示由 http 拦截器统一 Toast 弹出（含后端具体 message）
  }
};

// ---- 创建时间格式化：后端统一以 UTC 存储，无时区后缀时补齐 'Z' 按 UTC 解析为本地，避免时差 ----
const formatCreatedAt = (raw?: string): string => {
  if (!raw) return '--';
  const hasZone = raw.endsWith('Z') || /[+-]\d{2}:?\d{2}$/.test(raw);
  const iso = hasZone ? raw : raw + 'Z';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '--';
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

// ================= 移动端双模与详情抽屉 =================
const mobileViewMode = ref<'card' | 'compact'>(
  (localStorage.getItem('scada_user_mobile_view') as 'card' | 'compact') || 'card'
);
const setMobileViewMode = (mode: 'card' | 'compact') => {
  mobileViewMode.value = mode;
  localStorage.setItem('scada_user_mobile_view', mode);
};

const selectedUserDetail = ref<SystemUser | null>(null);
const openUserDetail = (u: SystemUser) => {
  selectedUserDetail.value = u;
};
const closeUserDetail = () => {
  selectedUserDetail.value = null;
};

const copyUserDetail = async (u: SystemUser) => {
  const content = [
    `【SCADA 系统用户信息】`,
    `ID: #${u.id}`,
    `用户名: ${u.username}${u.username === 'admin' ? ' (系统内置超级管理员)' : ''}`,
    `角色权限: ${ROLE_LABELS[u.role] || u.role}`,
    `账号状态: ${u.status === 'Active' ? '正常启用' : '已停用'}`,
    `创建时间: ${formatCreatedAt(u.createdAt)}`
  ].join('\n');

  try {
    await navigator.clipboard.writeText(content);
    alert('已复制用户信息至剪贴板');
  } catch {
    // 容错
  }
};
</script>

<template>
  <div
    class="h-full overflow-y-auto space-y-6 text-[#1e293b] dark:text-slate-100 select-none p-4 sm:p-6 bg-slate-50/50 dark:bg-transparent">

    <!-- Top headers -->
    <div
      class="flex flex-col sm:flex-row sm:items-center justify-between border-b border-slate-200 dark:border-slate-800 pb-5 gap-4 text-left transition-colors">
      <div>
        <h1 class="text-xl font-bold font-sans text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Users class="w-5 h-5 text-indigo-500 dark:text-indigo-400" />
          <span>用户权限管理</span>
        </h1>
        <p class="text-xs text-slate-500 dark:text-slate-400 mt-1">
          管理系统用户账户，分配角色权限。支持管理员、操作员、观察员三种角色。
        </p>
      </div>

      <button @click="openNewUserModal"
        class="bg-slate-900 dark:bg-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white px-3.5 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 shadow-sm">
        <UserPlus class="w-4 h-4 text-sky-400 dark:text-white" />
        新建用户
      </button>
    </div>

    <!-- Status Stats row & searching -->
    <div
      class="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 bg-white dark:bg-slate-900 p-4 rounded-xl border border-slate-200 dark:border-slate-800 shadow-sm text-left select-none transition-colors">
      <div class="w-full sm:w-auto flex items-center justify-between sm:justify-start gap-4">
        <div
          class="flex items-center gap-5 text-xs font-sans font-semibold text-slate-500 dark:text-slate-400 shrink-0">
          <span class="inline-flex items-center gap-1">
            用户总数: <b class="text-indigo-600 dark:text-indigo-400 text-sm font-mono">{{ systemUsers.length }}</b>
          </span>
          <span class="inline-flex items-center gap-1 border-l border-slate-200 dark:border-slate-800 pl-4">
            已启用: <b class="text-emerald-600 dark:text-emerald-400 text-sm font-mono">{{systemUsers.filter(u => u.status
              === 'Active').length }}</b>
          </span>
        </div>

        <!-- 移动端双模切换开关 (方案一卡片 / 方案二紧凑) -->
        <div
          class="flex md:hidden items-center p-0.5 bg-slate-100 dark:bg-slate-800 rounded-lg border border-slate-200 dark:border-slate-700 shrink-0">
          <button @click="setMobileViewMode('card')"
            class="flex items-center gap-1 px-2 py-1 text-[11px] font-bold rounded-md transition-all cursor-pointer"
            :class="mobileViewMode === 'card'
              ? 'bg-white dark:bg-slate-700 text-indigo-600 dark:text-indigo-400 shadow-xs'
              : 'text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200'" title="方案一：卡片模式">
            <LayoutList class="w-3.5 h-3.5" />
            卡片
          </button>
          <button @click="setMobileViewMode('compact')"
            class="flex items-center gap-1 px-2 py-1 text-[11px] font-bold rounded-md transition-all cursor-pointer"
            :class="mobileViewMode === 'compact'
              ? 'bg-white dark:bg-slate-700 text-indigo-600 dark:text-indigo-400 shadow-xs'
              : 'text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200'"
            title="方案二：紧凑列表模式">
            <AlignJustify class="w-3.5 h-3.5" />
            紧凑
          </button>
        </div>
      </div>

      <!-- Live query search bar -->
      <div class="relative w-full sm:w-64">
        <input v-model="filterQuery" type="text" placeholder="搜索用户名或角色..."
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg py-1.5 pl-8 pr-3 text-xs placeholder-slate-400 dark:placeholder-slate-500 text-slate-900 dark:text-slate-100 focus:bg-white dark:focus:bg-slate-900 focus:outline-none focus:border-slate-800 dark:focus:border-sky-500" />
        <Search class="absolute left-2.5 top-2.5 w-3.5 h-3.5 text-slate-400 dark:text-slate-500" />
        <button v-if="filterQuery" @click="filterQuery = ''"
          class="absolute right-2 top-2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-300 focus:outline-none">
          <X class="w-3.5 h-3.5" />
        </button>
      </div>
    </div>

    <!-- Users administration table / mobile views -->
    <div
      class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm text-left transition-colors">
      <!-- 桌面端表格视图 -->
      <div class="hidden md:block overflow-x-auto">
        <table class="w-full text-xs">
          <thead>
            <tr
              class="bg-slate-50 dark:bg-slate-950 text-[10px] text-slate-400 dark:text-slate-500 uppercase font-bold tracking-wider divide-x divide-slate-100 dark:divide-slate-800">
              <th class="px-6 py-4">ID</th>
              <th class="px-6 py-4">用户名</th>
              <th class="px-6 py-4">角色</th>
              <th class="px-6 py-4">创建时间</th>
              <th class="px-6 py-4">状态</th>
              <th class="px-6 py-4 text-right">操作</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100 dark:divide-slate-800 font-mono">
            <tr v-for="u in filteredUsers" :key="u.id"
              class="hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-colors">
              <!-- User ID -->
              <td class="px-6 py-4 font-bold text-slate-500 dark:text-slate-400">{{ u.id }}</td>

              <!-- User name username with icon -->
              <td class="px-6 py-4 font-sans font-extrabold text-[13px] text-slate-800 dark:text-slate-100">
                <span class="flex items-center gap-1.5">
                  <UserCheck class="w-4 h-4 text-slate-400 dark:text-slate-500" />
                  <span>{{ u.username }}</span>
                  <span v-if="u.username === 'admin'"
                    class="text-[9px] font-bold font-mono bg-indigo-50 dark:bg-indigo-950/60 text-indigo-600 dark:text-indigo-400 border border-indigo-200 dark:border-indigo-800/60 px-1 py-0.5 rounded uppercase font-normal scale-90">
                    ROOT
                  </span>
                </span>
              </td>

              <!-- Role security category with specific visual badges -->
              <td class="px-6 py-4 font-sans font-bold">
                <span v-if="u.role === ROLE_ADMIN"
                  class="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-amber-50 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400 border border-amber-100 dark:border-amber-900/50 uppercase">
                  🔐 管理员
                </span>
                <span v-else-if="u.role === ROLE_OPERATOR"
                  class="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-blue-50 dark:bg-blue-950/60 text-blue-600 dark:text-blue-400 border border-blue-100 dark:border-blue-900/50 uppercase">
                  ⚡ 操作员
                </span>
                <span v-else
                  class="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border border-slate-200 dark:border-slate-700 uppercase">
                  👁️ 观察员
                </span>
              </td>

              <!-- Created At -->
              <td class="px-6 py-4 text-slate-400 dark:text-slate-500 text-[11px] font-bold leading-none">{{
                formatCreatedAt(u.createdAt) }}</td>

              <!-- System status toggles -->
              <td class="px-6 py-4 text-left">
                <span class="font-sans font-bold text-[10px] inline-flex items-center gap-1 px-2 py-0.5 rounded-full"
                  :class="u.status === 'Active' ? 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400' : 'bg-slate-50 dark:bg-slate-950 text-slate-400 dark:text-slate-500'">
                  <span class="w-1.5 h-1.5 rounded-full"
                    :class="u.status === 'Active' ? 'bg-emerald-500 animate-pulse' : 'bg-slate-300 dark:bg-slate-600'" />
                  {{ u.status === 'Active' ? '已启用' : '已停用' }}
                </span>
              </td>

              <!-- Actions columns -->
              <td class="px-6 py-4 text-right">
                <div class="flex items-center justify-end gap-3 select-none text-[11px] font-sans">
                  <button @click="openEditUserModal(u)"
                    class="text-[#1890ff] dark:text-sky-400 hover:text-sky-600 dark:hover:text-sky-300 cursor-pointer font-bold inline-flex items-center gap-0.5">
                    <Edit3 class="w-3.5 h-3.5" />
                    编辑
                  </button>
                  <button @click="openResetPwModal(u)"
                    class="text-[#722ed1] dark:text-violet-400 hover:text-violet-600 dark:hover:text-violet-300 cursor-pointer font-bold inline-flex items-center gap-0.5 ml-1">
                    <Lock class="w-3.5 h-3.5" />
                    重置密码
                  </button>
                  <button v-if="u.username !== 'admin'" @click="handleDeleteUser(u.id, u.username)"
                    class="text-rose-500 hover:text-rose-700 dark:hover:text-rose-400 cursor-pointer font-bold inline-flex items-center gap-0.5 ml-1">
                    <Trash2 class="w-3.5 h-3.5" />
                    删除
                  </button>
                  <span v-else
                    class="text-slate-350 dark:text-slate-500 cursor-not-allowed inline-flex items-center gap-0.5 text-[10px]"
                    title="系统默认管理员不可删除">
                    <Lock class="w-3 h-3 text-slate-300 dark:text-slate-600" />
                    系统保护
                  </span>
                </div>
              </td>
            </tr>

            <tr v-if="filteredUsers.length === 0">
              <td colspan="6" class="p-10 text-center text-slate-400 dark:text-slate-500 font-sans">
                暂无匹配的用户数据
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- 移动端双模视图 (方案一卡片 / 方案二紧凑) -->
      <div class="block md:hidden">
        <div v-if="filteredUsers.length === 0"
          class="p-10 text-center text-slate-400 dark:text-slate-500 font-sans text-xs">
          暂无匹配的用户数据
        </div>

        <!-- 方案一：完整卡片视图 (Card Mode) -->
        <div v-else-if="mobileViewMode === 'card'" class="p-3 space-y-3">
          <div v-for="u in filteredUsers" :key="'m-user-card-' + u.id"
            class="bg-slate-50/70 dark:bg-slate-800/50 rounded-xl p-3.5 border border-slate-200/80 dark:border-slate-700/80 space-y-2.5 transition-colors shadow-xs">
            <!-- 头部：头像 + 用户名 + ROOT + 状态 -->
            <div class="flex items-start justify-between gap-2">
              <div class="flex items-center gap-2.5 min-w-0 flex-1">
                <div
                  class="w-9 h-9 rounded-xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 flex items-center justify-center shrink-0 shadow-xs">
                  <UserCheck class="w-4 h-4 text-indigo-500 dark:text-indigo-400" />
                </div>
                <div class="min-w-0">
                  <div class="flex items-center gap-1.5 flex-wrap">
                    <span class="font-extrabold text-sm text-slate-800 dark:text-white truncate font-sans">{{ u.username
                      }}</span>
                    <span v-if="u.username === 'admin'"
                      class="text-[9px] font-bold font-mono bg-indigo-50 dark:bg-indigo-950/60 text-indigo-600 dark:text-indigo-400 border border-indigo-200 dark:border-indigo-800/60 px-1 py-0.2 rounded uppercase scale-95">
                      ROOT
                    </span>
                  </div>
                  <p class="text-[10px] text-slate-400 dark:text-slate-500 font-mono">UID #{{ u.id }} · {{
                    formatCreatedAt(u.createdAt) }}</p>
                </div>
              </div>
              <div class="flex items-center gap-1.5 shrink-0">
                <span class="font-sans font-bold text-[10px] inline-flex items-center gap-1 px-2 py-0.5 rounded-full"
                  :class="u.status === 'Active' ? 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400 border border-emerald-200 dark:border-emerald-800' : 'bg-slate-100 dark:bg-slate-800 text-slate-400 dark:text-slate-500 border border-slate-200 dark:border-slate-700'">
                  <span class="w-1.5 h-1.5 rounded-full"
                    :class="u.status === 'Active' ? 'bg-emerald-500 animate-pulse' : 'bg-slate-300 dark:bg-slate-600'" />
                  {{ u.status === 'Active' ? '已启用' : '已停用' }}
                </span>
              </div>
            </div>

            <!-- 角色权限与属性标签 -->
            <div class="flex flex-wrap items-center gap-1.5 text-[11px]">
              <span v-if="u.role === ROLE_ADMIN"
                class="px-2 py-0.5 rounded-full text-[10px] font-bold bg-amber-50 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400 border border-amber-100 dark:border-amber-900/50">
                🔐 管理员权限
              </span>
              <span v-else-if="u.role === ROLE_OPERATOR"
                class="px-2 py-0.5 rounded-full text-[10px] font-bold bg-blue-50 dark:bg-blue-950/60 text-blue-600 dark:text-blue-400 border border-blue-100 dark:border-blue-900/50">
                ⚡ 操作员权限
              </span>
              <span v-else
                class="px-2 py-0.5 rounded-full text-[10px] font-bold bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border border-slate-200 dark:border-slate-700">
                👁️ 观察员只读
              </span>
            </div>

            <!-- 底部操作条 -->
            <div
              class="pt-2 border-t border-slate-200/60 dark:border-slate-800/80 flex items-center justify-between text-xs">
              <button @click="openUserDetail(u)"
                class="inline-flex items-center gap-1 text-[11px] font-bold text-slate-500 dark:text-slate-400 hover:text-indigo-600 dark:hover:text-indigo-400 cursor-pointer">
                <Eye class="w-3.5 h-3.5" />
                查看详情
              </button>
              <div class="flex items-center gap-1.5">
                <button @click="openEditUserModal(u)"
                  class="px-2 py-1 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-[#1890ff] dark:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-950/40 font-bold text-[11px] inline-flex items-center gap-1 cursor-pointer shadow-xs">
                  <Edit3 class="w-3 h-3" />
                  编辑
                </button>
                <button @click="openResetPwModal(u)"
                  class="px-2 py-1 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-[#722ed1] dark:text-violet-400 hover:bg-violet-50 dark:hover:bg-violet-950/40 font-bold text-[11px] inline-flex items-center gap-1 cursor-pointer shadow-xs">
                  <Lock class="w-3 h-3" />
                  重置密码
                </button>
                <button v-if="u.username !== 'admin'" @click="handleDeleteUser(u.id, u.username)"
                  class="px-2 py-1 rounded-lg border border-rose-200 dark:border-rose-900 bg-rose-50 dark:bg-rose-950/40 text-rose-600 dark:text-rose-400 font-bold text-[11px] inline-flex items-center gap-1 cursor-pointer"
                  title="注销删除用户">
                  <Trash2 class="w-3 h-3" />
                </button>
                <span v-else
                  class="text-[10px] text-slate-400 dark:text-slate-500 px-1 py-0.5 inline-flex items-center gap-0.5"
                  title="主系统超级管理员受内置保护">
                  <Lock class="w-3 h-3 text-slate-400" />
                  保护
                </span>
              </div>
            </div>
          </div>
        </div>

        <!-- 方案二：高密度紧凑列表 (Compact Mode) -->
        <div v-else-if="mobileViewMode === 'compact'" class="divide-y divide-slate-100 dark:divide-slate-800">
          <div v-for="u in filteredUsers" :key="'m-user-compact-' + u.id" @click="openUserDetail(u)"
            class="px-3 py-2.5 flex items-center gap-2 hover:bg-slate-50 dark:hover:bg-slate-800/40 active:bg-slate-100 dark:active:bg-slate-800 transition-colors cursor-pointer">
            <!-- 状态小圆点 -->
            <span class="w-2 h-2 rounded-full shrink-0"
              :class="u.status === 'Active' ? 'bg-emerald-500 animate-pulse' : 'bg-slate-300 dark:bg-slate-600'" />

            <!-- 角色徽标 -->
            <span v-if="u.role === ROLE_ADMIN"
              class="px-1.5 py-0.2 rounded text-[9px] font-bold bg-amber-50 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400 border border-amber-200 dark:border-amber-900/50 shrink-0">
              管
            </span>
            <span v-else-if="u.role === ROLE_OPERATOR"
              class="px-1.5 py-0.2 rounded text-[9px] font-bold bg-blue-50 dark:bg-blue-950/60 text-blue-600 dark:text-blue-400 border border-blue-200 dark:border-blue-900/50 shrink-0">
              操
            </span>
            <span v-else
              class="px-1.5 py-0.2 rounded text-[9px] font-bold bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border border-slate-200 dark:border-slate-700 shrink-0">
              观
            </span>

            <div class="flex-1 min-w-0">
              <div class="flex items-center gap-1">
                <span class="font-bold text-xs text-slate-800 dark:text-white truncate font-sans">{{ u.username
                  }}</span>
                <span v-if="u.username === 'admin'"
                  class="text-[8px] font-bold font-mono text-indigo-500 dark:text-indigo-400 border border-indigo-200 dark:border-indigo-800 px-0.5 rounded uppercase">
                  ROOT
                </span>
              </div>
              <p class="text-[10px] text-slate-400 dark:text-slate-500 truncate font-mono mt-0.5">
                #{{ u.id }} · {{ formatCreatedAt(u.createdAt) }}
              </p>
            </div>

            <div class="shrink-0 flex items-center gap-1">
              <span class="text-[10px] font-bold"
                :class="u.status === 'Active' ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'">
                {{ u.status === 'Active' ? '启用' : '停用' }}
              </span>
              <ChevronRight class="w-3.5 h-3.5 text-slate-300 dark:text-slate-600" />
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- MODAL: DEFINE NEW SYSTEM ACCOUNT / PRIVILEGES -->
    <div v-if="showModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div
        class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">

        <div
          class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest text-emerald-400">
            <ShieldCheck class="w-4 h-4" />
            <span>{{ isEditing ? '编辑用户' : '新建用户' }}</span>
          </div>
          <button @click="showModal = false" class="text-slate-400 hover:text-white cursor-pointer">
            <X class="w-4 h-4" />
          </button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">用户名</label>
            <input v-model="uName" type="text" placeholder="请输入用户名"
              :disabled="isEditing && editingOriginalName === 'admin'"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-slate-800 dark:focus:border-sky-500 disabled:bg-slate-100 dark:disabled:bg-slate-800 disabled:cursor-not-allowed" />
          </div>

          <div v-if="!isEditing">
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">密码</label>
            <input v-model="uPassword" type="password" placeholder="请输入密码"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-slate-800 dark:focus:border-sky-500" />
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">角色</label>
            <select v-model="uRole" :disabled="isEditing && editingOriginalName === 'admin'"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-slate-800 dark:focus:border-sky-500 font-sans font-semibold disabled:bg-slate-100 dark:disabled:bg-slate-800 disabled:cursor-not-allowed">
              <option value="Admin">管理员 - 设备控制与配置权限</option>
              <option value="Operator">操作员 - 设备操作权限</option>
              <option value="Viewer">观察员 - 只读权限</option>
            </select>
          </div>

          <div v-if="editingOriginalName !== 'admin'">
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">状态</label>
            <div class="flex items-center gap-4 py-1">
              <label
                class="flex items-center gap-1.5 font-mono font-bold text-slate-700 dark:text-slate-300 cursor-pointer text-xs">
                <input type="radio" value="Active" v-model="uStatus"
                  class="text-slate-800 dark:text-sky-500 focus:ring-0" />
                启用
              </label>
              <label
                class="flex items-center gap-1.5 font-mono font-bold text-slate-400 dark:text-slate-500 cursor-pointer text-xs">
                <input type="radio" value="Inactive" v-model="uStatus" class="text-rose-500 focus:ring-0" />
                停用
              </label>
            </div>
          </div>
        </div>

        <div
          class="bg-slate-50 dark:bg-slate-950 p-4 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800 shrink-0">
          <button @click="showModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer">
            取消
          </button>
          <button @click="handleSaveUser"
            class="px-4 py-1.5 bg-slate-900 dark:bg-sky-600 border border-slate-900 dark:border-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white rounded-lg cursor-pointer">
            保存
          </button>
        </div>

      </div>
    </div>

    <!-- MODAL: RESET USER PASSWORD -->
    <div v-if="showResetPwModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div
        class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">

        <div
          class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest text-violet-400">
            <Lock class="w-4 h-4" />
            <span>重置密码</span>
          </div>
          <button @click="showResetPwModal = false" class="text-slate-400 hover:text-white cursor-pointer">
            <X class="w-4 h-4" />
          </button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <p class="text-slate-500 dark:text-slate-400 font-bold">
            为用户 <span class="text-slate-800 dark:text-white font-mono">{{ resetPwName }}</span> 设置新密码
          </p>
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">新密码</label>
            <input v-model="resetPwPass" type="password" placeholder="至少 8 位，包含字母和数字"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-slate-800 dark:focus:border-violet-500" />
          </div>
        </div>

        <div
          class="bg-slate-50 dark:bg-slate-950 p-4 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800 shrink-0">
          <button @click="showResetPwModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer">
            取消
          </button>
          <button @click="handleResetPassword"
            class="px-4 py-1.5 bg-slate-900 dark:bg-violet-600 border border-slate-900 dark:border-violet-600 hover:bg-slate-800 dark:hover:bg-violet-500 font-bold text-xs text-white rounded-lg cursor-pointer">
            确认重置
          </button>
        </div>

      </div>
    </div>

    <!-- 移动端用户详情抽屉 (Bottom Sheet) -->
    <div v-if="selectedUserDetail"
      class="fixed inset-0 z-50 flex flex-col justify-end bg-black/50 backdrop-blur-xs md:hidden"
      @click.self="closeUserDetail">
      <div
        class="bg-white dark:bg-slate-900 rounded-t-2xl max-h-[85vh] flex flex-col shadow-2xl border-t border-slate-200 dark:border-slate-800 animate-in slide-in-from-bottom duration-200 text-left">
        <!-- 顶部拖拽把手 -->
        <div class="w-10 h-1 bg-slate-300 dark:bg-slate-700 rounded-full mx-auto mt-2.5 shrink-0" />

        <!-- 抽屉头部 -->
        <div class="px-4 py-3 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
          <div class="flex items-center gap-2 min-w-0">
            <div
              class="w-8 h-8 rounded-lg bg-indigo-50 dark:bg-indigo-950/60 border border-indigo-200 dark:border-indigo-800 flex items-center justify-center shrink-0">
              <UserCheck class="w-4 h-4 text-indigo-600 dark:text-indigo-400" />
            </div>
            <div class="min-w-0">
              <div class="flex items-center gap-1.5">
                <h3 class="font-bold text-sm text-slate-900 dark:text-white truncate">{{ selectedUserDetail.username }}
                </h3>
                <span v-if="selectedUserDetail.username === 'admin'"
                  class="text-[9px] font-bold font-mono bg-indigo-50 dark:bg-indigo-950/60 text-indigo-600 dark:text-indigo-400 border border-indigo-200 dark:border-indigo-800/60 px-1 py-0.2 rounded uppercase">
                  ROOT
                </span>
              </div>
              <p class="font-mono text-[10px] text-slate-400 dark:text-slate-500 truncate">用户账号详情</p>
            </div>
          </div>
          <button @click="closeUserDetail"
            class="p-1.5 rounded-lg text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer shrink-0">
            <X class="w-4 h-4" />
          </button>
        </div>

        <!-- 抽屉内容 -->
        <div class="p-4 overflow-y-auto space-y-4 text-xs">
          <!-- 核心元数据网格 -->
          <div
            class="grid grid-cols-2 gap-2.5 bg-slate-50 dark:bg-slate-950/60 p-3 rounded-xl border border-slate-200/80 dark:border-slate-800">
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">用户 ID</span>
              <span class="font-mono font-bold text-slate-800 dark:text-slate-200">#{{ selectedUserDetail.id }}</span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">登录用户名</span>
              <span class="font-mono font-bold text-slate-800 dark:text-slate-200 truncate block">{{
                selectedUserDetail.username }}</span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">角色权限</span>
              <span class="font-bold text-slate-800 dark:text-slate-200">
                {{ ROLE_LABELS[selectedUserDetail.role] || selectedUserDetail.role }}
              </span>
            </div>
            <div>
              <span class="text-[10px] text-slate-400 block mb-0.5">账号状态</span>
              <span class="font-bold"
                :class="selectedUserDetail.status === 'Active' ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'">
                {{ selectedUserDetail.status === 'Active' ? '● 正常使用中' : '○ 已停用' }}
              </span>
            </div>
            <div class="col-span-2">
              <span class="text-[10px] text-slate-400 block mb-0.5">账号创建时间</span>
              <span class="font-mono text-slate-700 dark:text-slate-300">
                {{ formatCreatedAt(selectedUserDetail.createdAt) }}
              </span>
            </div>
            <div class="col-span-2">
              <span class="text-[10px] text-slate-400 block mb-0.5">安全策略</span>
              <span class="text-slate-700 dark:text-slate-300 leading-relaxed block">
                {{ selectedUserDetail.username === 'admin' ? '系统核心主管理员根账号，受系统底层保护，禁止注销与删除。' :
                '标准中控人员账号，权限受管理员分配与随时注销约束。' }}
              </span>
            </div>
          </div>

          <!-- 角色权限说明卡片 -->
          <div
            class="bg-indigo-50/50 dark:bg-indigo-950/30 p-3 rounded-xl border border-indigo-100 dark:border-indigo-900/50 space-y-1 text-slate-700 dark:text-slate-300">
            <span class="text-[11px] font-bold text-indigo-900 dark:text-indigo-300 block">权限等级与职责：</span>
            <p v-if="selectedUserDetail.role === 'Admin'"
              class="text-[11px] text-slate-600 dark:text-slate-400 leading-relaxed">
              管理员：具备系统最高权限，可进行设备控制、点位写入、用户管理、全局报警规则配置与系统参数维护。
            </p>
            <p v-else-if="selectedUserDetail.role === 'Operator'"
              class="text-[11px] text-slate-600 dark:text-slate-400 leading-relaxed">
              操作员：具备日常生产监控、设备启停与设定值变更权限，可在现场和中控台确认生产报警。
            </p>
            <p v-else class="text-[11px] text-slate-600 dark:text-slate-400 leading-relaxed">
              观察员：只读模式，仅支持查看组态图元、实时监控大屏和历史日志，无法下发控制命令。
            </p>
          </div>
        </div>

        <!-- 底部操作条 -->
        <div
          class="p-3 border-t border-slate-100 dark:border-slate-800 bg-slate-50 dark:bg-slate-950/80 flex items-center gap-2">
          <button @click="copyUserDetail(selectedUserDetail)"
            class="flex-1 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 font-bold text-xs text-slate-700 dark:text-slate-200 inline-flex items-center justify-center gap-1.5 cursor-pointer shadow-xs">
            <Copy class="w-3.5 h-3.5" />
            复制信息
          </button>
          <button @click="openEditUserModal(selectedUserDetail); closeUserDetail()"
            class="flex-1 py-2 rounded-lg bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white inline-flex items-center justify-center gap-1.5 cursor-pointer shadow-xs">
            <Edit3 class="w-3.5 h-3.5" />
            编辑
          </button>
          <button @click="openResetPwModal(selectedUserDetail); closeUserDetail()"
            class="flex-1 py-2 rounded-lg bg-[#722ed1] hover:bg-violet-600 font-bold text-xs text-white inline-flex items-center justify-center gap-1.5 cursor-pointer shadow-xs">
            <Lock class="w-3.5 h-3.5" />
            重置密码
          </button>
          <button v-if="selectedUserDetail.username !== 'admin'"
            @click="handleDeleteUser(selectedUserDetail.id, selectedUserDetail.username); closeUserDetail()"
            class="py-2 px-3 rounded-lg border border-rose-200 dark:border-rose-900 bg-rose-50 dark:bg-rose-950/40 font-bold text-xs text-rose-600 dark:text-rose-400 inline-flex items-center justify-center gap-1 cursor-pointer"
            title="注销删除用户">
            <Trash2 class="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
