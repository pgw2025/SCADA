import { createRouter, createWebHistory, RouteRecordRaw } from 'vue-router';
import { isAuthenticated, loginUser } from '../store/userStore';

// 阶段5：角色约束元信息。
//  - roles 省略或为空 => 任意已登录用户可访问（如 /scada-view 普通用户与管理员均可）。
//  - roles: ['Admin'] => 仅管理员可访问（组态设计、设备/用户管理等后台页面）。
//  - 普通用户（Operator）登录后仅能进入 /scada-view（组态运行画面）。
const ADMIN = ['Admin'];

const routes: RouteRecordRaw[] = [
  { path: '/', component: () => import('../components/DashboardView.vue'), meta: { public: true } },
  { path: '/dashboard', component: () => import('../components/DashboardView.vue'), meta: { roles: ADMIN } },
  { path: '/live-data', component: () => import('../components/LiveDataView.vue'), meta: { roles: ADMIN } },
  { path: '/device-management', component: () => import('../components/DeviceManagementView.vue'), meta: { roles: ADMIN } },
  { path: '/device-variables', component: () => import('../components/DeviceVariableView.vue'), meta: { roles: ADMIN } },
  { path: '/data-models', component: () => import('../components/DataModelView.vue'), meta: { roles: ADMIN } },
  { path: '/scada-editor', component: () => import('../components/ScadaTopologyView.vue'), meta: { roles: ADMIN } },
  { path: '/system-logs', component: () => import('../components/SystemLogsView.vue'), meta: { roles: ADMIN } },
  { path: '/trigger-management', component: () => import('../components/TriggerManagementView.vue'), meta: { roles: ADMIN } },
  { path: '/task-management', component: () => import('../components/TaskManagementView.vue'), meta: { roles: ADMIN } },
  { path: '/system-scripts', component: () => import('../components/SystemScriptsView.vue'), meta: { roles: ADMIN } },
  { path: '/data-interfaces', component: () => import('../components/DataInterfacesView.vue'), meta: { roles: ADMIN } },
  { path: '/historical-query', component: () => import('../components/HistoricalQueryView.vue'), meta: { roles: ADMIN } },
  { path: '/mqtt-servers', component: () => import('../components/MqttServersView.vue'), meta: { roles: ADMIN } },
  { path: '/data-conversion', component: () => import('../components/DataConversionView.vue'), meta: { roles: ADMIN } },
  { path: '/user-management', component: () => import('../components/UserManagementView.vue'), meta: { roles: ADMIN } },
  { path: '/database-management', component: () => import('../components/DatabaseManagementView.vue'), meta: { roles: ADMIN } },
  { path: '/settings-center', component: () => import('../components/SettingsCenterView.vue'), meta: { roles: ADMIN } },
  // 阶段4：组态运行画面——所有已登录角色均可访问（Admin/Operator/Viewer）
  { path: '/scada-view', component: () => import('../components/ScadaRuntimeView.vue'), meta: { roles: ['Admin', 'Operator', 'Viewer'] } }
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
router.beforeEach((to, _from, next) => {
  if (!isAuthenticated.value) {
    if (to.path === '/' || to.meta.public) {
      return next();
    }
    return next('/');
  }

  // 已登录状态下访问根路径，自动跳转到角色默认首页
  if (to.path === '/') {
    const role = loginUser.value?.role;
    return next(role === 'Admin' ? '/dashboard' : '/scada-view');
  }

  const roles = (to.meta.roles as string[] | undefined) ?? [];
  if (roles.length > 0) {
    const role = loginUser.value?.role;
    if (!role || !roles.includes(role)) {
      return next(role === 'Admin' ? '/dashboard' : '/scada-view');
    }
  }
  next();
});

export default router;
