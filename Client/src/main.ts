import { createApp } from 'vue';
import App from './App.vue';
import router from './router';
import { initializeAuth } from './api/authApi';
import './index.css';

// boot 函数而非顶层 await：Vite 默认 build target（≈es2020）不支持 Top-level await，
// 顶层 await 会 dev 正常、生产构建失败。await 保证守卫首次运行时角色已回源就绪。
async function boot(): Promise<void> {
  await initializeAuth();
  const app = createApp(App).use(router);
  // 等待首次路由导航完成（含懒加载组件与全局守卫）再挂载：
  // 挂载时 route.meta.standalone 已就绪，/scada-view/:projectId 刷新时
  // 不会先闪现后台菜单/标题；挂载前由 index.html 内置的启动动画占位。
  await router.isReady();
  app.mount('#root');
}
boot();
