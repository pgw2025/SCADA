import { addLog } from '../services/logService';
import { systemConfig } from '../store/configStore';
import { isAuthenticated, loginUser, systemUsers, authInitialized } from '../store/userStore';
import { SystemUser, CreateUserDto, UpdateUserDto } from '../types';
import { http, TOKEN_KEY } from './http';

// 初始化认证状态（异步：本地校验 token 后回源 /api/Auth/me 取权威角色/状态）
export const initializeAuth = async (): Promise<void> => {
  try {
    const token = localStorage.getItem(TOKEN_KEY);
    if (!token) return;

    // 1) 本地快速校验（不发请求）：结构 + exp
    let payload: any;
    try {
      // JWT payload 使用 base64url 编码，atob 不识别 '-'/'_'，需先还原为标准 base64 并补齐填充
      const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
      // TextDecoder 正确处理非 ASCII 用户名（替代已废弃的 escape/atob 组合）
      const json = new TextDecoder().decode(Uint8Array.from(atob(padded), c => c.charCodeAt(0)));
      payload = JSON.parse(json);
      if (!payload.exp || payload.exp * 1000 <= Date.now()) throw new Error('expired');
      // 无有效 role/username 的 token 视为无效会话，不能默认授予管理员身份
      if (!payload.role || !payload.username) throw new Error('no identity');
    } catch {
      localStorage.removeItem(TOKEN_KEY); // 过期/结构异常 → 清除（不发 /me）
      return;
    }

    // 2) 临时态：token 快照，用于防闪屏，以及回源失败时的降级身份
    isAuthenticated.value = true;
    loginUser.value = { username: payload.username, role: payload.role };
    addLog('安全认证', 'Token 自动登录成功（待回源校验）', 'normal');

    // 3) 回源：用数据库中最新角色/状态覆盖 token 快照
    try {
      const res = await http.get(`${systemConfig.value.backendApiUrl}/api/Auth/me`, { timeout: 5000 });
      const u = res.data;
      if (u && u.status === 'Active') {
        loginUser.value = { username: u.username, role: u.role };
      } else {
        // 账号不存在/已停用/角色被收回 → 会话确认无效，清理
        localStorage.removeItem(TOKEN_KEY);
        isAuthenticated.value = false;
        loginUser.value = null;
      }
    } catch (error: any) {
      if (error.response?.status === 401) {
        // 签名无效/被吊销等 → 会话确认无效，清理
        localStorage.removeItem(TOKEN_KEY);
        isAuthenticated.value = false;
        loginUser.value = null;
      } else {
        // 网络错误/超时/后端不可达 → 保留会话，沿用 token 快照（与现状同等降级，绝不误杀）
        addLog('安全认证', '回源校验暂不可用，沿用本地会话', 'warning');
      }
    }
  } catch {
    // 最后兜底：任何未预见异常按未登录处理，绝不让异常向外抛出阻塞应用启动
    isAuthenticated.value = false;
    loginUser.value = null;
    try { localStorage.removeItem(TOKEN_KEY); } catch { /* ignore */ }
  } finally {
    // 无论成败必须置位，否则路由守卫会一直等待初始化
    authInitialized.value = true;
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

export const resetSystemUserPassword = async (id: number, newPassword: string): Promise<void> => {
  // 后端路由为 POST /api/SystemUser/{id}/reset-password（管理员重置他人密码）
  await http.post(`${systemConfig.value.backendApiUrl}/api/SystemUser/${id}/reset-password`, { newPassword });
};

export const changeMyPassword = async (oldPassword: string, newPassword: string): Promise<void> => {
  // 后端路由为 POST /api/Auth/change-password（任意已登录用户自主改密，需验证原密码）
  await http.post(`${systemConfig.value.backendApiUrl}/api/Auth/change-password`, { oldPassword, newPassword });
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
