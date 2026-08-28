<script setup lang="ts">
import { ref } from 'vue';
import { onMounted } from 'vue';
import {
  Database,
  Plus,
  Trash2,
  RefreshCw,
  HardDrive,
  DatabaseBackup,
  Play,
  Save
} from 'lucide-vue-next';
import { showToast } from '../services/toastService';
import { addLog } from '../store/index';
import {
  DatabaseConfig,
  MainDatabaseConfig,
  TestConnectionRequest,
  TestConnectionResult,
  HistoryMigrationResult,
  DatabaseBackendType
} from '../types';
import {
  fetchDatabaseConfigs,
  createDatabaseConfig,
  updateDatabaseConfig,
  deleteDatabaseConfig,
  fetchMainDatabaseConfig,
  saveMainDatabaseConfig,
  testDatabaseConnection,
  migrateHistoryData
} from '../api/databaseApi';

const loading = ref(false);
const configs = ref<DatabaseConfig[]>([]);
const migration = ref<HistoryMigrationResult | null>(null);
const migrationBusy = ref(false);

const testers = ref<Record<number, { loading: boolean; result?: TestConnectionResult }>>({});

// ---- 主库表单 ----
const mainConfig = ref<MainDatabaseConfig>({
  host: '',
  port: 3306,
  databaseName: '',
  username: '',
  password: null,
  hasPassword: false
});
const mainTested = ref<TestConnectionResult | null>(null);
const mainTesting = ref(false);

// ---- 新增配置表单 ----
const newDialogOpen = ref(false);
const newConfig = ref<DatabaseConfig>({
  id: 0,
  name: '',
  type: 'Historical',
  backendType: 'InfluxDB',
  host: '',
  port: 8086,
  username: '',
  password: null,
  databaseName: '',
  token: null,
  org: '',
  bucket: '',
  isActive: true
});

const backendOptions: DatabaseBackendType[] = ['MySQL', 'PostgreSQL', 'SQLite', 'InfluxDB', 'TimescaleDB'];

const loadConfigs = async () => {
  loading.value = true;
  try {
    const res = await fetchDatabaseConfigs();
    configs.value = res.data ?? [];
  } catch {
    configs.value = [];
  } finally {
    loading.value = false;
  }
};

const loadMain = async () => {
  try {
    const res = await fetchMainDatabaseConfig();
    mainConfig.value = res.data ?? mainConfig.value;
  } catch {
    /* 主库读取失败由拦截器统一提示 */
  }
};

const saveConfig = async (db: DatabaseConfig) => {
  if (!db.name?.trim() || !db.host?.trim() || !db.port || !db.username?.trim()) {
    showToast('请填写 名称/主机/端口/用户名 等必填项', 'warning');
    return;
  }
  try {
    if (db.id > 0) {
      await updateDatabaseConfig({ ...db });
      showToast(`配置 [${db.name}] 已保存`, 'success');
    } else {
      db.type = newConfig.value.type;
      db.backendType = newConfig.value.backendType;
      db.token = newConfig.value.token;
      db.org = newConfig.value.org;
      db.bucket = newConfig.value.bucket;
      await createDatabaseConfig({ ...db });
      newDialogOpen.value = false;
      resetNewConfig();
      showToast('配置已新增', 'success');
    }
    addLog('数据库管理', `保存数据库配置 [${db.name}]`, 'normal');
    await loadConfigs();
  } catch {
    /* 拦截器已提示 */
  }
};

const removeConfig = async (db: DatabaseConfig) => {
  if (!confirm(`确定删除数据库配置 [${db.name}] 吗？`)) return;
  try {
    await deleteDatabaseConfig(db.id);
    addLog('数据库管理', `删除数据库配置 [${db.name}]`, 'normal');
    showToast('配置已删除', 'success');
    await loadConfigs();
  } catch {
    /* 拦截器已提示 */
  }
};

