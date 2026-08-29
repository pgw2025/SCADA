import tailwindcss from '@tailwindcss/vite';
import vue from '@vitejs/plugin-vue';
import path from 'path';
import {defineConfig} from 'vite';

export default defineConfig(() => {
  return {
    plugins: [vue(), tailwindcss()],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, '.'),
      },
    },
    server: {
      // HMR is disabled in AI Studio via DISABLE_HMR env var.
      // Do not modifyâ€file watching is disabled to prevent flickering during agent edits.
      hmr: process.env.DISABLE_HMR !== 'true',
      // Disable file watching when DISABLE_HMR is true to save CPU during agent edits.
      watch: process.env.DISABLE_HMR === 'true' ? null : {},
      // 开发环境代理：前端相对路径 /api/* 和 /hubs/* 转发到后端 ASP.NET Core WebAPI (:5555)
      // 生产环境由 nginx/反向代理处理，前端代码无需感知后端地址
      proxy: {
        '/api': {
          target: 'http://localhost:5555',
          changeOrigin: true,
        },
        // 开放 API 网关：/open/* 真实测试也要经开发代理转发到后端 :5555
        '/open': {
          target: 'http://localhost:5555',
          changeOrigin: true,
        },
        '/hubs': {
          target: 'http://localhost:5555',
          changeOrigin: true,
          // SignalR 依赖 WebSocket，必须开启 ws 代理
          ws: true,
        },
      },
    },
  };
});
