import tailwindcss from '@tailwindcss/vite';
import vue from '@vitejs/plugin-vue';
import path from 'path';
import {defineConfig} from 'vite';
import {VitePWA} from 'vite-plugin-pwa';

export default defineConfig(() => {
  return {
    plugins: [
      vue(),
      tailwindcss(),
      VitePWA({
        strategies: 'injectManifest',
        srcDir: 'src',
        filename: 'sw.ts',
        registerType: 'prompt',
        injectRegister: false,
        includeAssets: [
          'favicon.ico',
          'apple-touch-icon.png',
          'pwa/icon-192.png',
          'pwa/icon-512.png',
          'pwa/icon-maskable-512.png'
        ],
        manifest: {
          name: '晋鑫设备管理系统',
          short_name: '晋鑫设备',
          description: '工业控制与数据采集平台（PWA）',
          id: '/',
          start_url: '/',
          scope: '/',
          display: 'standalone',
          orientation: 'any',
          background_color: '#f1f5f9',
          theme_color: '#0ea5e9',
          lang: 'zh-CN',
          dir: 'ltr',
          icons: [
            { src: 'pwa/icon-192.png', sizes: '192x192', type: 'image/png' },
            { src: 'pwa/icon-512.png', sizes: '512x512', type: 'image/png' },
            { src: 'pwa/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' }
          ],
          shortcuts: [
            { name: '组态运行', url: '/scada-view', description: '打开组态运行画面' },
            { name: '报警管理', url: '/alarm-management', description: '查看报警管理' }
          ]
        },
        workbox: {
          globPatterns: ['**/*.{js,css,html,svg,png,ico,woff2}'],
          cleanupOutdatedCaches: true,
          navigateFallback: 'index.html',
          navigateFallbackDenylist: [/^\/api\//, /^\/open\//, /^\/hubs\//],
          maximumFileSizeToCacheInBytes: 3 * 1024 * 1024
        },
        devOptions: { enabled: true, type: 'module' }
      })
    ],
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
    build: {
      rollupOptions: {
        output: {
          // 组件模板源（widgetRegistry 转发层 + 实现模块）强制并入同一 chunk，
          // 消除「re-export 跨 chunk 循环引用」的 Rollup warning（P2 验收要求 0 新增 warning）。
          // 消费方（组件库/画布/属性面板）的 import 路径保持 widgetRegistry 不变（审查 A8）。
          manualChunks(id: string) {
            if (/src[\\/](widgetTemplates|builtinSeeds|builtinRenderers|widgetRegistry)\.ts/.test(id)) {
              return 'widget-template';
            }
          },
        },
      },
    },
  };
});
