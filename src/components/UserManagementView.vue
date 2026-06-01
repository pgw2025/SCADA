<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { 
  systemUsers, 
  addLog, 
  loadSystemUsers, 
  createSystemUser, 
  updateSystemUser, 
  deleteSystemUser 
} from '../store';
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

// Form Fields
const uName = ref('');
const uRole = ref<string>('操作员');
const uStatus = ref<string>('active');
const uPassword = ref('');

const filterQuery = ref('');

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
  return systemUsers.value.filter(u => u.username.toLowerCase().includes(query) || u.role.toLowerCase().includes(query));
});

const openNewUserModal = () => {
  isEditing.value = false;
  editingUserId.value = null;
  uName.value = '';
  uRole.value = '操作员';
  uStatus.value = 'active';
  uPassword.value = '';
  showModal.value = true;
};

const openEditUserModal = (user: SystemUser) => {
  isEditing.value = true;
  editingUserId.value = user.id;
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
  } catch (error: any) {
    alert('保存用户失败: ' + (error.response?.data?.message || error.message));
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
    } catch (error: any) {
      alert('删除用户失败: ' + (error.response?.data?.message || error.message));
    }
  }
};
</script>

<template>
  <div class="h-full overflow-y-auto space-y-6 text-[#1e293b] select-none p-4 sm:p-6 bg-slate-50/50">
    
    <!-- Top headers -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between border-b border-slate-200 pb-5 gap-4 text-left">
      <div>
        <h1 class="text-xl font-bold font-sans text-slate-900 tracking-tight flex items-center gap-2">
          <Users class="w-5 h-5 text-indigo-500" />
          <span>全车间中控操作员与安全级别管理</span>
        </h1>
        <p class="text-xs text-slate-500 mt-1">
          划分员工权限范围。级别包含：超级管理员（全面写入写配置）、管理员（常规控制）、操作员（常规调试控制）与观察员（只读防触碰）。
        </p>
      </div>

      <button 
        @click="openNewUserModal"
        class="bg-slate-900 hover:bg-slate-800 font-bold text-xs text-white px-3.5 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 shadow-sm"
      >
        <UserPlus class="w-4 h-4 text-sky-400" />
        新建立中控操作席帐户
      </button>
    </div>

    <!-- Status Stats row & searching -->
    <div class="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 bg-white p-4 rounded-xl border border-slate-200 shadow-sm text-left select-none">
      <div class="flex items-center gap-5 text-xs font-sans font-semibold text-slate-500 shrink-0">
        <span class="inline-flex items-center gap-1">
          已开设席位: <b class="text-indigo-600 text-sm font-mono">{{ systemUsers.length }}</b> 个
        </span>
        <span class="inline-flex items-center gap-1 border-l border-slate-200 pl-4">
          安全运行正常: <b class="text-emerald-600 text-sm font-mono">{{ systemUsers.filter(u => u.status==='active').length }}</b> 席
        </span>
        <span class="inline-flex items-center gap-1 border-l border-slate-200 pl-4 text-slate-400 font-normal">
          主防爆防护罩等级: 4级
        </span>
      </div>

      <!-- Live query search bar -->
      <div class="relative w-full sm:w-64">
        <input 
          v-model="filterQuery"
          type="text"
          placeholder="检索系统用户名或级别权限..."
          class="w-full bg-slate-50 border border-slate-200 rounded-lg py-1.5 pl-8 pr-3 text-xs placeholder-slate-400 focus:bg-white focus:outline-none focus:border-slate-800"
        />
        <Search class="absolute left-2.5 top-2.5 w-3.5 h-3.5 text-slate-400" />
        <button 
          v-if="filterQuery" 
          @click="filterQuery = ''" 
          class="absolute right-2 top-2 text-slate-400 hover:text-slate-600 focus:outline-none"
        >
          <X class="w-3.5 h-3.5" />
        </button>
      </div>
    </div>

    <!-- Users administration table layout -->
    <div class="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm text-left">
      <table class="w-full text-xs">
        <thead>
          <tr class="bg-slate-50 text-[10px] text-slate-400 uppercase font-bold tracking-wider divide-x divide-slate-100">
            <th class="px-6 py-4">用户代号 (ID)</th>
            <th class="px-6 py-4">用户登录名 / Operator Username</th>
            <th class="px-6 py-4">隶属安全授权等级</th>
            <th class="px-6 py-4">创建登入时间</th>
            <th class="px-6 py-4">电气防抱状态</th>
            <th class="px-6 py-4 text-right">管理操作</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100 font-mono">
          <tr 
            v-for="u in filteredUsers" 
            :key="u.id"
            class="hover:bg-slate-50/50 transition-colors"
          >
            <!-- User ID -->
            <td class="px-6 py-4 font-bold text-slate-500">{{ u.id }}</td>
            
            <!-- User name username with icon -->
            <td class="px-6 py-4 font-sans font-extrabold text-[13px] text-slate-800">
              <span class="flex items-center gap-1.5">
                <UserCheck class="w-4 h-4 text-slate-400" />
                <span>{{ u.username }}</span>
                <span v-if="u.username === 'admin'" class="text-[9px] font-bold font-mono bg-indigo-50 text-indigo-600 px-1 py-0.5 rounded uppercase font-normal scale-90">
                  ROOT
                </span>
              </span>
            </td>

            <!-- Role security category with specific visual badges -->
            <td class="px-6 py-4 font-sans font-bold">
              <span 
                v-if="u.role === '超级管理员'"
                class="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-rose-50 text-rose-600 border border-rose-100 uppercase"
              >
                🛡️ 超级管理员
              </span>
              <span 
                v-else-if="u.role === '管理员'"
                class="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-amber-50 text-amber-600 border border-amber-100 uppercase"
              >
                🔐 管理员
              </span>
              <span 
                v-else-if="u.role === '操作员'"
                class="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-blue-50 text-blue-600 border border-blue-100 uppercase"
              >
                ⚡ 操作员
              </span>
              <span 
                v-else
                class="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-slate-100 text-slate-500 border border-slate-200 uppercase"
              >
                👁️ 观察员
              </span>
            </td>

            <!-- Created At -->
            <td class="px-6 py-4 text-slate-400 text-[11px] font-bold leading-none">--</td>

            <!-- System status toggles -->
            <td class="px-6 py-4 text-left">
              <span 
                class="font-sans font-bold text-[10px] inline-flex items-center gap-1 px-2 py-0.5 rounded-full"
                :class="u.status === 'active' ? 'bg-emerald-50 text-emerald-600' : 'bg-slate-50 text-slate-400'"
              >
                <span class="w-1.5 h-1.5 rounded-full" :class="u.status === 'active' ? 'bg-emerald-500 animate-pulse' : 'bg-slate-300'" />
                {{ u.status === 'active' ? '授权激活中' : '静默锁卡中' }}
              </span>
            </td>

            <!-- Actions columns -->
            <td class="px-6 py-4 text-right">
              <div class="flex items-center justify-end gap-3 select-none text-[11px] font-sans">
                <button 
                  @click="openEditUserModal(u)"
                  class="text-[#1890ff] hover:text-sky-600 cursor-pointer font-bold inline-flex items-center gap-0.5"
                >
                  <Edit3 class="w-3.5 h-3.5" />
                  修改授权
                </button>
                <button 
                  v-if="u.username !== 'admin'"
                  @click="handleDeleteUser(u.id, u.username)"
                  class="text-rose-500 hover:text-rose-700 cursor-pointer font-bold inline-flex items-center gap-0.5 ml-1"
                >
                  <Trash2 class="w-3.5 h-3.5" />
                  回收权限
                </button>
                <span v-else class="text-slate-350 cursor-not-allowed inline-flex items-center gap-0.5 text-[10px]" title="主管理芯片受硬件防爆箱物理保护">
                  <Lock class="w-3 h-3 text-slate-300" />
                  防爆保护
                </span>
              </div>
            </td>
          </tr>

          <tr v-if="filteredUsers.length === 0">
            <td colspan="6" class="p-10 text-center text-slate-400 font-sans">
              没有发现该账户名或权限等级的活跃席位。
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- MODAL: DEFINE NEW SYSTEM ACCOUNT / PRIVILEGES -->
    <div v-if="showModal" class="fixed inset-0 bg-slate-900/40 backdrop-blur-xs flex items-center justify-center z-50 p-4">
      <div class="bg-white rounded-xl shadow-xl border border-slate-100 max-w-sm w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        
        <div class="bg-slate-900 text-white p-4 flex items-center justify-between">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <ShieldCheck class="w-4 h-4 text-emerald-400" />
            <span>{{ isEditing ? '修改中控操作安全特权' : '分配新中控台席位与口令' }}</span>
          </div>
          <button @click="showModal = false" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs">
          <div>
            <label class="text-slate-500 font-bold block mb-1">操作席登入名称 (Operator Username)</label>
            <input 
              v-model="uName"
              type="text"
              placeholder="e.g. engineer_zhou"
              :disabled="isEditing && uName === 'admin'"
              class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 font-mono focus:bg-white text-slate-900 focus:outline-none focus:border-slate-800 disabled:bg-slate-100 disabled:cursor-not-allowed"
            />
          </div>

          <div v-if="!isEditing">
            <label class="text-slate-500 font-bold block mb-1">初始登录密码 (Password)</label>
            <input 
              v-model="uPassword"
              type="password"
              placeholder="请输入初始密码"
              class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 font-mono focus:bg-white text-slate-900 focus:outline-none focus:border-slate-800"
            />
          </div>

          <div>
            <label class="text-slate-500 font-bold block mb-1">角色/安全授权等级级别</label>
            <select 
              v-model="uRole"
              :disabled="isEditing && uName === 'admin'"
              class="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 focus:bg-white text-slate-900 focus:outline-none focus:border-slate-800 font-sans font-semibold disabled:bg-slate-100 disabled:cursor-not-allowed"
            >
              <option value="超级管理员">超级管理员 (ROOT - 硬件写入与系统底层物理配置)</option>
              <option value="管理员">管理员 (MANAGER - 变量改写与自动触发调度)</option>
              <option value="操作员">操作员 (OPERATOR - 常规现场状态改写和工艺设备调度)</option>
              <option value="观察员">观察员 (VIEWER - 纯只读物理大屏监控防误触)</option>
            </select>
          </div>

          <div v-if="uName !== 'admin'">
            <label class="text-slate-500 font-bold block mb-1">授权物理物理状态</label>
            <div class="flex items-center gap-4 py-1">
              <label class="flex items-center gap-1.5 font-mono font-bold text-slate-700 cursor-pointer text-xs">
                <input type="radio" value="active" v-model="uStatus" class="text-slate-800 focus:ring-0" />
                激活并登入 (Active)
              </label>
              <label class="flex items-center gap-1.5 font-mono font-bold text-slate-400 cursor-pointer text-xs">
                <input type="radio" value="inactive" v-model="uStatus" class="text-rose-500 focus:ring-0" />
                锁锁卡停权限 (Inactive)
              </label>
            </div>
          </div>
        </div>

        <div class="bg-slate-50 p-4 flex justify-end gap-2 border-t border-slate-100 shrink-0">
          <button 
            @click="showModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 bg-white hover:bg-slate-50 font-bold text-xs text-slate-600 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleSaveUser"
            class="px-4 py-1.5 bg-slate-900 border border-slate-900 hover:bg-slate-800 font-bold text-xs text-white rounded-lg cursor-pointer"
          >
            保存并部署
          </button>
        </div>

      </div>
    </div>

  </div>
</template>
