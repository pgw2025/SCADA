import axios from 'axios';
import { addLog } from '../services/logService';
import { isAuthenticated, loginUser } from '../store/userStore';

export const TOKEN_KEY = 'scada_access_token';

const getToken = () => localStorage.getItem(TOKEN_KEY);

/**
 * 统一 HTTP 客户端（架构统一：单一 axios 实例）。
 *
 * 前端所有 API 请求都走此实例，避免各 api 文件分别 new/直连 axios 导致：
 *  - JWT Token 注入 / 401 登录态失效处理无法集中；
 *  - 换用原生 fetch 的接口（如历史查询）漏带 Authorization 而 401。
 *
 * 注意：不设置 baseURL。URL 由各调用方拼接 systemConfig.backendApiUrl
 * （空串 = 相对路径，由 Vite dev proxy / 反代转发），本实例只负责
 * 统一注入 Token 与处理 401，不二次前缀，避免双拼。
 */
export const http = axios.create();

/**
 * 从 axios 错误中提取后端 ApiResponse 的具体错误信息，供 UI 直接展示。
 * 后端统一返回 { success, message, errors }：
 *  - BusinessException：message 即业务错误文案
 *  - 模型校验失败：message 为"数据校验失败"，errors 为 { 字段: [原因...] }
 */
export const extractApiError = (error: any): string => {
  const data = error?.response?.data;
  if (data?.message) {
    const fieldErrors = data.errors
      ? '\n' + Object.entries(data.errors)
          .map(([field, msgs]) => `${field}: ${(Array.isArray(msgs) ? msgs : [msgs]).join('; ')}`)
          .join('\n')
      : '';
    return data.message + fieldErrors;
  }
  return error?.message || '未知错误';
};

// 请求拦截：为所有请求自动附加 JWT Token
http.interceptors.request.use((config) => {
  const token = getToken();
  if (token) {
    config.headers.set('Authorization', `Bearer ${token}`);
  }
  return config;
});

// 响应拦截：Token 失效（401）时清除凭证回到登录态
http.interceptors.response.use(
  (response) => response,
  (error) => {
    // 登录接口自身的 401 属于"用户名或密码错误"，不属于凭证失效，跳过强制登出
    const isLoginRequest = error.config?.url?.includes('/api/Auth/login');
    if (error.response?.status === 401 && !isLoginRequest && getToken()) {
      localStorage.removeItem(TOKEN_KEY);
      isAuthenticated.value = false;
      loginUser.value = null;
      addLog('安全认证', '登录状态已失效，请重新登录', 'warning');
    }
    return Promise.reject(error);
  }
);