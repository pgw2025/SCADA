import { createRouter, createWebHistory, RouteRecordRaw } from 'vue-router';

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/dashboard' },
  { path: '/dashboard', component: () => import('../components/DashboardView.vue') },
  { path: '/live-data', component: () => import('../components/LiveDataView.vue') },
  { path: '/device-management', component: () => import('../components/DeviceManagementView.vue') },
  { path: '/data-models', component: () => import('../components/DataModelView.vue') },
  { path: '/scada-editor', component: () => import('../components/ScadaTopologyView.vue') },
  { path: '/system-logs', component: () => import('../components/SystemLogsView.vue') },
  { path: '/trigger-management', component: () => import('../components/TriggerManagementView.vue') },
  { path: '/task-management', component: () => import('../components/TaskManagementView.vue') },
  { path: '/system-scripts', component: () => import('../components/SystemScriptsView.vue') },
  { path: '/data-interfaces', component: () => import('../components/DataInterfacesView.vue') },
  { path: '/historical-query', component: () => import('../components/HistoricalQueryView.vue') },
  { path: '/mqtt-servers', component: () => import('../components/MqttServersView.vue') },
  { path: '/data-conversion', component: () => import('../components/DataConversionView.vue') },
  { path: '/user-management', component: () => import('../components/UserManagementView.vue') },
  { path: '/database-management', component: () => import('../components/DatabaseManagementView.vue') },
  { path: '/settings-center', component: () => import('../components/SettingsCenterView.vue') }
];

const router = createRouter({
  history: createWebHistory(),
  routes
});

export default router;