const doTestDb = async (db: DatabaseConfig) => {
  testers.value[db.id] = { loading: true };
  const req: TestConnectionRequest = {
    backendType: db.backendType,
    host: db.host,
    port: db.port,
    username: db.username,
    password: db.password ?? '',
    databaseName: db.databaseName,
    token: db.token,
    org: db.org,
    bucket: db.bucket || db.databaseName
  };
  try {
    const res = await testDatabaseConnection(req);
    testers.value[db.id] = { loading: false, result: res.data };
    addLog('数据库管理', `测试连接 [${db.name}]：${res.data.success ? '成功' : '失败'}`, res.data.success ? 'info' : 'warning');
  } catch {
    testers.value[db.id] = { loading: false, result: { success: false, latencyMs: 0, message: '请求失败' } };
  }
};

const doTestMain = async () => {
  mainTesting.value = true;
  mainTested.value = null;
  const req: TestConnectionRequest = {
    backendType: 'MySQL',
    host: mainConfig.value.host,
    port: mainConfig.value.port,
    username: mainConfig.value.username,
    password: mainConfig.value.password ?? '',
    databaseName: mainConfig.value.databaseName
  };
  try {
    const res = await testDatabaseConnection(req);
    mainTested.value = res.data;
  } catch {
    mainTested.value = { success: false, latencyMs: 0, message: '请求失败' };
  } finally {
    mainTesting.value = false;
  }
};

const doSaveMain = async () => {
  if (!mainConfig.value.host?.trim() || !mainConfig.value.port || !mainConfig.value.username?.trim() || !mainConfig.value.databaseName?.trim()) {
    showToast('主库的 主机/端口/用户名/库名 均不能为空', 'warning');
    return;
  }
  try {
    await saveMainDatabaseConfig({ ...mainConfig.value });
    showToast('主库配置已保存，需要重启服务后生效', 'success');
    addLog('数据库管理', '保存主库(MySQL)连接配置', 'normal');
  } catch {
    /* 拦截器已提示 */
  }
};

const doMigrate = async () => {
  migrationBusy.value = true;
  migration.value = null;
  try {
    const res = await migrateHistoryData();
    migration.value = res.data;
    showToast(res.data.message || (res.data.isRunning ? '迁移任务进行中' : '迁移完成'), res.data.isRunning ? 'warning' : 'success');
  } catch {
    /* 拦截器已提示 */
  } finally {
    migrationBusy.value = false;
  }
};

const resetNewConfig = () => {
  newConfig.value = {
    id: 0, name: '', type: 'Historical', backendType: 'InfluxDB', host: '',
    port: 8086, username: '', password: null, databaseName: '', token: null,
    org: '', bucket: '', isActive: true
  };
};

