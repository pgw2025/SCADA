import { addLog } from '../services/logService';
import { systemConfig } from '../store/configStore';
import { isAuthenticated, loginUser, systemUsers } from '../store/userStore';
import { SystemUser, CreateUserDto, UpdateUserDto } from '../types';
import { http, TOKEN_KEY } from './http';

// 初始化认证状态
export const initializeAuth = () => {
  const token = localStorage.getItem(TOKEN_KEY);
  if (token) {
    try {
      // JWT payload 使用 base64url 编码，atob 不识别 '-'/'_'，需先还原为标准 base64 并补齐填充
      const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
      const payload = JSON.parse(atob(padded));
      if (payload.exp * 1000 > Date.now()) {
        // 无有效 role/username 的 token 视为无效会话，不能默认授予管理员身份
        if (!payload.role || !payload.username) {
          localStorage.removeItem(TOKEN_KEY);
          return;
        }
        isAuthenticated.value = true;
        loginUser.value = {
          username: payload.username,
          role: payload.role
        };
        addLog('安全认证', 'Token 自动登录成功', 'normal');
      } else {
        localStorage.removeItem(TOKEN_KEY);
      }
    } catch {
      localStorage.removeItem(TOKEN_KEY);
    }
  }
};

// 登录接口对接
export const performLogin = async (username: string, passwordString: string): Promise<{ success: boolean; errorMessage?: string }> => {
  try {
    const response = await http.post(`${systemConfig.value.backendApiUrl}/api/Auth/login`, {
      username: username,
      password: passwordString
    });

    if (response.data && response.data.success) {
      const role = response.data.user?.role;
      const userName = response.data.user?.username;
      // 角色信息缺失视为异常响应，不授予任何身份（避免默认管理员风险）
      if (!role || !userName) {
        return { success: false, errorMessage: '登录响应缺少用户信息' };
      }
      const token = response.data.token;
      localStorage.setItem(TOKEN_KEY, token);

      isAuthenticated.value = true;
      loginUser.value = {
        username: userName,
        role
      };

      addLog('安全认证', `用户 [${username}] 通过API登录系统成功`, 'normal');
      return { success: true };
    } else {
      const errorMsg = response.data?.message || '用户名或密码错误';
      addLog('安全认证', `用户 [${username}] 登录失败: ${errorMsg}`, 'warning');
      return { success: false, errorMessage: errorMsg };
    }
  } catch (error: any) {
    const errorMessage = error.response?.data?.message || error.message || '服务器连接失败，请检查网络或后端服务';
    addLog('安全认证', `登录失败: ${errorMessage}`, 'warning');
    return { success: false, errorMessage: errorMessage };
  }
};

export const performLogout = () => {
  addLog('安全认证', `用户 [${loginUser.value?.username || 'admin'}] 注销系统登录`, 'normal');
  isAuthenticated.value = false;
  loginUser.value = null;
  localStorage.removeItem(TOKEN_KEY);
};

export const fetchSystemUsers = async (): Promise<SystemUser[]> => {
  const response = await http.get(`${systemConfig.value.backendApiUrl}/api/SystemUser`);
  return response.data;
};

export const createSystemUser = async (userData: CreateUserDto): Promise<SystemUser> => {
  const response = await http.post(`${systemConfig.value.backendApiUrl}/api/SystemUser`, userData);
  return response.data;
};

export const updateSystemUser = async (userData: UpdateUserDto): Promise<SystemUser> => {
  // 后端路由为 PUT /api/SystemUser/{id}，必须携带 id
  const response = await http.put(`${systemConfig.value.backendApiUrl}/api/SystemUser/${userData.id}`, userData);
  return response.data;
};

export const deleteSystemUser = async (id: number): Promise<void> => {
  await http.delete(`${systemConfig.value.backendApiUrl}/api/SystemUser/${id}`);
};

export const loadSystemUsers = async (): Promise<SystemUser[]> => {
  try {
    const users = await fetchSystemUsers();
    systemUsers.value = users;
    return users;
  } catch (error: any) {
    addLog('用户管理', `加载用户列表失败: ${error.message}`, 'warning');
    throw error;
  }
};
