import axios from 'axios';
import { addLog } from '../services/logService';
import { systemConfig } from '../store/configStore';
import { isAuthenticated, loginUser, systemUsers } from '../store/userStore';
import { SystemUser, CreateUserDto, UpdateUserDto } from '../types';

const TOKEN_KEY = 'scada_access_token';

// 初始化认证状态
export const initializeAuth = () => {
  const token = localStorage.getItem(TOKEN_KEY);
  if (token) {
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      if (payload.exp * 1000 > Date.now()) {
        isAuthenticated.value = true;
        loginUser.value = {
          username: payload.username || 'admin',
          role: payload.role || '系统管理员'
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
    const response = await axios.post(`${systemConfig.value.backendApiUrl}/api/Auth/login`, {
      username: username,
      password: passwordString
    });

    if (response.data && response.data.success) {
      const token = response.data.token;
      localStorage.setItem(TOKEN_KEY, token);

      isAuthenticated.value = true;
      loginUser.value = {
        username: response.data.user?.username || username,
        role: response.data.user?.role || '系统管理员'
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
  const response = await axios.get(`${systemConfig.value.backendApiUrl}/api/SystemUser`);
  return response.data;
};

export const createSystemUser = async (userData: CreateUserDto): Promise<SystemUser> => {
  const response = await axios.post(`${systemConfig.value.backendApiUrl}/api/SystemUser`, userData);
  return response.data;
};

export const updateSystemUser = async (userData: UpdateUserDto): Promise<SystemUser> => {
  // 后端路由为 PUT /api/SystemUser/{id}，必须携带 id
  const response = await axios.put(`${systemConfig.value.backendApiUrl}/api/SystemUser/${userData.id}`, userData);
  return response.data;
};

export const deleteSystemUser = async (id: number): Promise<void> => {
  await axios.delete(`${systemConfig.value.backendApiUrl}/api/SystemUser/${id}`);
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