onMounted(() => {
  loadConfigs();
  loadMain();
});
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent overflow-y-auto">

    <!-- Title banner -->
    <div class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex items-center justify-between gap-4 text-left transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Database class="w-5 h-5 text-indigo-500" />
          数据库管理
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          主库(MySQL)自举配置、时序/历史库连接注册与连接测试，以及存量历史数据迁移。
        </p>
      </div>
      <button @click="newDialogOpen = !newDialogOpen"
        class="px-3 py-1.5 text-xs font-bold text-white bg-indigo-600 hover:bg-indigo-500 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5">
        <Plus class="w-3.5 h-3.5" /> 新增配置
      </button>
    </div>

    <div class="flex-1 p-6 space-y-6 text-left">

      <!-- 新增配置 -->
      <div v-if="newDialogOpen" class="bg-white dark:bg-slate-900 border border-indigo-300 dark:border-indigo-800 rounded-xl p-5 space-y-4 text-xs">
        <h3 class="font-bold text-indigo-600 dark:text-indigo-400">新建数据库配置</h3>
        <div class="grid grid-cols-2 lg:grid-cols-4 gap-3">
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">名称 *</label>
            <input v-model="newConfig.name" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">用途</label>
            <select v-model="newConfig.type" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none font-bold">
              <option value="Historical">历史库</option>
              <option value="Realtime">实时/业务库</option>
            </select></div>
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">后端类型</label>
            <select v-model="newConfig.backendType" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none font-bold">
              <option v-for="b in backendOptions" :key="b" :value="b">{{ b }}</option>
            </select></div>
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">是否生效</label>
            <label class="flex items-center gap-2 mt-1.5 cursor-pointer">
              <input v-model="newConfig.isActive" type="checkbox" class="accent-indigo-600" />
              <span>启用</span>
            </label></div>
        </div>
        <div class="grid grid-cols-2 lg:grid-cols-4 gap-3">
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">主机 Host</label>
            <input v-model="newConfig.host" placeholder="127.0.0.1" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">端口 Port</label>
            <input v-model.number="newConfig.port" type="number" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">用户名</label>
            <input v-model="newConfig.username" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">密码</label>
            <input v-model="newConfig.password" type="password" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">数据库名称</label>
            <input v-model="newConfig.databaseName" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div v-if="newConfig.backendType === 'InfluxDB'"><label class="text-[10px] text-slate-400 font-bold block mb-1">Token</label>
            <input v-model="newConfig.token" type="password" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div v-if="newConfig.backendType === 'InfluxDB'"><label class="text-[10px] text-slate-400 font-bold block mb-1">Org</label>
            <input v-model="newConfig.org" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div v-if="newConfig.backendType === 'InfluxDB'"><label class="text-[10px] text-slate-400 font-bold block mb-1">Bucket</label>
            <input v-model="newConfig.bucket" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
        </div>
        <div class="flex justify-end gap-2">
          <button @click="newDialogOpen = false" class="px-3 py-1.5 border border-slate-200 rounded-lg text-slate-600 dark:text-slate-300 cursor-pointer">取消</button>
          <button @click="saveConfig(newConfig)" class="px-4 py-1.5 font-bold text-white bg-indigo-600 hover:bg-indigo-500 rounded-lg cursor-pointer">保存</button>
        </div>
      </div>

      <!-- 主库配置 -->
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 divide-y divide-slate-100 dark:divide-slate-800">
        <div class="flex items-center gap-3 pb-4">
          <div class="w-10 h-10 rounded-lg bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400 flex items-center justify-center shrink-0">
            <DatabaseBackup class="w-5 h-5" />
          </div>
          <div>
            <span class="text-[9px] uppercase tracking-wider font-bold text-sky-600 dark:text-sky-400 block">主库 · MySQL（自举依赖）</span>
            <h3 class="font-bold text-xs text-slate-900 dark:text-slate-100 mt-0.5">系统主数据库连接（重启后生效）</h3>
          </div>
        </div>
        <div class="pt-4 grid grid-cols-2 lg:grid-cols-5 gap-3 text-xs">
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">主机 Host</label>
            <input v-model="mainConfig.host" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">端口 Port</label>
            <input v-model.number="mainConfig.port" type="number" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">数据库名称</label>
            <input v-model="mainConfig.databaseName" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">用户名</label>
            <input v-model="mainConfig.username" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
          <div><label class="text-[10px] text-slate-400 font-bold block mb-1">密码（留空=保持原值）</label>
            <input v-model="mainConfig.password" :placeholder="mainConfig.hasPassword ? '******' : '请输入密码'" type="password" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
        </div>
        <div class="pt-4 flex flex-wrap items-center justify-between gap-2">
          <div class="text-[11px] font-mono"
            :class="mainTested?.success ? 'text-emerald-600' : 'text-rose-600'">
            <span v-if="mainTesting" class="text-slate-400">正在测试连接...</span>
            <span v-else-if="mainTested">{{ mainTested.message }}（{{ mainTested.latencyMs }}ms）</span>
          </div>
          <div class="flex gap-2">
            <button @click="doTestMain" :disabled="mainTesting" class="px-3 py-1.5 border border-slate-200 dark:border-slate-700 rounded-lg text-xs font-bold text-slate-700 dark:text-slate-200 cursor-pointer inline-flex items-center gap-1 disabled:opacity-50">
              <RefreshCw class="w-3.5 h-3.5" :class="{ 'animate-spin': mainTesting }" /> 测试连接
            </button>
            <button @click="doSaveMain" class="px-4 py-1.5 rounded-lg text-xs font-bold text-white bg-slate-900 dark:bg-indigo-600 cursor-pointer inline-flex items-center gap-1">
              <Save class="w-3.5 h-3.5" /> 保存主库配置
            </button>
          </div>
        </div>
      </div>

      <!-- 配置卡片列表 -->
      <div class="grid grid-cols-1 xl:grid-cols-2 gap-6">
        <div v-for="db in configs" :key="db.id"
          class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs divide-y divide-slate-100 dark:divide-slate-800 space-y-4 transition-colors">
          <div class="flex items-start justify-between">
            <div class="flex items-center gap-3">
              <div class="w-11 h-11 rounded-lg flex items-center justify-center shrink-0"
                :class="db.type === 'Realtime' ? 'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400' : 'bg-purple-50 dark:bg-purple-950/60 text-purple-600 dark:text-purple-400'">
                <HardDrive class="w-6 h-6" />
              </div>
              <div>
                <span class="text-[9px] uppercase tracking-wider font-bold block"
                  :class="db.type === 'Realtime' ? 'text-sky-600 dark:text-sky-400' : 'text-purple-600 dark:text-purple-400'">
                  {{ db.type === 'Realtime' ? '实时缓存库' : '时序数据库' }} · {{ db.backendType }}
                </span>
                <h3 class="font-bold text-xs text-slate-900 dark:text-slate-100 mt-0.5 leading-snug">{{ db.name }}</h3>
              </div>
            </div>
            <div class="flex items-center gap-2">
              <span class="inline-flex items-center gap-1 px-2 py-1 rounded-full text-[10px] font-bold"
                :class="db.isActive ? 'bg-emerald-50 text-emerald-600 border border-emerald-100' : 'bg-slate-50 text-slate-500 border border-slate-200'">
                <span class="w-1.5 h-1.5 rounded-full" :class="db.isActive ? 'bg-emerald-500' : 'bg-slate-300'" />
                {{ db.isActive ? '生效中' : '备用(未生效)' }}
              </span>
              <span v-if="db.id > 0" class="text-slate-300 dark:text-slate-600 cursor-pointer hover:text-rose-500" @click="removeConfig(db)">
                <Trash2 class="w-4 h-4" />
              </span>
            </div>
          </div>

          <div class="pt-4 space-y-3.5 text-xs font-sans">
            <div class="grid grid-cols-3 gap-3">
              <div>
                <label class="text-[10px] text-slate-400 font-bold block mb-1">数据库类型</label>
                <select v-model="db.backendType"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 text-slate-800 dark:text-slate-100 font-bold outline-none">
                  <option v-for="b in backendOptions" :key="b" :value="b">{{ b }}</option>
                </select>
              </div>
              <div class="col-span-2">
                <label class="text-[10px] text-slate-400 font-bold block mb-1">主机 Host</label>
                <input v-model="db.host" type="text" placeholder="127.0.0.1"
                  class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 font-mono outline-none" />
              </div>
            </div>
            <div class="grid grid-cols-4 gap-3">
              <div><label class="text-[10px] text-slate-400 font-bold block mb-1">端口 Port</label>
                <input v-model.number="db.port" type="number" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 font-mono outline-none" /></div>
              <div><label class="text-[10px] text-slate-400 font-bold block mb-1">用户名</label>
                <input v-model="db.username" type="text" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
              <div><label class="text-[10px] text-slate-400 font-bold block mb-1">数据库名称</label>
                <input v-model="db.databaseName" type="text" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 font-bold font-mono outline-none" /></div>
              <div><label class="text-[10px] text-slate-400 font-bold block mb-1">生效开关</label>
                <label class="flex items-center gap-2 mt-1.5 cursor-pointer">
                  <input v-model="db.isActive" type="checkbox" class="accent-indigo-600" />
                  <span>生效</span>
                </label></div>
            </div>

            <!-- InfluxDB 专属字段 -->
            <div v-if="db.backendType === 'InfluxDB'" class="grid grid-cols-3 gap-3">
              <div><label class="text-[10px] text-slate-400 font-bold block mb-1">Token（留空=保持）</label>
                <input v-model="db.token" type="password" :placeholder="db.hasToken ? '******' : ''" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
              <div><label class="text-[10px] text-slate-400 font-bold block mb-1">Org</label>
                <input v-model="db.org" type="text" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
              <div><label class="text-[10px] text-slate-400 font-bold block mb-1">Bucket</label>
                <input v-model="db.bucket" type="text" class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-1.5 outline-none" /></div>
            </div>
          </div>

          <div class="pt-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3 text-[11px]">
            <div class="flex-1 min-w-0 pr-4">
              <div v-if="testers[db.id]?.loading" class="flex items-center gap-1.5 text-[#1890ff] font-bold">
                <RefreshCw class="w-3.5 h-3.5 animate-spin" /><span>正在测试连接...</span>
              </div>
              <div v-else-if="testers[db.id]?.result" class="leading-relaxed font-medium"
                :class="testers[db.id].result!.success ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400'">
                <span class="font-bold block">{{ testers[db.id].result!.success ? '连接成功' : '连接失败' }}</span>
                <span class="text-[10px] font-mono text-slate-400 dark:text-slate-500 block mt-0.5">{{ testers[db.id].result!.message }}（{{ testers[db.id].result!.latencyMs }}ms）</span>
              </div>
              <p v-else class="text-slate-400 dark:text-slate-500">配置完成后点击"测试连接"验证连通性。</p>
            </div>
            <div class="flex items-center gap-2 self-end shrink-0">
              <button @click="doTestDb(db)" :disabled="testers[db.id]?.loading"
                class="px-3 py-1.5 border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-slate-700 dark:text-slate-200 rounded-lg inline-flex items-center gap-1 cursor-pointer disabled:opacity-50">
                <RefreshCw class="w-3.5 h-3.5" /> 测试连接
              </button>
              <button @click="saveConfig(db)"
                class="px-4 py-1.5 font-bold text-white bg-slate-900 dark:bg-indigo-600 hover:bg-slate-800 dark:hover:bg-indigo-500 rounded-lg inline-flex items-center gap-1 cursor-pointer">
                <Save class="w-3.5 h-3.5" /> 保存
              </button>
            </div>
          </div>
        </div>

        <div v-if="!loading && configs.length === 0"
          class="col-span-full border border-dashed border-slate-300 dark:border-slate-700 rounded-xl p-8 text-center text-sm text-slate-400">
          尚未配置时序/历史数据库，点击右上角「新增配置」创建。
        </div>
      </div>

      <!-- 历史数据迁移 -->
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div class="space-y-1 max-w-2xl text-left">
          <h4 class="font-bold text-xs text-slate-900 dark:text-slate-100 flex items-center gap-1.5">
            <Play class="w-4 h-4 text-indigo-500" />
            历史数据迁移
          </h4>
          <p class="text-[11px] text-slate-500 dark:text-slate-400 leading-relaxed">
            将 MySQL 存量历史数据一次性迁移写入当前生效的 InfluxDB 历史库，供趋势曲线读取旧记录。迁移前会自动将历史库客户端重建到生效配置。
          </p>
          <p v-if="migration" class="text-[11px] font-mono mt-1"
            :class="migration.message.includes('迁移中断') || migration.message.includes('失败') ? 'text-rose-600' : 'text-emerald-600'">
            {{ migration.message }}
          </p>
        </div>
        <button @click="doMigrate" :disabled="migrationBusy"
          class="px-4 py-2 font-bold text-white bg-indigo-600 hover:bg-indigo-500 rounded-lg text-xs inline-flex items-center gap-1.5 cursor-pointer disabled:opacity-50 shrink-0">
          <RefreshCw class="w-3.5 h-3.5" :class="{ 'animate-spin': migrationBusy }" />
          开始迁移
        </button>
      </div>

    </div>
  </div>
</template>