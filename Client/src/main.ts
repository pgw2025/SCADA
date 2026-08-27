import { createApp } from 'vue';
import App from './App.vue';
import router from './router';
import { initializeAuth } from './api/authApi';
import './index.css';

// boot 函数而非顶层 await：Vite 默认 build target（≈es2020）不支持 Top-level await，
// 顶层 await 会 dev 正常、生产构建失败。await 保证守卫首次运行时角色已回源就绪。
async function boot(): Promise<void> {
  await initializeAuth();
  createApp(App).use(router).mount('#root');
}
boot();
