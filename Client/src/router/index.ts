import { watch } from 'vue';
import { createRouter, createWebHistory, RouteRecordRaw } from 'vue-router';
import { isAuthenticated, loginUser, authInitialized } from '../store/userStore';
import { TOKEN_KEY } from '../api/http';
import { ROLE_ADMIN, ROLE_OPERATOR, ROLE_VIEWER, ADMIN_ROLES } from '../constants/roles';

// 阶段5：角色约束元信息。
//  - roles 省略或为空 => 任意已登录用户可访问（如 /scada-view 普通用户与管理员均可）。
//  - roles: ADMIN_ROLES => 仅管理员可访问（组态设计、设备/用户管理等后台页面）。
//  - 普通用户（Operator）登录后仅能进入 /scada-view（组态运行画面）。
//  角色字面量统一来自 constants/roles.ts，禁止此处再硬编码字符串。
const routes: RouteRecordRaw[] = [
  { path: '/', component: () => import('../components/DashboardView.vue'), meta: { public: true } },
  { path: '/dashboard', component: () => import('../components/DashboardView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/live-data', component: () => import('../components/LiveDataView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/device-management', component: () => import('../components/DeviceManagementView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/device-variables', component: () => import('../components/DeviceVariableView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/data-models', component: () => import('../components/DataModelView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/scada-editor', component: () => import('../components/ScadaTopologyView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/system-logs', component: () => import('../components/SystemLogsView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/task-management', component: () => import('../components/TaskManagementView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/system-scripts', component: () => import('../components/SystemScriptsView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/alarm-management', component: () => import('../components/AlarmManagementView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/data-interfaces', component: () => import('../components/DataInterfacesView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/historical-query', component: () => import('../components/HistoricalQueryView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/mqtt-servers', component: () => import('../components/MqttServersView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/data-conversion', component: () => import('../components/DataConversionView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/user-management', component: () => import('../components/UserManagementView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/database-management', component: () => import('../components/DatabaseManagementView.vue'), meta: { roles: ADMIN_ROLES } },
  { path: '/settings-center', component: () => import('../components/SettingsCenterView.vue'), meta: { roles: ADMIN_ROLES } },
  // 阶段4/方案B：组态运行画面——所有已登录角色均可访问（Admin/Operator/Viewer）。
  // 一级：/scada-view 工程卡片列表；二级：/scada-view/:projectId 具体工程组态画布（纯播放器）。
  // standalone: App.vue 据此隐藏系统顶部菜单与侧边栏；ScadaPlayerView 自身无任何工具栏/按钮，
  // 画面切换完全依赖组态内配置的跳转按钮，渲染即铺满整屏（新标签页打开）。
  { path: '/scada-view', component: () => import('../components/ProjectPickerView.vue'), meta: { roles: [ROLE_ADMIN, ROLE_OPERATOR, ROLE_VIEWER] } },
  { path: '/scada-view/:projectId', component: () => import('../components/ScadaPlayerView.vue'), meta: { roles: [ROLE_ADMIN, ROLE_OPERATOR, ROLE_VIEWER], standalone: true } }
];

const router = createRouter({
  history: createWebHistory(),
  routes
});

// 阶段5：全局前置守卫——角色隔离与登录拦截。
//  - 未登录 → 允许停留在 '/'（App.vue 会渲染登录界面），若访问其他受限页面则重定向至 '/'。
//  - 已登录若访问 '/' → 根据角色跳转到对应首页（Admin -> /dashboard, 其他 -> /scada-view）。
//  - 已登录但目标路由 roles 不含当前角色 → 普通用户落到 /scada-view（组态运行），
//    管理员落到 /dashboard，避免普通用户误入组态设计/管理后台。
// 按角色计算默认首页；返回 null 表示无有效角色（视为无效会话），绝不能返回 '/'
// （否则会与下方"已登录访问 /"分支互相重定向，形成 vue-router 无限重定向死循环）。
const defaultHomeFor = (role?: string): string | null =>
  role === ROLE_ADMIN ? '/dashboard'
  : (role === ROLE_OPERATOR || role === ROLE_VIEWER) ? '/scada-view'
  : null;

router.beforeEach(async (to, _from, next) => {
  // 就绪等待：initializeAuth（含回源 /me）通常在 boot 阶段已完成；此为兜底等待。
  // 10s 超时后按现状放行，避免极端情况下导航永久挂起。
  if (!authInitialized.value) {
    await Promise.race([
      new Promise<void>((resolve) => {
        const stop = watch(authInitialized, (ready) => {
          if (ready) { stop(); resolve(); }
        });
      }),
      new Promise<void>((resolve) => setTimeout(resolve, 10_000)),
    ]);
  }

  if (!isAuthenticated.value) {
    if (to.path === '/' || to.meta.public) {
      return next();
    }
    return next('/');
  }

  // 状态不一致防护：已登录却无有效角色 → 视为无效会话，清理后回登录页
  const role = loginUser.value?.role;
  const home = defaultHomeFor(role);
  if (!home) {
    localStorage.removeItem(TOKEN_KEY);
    isAuthenticated.value = false;
    loginUser.value = null;
    return next('/');
  }

  // 已登录状态下访问根路径，自动跳转到角色默认首页
  if (to.path === '/') {
    return next(home);
  }

  const roles = (to.meta.roles as string[] | undefined) ?? [];
  if (roles.length > 0 && !roles.includes(role!)) {
    return next(home);
  }
  next();
});

export default router;
