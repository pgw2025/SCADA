<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { 
  systemUsers, 
  addLog, 
  loadSystemUsers, 
  createSystemUser, 
  updateSystemUser, 
  deleteSystemUser 
} from '../store/index';
import { SystemUser } from '../types';
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
  UserCheck
} from 'lucide-vue-next';

const showModal = ref(false);
const isEditing = ref(false);
const editingUserId = ref<number | null>(null);
// 编辑时记录原始用户名，用于判定内置 admin 锁定保护（不能依赖可编辑的 uName 实时值）
const editingOriginalName = ref('');

// Form Fields
const uName = ref('');
const uRole = ref<string>('Operator');
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
  uRole.value = 'Operator';
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
</script>

<template>
  <div class="h-full overflow-y-auto space-y-6 text-[#1e293b] dark:text-slate-100 select-none p-4 sm:p-6 bg-slate-50/50 dark:bg-transparent">
    
    <!-- Top headers -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between border-b border-slate-200 dark:border-slate-800 pb-5 gap-4 text-left transition-colors">
      <div>
        <h1 class="text-xl font-bold font-sans text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Users class="w-5 h-5 text-indigo-500 dark:text-indigo-400" />
          <span>用户权限管理</span>
        </h1>
        <p class="text-xs text-slate-500 dark:text-slate-400 mt-1">
          管理系统用户账户，分配角色权限。支持超级管理员、管理员、操作员、观察员四种角色。
        </p>
      </div>

      <button 
        @click="openNewUserModal"
        class="bg-slate-900 dark:bg-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white px-3.5 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 shadow-sm"
      >
        <UserPlus class="w-4 h-4 text-sky-400 dark:text-white" />
        新建用户
      </button>
    </div>

    <!-- Status Stats row & searching -->
    <div class="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 bg-white dark:bg-slate-900 p-4 rounded-xl border border-slate-200 dark:border-slate-800 shadow-sm text-left select-none transition-colors">
      <div class="flex items-center gap-5 text-xs font-sans font-semibold text-slate-500 dark:text-slate-400 shrink-0">
        <span class="inline-flex items-center gap-1">
          用户总数: <b class="text-indigo-600 dark:text-indigo-400 text-sm font-mono">{{ systemUsers.length }}</b>
        </span>
        <span class="inline-flex items-center gap-1 border-l border-slate-200 dark:border-slate-800 pl-4">
          已启用: <b class="text-emerald-600 dark:text-emerald-400 text-sm font-mono">{{ systemUsers.filter(u => u.status === 'Active').length }}</b>
        </span>
      </div>

      <!-- Live query search bar -->
      <div class="relative w-full sm:w-64">
        <input 
          v-model="filterQuery"
          type="text"
          placeholder="搜索用户名或角色..."
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg py-1.5 pl-8 pr-3 text-xs placeholder-slate-400 dark:placeholder-slate-500 text-slate-900 dark:text-slate-100 focus:bg-white dark:focus:bg-slate-900 focus:outline-none focus:border-slate-800 dark:focus:border-sky-500"
        />
        <Search class="absolute left-2.5 top-2.5 w-3.5 h-3.5 text-slate-400 dark:text-slate-500" />
        <button 
          v-if="filterQuery" 
          @click="filterQuery = ''" 
          class="absolute right-2 top-2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-300 focus:outline-none"
        >
          <X class="w-3.5 h-3.5" />
        </button>
      </div>
    </div>

    <!-- Users administration table layout -->
    <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm text-left transition-colors">
      <table class="w-full text-xs">
        <thead>
          <tr class="bg-slate-50 dark:bg-slate-950 text-[10px] text-slate-400 dark:text-slate-500 uppercase font-bold tracking-wider divide-x divide-slate-100 dark:divide-slate-800">
            <th class="px-6 py-4">ID</th>
            <th class="px-6 py-4">用户名</th>
            <th class="px-6 py-4">角色</th>
            <th class="px-6 py-4">创建时间</th>
            <th class="px-6 py-4">状态</th>
            <th class="px-6 py-4 text-right">操作</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100 dark:divide-slate-800 font-mono">
          <tr 
            v-for="u in filteredUsers" 
            :key="u.id"
            class="hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-colors"
          >
            <!-- User ID -->
            <td class="px-6 py-4 font-bold text-slate-500 dark:text-slate-400">{{ u.id }}</td>
            
            <!-- User name username with icon -->
            <td class="px-6 py-4 font-sans font-extrabold text-[13px] text-slate-800 dark:text-slate-100">
              <span class="flex items-center gap-1.5">
                <UserCheck class="w-4 h-4 text-slate-400 dark:text-slate-500" />
                <span>{{ u.username }}</span>
                <span v-if="u.username === 'admin'" class="text-[9px] font-bold font-mono bg-indigo-50 dark:bg-indigo-950/60 text-indigo-600 dark:text-indigo-400 border border-indigo-200 dark:border-indigo-800/60 px-1 py-0.5 rounded uppercase font-normal scale-90">
                  ROOT
                </span>
              </span>
            </td>

            <!-- Role security category with specific visual badges -->
            <td class="px-6 py-4 font-sans font-bold">
              <span 
                v-if="u.role === 'Admin'"
                class="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-amber-50 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400 border border-amber-100 dark:border-amber-900/50 uppercase"
              >
                🔐 管理员
              </span>
              <span 
                v-else-if="u.role === 'Operator'"
                class="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-blue-50 dark:bg-blue-950/60 text-blue-600 dark:text-blue-400 border border-blue-100 dark:border-blue-900/50 uppercase"
              >
                ⚡ 操作员
              </span>
              <span 
                v-else
                class="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border border-slate-200 dark:border-slate-700 uppercase"
              >
                👁️ 观察员
              </span>
            </td>

            <!-- Created At -->
            <td class="px-6 py-4 text-slate-400 dark:text-slate-500 text-[11px] font-bold leading-none">--</td>

            <!-- System status toggles -->
            <td class="px-6 py-4 text-left">
              <span 
                class="font-sans font-bold text-[10px] inline-flex items-center gap-1 px-2 py-0.5 rounded-full"
                :class="u.status === 'Active' ? 'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400' : 'bg-slate-50 dark:bg-slate-950 text-slate-400 dark:text-slate-500'"
              >
                <span class="w-1.5 h-1.5 rounded-full" :class="u.status === 'Active' ? 'bg-emerald-500 animate-pulse' : 'bg-slate-300 dark:bg-slate-600'" />
                {{ u.status === 'Active' ? '已启用' : '已停用' }}
              </span>
            </td>

            <!-- Actions columns -->
            <td class="px-6 py-4 text-right">
              <div class="flex items-center justify-end gap-3 select-none text-[11px] font-sans">
                <button 
                  @click="openEditUserModal(u)"
                  class="text-[#1890ff] dark:text-sky-400 hover:text-sky-600 dark:hover:text-sky-300 cursor-pointer font-bold inline-flex items-center gap-0.5"
                >
                  <Edit3 class="w-3.5 h-3.5" />
                  编辑
                </button>
                <button 
                  v-if="u.username !== 'admin'"
                  @click="handleDeleteUser(u.id, u.username)"
                  class="text-rose-500 hover:text-rose-700 dark:hover:text-rose-400 cursor-pointer font-bold inline-flex items-center gap-0.5 ml-1"
                >
                  <Trash2 class="w-3.5 h-3.5" />
                  删除
                </button>
                <span v-else class="text-slate-350 dark:text-slate-500 cursor-not-allowed inline-flex items-center gap-0.5 text-[10px]" title="系统默认管理员不可删除">
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

    <!-- MODAL: DEFINE NEW SYSTEM ACCOUNT / PRIVILEGES -->
    <div v-if="showModal" class="fixed inset-0 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest text-emerald-400">
            <ShieldCheck class="w-4 h-4" />
            <span>{{ isEditing ? '编辑用户' : '新建用户' }}</span>
          </div>
          <button @click="showModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">用户名</label>
            <input 
              v-model="uName"
              type="text"
              placeholder="请输入用户名"
              :disabled="isEditing && editingOriginalName === 'admin'"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-slate-800 dark:focus:border-sky-500 disabled:bg-slate-100 dark:disabled:bg-slate-800 disabled:cursor-not-allowed"
            />
          </div>

          <div v-if="!isEditing">
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">密码</label>
            <input 
              v-model="uPassword"
              type="password"
              placeholder="请输入密码"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 font-mono focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-slate-800 dark:focus:border-sky-500"
            />
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">角色</label>
            <select 
              v-model="uRole"
              :disabled="isEditing && editingOriginalName === 'admin'"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-slate-800 dark:focus:border-sky-500 font-sans font-semibold disabled:bg-slate-100 dark:disabled:bg-slate-800 disabled:cursor-not-allowed"
            >
              <option value="Admin">管理员 - 设备控制与配置权限</option>
              <option value="Operator">操作员 - 设备操作权限</option>
              <option value="Viewer">观察员 - 只读权限</option>
            </select>
          </div>

          <div v-if="editingOriginalName !== 'admin'">
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">状态</label>
            <div class="flex items-center gap-4 py-1">
              <label class="flex items-center gap-1.5 font-mono font-bold text-slate-700 dark:text-slate-300 cursor-pointer text-xs">
                <input type="radio" value="Active" v-model="uStatus" class="text-slate-800 dark:text-sky-500 focus:ring-0" />
                启用
              </label>
              <label class="flex items-center gap-1.5 font-mono font-bold text-slate-400 dark:text-slate-500 cursor-pointer text-xs">
                <input type="radio" value="Inactive" v-model="uStatus" class="text-rose-500 focus:ring-0" />
                停用
              </label>
            </div>
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-4 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800 shrink-0">
          <button 
            @click="showModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleSaveUser"
            class="px-4 py-1.5 bg-slate-900 dark:bg-sky-600 border border-slate-900 dark:border-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white rounded-lg cursor-pointer"
          >
            保存
          </button>
        </div>

      </div>
    </div>

  </div>
</template>
